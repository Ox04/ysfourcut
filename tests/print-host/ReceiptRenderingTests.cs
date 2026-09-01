using SkiaSharp;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Rendering;

namespace YsFourcut.Host.Tests;

/// <summary>
/// 실제 Linux .NET + Skia에서 PNG를 만들고, PNG를 다시 디코딩해 픽셀을 비교한다.
/// PNG 압축 바이트의 동일성이 아니라 디코딩한 픽셀/치수를 비교한다.
/// </summary>
public sealed class ReceiptRenderingTests
{
    private static readonly PrinterProfile Profile80 = LoadPreset("virtual-80mm-8dpmm");
    private static readonly PrinterProfile Profile58 = LoadPreset("virtual-58mm-8dpmm");

    [Fact]
    public void Content_png_pixels_match_input_bits_exactly()
    {
        // 0x80(맨 왼쪽)과 0x01(8번째) 방향, 홀수 폭, 마지막 행을 모두 포함한 고정 패턴.
        var width = 9;
        var height = 4;
        var stride = 2;
        var data = new byte[]
        {
            0b1000_0000, 0b0000_0000, // 행 0: x=0만 검정
            0b0000_0001, 0b0000_0000, // 행 1: x=7만 검정
            0b0000_0000, 0b1000_0000, // 행 2: x=8(마지막 열)만 검정
            0b1010_1010, 0b1000_0000, // 행 3(마지막 행): 0,2,4,6,8 검정
        };

        var result = Render(data, width, height, stride, Profile80);

        Assert.Equal(width, result.Content.WidthPx);
        Assert.Equal(height, result.Content.HeightPx);

        using var decoded = SKBitmap.Decode(result.Content.Bytes);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var expectedBlack = MonoBitmapDecoder.IsBlack(data, stride, x, y);
                AssertPixel(decoded, x, y, expectedBlack, $"content ({x},{y})");
            }
        }
    }

    [Fact]
    public void Content_png_round_trips_a_full_byte_width_pattern()
    {
        const int width = 16;
        const int height = 3;
        const int stride = 2;
        var data = new byte[] { 0xFF, 0x00, 0x00, 0xFF, 0x81, 0x81 };

        var result = Render(data, width, height, stride, Profile80);
        using var decoded = SKBitmap.Decode(result.Content.Bytes);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                AssertPixel(decoded, x, y, MonoBitmapDecoder.IsBlack(data, stride, x, y), $"({x},{y})");
            }
        }
    }

    [Fact]
    public void Paper_crop_equals_content_and_margins_stay_white()
    {
        const int width = 100; // 홀수 폭이 아닌 8의 배수도 아닌 값
        const int height = 12;
        var stride = (width + 7) / 8;
        var data = CheckerPattern(width, height, stride);

        var result = Render(data, width, height, stride, Profile80);
        var layout = result.Layout;

        using var paper = SKBitmap.Decode(result.Paper.Bytes);
        using var content = SKBitmap.Decode(result.Content.Bytes);

        Assert.Equal(layout.PaperWidthDots, paper.Width);
        Assert.Equal(layout.PaperHeightDots, paper.Height);

        // 1) contentRect crop이 content와 정확히 같다.
        for (var y = 0; y < content.Height; y++)
        {
            for (var x = 0; x < content.Width; x++)
            {
                Assert.Equal(
                    content.GetPixel(x, y),
                    paper.GetPixel(layout.ContentXDots + x, layout.ContentYDots + y));
            }
        }

        // 2) 내용 영역 밖은 전부 순백이다(여백·이송 중복 없음).
        var white = new SKColor(255, 255, 255);
        for (var y = 0; y < paper.Height; y++)
        {
            for (var x = 0; x < paper.Width; x++)
            {
                var insideContent =
                    x >= layout.ContentXDots && x < layout.ContentXDots + layout.ContentWidthDots &&
                    y >= layout.ContentYDots && y < layout.ContentYDots + layout.ContentHeightDots;

                if (!insideContent)
                {
                    Assert.Equal(white, paper.GetPixel(x, y));
                }
            }
        }
    }

    [Fact]
    public void Eighty_millimetre_preset_matches_the_design_example()
    {
        // SERVICE_DESIGN.md 5절 / VIRTUAL_PRINTER_DESIGN.md 5절의 계산 예시.
        const int width = 576;
        const int height = 1788;
        const int stride = 72;

        var result = Render(new byte[stride * height], width, height, stride, Profile80);
        var layout = result.Layout;

        Assert.Equal(640, layout.PaperWidthDots);
        Assert.Equal(1876, layout.PaperHeightDots);
        Assert.Equal(32, layout.ContentXDots);
        Assert.Equal(24, layout.ContentYDots);
        Assert.Equal(80, layout.PaperWidthMm, 3);
        Assert.Equal(234.5, layout.PaperHeightMm, 3);
        Assert.Equal(203.2, layout.DpiX, 3);

        Assert.Equal(576, result.Content.WidthPx);
        Assert.Equal(1788, result.Content.HeightPx);
        Assert.Equal(640, result.Paper.WidthPx);
        Assert.Equal(1876, result.Paper.HeightPx);
    }

    [Fact]
    public void Fifty_eight_millimetre_preset_uses_its_own_numbers()
    {
        const int width = 384;
        const int height = 200;
        const int stride = 48;

        var result = Render(new byte[stride * height], width, height, stride, Profile58);
        var layout = result.Layout;

        Assert.Equal(464, layout.PaperWidthDots);
        Assert.Equal(24 + 200 + 64, layout.PaperHeightDots);
        Assert.Equal(40, layout.ContentXDots);
        Assert.Equal(24, layout.ContentYDots);
        Assert.Equal(58, layout.PaperWidthMm, 3);
    }

    [Fact]
    public void Same_input_renders_the_same_pixels_and_sizes()
    {
        const int width = 64;
        const int height = 20;
        const int stride = 8;
        var data = CheckerPattern(width, height, stride);

        var first = Render(data, width, height, stride, Profile80);
        var second = Render(data, width, height, stride, Profile80);

        Assert.Equal(first.Paper.WidthPx, second.Paper.WidthPx);
        Assert.Equal(first.Paper.HeightPx, second.Paper.HeightPx);

        using var a = SKBitmap.Decode(first.Paper.Bytes);
        using var b = SKBitmap.Decode(second.Paper.Bytes);

        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                Assert.Equal(a.GetPixel(x, y), b.GetPixel(x, y));
            }
        }
    }

    /// <summary>04번 이전의 대체 구현. 외관 렌더러가 없으면 미제공으로 표시하고 가짜 이미지를 만들지 않는다.</summary>
    [Fact]
    public void Fallback_renderer_reports_appearance_as_unavailable()
    {
        var result = Render(new byte[8], 8, 8, 1, Profile80);

        Assert.Null(result.Appearance);
        Assert.Equal(UnimplementedAppearanceRenderer.NotImplementedReason, result.AppearanceUnavailableReason);
        Assert.Equal(2, result.Artifacts.Count);
        Assert.DoesNotContain(result.Artifacts, a => a.Kind == ArtifactKinds.Appearance);

        Assert.Throws<NotImplementedException>(() => new UnimplementedAppearanceRenderer().Render(
            new ReceiptAppearanceRequest(result.Layout, null!, "straight", "a1", 0, [])));
    }

    [Fact]
    public void Result_carries_source_digest_and_encoded_sizes()
    {
        const int width = 32;
        const int height = 10;
        const int stride = 4;
        var data = CheckerPattern(width, height, stride);
        var digest = PrintJobRequestValidator.ComputeDigest(data, width, height);

        var result = Render(data, width, height, stride, Profile80);

        Assert.Equal(digest, result.SourceDigest);
        Assert.True(result.Content.ByteLength > 0);
        Assert.True(result.Paper.ByteLength > 0);
        Assert.Equal(result.Content.ByteLength + result.Paper.ByteLength, result.TotalEncodedBytes);
    }

    [Fact]
    public void Refuses_content_wider_than_printable_width()
    {
        // 프로필 인쇄 가능 폭(576dot)을 넘는 배치는 만들지 않는다.
        Assert.Throws<ArgumentOutOfRangeException>(() => ReceiptLayout.Create(Profile80, 600, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReceiptLayout.Create(Profile80, 576, 0));
    }

    [Fact]
    public void Enforces_pixel_limit_before_rendering()
    {
        const int width = 576;
        const int height = 1788;
        const int stride = 72;
        var renderer = CreateRenderer(new RenderLimits(MaxPixelsPerImage: 1_000_000));

        var error = Assert.Throws<RenderLimitExceededException>(() => renderer.Render(
            Bitmap(new byte[stride * height], width, height, stride), Profile80));

        Assert.Equal("RENDER_LIMIT_EXCEEDED", error.Code);
        Assert.Equal("pixelsPerImage", error.Details["limit"]);
    }

    [Fact]
    public void Enforces_encoded_bytes_limit_per_job()
    {
        const int width = 576;
        const int height = 400;
        var stride = 72;
        var renderer = CreateRenderer(new RenderLimits(MaxEncodedBytesPerJob: 32));

        var error = Assert.Throws<RenderLimitExceededException>(() => renderer.Render(
            Bitmap(CheckerPattern(width, height, stride), width, height, stride), Profile80));

        Assert.Equal("encodedBytesPerJob", error.Details["limit"]);
    }

    [Fact]
    public void Narrower_content_is_left_aligned_at_the_printable_origin()
    {
        const int width = 64;
        const int height = 4;
        const int stride = 8;
        var data = new byte[stride * height];
        data[0] = 0b1000_0000; // (0,0)만 검정

        var result = Render(data, width, height, stride, Profile80);

        Assert.Equal(32, result.Layout.ContentXDots);
        using var paper = SKBitmap.Decode(result.Paper.Bytes);
        AssertPixel(paper, 32, 24, expectedBlack: true, "내용 원점");
        AssertPixel(paper, 31, 24, expectedBlack: false, "원점 왼쪽 여백");
    }

    private static void AssertPixel(SKBitmap bitmap, int x, int y, bool expectedBlack, string because)
    {
        var pixel = bitmap.GetPixel(x, y);
        var expected = expectedBlack ? new SKColor(0, 0, 0) : new SKColor(255, 255, 255);
        Assert.True(pixel == expected, $"{because}: {pixel} (기대 {expected})");
    }

    private static byte[] CheckerPattern(int width, int height, int stride)
    {
        var data = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var byteIndex = 0; byteIndex < stride; byteIndex++)
            {
                data[(y * stride) + byteIndex] = (byte)((y + byteIndex) % 2 == 0 ? 0b1010_1010 : 0b0101_0101);
            }
        }

        // 행 우측 남는 비트는 흰색이어야 한다(검증기 계약과 동일하게 유지).
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

    private static DecodedBitmap Bitmap(byte[] data, int width, int height, int stride)
        => new(width, height, stride, data, PrintJobRequestValidator.ComputeDigest(data, width, height));

    private static ReceiptRenderResult Render(byte[] data, int width, int height, int stride, PrinterProfile profile)
        => CreateRenderer(RenderLimits.Default).Render(Bitmap(data, width, height, stride), profile);

    private static ReceiptRenderer CreateRenderer(RenderLimits limits)
    {
        var backend = new CrossEscPosSkiaAdapter();
        return new ReceiptRenderer(
            backend,
            new ReceiptPaperComposer(backend),
            new UnimplementedAppearanceRenderer(),
            limits);
    }

    private static PrinterProfile LoadPreset(string profileId)
        => PrinterProfileStore.LoadPresets(Path.Combine(AppContext.BaseDirectory, "Profiles"))
            .Single(p => p.ProfileId == profileId);
}
