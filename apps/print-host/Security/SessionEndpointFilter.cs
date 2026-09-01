using YsFourcut.Host.Contracts;

namespace YsFourcut.Host.Security;

/// <summary>
/// 일회용 코드 교환과 health를 제외한 모든 API에 유효 세션을 요구한다.
/// </summary>
public sealed class SessionEndpointFilter(SessionStore sessions) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var token = http.Request.Cookies[LocalAccessOptions.SessionCookieName];

        if (string.IsNullOrEmpty(token))
        {
            return ApiResults.Error(
                StatusCodes.Status401Unauthorized,
                ErrorCodes.SessionRequired,
                "로컬 호스트와 먼저 연결해야 합니다.");
        }

        var session = sessions.Validate(token);
        if (session is null)
        {
            // 만료·무효 세션 쿠키는 즉시 지운다.
            http.Response.Cookies.Delete(LocalAccessOptions.SessionCookieName, SessionCookie.DeleteOptions);
            return ApiResults.Error(
                StatusCodes.Status401Unauthorized,
                ErrorCodes.SessionInvalid,
                "세션이 만료되었습니다. 다시 연결해 주세요.");
        }

        http.SetLocalSession(session);
        return await next(context);
    }
}

public static class SessionCookie
{
    /// <summary>
    /// Domain 생략(호스트 전용), Path=/, HttpOnly, SameSite=Strict.
    /// 기본 루프백 HTTP이므로 Secure에 의존하지 않는다 (SERVICE_DESIGN.md 10절).
    /// </summary>
    public static CookieOptions IssueOptions => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Secure = false,
        IsEssential = true,
    };

    public static CookieOptions DeleteOptions => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Secure = false,
    };
}

public static class SessionHttpContextExtensions
{
    private const string ItemKey = "ysfourcut.session";

    public static void SetLocalSession(this HttpContext context, LocalSession session)
        => context.Items[ItemKey] = session;

    /// <summary>세션이 필요한 endpoint에서만 호출한다. 필터가 이미 확인했다.</summary>
    public static LocalSession GetLocalSession(this HttpContext context)
        => context.Items[ItemKey] as LocalSession
           ?? throw new InvalidOperationException("세션 없이 보호된 endpoint에 접근했습니다.");

    public static LocalSession? FindLocalSession(this HttpContext context, SessionStore sessions)
    {
        if (context.Items[ItemKey] is LocalSession existing)
        {
            return existing;
        }

        var token = context.Request.Cookies[LocalAccessOptions.SessionCookieName];
        return sessions.Validate(token);
    }
}

public static class SessionRouteExtensions
{
    public static RouteHandlerBuilder RequireLocalSession(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter<SessionEndpointFilter>();
}
