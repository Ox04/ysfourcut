using CrossEscPos.Graphics;
using YsFourcut.Host.Jobs;

namespace YsFourcut.Host.Rendering;

/// <summary>
/// 1비트 입력을 픽셀로 푼다. 계약은 msb-first, 1=검정, 위에서 아래로, 왼쪽부터다.
/// 행 우측 남는 비트는 읽지 않는다(검증기가 이미 흰색 0임을 확인한다).
/// 사진을 다시 크롭·보정·디더링하지 않는다.
/// </summary>
public static class MonoBitmapDecoder
{
    public static readonly ReceiptColor Black = new(0, 0, 0);
    public static readonly ReceiptColor White = new(255, 255, 255);

    public static ReceiptColor[] ToRowMajorPixels(DecodedBitmap bitmap)
        => ToRowMajorPixels(bitmap.Data, bitmap.WidthDots, bitmap.HeightDots, bitmap.StrideBytes);

    /// <summary>라이브러리 버퍼에 정확히 width × height개의 픽셀을 전달하기 위한 배열을 만든다.</summary>
    public static ReceiptColor[] ToRowMajorPixels(ReadOnlySpan<byte> data, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "widthDots와 heightDots는 양수여야 합니다.");
        }

        if (stride != (width + 7) / 8)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "strideBytes는 ceil(widthDots / 8)이어야 합니다.");
        }

        if (data.Length != stride * height)
        {
            throw new ArgumentOutOfRangeException(nameof(data), "데이터 길이가 strideBytes × heightDots와 다릅니다.");
        }

        var pixelCount = (long)width * height;
        if (pixelCount > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "픽셀 수가 배열 한도를 넘습니다.");
        }

        var pixels = new ReceiptColor[pixelCount];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * stride;
            var target = y * width;

            for (var x = 0; x < width; x++)
            {
                pixels[target + x] = IsBlackBit(data[rowStart + (x >> 3)], x) ? Black : White;
            }
        }

        return pixels;
    }

    public static bool IsBlack(ReadOnlySpan<byte> data, int stride, int x, int y)
        => IsBlackBit(data[(y * stride) + (x >> 3)], x);

    /// <summary>x=0이 최상위 비트(0x80), x=7이 최하위 비트(0x01)다.</summary>
    private static bool IsBlackBit(byte value, int x)
        => ((value >> (7 - (x & 7))) & 1) == 1;
}
