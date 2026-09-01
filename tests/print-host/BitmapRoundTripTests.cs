using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;
using YsFourcut.Host.Contracts;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Rendering;

namespace YsFourcut.Host.Tests;

/// <summary>
/// 웹(packages/imaging)이 pack한 인공 fixture를 실제 검증기·03번 렌더러로 복원해 픽셀을 비교한다.
/// fixture는 `YSFOURCUT_UPDATE_FIXTURES=1 npm run test --workspace packages/imaging`로 만든다.
/// 개인 사진이 아니라 코드로 만든 패턴만 쓴다.
/// </summary>
public sealed class BitmapRoundTripTests
{
    private static readonly PrinterProfile Profile80 = LoadPreset("virtual-80mm-8dpmm");
    private static readonly IReadOnlyList<WebBitmapFixture> Fixtures = LoadFixtures();

    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in Fixtures)
        {
            data.Add(fixture.Name);
        }

        return data;
    }

    [Fact]
    public void Fixture_file_covers_the_required_cases()
    {
        Assert.Equal(
            new[]
            {
                "all-black-16x8",
                "all-white-16x8",
                "gray-50-floyd-13x9",
                "gray-gradient-ordered-17x12",
                "transparent-background-12x6",
                "odd-width-9x5-directional",
                "strip-mini-96",
            },
            Fixtures.Select(fixture => fixture.Name).ToArray());
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Web_bitmap_passes_the_real_validator(string name)
    {
        var fixture = Find(name);

        var accepted = PrintJobRequestValidator.TryValidateBitmap(
            fixture.ToContract(), Profile80, ServiceLimits.Default, out var decoded, out var failure);

        Assert.True(accepted, failure?.Detail.Message);
        Assert.NotNull(decoded);
        Assert.Equal(fixture.WidthDots, decoded!.WidthDots);
        Assert.Equal(fixture.HeightDots, decoded.HeightDots);
        Assert.Equal((fixture.WidthDots + 7) / 8, decoded.StrideBytes);
        Assert.Equal(decoded.StrideBytes * decoded.HeightDots, decoded.Data.Length);
    }

    /// <summary>
    /// AC-09-04: pack한 비트를 03번 렌더러로 복원해 전 픽셀을 비교한다.
    /// msb-first · 1=검정 · stride · 상하 방향이 하나라도 다르면 실패한다.
    /// </summary>
    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Content_png_matches_the_web_pixels(string name)
    {
        var fixture = Find(name);
        var result = Render(fixture);

        Assert.Equal(fixture.WidthDots, result.Content.WidthPx);
        Assert.Equal(fixture.HeightDots, result.Content.HeightPx);

        using var decoded = SKBitmap.Decode(result.Content.Bytes);
        Assert.Equal(fixture.WidthDots, decoded.Width);
        Assert.Equal(fixture.HeightDots, decoded.Height);

        var black = new SKColor(0, 0, 0);
        var white = new SKColor(255, 255, 255);

        for (var y = 0; y < fixture.HeightDots; y++)
        {
            var row = fixture.ExpectedRows[y];
            Assert.Equal(fixture.WidthDots, row.Length);

            for (var x = 0; x < fixture.WidthDots; x++)
            {
                var expected = row[x] == '#' ? black : white;
                Assert.True(
                    decoded.GetPixel(x, y) == expected,
                    $"{fixture.Name} ({x},{y}): {decoded.GetPixel(x, y)} (기대 {expected})");
            }
        }
    }

    /// <summary>흰 padding·상하 방향은 지면 PNG에서도 유지된다.</summary>
    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Paper_png_keeps_the_content_at_the_origin_and_white_padding(string name)
    {
        var fixture = Find(name);
        var result = Render(fixture);
        var layout = result.Layout;

        using var paper = SKBitmap.Decode(result.Paper.Bytes);
        using var content = SKBitmap.Decode(result.Content.Bytes);

        var white = new SKColor(255, 255, 255);

        for (var y = 0; y < paper.Height; y++)
        {
            for (var x = 0; x < paper.Width; x++)
            {
                var insideContent =
                    x >= layout.ContentXDots && x < layout.ContentXDots + layout.ContentWidthDots &&
                    y >= layout.ContentYDots && y < layout.ContentYDots + layout.ContentHeightDots;

                var actual = paper.GetPixel(x, y);
                if (insideContent)
                {
                    Assert.Equal(content.GetPixel(x - layout.ContentXDots, y - layout.ContentYDots), actual);
                }
                else
                {
                    Assert.Equal(white, actual);
                }
            }
        }
    }

    /// <summary>행 우측의 남는 비트는 흰색 0이어야 하고, 그 열은 지면에서도 흰색으로 남는다.</summary>
    [Theory]
    [InlineData("gray-50-floyd-13x9")]
    [InlineData("gray-gradient-ordered-17x12")]
    [InlineData("odd-width-9x5-directional")]
    public void Odd_width_padding_bits_are_white(string name)
    {
        var fixture = Find(name);
        var data = Convert.FromBase64String(fixture.DataBase64);
        var stride = (fixture.WidthDots + 7) / 8;
        var usedBits = fixture.WidthDots % 8;

        Assert.NotEqual(0, usedBits);
        var paddingMask = (byte)(0xFF >> usedBits);

        for (var y = 0; y < fixture.HeightDots; y++)
        {
            Assert.Equal(0, data[(y * stride) + stride - 1] & paddingMask);
        }
    }

    /// <summary>첫 행이 검정, 마지막 행이 흰색인 fixture로 상하 방향을 확정한다.</summary>
    [Fact]
    public void Top_down_direction_is_preserved()
    {
        var fixture = Find("odd-width-9x5-directional");
        Assert.Equal("#########", fixture.ExpectedRows[0]);
        Assert.Equal(".........", fixture.ExpectedRows[4]);

        var result = Render(fixture);
        using var content = SKBitmap.Decode(result.Content.Bytes);

        for (var x = 0; x < fixture.WidthDots; x++)
        {
            Assert.Equal(new SKColor(0, 0, 0), content.GetPixel(x, 0));
            Assert.Equal(new SKColor(255, 255, 255), content.GetPixel(x, fixture.HeightDots - 1));
        }

        // x=0은 0x80, x=7은 0x01, 마지막 열은 다음 바이트의 최상위 비트다.
        Assert.Equal(new SKColor(0, 0, 0), content.GetPixel(0, 1));
        Assert.Equal(new SKColor(0, 0, 0), content.GetPixel(7, 2));
        Assert.Equal(new SKColor(0, 0, 0), content.GetPixel(8, 3));
    }

    /// <summary>투명 배경은 흰 종이로 확정된다(감열지에는 투명이 없다).</summary>
    [Fact]
    public void Transparent_source_becomes_white_paper()
    {
        var fixture = Find("transparent-background-12x6");
        var result = Render(fixture);
        using var content = SKBitmap.Decode(result.Content.Bytes);

        for (var y = 0; y < fixture.HeightDots; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                Assert.Equal(new SKColor(255, 255, 255), content.GetPixel(x, y));
            }

            for (var x = 6; x < fixture.WidthDots; x++)
            {
                Assert.Equal(new SKColor(0, 0, 0), content.GetPixel(x, y));
            }
        }
    }

    private static WebBitmapFixture Find(string name)
        => Fixtures.Single(fixture => fixture.Name == name);

    private static ReceiptRenderResult Render(WebBitmapFixture fixture)
    {
        var accepted = PrintJobRequestValidator.TryValidateBitmap(
            fixture.ToContract(), Profile80, ServiceLimits.Default, out var decoded, out var failure);

        Assert.True(accepted, failure?.Detail.Message);

        var backend = new CrossEscPosSkiaAdapter();
        var renderer = new ReceiptRenderer(
            backend,
            new ReceiptPaperComposer(backend),
            new UnimplementedAppearanceRenderer(),
            RenderLimits.Default);

        return renderer.Render(decoded!, Profile80);
    }

    private static IReadOnlyList<WebBitmapFixture> LoadFixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "web-bitmap-fixtures.json");
        var document = JsonSerializer.Deserialize<WebBitmapFixtureFile>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"fixture 파일을 읽지 못했습니다: {path}");

        return document.Fixtures;
    }

    private static PrinterProfile LoadPreset(string profileId)
        => PrinterProfileStore.LoadPresets(Path.Combine(AppContext.BaseDirectory, "Profiles"))
            .Single(profile => profile.ProfileId == profileId);

    private sealed record WebBitmapFixtureFile(
        [property: JsonPropertyName("fixtures")] IReadOnlyList<WebBitmapFixture> Fixtures);

    private sealed record WebBitmapFixture(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("widthDots")] int WidthDots,
        [property: JsonPropertyName("heightDots")] int HeightDots,
        [property: JsonPropertyName("strideBytes")] int StrideBytes,
        [property: JsonPropertyName("bitOrder")] string BitOrder,
        [property: JsonPropertyName("blackBit")] int BlackBit,
        [property: JsonPropertyName("dataBase64")] string DataBase64,
        [property: JsonPropertyName("expectedRows")] IReadOnlyList<string> ExpectedRows)
    {
        public PrintJobBitmap ToContract()
            => new(WidthDots, HeightDots, StrideBytes, BitOrder, BlackBit, DataBase64);
    }
}
