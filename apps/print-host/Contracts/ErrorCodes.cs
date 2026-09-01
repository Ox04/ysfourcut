namespace YsFourcut.Host.Contracts;

/// <summary>
/// 구조화된 오류 코드. 웹 UI가 코드별 한국어 안내를 붙인다.
/// 원본 사진·비트맵·인증값은 어떤 오류 응답에도 넣지 않는다.
/// TypeScript 쪽 동일 목록은 apps/web/src/api/contract.ts에 있다.
/// </summary>
public static class ErrorCodes
{
    // 전송·요청 형식
    public const string HostNotAllowed = "HOST_NOT_ALLOWED";
    public const string OriginNotAllowed = "ORIGIN_NOT_ALLOWED";
    public const string ClientHeaderRequired = "CLIENT_HEADER_REQUIRED";
    public const string ContentTypeUnsupported = "CONTENT_TYPE_UNSUPPORTED";
    public const string BodyTooLarge = "BODY_TOO_LARGE";
    public const string MalformedJson = "MALFORMED_JSON";
    public const string SchemaVersionUnsupported = "SCHEMA_VERSION_UNSUPPORTED";
    public const string ValidationFailed = "VALIDATION_FAILED";

    // 인증·세션
    public const string SessionRequired = "SESSION_REQUIRED";
    public const string SessionInvalid = "SESSION_INVALID";
    public const string BootstrapCodeInvalid = "BOOTSTRAP_CODE_INVALID";
    public const string BootstrapCodeExpired = "BOOTSTRAP_CODE_EXPIRED";
    public const string BootstrapCodeAlreadyUsed = "BOOTSTRAP_CODE_ALREADY_USED";

    // 프린터·프로필
    public const string PrinterNotFound = "PRINTER_NOT_FOUND";
    public const string ProfileChanged = "PROFILE_CHANGED";
    public const string ProfileUnverified = "PROFILE_UNVERIFIED";
    public const string ProfileKindMismatch = "PROFILE_KIND_MISMATCH";
    public const string PlatformUnsupported = "PLATFORM_UNSUPPORTED";

    // 비트맵·작업
    public const string InvalidBitmap = "INVALID_BITMAP";
    public const string PageSizeUnsupported = "PAGE_SIZE_UNSUPPORTED";
    public const string JobIdConflict = "JOB_ID_CONFLICT";
    public const string PrinterBusy = "PRINTER_BUSY";
    public const string PrintOutcomeUnknown = "PRINT_OUTCOME_UNKNOWN";

    // 렌더링
    public const string RenderLimitExceeded = "RENDER_LIMIT_EXCEEDED";
    public const string RenderTimeout = "RENDER_TIMEOUT";
    public const string VirtualRenderFailed = "VIRTUAL_RENDER_FAILED";
    public const string JobRecordFailed = "JOB_RECORD_FAILED";
    public const string HostRestarted = "HOST_RESTARTED";

    // 가상 모드의 모의 장치 오류. 실제 센서 감지가 아니다.
    public const string VirtualOutOfPaper = "VIRTUAL_OUT_OF_PAPER";
    public const string VirtualCoverOpen = "VIRTUAL_COVER_OPEN";
    public const string VirtualPrinterOffline = "VIRTUAL_PRINTER_OFFLINE";

    /// <summary>가상 실패는 종이·OS 큐 확인이 필요 없다.</summary>
    public const string ResolveNotRequired = "RESOLVE_NOT_REQUIRED";

    // 결과 이미지
    public const string ArtifactNotReady = "ARTIFACT_NOT_READY";
    public const string ArtifactExpired = "ARTIFACT_EXPIRED";
    public const string ArtifactNotFound = "ARTIFACT_NOT_FOUND";

    // 아직 구현하지 않은 경계
    public const string NotImplemented = "NOT_IMPLEMENTED";
    public const string InternalError = "INTERNAL_ERROR";
}
