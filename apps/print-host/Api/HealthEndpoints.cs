using YsFourcut.Host.Contracts;
using YsFourcut.Host.Platform;
using YsFourcut.Host.Printing;
using YsFourcut.Host.Security;

namespace YsFourcut.Host.Api;

public static class HealthEndpoints
{
    /// <summary>
    /// 인증 전에도 조회할 수 있는 유일한 API. 버전·instance·모드·준비 상태만 반환하고
    /// 파일 경로·프로필 상세·사진 관련 정보는 넣지 않는다.
    /// </summary>
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", (HttpContext context, HostInfo host, SessionStore sessions, IHostEnvironment env) =>
        {
            var session = context.FindLocalSession(sessions);

            return Results.Ok(new HealthResponse(
                SchemaVersion: ApiSchema.Version,
                Status: "ok",
                Ready: true,
                PrinterMode: host.Mode.ToApiValue(),
                HostInstanceId: host.InstanceId,
                HostVersion: host.Version,
                Environment: env.EnvironmentName,
                Session: new SessionStatus(session is not null)));
        });
    }
}
