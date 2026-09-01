using CrossEscPos.Rendering.Skia;
using SkiaSharp;

namespace YsFourcut.Host.Rendering;

/// <summary>
/// 추정 효과 이름. 기본은 전부 꺼져 있다. 실측하지 않은 장치 특성의 **추정**이며
/// AHAPOS의 실제 농도·피드와 같다는 뜻이 아니다.
/// </summary>
public static class EstimatedEffects
{
    public const string InkBleed = "ink_bleed";
    public const string Banding = "banding";

    public static bool IsKnown(string name) => name is InkBleed or Banding;
}

/// <summary>
/// 지면(paper)을 실제 감열 영수증처럼 보이게 합성한다. 별도 캔버스에서만 그리며
/// content/paper 원본 픽셀은 건드리지 않는다. 바깥 여백과 그림자는 물리 치수에 포함되지 않는다.
///
/// 합성 순서: 바닥 그림자 → 종이색 → 아주 약한 섬유 질감 → (선택) 밴딩 →
/// 잉크(원본 지면을 Multiply) → (선택) 번짐 → 종이 두께 가장자리.
/// 잉크는 Multiply로 올리므로 검정 dot는 검정 그대로 남고 흰 여백만 종이색이 된다.
///
/// 그래픽 엔진은 03번과 같은 SkiaSharp 3.119.4다. 새 엔진을 도입하지 않으며,
/// 외형 전용 효과에만 SkiaSharp API를 직접 사용한다(VIRTUAL_PRINTER_DESIGN.md 2절).
/// </summary>
public sealed class ReceiptAppearanceRenderer(IReceiptImageBackend backend) : IReceiptAppearanceRenderer
{
    /// <summary>외형 알고리즘 버전. 바뀌면 같은 seed라도 결과가 달라질 수 있으므로 작업에 함께 기록한다.</summary>
    public const string Revision = "a1";

    /// <summary>그림자를 담기 위한 캔버스 여백(px). 종이의 물리 크기가 아니다.</summary>
    public const int OuterMarginPx = 24;

    private const int ShadowOffsetYPx = 6;
    private const float ShadowSigma = 7f;
    private const byte ShadowAlpha = 70;

    // 밝은 아이보리. 검정 잉크와의 대비를 유지하기 위해 아주 옅게만 색을 준다.
    private static readonly SKColor PaperBase = new(250, 247, 241);
    private static readonly SKColor PaperEdge = new(223, 216, 203);
    private static readonly SKColor ShadowColor = new(60, 52, 44);

    /// <summary>절취 요철의 최대 깊이(dot). 앞뒤 이송 여백 안에서만 움직여 내용을 자르지 않는다.</summary>
    private const int MaxTearDepthDots = 6;

    public bool IsImplemented => true;

    public (int WidthPx, int HeightPx) Measure(ReceiptLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return (layout.PaperWidthDots + (OuterMarginPx * 2), layout.PaperHeightDots + (OuterMarginPx * 2));
    }

    public ReceiptAppearanceResult Render(ReceiptAppearanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Paper);

        foreach (var effect in request.EstimatedEffects)
        {
            if (!EstimatedEffects.IsKnown(effect))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request), $"알 수 없는 추정 효과입니다: {effect}");
            }
        }

        if (request.Paper is not SkiaReceiptImage skiaPaper)
        {
            throw new InvalidOperationException("지면 이미지가 Skia 이미지가 아닙니다.");
        }

        var layout = request.Layout;
        var (canvasWidth, canvasHeight) = Measure(layout);

        var paperLeft = OuterMarginPx;
        var paperTop = OuterMarginPx;
        var paperRight = paperLeft + layout.PaperWidthDots;
        var paperBottom = paperTop + layout.PaperHeightDots;

        var bitmap = new SKBitmap(new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        SkiaReceiptImage? image = null;

        try
        {
            using (var canvas = new SKCanvas(bitmap))
            {
                // 배경은 투명하게 둔다. 화면의 바닥은 웹이 정하고, 여기에는 종이와 그림자만 담는다.
                canvas.Clear(SKColors.Transparent);

                using var silhouette = BuildPaperPath(
                    request.CutStyle, request.Seed, layout, paperLeft, paperTop, paperRight, paperBottom, canvasHeight);

                DrawFloorShadow(canvas, silhouette);
                DrawPaper(canvas, silhouette, layout, request.Seed);

                canvas.Save();
                canvas.ClipPath(silhouette, antialias: true);

                if (request.EstimatedEffects.Contains(EstimatedEffects.Banding))
                {
                    DrawBanding(canvas, request.Seed, paperLeft, paperTop, paperRight, paperBottom);
                }

                DrawInk(canvas, skiaPaper.Bitmap, paperLeft, paperTop);

                if (request.EstimatedEffects.Contains(EstimatedEffects.InkBleed))
                {
                    DrawInkBleed(canvas, skiaPaper.Bitmap, paperLeft, paperTop);
                }

                canvas.Restore();

                DrawPaperEdge(canvas, silhouette);
                canvas.Flush();
            }

            image = new SkiaReceiptImage(bitmap);
            var png = backend.EncodePng(image);

            return new ReceiptAppearanceResult(
                PngBytes: png,
                WidthPx: canvasWidth,
                HeightPx: canvasHeight,
                AppearanceRevision: Revision,
                Seed: request.Seed,
                EstimatedEffects: [.. request.EstimatedEffects]);
        }
        finally
        {
            // SkiaReceiptImage가 비트맵 소유권을 가져가므로 둘 중 하나만 dispose한다.
            if (image is not null)
            {
                image.Dispose();
            }
            else
            {
                bitmap.Dispose();
            }
        }
    }

    /// <summary>
    /// 종이 외곽선. straight는 수평 절단, tear는 이송 여백 안에서만 흔들리는 불규칙한 절취면,
    /// none은 잘리지 않은 롤이라 위아래가 캔버스 밖으로 이어진다.
    /// </summary>
    private static SKPath BuildPaperPath(
        string cutStyle,
        long seed,
        ReceiptLayout layout,
        int left,
        int top,
        int right,
        int bottom,
        int canvasHeight)
    {
        var path = new SKPath();

        if (cutStyle == Profiles.CutStyles.None)
        {
            // 미절취 롤: 위아래 끝을 캔버스 밖으로 이어 자르지 않은 상태를 나타낸다.
            path.AddRect(new SKRect(left, 0, right, canvasHeight));
            return path;
        }

        if (cutStyle != Profiles.CutStyles.Tear)
        {
            path.AddRect(new SKRect(left, top, right, bottom));
            return path;
        }

        var topDepth = TearDepth(layout.LeadingFeedDots);
        var bottomDepth = TearDepth(layout.TrailingFeedDots);

        path.MoveTo(left, top + TearOffset(seed, 0, left, topDepth));
        for (var x = left; x <= right; x++)
        {
            path.LineTo(x, top + TearOffset(seed, 0, x, topDepth));
        }

        path.LineTo(right, bottom - TearOffset(seed, 1, right, bottomDepth));
        for (var x = right; x >= left; x--)
        {
            path.LineTo(x, bottom - TearOffset(seed, 1, x, bottomDepth));
        }

        path.Close();
        return path;
    }

    /// <summary>요철 깊이는 이송 여백의 1/3과 6dot 중 작은 값이다. 사진·문구까지 닿지 않는다.</summary>
    private static int TearDepth(int feedDots) => Math.Clamp(Math.Min(MaxTearDepthDots, feedDots / 3), 0, MaxTearDepthDots);

    /// <summary>
    /// 굵은 흔들림과 잔결을 더한 불규칙선. 규칙적인 톱니를 만들지 않고 seed에만 의존한다.
    /// </summary>
    private static float TearOffset(long seed, int edge, int x, int depth)
    {
        if (depth <= 0)
        {
            return 0;
        }

        var coarse = InterpolatedNoise(seed, edge * 31, x, 17);
        var fine = InterpolatedNoise(seed, (edge * 31) + 7, x, 5);
        var value = (coarse * 0.72f) + (fine * 0.28f);
        return value * depth;
    }

    /// <summary>제어점 사이를 부드럽게 잇는 [0,1) 잡음.</summary>
    private static float InterpolatedNoise(long seed, int channel, int x, int step)
    {
        var index = (int)Math.Floor((double)x / step);
        var t = (x - (index * (double)step)) / step;
        var a = Random01(seed, channel, index);
        var b = Random01(seed, channel, index + 1);
        var smooth = t * t * (3 - (2 * t)); // smoothstep
        return (float)(a + ((b - a) * smooth));
    }

    private static void DrawFloorShadow(SKCanvas canvas, SKPath silhouette)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = ShadowColor.WithAlpha(ShadowAlpha),
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, ShadowSigma),
        };

        canvas.Save();
        canvas.Translate(0, ShadowOffsetYPx);
        canvas.DrawPath(silhouette, paint);
        canvas.Restore();
    }

    private static void DrawPaper(SKCanvas canvas, SKPath silhouette, ReceiptLayout layout, long seed)
    {
        using (var fill = new SKPaint { IsAntialias = true, Color = PaperBase })
        {
            canvas.DrawPath(silhouette, fill);
        }

        // 아주 약한 섬유 질감. 밝기를 최대 3단계만 낮춰 내용 대비를 떨어뜨리지 않는다.
        var bounds = silhouette.Bounds;
        var width = (int)Math.Ceiling(bounds.Width);
        var height = (int)Math.Ceiling(bounds.Height);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        using var fibre = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var pixels = fibre.Pixels;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // 세로로 길게 늘인 잡음이라 감열지 섬유처럼 보인다.
                var noise = Random01(seed, 101, (y / 3 * 100_003) + x);
                var streak = Random01(seed, 103, x);
                var drop = (byte)Math.Round((noise * 2.2) + (streak * 0.9));
                var level = (byte)(255 - drop);
                pixels[(y * width) + x] = new SKColor(level, level, level);
            }
        }

        fibre.Pixels = pixels;

        canvas.Save();
        canvas.ClipPath(silhouette, antialias: true);
        using (var texturePaint = new SKPaint { BlendMode = SKBlendMode.Multiply })
        {
            canvas.DrawBitmap(fibre, bounds.Left, bounds.Top, texturePaint);
        }

        canvas.Restore();

        _ = layout;
    }

    /// <summary>원본 지면을 Multiply로 올린다. 흰 여백은 종이색이 되고 검정 dot는 검정으로 남는다.</summary>
    private static void DrawInk(SKCanvas canvas, SKBitmap paper, int left, int top)
    {
        using var paint = new SKPaint { BlendMode = SKBlendMode.Multiply };
        canvas.DrawBitmap(paper, left, top, paint);
    }

    /// <summary>추정 효과: 잉크가 약간 번진 느낌. 기본은 꺼져 있다.</summary>
    private static void DrawInkBleed(SKCanvas canvas, SKBitmap paper, int left, int top)
    {
        using var paint = new SKPaint
        {
            BlendMode = SKBlendMode.Multiply,
            Color = SKColors.White.WithAlpha(90),
            ImageFilter = SKImageFilter.CreateBlur(0.7f, 0.7f),
        };

        canvas.DrawBitmap(paper, left, top, paint);
    }

    /// <summary>추정 효과: 감열 헤드의 옅은 가로 밴딩. 기본은 꺼져 있다.</summary>
    private static void DrawBanding(SKCanvas canvas, long seed, int left, int top, int right, int bottom)
    {
        using var paint = new SKPaint { BlendMode = SKBlendMode.Multiply, IsAntialias = false };

        for (var y = top; y < bottom; y += 3)
        {
            var strength = Random01(seed, 211, y);
            if (strength < 0.65f)
            {
                continue;
            }

            var level = (byte)(255 - Math.Round((strength - 0.65f) * 18));
            paint.Color = new SKColor(level, level, level);
            canvas.DrawRect(new SKRect(left, y, right, y + 1), paint);
        }
    }

    private static void DrawPaperEdge(SKCanvas canvas, SKPath silhouette)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            Color = PaperEdge,
        };

        canvas.DrawPath(silhouette, paint);
    }

    /// <summary>seed에만 의존하는 [0,1) 난수. 같은 seed·revision이면 항상 같은 결과가 나온다.</summary>
    private static float Random01(long seed, int channel, int index)
    {
        unchecked
        {
            var h = (ulong)seed * 0x9E3779B97F4A7C15UL;
            h ^= (ulong)(uint)channel * 0xBF58476D1CE4E5B9UL;
            h += (ulong)(uint)index * 0x94D049BB133111EBUL;
            h ^= h >> 31;
            h *= 0xBF58476D1CE4E5B9UL;
            h ^= h >> 29;
            h *= 0x94D049BB133111EBUL;
            h ^= h >> 32;
            return (uint)(h >> 32) / 4294967296f;
        }
    }
}
