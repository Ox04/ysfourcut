using System.Buffers.Binary;
using YsFourcut.Host.Contracts;

namespace YsFourcut.Host.Rendering;

/// <summary>
/// 서비스 초기 안전 한도 (VIRTUAL_PRINTER_DESIGN.md 6절). 장치 사양이 아니다.
/// 한도 초과는 오류로 처리하고 잘라내기·자동 축소로 숨기지 않는다.
/// </summary>
public sealed record RenderLimits(
    int MaxPixelsPerImage = 8_000_000,
    int MaxEncodedBytesPerJob = 32 * 1024 * 1024)
{
    public static RenderLimits Default { get; } = new();
}

public sealed class RenderLimitExceededException(string message, IReadOnlyDictionary<string, object?> details)
    : Exception(message)
{
    public string Code => ErrorCodes.RenderLimitExceeded;

    public IReadOnlyDictionary<string, object?> Details { get; } = details;
}

/// <summary>인코딩 결과가 실제로 기대한 크기의 PNG인지 헤더(IHDR)로 확인한다.</summary>
internal static class PngHeader
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool TryReadSize(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (png.Length < 24 || !png[..8].SequenceEqual(Signature) ||
            !png.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        return width > 0 && height > 0;
    }
}
