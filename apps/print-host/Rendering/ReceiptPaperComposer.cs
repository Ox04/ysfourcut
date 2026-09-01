using CrossEscPos.Graphics;

namespace YsFourcut.Host.Rendering;

/// <summary>
/// 정확한 dot 크기의 순백 캔버스를 만들고 정수 (x, y)에 내용을 1:1로 그린다.
/// 확대/축소 overload(DrawImage(image, rect))를 쓰지 않고, 재디더링·보정도 하지 않는다.
/// 좌우 비인쇄 여백과 앞뒤 이송만 더한다.
/// </summary>
public sealed class ReceiptPaperComposer(IReceiptImageBackend backend)
{
    /// <summary>반환한 이미지는 호출자가 dispose한다.</summary>
    public IReceiptImage Compose(IReceiptImage content, ReceiptLayout layout)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(layout);

        if (content.Width != layout.ContentWidthDots || content.Height != layout.ContentHeightDots)
        {
            throw new InvalidOperationException(
                $"내용 이미지 크기가 배치와 다릅니다: {content.Width}x{content.Height} vs " +
                $"{layout.ContentWidthDots}x{layout.ContentHeightDots}");
        }

        layout.EnsureValid();

        var paper = backend.CreateFilled(layout.PaperWidthDots, layout.PaperHeightDots, MonoBitmapDecoder.White);
        try
        {
            using var canvas = backend.CreateCanvas(paper);
            canvas.DrawImage(content, layout.ContentXDots, layout.ContentYDots);
            canvas.Flush();
        }
        catch
        {
            paper.Dispose();
            throw;
        }

        return paper;
    }
}
