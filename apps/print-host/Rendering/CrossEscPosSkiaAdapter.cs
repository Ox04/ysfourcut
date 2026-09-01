using CrossEscPos.Graphics;
using CrossEscPos.Rendering.Skia;

namespace YsFourcut.Host.Rendering;

/// <summary>
/// 기존 CrossEscPos Skia 렌더링 모듈의 얇은 어댑터.
/// 공개 이미지/캔버스/인코더만 재사용하고 Core의 PaperConfiguration·Receipt·ReceiptBitmapLine·
/// FeedEscPos·로거는 호출하지 않는다(자동 축소·원시 이미지 로그·자동 배율을 피하기 위함).
/// </summary>
public interface IReceiptImageBackend
{
    /// <summary>정확히 width × height개의 픽셀로 이미지를 만든다.</summary>
    IReceiptImage FromPixels(int width, int height, ReceiptColor[] rowMajorPixels);

    IReceiptImage CreateFilled(int width, int height, ReceiptColor fill);

    IReceiptCanvas CreateCanvas(IReceiptImage image);

    byte[] EncodePng(IReceiptImage image);
}

public sealed class CrossEscPosSkiaAdapter : IReceiptImageBackend
{
    private readonly SkiaImageFactory _factory = new();
    private readonly SkiaImageEncoder _encoder = new();

    public IReceiptImage FromPixels(int width, int height, ReceiptColor[] rowMajorPixels)
    {
        ArgumentNullException.ThrowIfNull(rowMajorPixels);

        var expected = (long)width * height;
        if (rowMajorPixels.LongLength != expected)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rowMajorPixels),
                $"픽셀 수가 {expected}개가 아닙니다: {rowMajorPixels.LongLength}");
        }

        return _factory.FromPixels(width, height, rowMajorPixels);
    }

    public IReceiptImage CreateFilled(int width, int height, ReceiptColor fill)
        => _factory.Create(width, height, fill);

    public IReceiptCanvas CreateCanvas(IReceiptImage image)
        => _factory.CreateCanvas(image);

    public byte[] EncodePng(IReceiptImage image)
        => _encoder.EncodePng(image);
}
