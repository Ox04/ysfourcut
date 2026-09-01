using CrossEscPos.Graphics;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Profiles;

namespace YsFourcut.Host.Rendering;

public static class ArtifactKinds
{
    public const string Content = "content";
    public const string Paper = "paper";
    public const string Appearance = "appearance";
}

public sealed record RenderedPng(string Kind, byte[] Bytes, int WidthPx, int HeightPx)
{
    public int ByteLength => Bytes.Length;
}

/// <summary>
/// 한 작업의 렌더링 결과. appearance는 04번 전까지 null이며 이유를 함께 남긴다.
/// 부분 결과를 정상 결과처럼 표시하지 않는다.
/// </summary>
public sealed record ReceiptRenderResult(
    ReceiptLayout Layout,
    RenderedPng Content,
    RenderedPng Paper,
    RenderedPng? Appearance,
    string? AppearanceUnavailableReason,
    string SourceDigest)
{
    public int TotalEncodedBytes => Content.ByteLength + Paper.ByteLength + (Appearance?.ByteLength ?? 0);

    public IReadOnlyList<RenderedPng> Artifacts =>
        Appearance is null ? [Content, Paper] : [Content, Paper, Appearance];
}

/// <summary>
/// 입력 1비트 → content PNG → 지면 PNG. (VIRTUAL_PRINTER_DESIGN.md 6절의 3~7단계)
/// 작업 큐·상태·HTTP·artifact 보관은 05번이 담당한다. 여기서는 이미지만 만든다.
/// 모든 이미지/캔버스는 이 메서드 안에서 dispose한다.
/// </summary>
public sealed class ReceiptRenderer(
    IReceiptImageBackend backend,
    ReceiptPaperComposer composer,
    IReceiptAppearanceRenderer appearanceRenderer,
    RenderLimits limits)
{
    /// <summary>
    /// <paramref name="estimatedEffects"/>는 추정 효과(잉크 번짐·밴딩)이며 기본은 비어 있다(꺼짐).
    /// content/paper에는 어떤 경우에도 적용하지 않는다.
    /// </summary>
    public ReceiptRenderResult Render(
        DecodedBitmap bitmap,
        PrinterProfile profile,
        long appearanceSeed = 0,
        IReadOnlyList<string>? estimatedEffects = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(profile);

        var layout = ReceiptLayout.Create(profile, bitmap.WidthDots, bitmap.HeightDots);

        // 인코딩 전에 픽셀 수를 먼저 확인한다. 큰 입력을 그린 뒤에 실패하지 않는다.
        EnsurePixelLimit(ArtifactKinds.Content, layout.ContentPixelCount);
        EnsurePixelLimit(ArtifactKinds.Paper, layout.PaperPixelCount);

        var pixels = MonoBitmapDecoder.ToRowMajorPixels(bitmap);

        using var contentImage = backend.FromPixels(layout.ContentWidthDots, layout.ContentHeightDots, pixels);
        var content = Encode(ArtifactKinds.Content, contentImage, layout.ContentWidthDots, layout.ContentHeightDots);

        using var paperImage = composer.Compose(contentImage, layout);
        var paper = Encode(ArtifactKinds.Paper, paperImage, layout.PaperWidthDots, layout.PaperHeightDots);

        RenderedPng? appearance = null;
        string? appearanceUnavailableReason = UnimplementedAppearanceRenderer.NotImplementedReason;

        if (appearanceRenderer.IsImplemented)
        {
            // 외형 캔버스는 지면보다 크므로 그리기 전에 픽셀 수를 먼저 확인한다.
            var (plannedWidth, plannedHeight) = appearanceRenderer.Measure(layout);
            EnsurePixelLimit(ArtifactKinds.Appearance, (long)plannedWidth * plannedHeight);

            var request = new ReceiptAppearanceRequest(
                Layout: layout,
                Paper: paperImage,
                CutStyle: profile.Paper.CutStyle,
                AppearanceRevision: ReceiptAppearanceRenderer.Revision,
                Seed: appearanceSeed,
                EstimatedEffects: estimatedEffects ?? []);

            var result = appearanceRenderer.Render(request);

            if (result.WidthPx != plannedWidth || result.HeightPx != plannedHeight ||
                !PngHeader.TryReadSize(result.PngBytes, out var pngWidth, out var pngHeight) ||
                pngWidth != plannedWidth || pngHeight != plannedHeight)
            {
                throw new InvalidOperationException(
                    $"appearance PNG 크기가 예고한 캔버스와 다릅니다: 기대 {plannedWidth}x{plannedHeight}");
            }

            appearance = new RenderedPng(ArtifactKinds.Appearance, result.PngBytes, result.WidthPx, result.HeightPx);
            appearanceUnavailableReason = null;
        }

        var rendered = new ReceiptRenderResult(
            layout, content, paper, appearance, appearanceUnavailableReason, bitmap.SourceDigest);

        EnsureEncodedBytesLimit(rendered.TotalEncodedBytes);
        return rendered;
    }

    /// <summary>인코딩한 뒤 PNG 헤더로 실제 크기를 다시 확인한다.</summary>
    private RenderedPng Encode(string kind, IReceiptImage image, int expectedWidth, int expectedHeight)
    {
        if (image.Width != expectedWidth || image.Height != expectedHeight)
        {
            throw new InvalidOperationException(
                $"{kind} 이미지 크기가 배치와 다릅니다: {image.Width}x{image.Height} vs {expectedWidth}x{expectedHeight}");
        }

        var bytes = backend.EncodePng(image);

        if (bytes.Length == 0 || !PngHeader.TryReadSize(bytes, out var width, out var height))
        {
            throw new InvalidOperationException($"{kind} PNG 인코딩 결과가 올바른 PNG가 아닙니다.");
        }

        if (width != expectedWidth || height != expectedHeight)
        {
            throw new InvalidOperationException(
                $"{kind} PNG 크기가 배치와 다릅니다: {width}x{height} vs {expectedWidth}x{expectedHeight}");
        }

        return new RenderedPng(kind, bytes, width, height);
    }

    private void EnsurePixelLimit(string kind, long pixelCount)
    {
        if (pixelCount > limits.MaxPixelsPerImage)
        {
            throw new RenderLimitExceededException(
                $"{kind} 이미지의 픽셀 수가 한도를 넘습니다.",
                new Dictionary<string, object?>
                {
                    ["kind"] = kind,
                    ["limit"] = "pixelsPerImage",
                    ["maxPixels"] = limits.MaxPixelsPerImage,
                    ["pixels"] = pixelCount,
                });
        }
    }

    private void EnsureEncodedBytesLimit(int totalBytes)
    {
        if (totalBytes > limits.MaxEncodedBytesPerJob)
        {
            throw new RenderLimitExceededException(
                "작업의 PNG 합계 크기가 한도를 넘습니다.",
                new Dictionary<string, object?>
                {
                    ["limit"] = "encodedBytesPerJob",
                    ["maxEncodedBytes"] = limits.MaxEncodedBytesPerJob,
                    ["encodedBytes"] = totalBytes,
                });
        }
    }
}
