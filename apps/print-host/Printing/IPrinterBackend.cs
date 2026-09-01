using YsFourcut.Host.Jobs;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Rendering;

namespace YsFourcut.Host.Printing;

/// <summary>가상/실물이 공유하는 출력 백엔드 경계. 렌더러와 실물 어댑터가 서로 자동 대체되지 않는다.</summary>
public interface IPrinterBackend
{
    PrinterMode Mode { get; }

    Task<PrinterBackendResult> PrintAsync(PrinterBackendRequest request, CancellationToken cancellationToken);
}

public sealed record PrinterBackendRequest(
    string ClientJobId,
    PrinterProfile Profile,
    DecodedBitmap Bitmap,
    long AppearanceSeed,
    IReadOnlyList<string> EstimatedEffects,
    Virtual.VirtualFault? InjectedFault = null);

/// <summary>
/// 가상 출력의 결과. 실물 여부와 spool ID는 가상에서 각각 false/null이다.
/// 실패는 부분 결과를 성공으로 표시하지 않고 코드와 함께 돌려준다.
/// </summary>
public sealed record PrinterBackendResult(
    bool Succeeded,
    ReceiptLayout? Layout,
    IReadOnlyList<RenderedPng> Artifacts,
    string? AppearanceRevision,
    long AppearanceSeed,
    IReadOnlyList<string> EstimatedEffects,
    string? FailureCode,
    string? FailureMessage,
    /// <summary>실패했지만 진단용 부분 이미지가 있는 경우. 정상 결과로 표시하지 않는다.</summary>
    bool IsPartial = false)
{
    public static PrinterBackendResult Failure(string code, string message, long seed)
        => new(false, null, [], null, seed, [], code, message);

    public static PrinterBackendResult PartialFailure(
        string code,
        string message,
        long seed,
        IReadOnlyList<RenderedPng> artifacts)
        => new(false, null, artifacts, null, seed, [], code, message, IsPartial: true);
}
