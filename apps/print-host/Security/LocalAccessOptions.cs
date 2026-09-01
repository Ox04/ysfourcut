namespace YsFourcut.Host.Security;

/// <summary>
/// 루프백 전용 접근 정책. CORS "*"를 쓰지 않고 교차 출처 읽기도 허용하지 않는다
/// (SERVICE_DESIGN.md 10절). 프록시를 지나도 같은 검사를 유지한다.
/// </summary>
public sealed class LocalAccessOptions
{
    /// <summary>브라우저 fetch만 보낼 수 있는 전용 헤더. 교차 출처에서는 preflight가 필요해 통과하지 못한다.</summary>
    public const string ClientHeaderName = "X-YSFourcut-Client";
    public const string ClientHeaderValue = "web";

    public const string SessionCookieName = "ysfourcut_session";

    /// <summary>Host 헤더의 호스트명. Vite 프록시가 Host를 127.0.0.1:4317로 바꿔도 통과한다.</summary>
    public IReadOnlyList<string> AllowedHostNames { get; init; } = ["127.0.0.1"];

    /// <summary>변경 요청에 요구하는 정확한 Origin. 개발에서만 5173 화면 주소가 추가된다.</summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>페어링 뒤 브라우저를 되돌려 보낼 앱 주소. 개발은 Vite 화면, 운영은 같은 호스트.</summary>
    public string AppUrl { get; init; } = "/";

    public long MaxRequestBodyBytes { get; init; } = 1024 * 1024;
}
