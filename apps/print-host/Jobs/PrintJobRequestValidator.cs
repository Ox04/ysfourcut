using System.Security.Cryptography;
using YsFourcut.Host.Contracts;
using YsFourcut.Host.Printing;
using YsFourcut.Host.Profiles;

namespace YsFourcut.Host.Jobs;

/// <summary>
/// 서비스 안전 기본값. AHAPOS의 장치 사양이 아니다 (SERVICE_DESIGN.md 8절).
/// </summary>
public sealed record ServiceLimits(
    long MaxRequestBodyBytes = 1024 * 1024,
    int MaxWidthDots = 1024,
    int MaxHeightDots = 4096,
    int MaxDecodedBytes = 512 * 1024)
{
    public static ServiceLimits Default { get; } = new();
}

/// <summary>검증을 통과한 1비트 비트맵. 픽셀 데이터는 로그·응답에 넣지 않는다.</summary>
public sealed record DecodedBitmap(
    int WidthDots,
    int HeightDots,
    int StrideBytes,
    byte[] Data,
    string SourceDigest);

public sealed record ValidationFailure(int StatusCode, ApiErrorDetail Detail);

/// <summary>
/// `POST /api/print-jobs` 요청 검증. 05번의 접수/큐와 분리해 두어 단위 테스트로 확인한다.
/// 비트 해석 계약: msb-first, blackBit=1, 위에서 아래로, 행 우측 남는 비트는 흰색 0.
/// </summary>
public static class PrintJobRequestValidator
{
    public const string ExpectedBitOrder = "msb-first";
    public const int ExpectedBlackBit = 1;

    public static bool TryValidateEnvelope(PrintJobRequest? request, out ValidationFailure? failure)
    {
        if (request is null)
        {
            failure = Invalid(StatusCodes.Status400BadRequest, ErrorCodes.MalformedJson, "요청 본문이 비어 있습니다.");
            return false;
        }

        if (request.SchemaVersion != ApiSchema.Version)
        {
            failure = Invalid(
                StatusCodes.Status400BadRequest,
                ErrorCodes.SchemaVersionUnsupported,
                $"지원하는 schemaVersion은 {ApiSchema.Version}입니다.",
                new Dictionary<string, object?>
                {
                    ["expected"] = ApiSchema.Version,
                    ["received"] = request.SchemaVersion,
                });
            return false;
        }

        // 클릭 한 번에 만든 UUID여야 중복 접수를 막을 수 있다. 하이픈 표준 형식만 받는다.
        if (string.IsNullOrWhiteSpace(request.ClientJobId) ||
            !Guid.TryParseExact(request.ClientJobId, "D", out _))
        {
            failure = Invalid(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationFailed,
                "clientJobId는 하이픈이 있는 표준 UUID 문자열이어야 합니다.",
                new Dictionary<string, object?> { ["field"] = "clientJobId" });
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.ProfileId) || string.IsNullOrWhiteSpace(request.ProfileRevision))
        {
            failure = Invalid(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationFailed,
                "profileId와 profileRevision이 필요합니다.",
                new Dictionary<string, object?> { ["field"] = "profileId|profileRevision" });
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>서버 모드와 프로필 종류/검증 상태가 맞는지 확인한다. 자동으로 다른 모드로 바꾸지 않는다.</summary>
    public static bool TryValidateProfileCompatibility(
        PrinterMode mode,
        PrinterProfile profile,
        ProfileVerification verification,
        out ValidationFailure? failure)
    {
        var expectedKind = mode == PrinterMode.Physical ? ProfileKinds.Hardware : ProfileKinds.Virtual;
        if (!string.Equals(profile.Kind, expectedKind, StringComparison.Ordinal))
        {
            failure = Invalid(
                StatusCodes.Status409Conflict,
                ErrorCodes.ProfileKindMismatch,
                $"서버가 {mode.ToApiValue()} 모드이므로 kind={expectedKind} 프로필만 사용할 수 있습니다.",
                new Dictionary<string, object?>
                {
                    ["printerMode"] = mode.ToApiValue(),
                    ["profileKind"] = profile.Kind,
                });
            return false;
        }

        if (mode == PrinterMode.Physical && !verification.Verified)
        {
            failure = Invalid(
                StatusCodes.Status409Conflict,
                ErrorCodes.ProfileUnverified,
                "실물 출력은 현재 revision의 종이 검수를 마친 프로필에서만 할 수 있습니다.",
                new Dictionary<string, object?> { ["revision"] = profile.Revision });
            return false;
        }

        failure = null;
        return true;
    }

    public static bool TryValidateBitmap(
        PrintJobBitmap? bitmap,
        PrinterProfile profile,
        ServiceLimits limits,
        out DecodedBitmap? decoded,
        out ValidationFailure? failure)
    {
        decoded = null;

        if (bitmap is null)
        {
            failure = InvalidBitmap("missing_bitmap", "bitmap 필드가 없습니다.");
            return false;
        }

        if (bitmap.WidthDots is not { } width || bitmap.HeightDots is not { } height ||
            bitmap.StrideBytes is not { } stride || bitmap.BlackBit is not { } blackBit ||
            string.IsNullOrEmpty(bitmap.BitOrder) || bitmap.DataBase64 is null)
        {
            failure = InvalidBitmap("missing_field", "widthDots, heightDots, strideBytes, bitOrder, blackBit, dataBase64가 모두 필요합니다.");
            return false;
        }

        if (!string.Equals(bitmap.BitOrder, ExpectedBitOrder, StringComparison.Ordinal))
        {
            failure = InvalidBitmap("bit_order_unsupported", $"bitOrder는 '{ExpectedBitOrder}'만 지원합니다.",
                new Dictionary<string, object?> { ["received"] = bitmap.BitOrder });
            return false;
        }

        if (blackBit != ExpectedBlackBit)
        {
            failure = InvalidBitmap("black_bit_unsupported", $"blackBit는 {ExpectedBlackBit}만 지원합니다.",
                new Dictionary<string, object?> { ["received"] = blackBit });
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            failure = InvalidBitmap("non_positive_dimension", "widthDots와 heightDots는 양의 정수여야 합니다.");
            return false;
        }

        if (width > limits.MaxWidthDots || height > limits.MaxHeightDots)
        {
            failure = PageSize(
                "서비스 상한을 넘는 이미지 크기입니다.",
                new Dictionary<string, object?>
                {
                    ["limit"] = "service",
                    ["maxWidthDots"] = limits.MaxWidthDots,
                    ["maxHeightDots"] = limits.MaxHeightDots,
                    ["widthDots"] = width,
                    ["heightDots"] = height,
                });
            return false;
        }

        if (width > profile.Limits.MaxContentWidthDots || height > profile.Limits.MaxContentHeightDots)
        {
            failure = PageSize(
                "현재 프로필의 인쇄 가능 범위를 넘는 이미지 크기입니다.",
                new Dictionary<string, object?>
                {
                    ["limit"] = "profile",
                    ["profileId"] = profile.ProfileId,
                    ["maxContentWidthDots"] = profile.Limits.MaxContentWidthDots,
                    ["maxContentHeightDots"] = profile.Limits.MaxContentHeightDots,
                    ["widthDots"] = width,
                    ["heightDots"] = height,
                });
            return false;
        }

        var expectedStride = (width + 7) / 8;
        if (stride != expectedStride)
        {
            failure = InvalidBitmap("stride_mismatch", "strideBytes는 ceil(widthDots / 8)이어야 합니다.",
                new Dictionary<string, object?>
                {
                    ["expectedStrideBytes"] = expectedStride,
                    ["receivedStrideBytes"] = stride,
                });
            return false;
        }

        var expectedBytes = (long)stride * height;
        var maxDecoded = Math.Min(limits.MaxDecodedBytes, profile.Limits.MaxDecodedBytes);
        if (expectedBytes > maxDecoded)
        {
            failure = PageSize(
                "해제된 1비트 데이터가 상한을 넘습니다.",
                new Dictionary<string, object?>
                {
                    ["limit"] = "decodedBytes",
                    ["maxDecodedBytes"] = maxDecoded,
                    ["expectedBytes"] = expectedBytes,
                });
            return false;
        }

        var buffer = new byte[expectedBytes];
        if (!Convert.TryFromBase64String(bitmap.DataBase64, buffer, out var written))
        {
            // 길이가 더 길어서 실패했는지, 형식이 잘못됐는지 구분한다.
            var reason = IsBase64(bitmap.DataBase64) ? "data_length_mismatch" : "base64_invalid";
            failure = InvalidBitmap(
                reason,
                reason == "base64_invalid"
                    ? "dataBase64를 base64로 해석하지 못했습니다."
                    : "해제된 데이터 길이가 strideBytes × heightDots와 다릅니다.",
                new Dictionary<string, object?> { ["expectedBytes"] = expectedBytes });
            return false;
        }

        if (written != expectedBytes)
        {
            failure = InvalidBitmap("data_length_mismatch", "해제된 데이터 길이가 strideBytes × heightDots와 다릅니다.",
                new Dictionary<string, object?>
                {
                    ["expectedBytes"] = expectedBytes,
                    ["actualBytes"] = written,
                });
            return false;
        }

        if (!PaddingBitsAreWhite(buffer, width, stride, height, out var badRow))
        {
            failure = InvalidBitmap("padding_not_white", "행 우측 남는 비트는 흰색(0)이어야 합니다.",
                new Dictionary<string, object?> { ["row"] = badRow });
            return false;
        }

        decoded = new DecodedBitmap(width, height, stride, buffer, ComputeDigest(buffer, width, height));
        failure = null;
        return true;
    }

    /// <summary>내용 비트의 digest. PNG 파일 해시와 혼용하지 않는다.</summary>
    public static string ComputeDigest(byte[] data, int width, int height)
    {
        var header = System.Text.Encoding.ASCII.GetBytes($"ysf1:{width}x{height}:");
        var hash = SHA256.HashData([.. header, .. data]);
        return "sha256-" + Convert.ToHexStringLower(hash);
    }

    private static bool PaddingBitsAreWhite(byte[] data, int width, int stride, int height, out int badRow)
    {
        badRow = -1;
        var usedBitsInLastByte = width % 8;
        if (usedBitsInLastByte == 0)
        {
            return true;
        }

        var paddingMask = (byte)(0xFF >> usedBitsInLastByte);
        for (var row = 0; row < height; row++)
        {
            var lastByte = data[(row * stride) + stride - 1];
            if ((lastByte & paddingMask) != 0)
            {
                badRow = row;
                return false;
            }
        }

        return true;
    }

    private static bool IsBase64(string value)
    {
        var probe = new byte[((value.Length / 4) + 1) * 3];
        return Convert.TryFromBase64String(value, probe, out _);
    }

    private static ValidationFailure Invalid(
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? details = null)
        => new(statusCode, new ApiErrorDetail(code, message, details));

    private static ValidationFailure InvalidBitmap(
        string reason,
        string message,
        Dictionary<string, object?>? details = null)
    {
        var payload = details ?? [];
        payload["reason"] = reason;
        return new ValidationFailure(
            StatusCodes.Status400BadRequest,
            new ApiErrorDetail(ErrorCodes.InvalidBitmap, message, payload));
    }

    private static ValidationFailure PageSize(string message, Dictionary<string, object?> details)
        => new(
            StatusCodes.Status400BadRequest,
            new ApiErrorDetail(ErrorCodes.PageSizeUnsupported, message, details));
}
