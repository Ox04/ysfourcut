namespace YsFourcut.Host.Contracts;

/// <summary>모든 요청·응답이 공유하는 스키마 버전.</summary>
public static class ApiSchema
{
    public const int Version = 1;
}

// ── 오류 ──────────────────────────────────────────────────────────────────────

public sealed record ApiErrorBody(int SchemaVersion, ApiErrorDetail Error);

public sealed record ApiErrorDetail(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Details = null);

// ── health ────────────────────────────────────────────────────────────────────

/// <summary>인증 없이 조회 가능한 최소 비민감 상태. 파일 경로·사진·인증값을 넣지 않는다.</summary>
public sealed record HealthResponse(
    int SchemaVersion,
    string Status,
    bool Ready,
    string PrinterMode,
    string HostInstanceId,
    string HostVersion,
    string Environment,
    SessionStatus Session);

public sealed record SessionStatus(bool Authenticated);

// ── bootstrap ─────────────────────────────────────────────────────────────────

public sealed record BootstrapRequest(int? SchemaVersion, string? Code);

public sealed record BootstrapResponse(
    int SchemaVersion,
    SessionStatus Session,
    int SessionIdleTimeoutSeconds,
    string HostInstanceId);

// ── 프린터·프로필 ─────────────────────────────────────────────────────────────

/// <summary>
/// 사용 가능한 프린터 목록. <c>OsQueryPerformed</c>는 OS 프린터 큐를 열거했는지 나타내며
/// virtual 모드에서는 항상 false다(Windows 열거 함수를 호출하지 않는다).
/// </summary>
public sealed record PrintersResponse(
    int SchemaVersion,
    string PrinterMode,
    bool OsQueryPerformed,
    IReadOnlyList<PrinterProfileView> Printers);

public sealed record PrinterProfileResponse(
    int SchemaVersion,
    string PrinterMode,
    PrinterProfileView Profile,
    ServiceLimitsView ServiceLimits);

public sealed record PrinterProfileView(
    string ProfileId,
    string Revision,
    string Kind,
    string DisplayName,
    bool HardwareVerified,
    bool IsSelected,
    PaperGeometryView Paper,
    ProfileLimitsView Limits,
    VerificationView Verification,
    string Notes);

public sealed record PaperGeometryView(
    int PaperWidthDots,
    int ContentWidthDots,
    int SideMarginDots,
    int LeadingFeedDots,
    int TrailingFeedDots,
    double DotsPerMmX,
    double DotsPerMmY,
    double DpiX,
    double DpiY,
    double PaperWidthMm,
    double ContentWidthMm,
    string CutStyle);

public sealed record ProfileLimitsView(
    int MaxContentWidthDots,
    int MaxContentHeightDots,
    int MaxDecodedBytes);

public sealed record VerificationView(
    bool Verified,
    string? VerifiedRevision,
    DateTimeOffset? VerifiedAtUtc,
    string? TestJobId);

public sealed record ServiceLimitsView(
    long MaxRequestBodyBytes,
    int MaxWidthDots,
    int MaxHeightDots,
    int MaxDecodedBytes);

/// <summary>
/// 큐·출력 설정 저장. <paramref name="ExpectedRevision"/>은 클라이언트가 마지막으로 본
/// **현재 활성 프로필**의 revision이며, 다르면 409 PROFILE_CHANGED다.
/// </summary>
public sealed record UpdatePrinterProfileRequest(
    int? SchemaVersion,
    string? ProfileId,
    string? ExpectedRevision,
    string? CutStyle);

// ── 출력 작업 (05번에서 접수 구현) ────────────────────────────────────────────

public sealed record PrintJobRequest(
    int? SchemaVersion,
    string? ClientJobId,
    string? ProfileId,
    string? ProfileRevision,
    PrintJobBitmap? Bitmap);

public sealed record PrintJobBitmap(
    int? WidthDots,
    int? HeightDots,
    int? StrideBytes,
    string? BitOrder,
    int? BlackBit,
    string? DataBase64);

/// <summary>
/// 출력 작업 상태. 가상 모드에서는 IsPhysical=false, SpoolJobId=null이며
/// terminal 상태에서 QueueBusy=false다. 실패는 Failure로 분명히 드러낸다.
/// </summary>
public sealed record PrintJobStatusResponse(
    int SchemaVersion,
    string ClientJobId,
    string PrinterMode,
    string State,
    bool IsPhysical,
    string? SpoolJobId,
    bool QueueBusy,
    string ProfileId,
    string ProfileRevision,
    string SourceDigest,
    ReceiptLayoutView? Layout,
    IReadOnlyList<ArtifactView> Artifacts,
    SimulationView? Simulation,
    PrintJobFailureView? Failure,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PrintJobFailureView(string Code, string Message);

/// <summary>
/// 세션의 결과 이미지 정리 응답. 다음 사용자 세션은 <c>RemainingJobs == 0</c>을 확인했거나
/// health의 <c>hostInstanceId</c>가 바뀐(=캐시가 빈 새 호스트) 경우에만 시작한다.
/// </summary>
public sealed record ClearArtifactsResponse(
    int SchemaVersion,
    int ClearedJobs,
    int RemainingJobs,
    string HostInstanceId);

public sealed record ReceiptLayoutView(
    int PaperWidthDots,
    int PaperHeightDots,
    int ContentXDots,
    int ContentYDots,
    int ContentWidthDots,
    int ContentHeightDots,
    int LeadingFeedDots,
    int TrailingFeedDots,
    double DpiX,
    double DpiY);

public sealed record ArtifactView(
    string Kind,
    string Path,
    bool Available,
    bool Complete,
    int? WidthPx,
    int? HeightPx,
    int? ByteLength,
    DateTimeOffset? ExpiresAtUtc);

public sealed record SimulationView(
    string AppearanceRevision,
    long Seed,
    IReadOnlyList<string> EstimatedEffects,
    string? InjectedFault,
    bool IsSimulated);

/// <summary>
/// 가상 모드 전용 실패 주입. 다음 작업 하나에만 적용된다.
/// <c>Fault</c>가 null이면 주입을 해제한다. 실제 장치 상태가 아니라 모의 오류다.
/// </summary>
public sealed record VirtualFaultRequest(
    int? SchemaVersion,
    string? Fault,
    int? DelayMs,
    int? FailAfterRows);

public sealed record VirtualFaultResponse(
    int SchemaVersion,
    string PrinterMode,
    VirtualFaultView? Fault,
    IReadOnlyList<string> AvailableFaults);

/// <summary>주입된 모의 오류. 화면에 항상 표시한다.</summary>
public sealed record VirtualFaultView(string Kind, int DelayMs, int? FailAfterRows, bool IsSimulated = true);
