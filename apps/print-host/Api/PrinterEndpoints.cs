using YsFourcut.Host.Contracts;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Platform;
using YsFourcut.Host.Printing;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Security;

namespace YsFourcut.Host.Api;

public static class PrinterEndpoints
{
    public static void MapPrinterEndpoints(this IEndpointRouteBuilder app)
    {
        // virtual 모드에서는 가상 프리셋만 반환한다. Windows 프린터 열거를 호출하지 않는다.
        app.MapGet("/api/printers", (PrinterProfileStore store, HostInfo host) =>
        {
            var verification = store.Verification;
            var selectedId = store.SelectedProfileId;

            return Results.Ok(new PrintersResponse(
                SchemaVersion: ApiSchema.Version,
                PrinterMode: host.Mode.ToApiValue(),
                OsQueryPerformed: false,
                Printers:
                [
                    .. store.ListProfiles()
                        .Select(p => ProfileViewMapper.ToView(p, verification, p.ProfileId == selectedId)),
                ]));
        }).RequireLocalSession();

        app.MapGet("/api/printer-profile", (PrinterProfileStore store, HostInfo host, ServiceLimits limits) =>
            Results.Ok(BuildProfileResponse(store, host, limits))).RequireLocalSession();

        app.MapPut("/api/printer-profile", (
            UpdatePrinterProfileRequest? request,
            PrinterProfileStore store,
            HostInfo host,
            ServiceLimits limits) =>
        {
            if (request is null || request.SchemaVersion != ApiSchema.Version)
            {
                return ApiResults.Error(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.SchemaVersionUnsupported,
                    $"지원하는 schemaVersion은 {ApiSchema.Version}입니다.");
            }

            if (string.IsNullOrWhiteSpace(request.ProfileId) || string.IsNullOrWhiteSpace(request.ExpectedRevision))
            {
                return ApiResults.Error(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.ValidationFailed,
                    "profileId와 expectedRevision이 필요합니다.",
                    new Dictionary<string, object?> { ["field"] = "profileId|expectedRevision" });
            }

            if (!store.TryUpdate(request.ProfileId, request.ExpectedRevision, request.CutStyle, out _, out var error))
            {
                var status = error!.Code switch
                {
                    ErrorCodes.PrinterNotFound => StatusCodes.Status404NotFound,
                    ErrorCodes.ProfileChanged => StatusCodes.Status409Conflict,
                    _ => StatusCodes.Status400BadRequest,
                };

                return ApiResults.Error(status, error.Code, error.Message, error.Details);
            }

            return Results.Ok(BuildProfileResponse(store, host, limits));
        }).RequireLocalSession();

        // 실물 검수 경로. 실제 인쇄와 검증 기록은 91번에서 붙인다.
        app.MapPost("/api/printer-tests", () =>
            ApiResults.NotImplemented("테스트 인쇄", "tasks/05.md, tasks/91.md")).RequireLocalSession();

        app.MapPost("/api/printer-profile/verify", () =>
            ApiResults.NotImplemented("프로필 실물 검증 기록", "tasks/91.md")).RequireLocalSession();
    }

    private static PrinterProfileResponse BuildProfileResponse(
        PrinterProfileStore store,
        HostInfo host,
        ServiceLimits limits)
    {
        var active = store.Active;
        return new PrinterProfileResponse(
            SchemaVersion: ApiSchema.Version,
            PrinterMode: host.Mode.ToApiValue(),
            Profile: ProfileViewMapper.ToView(active, store.Verification, isSelected: true),
            ServiceLimits: ProfileViewMapper.ToView(limits));
    }
}
