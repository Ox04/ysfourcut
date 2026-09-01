using SkiaSharp;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Rendering;

namespace YsFourcut.Host.Tests;

/// <summary>
/// 외관은 별도 캔버스에서만 만들어지고 content/paper 원본을 바꾸지 않는다.
/// 같은 seed·revision이면 결과가 같고, 절취 외형이 내용 픽셀을 자르지 않는다.
/// </summary>
public sealed class ReceiptAppearanceTests
{
    private const int Margin = ReceiptAppearanceRenderer.OuterMarginPx;

    private static readonly PrinterProfile Profile80 = LoadPreset("virtual-80mm-8dpmm");
    private static readonly PrinterProfile Profile58 = LoadPreset("virtual-58mm-8dpmm");

    [Fact]
    public void Appearance_canvas_is_paper_plus_outer_margin()
    {
        var result = Render(Profile80, 576, 200);
        var layout = result.Layout;

        Assert.NotNull(result.Appearance);
        Assert.Null(result.AppearanceUnavailableReason);
        Assert.Equal(3, result.Artifacts.Count);

        Assert.Equal(layout.PaperWidthDots + (Margin * 2), result.Appearance!.WidthPx);
        Assert.Equal(layout.PaperHeightDots + (Margin * 2), result.Appearance.HeightPx);

        // 바깥 여백은 그림자용이며 물리 치수(mm)에는 들어가지 않는다.
        Assert.Equal(80, layout.PaperWidthMm, 3);
        Assert.Equal(640, layout.PaperWidthDots);
    }

    [Fact]
    public void Content_and_paper_bytes_are_identical_with_and_without_appearance()
    {
        var data = Pattern(576, 160, 72);
        var bitmap = Bitmap(data, 576, 160, 72);

        var withoutAppearance = CreateRenderer(new UnimplementedAppearanceRenderer()).Render(bitmap, Profile80);
        var withAppearance = CreateRenderer(NewAppearanceRenderer()).Render(bitmap, Profile80);

        Assert.Equal(withoutAppearance.Content.Bytes, withAppearance.Content.Bytes);
        Assert.Equal(withoutAppearance.Paper.Bytes, withAppearance.Paper.Bytes);
        Assert.Null(withoutAppearance.Appearance);
        Assert.NotNull(withAppearance.Appearance);
    }

    [Fact]
    public void Ink_stays_black_and_white_margin_becomes_paper_colour()
    {
        const int width = 120;
        const int height = 64;
        var stride = (width + 7) / 8;
        var data = Pattern(width, height, stride);

        var result = Render(Profile80, width, height, data);
        var layout = result.Layout;

        using var appearance = SKBitmap.Decode(result.Appearance!.Bytes);
        using var paper = SKBitmap.Decode(result.Paper.Bytes);

        var black = new SKColor(0, 0, 0);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = paper.GetPixel(layout.ContentXDots + x, layout.ContentYDots + y);
                var target = appearance.GetPixel(Margin + layout.ContentXDots + x, Margin + layout.ContentYDots + y);

                if (source == black)
                {
                    Assert.Equal(black, target);
                }
                else
                {
                    // 흰 여백은 밝은 종이색이어야 한다. 누렇게 덮어 대비를 떨어뜨리지 않는다.
                    Assert.True(target.Red >= 235 && target.Green >= 230 && target.Blue >= 225,
                        $"({x},{y}) 종이색이 너무 어둡습니다: {target}");
                    Assert.True(target.Alpha == 255, $"({x},{y}) 내용 영역은 불투명해야 합니다.");
                }
            }
        }
    }

    [Theory]
    [InlineData(CutStyles.Straight)]
    [InlineData(CutStyles.Tear)]
    [InlineData(CutStyles.None)]
    public void Cut_shape_never_eats_content_pixels(string cutStyle)
    {
        const int width = 576;
        const int height = 96;
        const int stride = 72;

        // 내용의 최상단·최하단 행을 전부 검정으로 채워 절취면이 닿는지 확인한다.
        var data = new byte[stride * height];
        for (var byteIndex = 0; byteIndex < stride; byteIndex++)
        {
            data[byteIndex] = 0xFF;
            data[((height - 1) * stride) + byteIndex] = 0xFF;
        }

        var result = Render(WithCut(Profile80, cutStyle), width, height, data);
        var layout = result.Layout;

        using var appearance = SKBitmap.Decode(result.Appearance!.Bytes);
        var black = new SKColor(0, 0, 0);

        foreach (var row in new[] { 0, height - 1 })
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = appearance.GetPixel(Margin + layout.ContentXDots + x, Margin + layout.ContentYDots + row);
                Assert.Equal(black, pixel);
            }
        }
    }

    [Fact]
    public void Cut_styles_produce_visibly_different_paper_shapes()
    {
        var straight = Render(WithCut(Profile80, CutStyles.Straight), 576, 96);
        var tear = Render(WithCut(Profile80, CutStyles.Tear), 576, 96);
        var none = Render(WithCut(Profile80, CutStyles.None), 576, 96);

        Assert.NotEqual(straight.Appearance!.Bytes, tear.Appearance!.Bytes);
        Assert.NotEqual(straight.Appearance.Bytes, none.Appearance!.Bytes);

        using var straightImage = SKBitmap.Decode(straight.Appearance.Bytes);
        using var noneImage = SKBitmap.Decode(none.Appearance.Bytes);

        // straight는 위아래에 바닥이 보이고, none(미절취 롤)은 종이가 캔버스 끝까지 이어진다.
        Assert.True(straightImage.GetPixel(straightImage.Width / 2, 1).Alpha < 200);
        Assert.Equal(255, noneImage.GetPixel(noneImage.Width / 2, 1).Alpha);
    }

    [Fact]
    public void Same_seed_and_revision_give_identical_pixels()
    {
        var data = Pattern(576, 120, 72);

        var first = Render(Profile80, 576, 120, data, seed: 42);
        var second = Render(Profile80, 576, 120, data, seed: 42);
        var other = Render(Profile80, 576, 120, data, seed: 43);

        Assert.Equal(first.Appearance!.Bytes, second.Appearance!.Bytes);
        Assert.Equal(ReceiptAppearanceRenderer.Revision, "a1");
        Assert.NotEqual(first.Appearance.Bytes, other.Appearance!.Bytes);
    }

    [Fact]
    public void Estimated_effects_are_off_by_default()
    {
        var data = Pattern(576, 120, 72);

        var byDefault = Render(Profile80, 576, 120, data);
        var empty = Render(Profile80, 576, 120, data, effects: []);
        var withEffects = Render(Profile80, 576, 120, data,
            effects: [EstimatedEffects.InkBleed, EstimatedEffects.Banding]);

        Assert.Equal(byDefault.Appearance!.Bytes, empty.Appearance!.Bytes);
        Assert.NotEqual(byDefault.Appearance.Bytes, withEffects.Appearance!.Bytes);
    }

    [Fact]
    public void Unknown_estimated_effect_is_rejected()
    {
        var data = Pattern(64, 16, 8);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Render(Profile80, 64, 16, data, effects: ["vintage_coffee_stain"]));

        Assert.Contains("추정 효과", error.Message);
    }

    [Fact]
    public void Outside_the_paper_stays_transparent_so_the_web_supplies_the_surface()
    {
        var result = Render(Profile80, 576, 96);

        using var appearance = SKBitmap.Decode(result.Appearance!.Bytes);

        Assert.Equal(0, appearance.GetPixel(0, 0).Alpha);
        Assert.Equal(0, appearance.GetPixel(appearance.Width - 1, 0).Alpha);
    }

    [Fact]
    public void Appearance_pixel_limit_is_checked_before_drawing()
    {
        const int width = 576;
        const int height = 1788;
        const int stride = 72;

        // paper(640×1876 = 1,200,640)는 통과하지만 appearance(688×1924 = 1,323,712)는 넘는 한도.
        var renderer = CreateRenderer(NewAppearanceRenderer(), new RenderLimits(MaxPixelsPerImage: 1_300_000));

        var error = Assert.Throws<RenderLimitExceededException>(() =>
            renderer.Render(Bitmap(new byte[stride * height], width, height, stride), Profile80));

        Assert.Equal("RENDER_LIMIT_EXCEEDED", error.Code);
        Assert.Equal("appearance", error.Details["kind"]);
        Assert.Equal("pixelsPerImage", error.Details["limit"]);
    }

    [Fact]
    public void Long_strip_and_small_preset_keep_content_inside_the_paper()
    {
        foreach (var (profile, width, height) in new[]
                 {
                     (Profile80, 576, 1788),
                     (Profile58, 384, 420),
                     (Profile80, 576, 48),
                 })
        {
            var stride = (width + 7) / 8;
            var result = Render(profile, width, height, Pattern(width, height, stride));
            var layout = result.Layout;

            Assert.Equal(layout.PaperWidthDots + (Margin * 2), result.Appearance!.WidthPx);
            Assert.Equal(layout.PaperHeightDots + (Margin * 2), result.Appearance.HeightPx);

            using var appearance = SKBitmap.Decode(result.Appearance.Bytes);

            // 내용 네 모서리가 모두 종이 위(불투명)에 있다.
            foreach (var (x, y) in new[]
                     {
                         (0, 0),
                         (width - 1, 0),
                         (0, height - 1),
                         (width - 1, height - 1),
                     })
            {
                var pixel = appearance.GetPixel(Margin + layout.ContentXDots + x, Margin + layout.ContentYDots + y);
                Assert.Equal(255, pixel.Alpha);
            }
        }
    }

    [Fact]
    public void Result_reports_revision_seed_and_effects()
    {
        var backend = new CrossEscPosSkiaAdapter();
        var renderer = new ReceiptAppearanceRenderer(backend);
        var layout = ReceiptLayout.Create(Profile80, 576, 64);

        Assert.True(renderer.IsImplemented);
        Assert.Equal((640 + (Margin * 2), layout.PaperHeightDots + (Margin * 2)), renderer.Measure(layout));

        using var paper = backend.CreateFilled(layout.PaperWidthDots, layout.PaperHeightDots, MonoBitmapDecoder.White);
        var result = renderer.Render(new ReceiptAppearanceRequest(
            layout, paper, CutStyles.Straight, ReceiptAppearanceRenderer.Revision, 7, [EstimatedEffects.Banding]));

        Assert.Equal("a1", result.AppearanceRevision);
        Assert.Equal(7, result.Seed);
        Assert.Equal([EstimatedEffects.Banding], result.EstimatedEffects);
    }

    private static PrinterProfile WithCut(PrinterProfile profile, string cutStyle)
        => profile with { Paper = profile.Paper with { CutStyle = cutStyle } };

    private static ReceiptRenderResult Render(
        PrinterProfile profile,
        int width,
        int height,
        byte[]? data = null,
        long seed = 20260831,
        IReadOnlyList<string>? effects = null)
    {
        var stride = (width + 7) / 8;
        var payload = data ?? new byte[stride * height];
        return CreateRenderer(NewAppearanceRenderer())
            .Render(Bitmap(payload, width, height, stride), profile, seed, effects);
    }

    private static ReceiptAppearanceRenderer NewAppearanceRenderer()
        => new(new CrossEscPosSkiaAdapter());

    private static ReceiptRenderer CreateRenderer(IReceiptAppearanceRenderer appearance, RenderLimits? limits = null)
    {
        var backend = new CrossEscPosSkiaAdapter();
        return new ReceiptRenderer(
            backend,
            new ReceiptPaperComposer(backend),
            appearance,
            limits ?? RenderLimits.Default);
    }

    private static DecodedBitmap Bitmap(byte[] data, int width, int height, int stride)
        => new(width, height, stride, data, PrintJobRequestValidator.ComputeDigest(data, width, height));

    private static byte[] Pattern(int width, int height, int stride)
    {
        var data = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var byteIndex = 0; byteIndex < stride; byteIndex++)
            {
                data[(y * stride) + byteIndex] = (byte)((y / 4 + byteIndex) % 3 == 0 ? 0b1100_1100 : 0b0011_0011);
            }
        }

        var usedBits = width % 8;
        if (usedBits != 0)
        {
            var mask = (byte)~(0xFF >> usedBits);
            for (var y = 0; y < height; y++)
            {
                data[(y * stride) + stride - 1] &= mask;
            }
        }

        return data;
    }

    private static PrinterProfile LoadPreset(string profileId)
        => PrinterProfileStore.LoadPresets(Path.Combine(AppContext.BaseDirectory, "Profiles"))
            .Single(p => p.ProfileId == profileId);
}
