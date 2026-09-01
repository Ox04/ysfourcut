using YsFourcut.Host.Contracts;
using YsFourcut.Host.Platform;
using YsFourcut.Host.Security;

namespace YsFourcut.Host.Api;

public static class BootstrapEndpoints
{
    public static void MapBootstrapEndpoints(this IEndpointRouteBuilder app)
    {
        // 일회용 페어링 화면. WSL에서 Linux GUI 브라우저 자동 실행을 가정할 수 없으므로
        // 사용자가 이 주소를 브라우저에서 직접 열고, 호스트가 코드를 URL fragment로 넘긴다.
        // fragment는 이후 요청에서 서버로 전송되지 않는다.
        app.MapGet("/pair", (HttpContext context, BootstrapCodeStore codes, LocalAccessOptions options) =>
        {
            var code = codes.Issue();
            var separator = options.AppUrl.Contains('#', StringComparison.Ordinal) ? string.Empty : "#";

            // Results.Redirect는 Location 값(=일회용 코드)을 프레임워크 Information 로그에 남긴다.
            // 로그 수준 설정에 기대지 않도록 헤더를 직접 쓰고 상태 코드만 돌려준다.
            context.Response.Headers.Location = $"{options.AppUrl}{separator}bootstrap={code}";
            return Results.StatusCode(StatusCodes.Status302Found);
        });

        app.MapPost("/api/bootstrap", (
            BootstrapRequest? request,
            HttpContext context,
            BootstrapCodeStore codes,
            SessionStore sessions,
            HostInfo host,
            ILoggerFactory loggerFactory) =>
        {
            if (request is null || request.SchemaVersion != ApiSchema.Version)
            {
                return ApiResults.Error(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.SchemaVersionUnsupported,
                    $"지원하는 schemaVersion은 {ApiSchema.Version}입니다.");
            }

            // 코드 값 자체는 어떤 분기에서도 로그·응답에 넣지 않는다.
            var exchange = codes.TryConsume(request.Code);
            if (exchange != BootstrapExchange.Ok)
            {
                var (code, message) = exchange switch
                {
                    BootstrapExchange.Expired => (ErrorCodes.BootstrapCodeExpired, "연결 코드가 만료되었습니다. 다시 연결해 주세요."),
                    BootstrapExchange.AlreadyUsed => (ErrorCodes.BootstrapCodeAlreadyUsed, "이미 사용한 연결 코드입니다. 다시 연결해 주세요."),
                    _ => (ErrorCodes.BootstrapCodeInvalid, "연결 코드가 올바르지 않습니다."),
                };

                loggerFactory.CreateLogger("YsFourcut.Bootstrap")
                    .LogWarning("일회용 코드 교환 실패: {Result}", exchange);

                return ApiResults.Error(StatusCodes.Status401Unauthorized, code, message);
            }

            var (token, session) = sessions.Create();
            context.Response.Cookies.Append(
                LocalAccessOptions.SessionCookieName,
                token,
                SessionCookie.IssueOptions);

            return Results.Ok(new BootstrapResponse(
                SchemaVersion: ApiSchema.Version,
                Session: new SessionStatus(true),
                SessionIdleTimeoutSeconds: (int)sessions.IdleTimeout.TotalSeconds,
                HostInstanceId: host.InstanceId));
        });
    }
}
