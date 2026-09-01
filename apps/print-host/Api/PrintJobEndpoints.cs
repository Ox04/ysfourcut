using Microsoft.Net.Http.Headers;
using YsFourcut.Host.Artifacts;
using YsFourcut.Host.Contracts;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Platform;
using YsFourcut.Host.Printing;
using YsFourcut.Host.Printing.Virtual;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Rendering;
using YsFourcut.Host.Security;

namespace YsFourcut.Host.Api;

/// <summary>
/// 출력 접수와 결과 이미지 조회. 결과 PNG는 메모리에서만 제공하고 공개 파일 경로를 만들지 않는다.
/// 현재 실행이 만든 작업은 세션에 묶이며 다른 세션에는 존재를 알리지 않는다.
/// 호스트를 다시 시작하면 세션 쿠키도 사라지므로 소유자를 대조할 수 없다. 그래서
/// **이전 실행의 기록**은 UUID를 아는 로컬 세션이 상태만 조회할 수 있고 이미지는 항상 만료로 응답한다.
/// </summary>
public static class PrintJobEndpoints
{
    private static readonly string[] ArtifactOrder =
        [ArtifactKinds.Content, ArtifactKinds.Paper, ArtifactKinds.Appearance];

    public static void MapPrintJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/print-jobs", (
            PrintJobRequest? request,
            HttpContext context,
            PrinterProfileStore store,
            PrintJobManager jobs,
            InMemoryReceiptArtifactStore artifacts,
            HostInfo host,
            ServiceLimits limits) =>
        {
            if (!PrintJobRequestValidator.TryValidateEnvelope(request, out var envelopeFailure))
            {
                return ApiResults.Error(envelopeFailure!.StatusCode, envelopeFailure.Detail);
            }

            var profile = store.Find(request!.ProfileId!);
            if (profile is null)
            {
                return ApiResults.Error(
                    StatusCodes.Status404NotFound,
                    ErrorCodes.PrinterNotFound,
                    "요청한 프로필을 찾을 수 없습니다.",
                    new Dictionary<string, object?> { ["profileId"] = request.ProfileId });
            }

            if (!string.Equals(profile.Revision, request.ProfileRevision, StringComparison.Ordinal))
            {
                return ApiResults.Error(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.ProfileChanged,
                    "프로필이 변경되었습니다. 최신 프로필을 조회한 뒤 다시 시도하세요.",
                    new Dictionary<string, object?>
                    {
                        ["profileId"] = profile.ProfileId,
                        ["currentRevision"] = profile.Revision,
                    });
            }

            if (!PrintJobRequestValidator.TryValidateProfileCompatibility(
                    host.Mode, profile, store.Verification, out var compatFailure))
            {
                return ApiResults.Error(compatFailure!.StatusCode, compatFailure.Detail);
            }

            if (!PrintJobRequestValidator.TryValidateBitmap(
                    request.Bitmap, profile, limits, out var bitmap, out var bitmapFailure))
            {
                return ApiResults.Error(bitmapFailure!.StatusCode, bitmapFailure.Detail);
            }

            var session = context.GetLocalSession();
            var submission = jobs.Submit(session.SessionId, request.ClientJobId!, bitmap!, profile, out var error);

            if (submission is null)
            {
                return ApiResults.Error(error!.StatusCode, error.Code, error.Message, error.Details);
            }

            // 접수는 렌더 완료를 기다리지 않는다. 웹이 상태를 조회한다.
            return Results.Json(
                ToStatus(submission.Job, artifacts),
                statusCode: StatusCodes.Status202Accepted);
        }).RequireLocalSession();

        app.MapGet("/api/print-jobs/{clientJobId}", (
            string clientJobId,
            HttpContext context,
            PrintJobManager jobs,
            InMemoryReceiptArtifactStore artifacts) =>
        {
            var session = context.GetLocalSession();
            var job = jobs.Find(session.SessionId, clientJobId);

            return job is null
                ? NotFound()
                : Results.Ok(ToStatus(job, artifacts));
        }).RequireLocalSession();

        app.MapGet("/api/print-jobs/{clientJobId}/artifacts/{kind}", (
            string clientJobId,
            string kind,
            HttpContext context,
            PrintJobManager jobs,
            InMemoryReceiptArtifactStore artifacts) =>
        {
            var session = context.GetLocalSession();
            var job = jobs.Find(session.SessionId, clientJobId);

            if (job is null || !ArtifactOrder.Contains(kind, StringComparer.Ordinal))
            {
                return NotFound();
            }

            if (job.QueueBusy)
            {
                return ApiResults.Error(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.ArtifactNotReady,
                    "아직 결과 이미지를 만드는 중입니다.",
                    new Dictionary<string, object?> { ["state"] = job.State });
            }

            var lookup = artifacts.TryGet(session.SessionId, clientJobId, kind, out var entry);

            if (lookup == ArtifactLookup.Found)
            {
                // 조회로 재렌더링하지 않는다. 이미 만들어 둔 바이트만 돌려준다.
                return Results.File(entry!.Bytes, contentType: "image/png", lastModified: null, entityTag: null);
            }

            // 만들어졌다가 사라진 경우(TTL·명시적 정리·호스트 재시작)는 만료로 알린다.
            // 종류별로 구분한다. 진단용 부분 이미지도 한 번 만들어졌으면 반복 조회에서 404로 바뀌지 않고,
            // 애초에 만들어진 적 없는 종류(부분 실패의 paper 등)는 404다.
            var existed = job.PublishedArtifactKinds.Contains(kind, StringComparer.Ordinal) ||
                          (job.RestoredFromRecord && job.State == PrintJobStates.Rendered);
            return lookup == ArtifactLookup.Expired || existed
                ? ApiResults.Error(
                    StatusCodes.Status410Gone,
                    ErrorCodes.ArtifactExpired,
                    "결과 이미지가 만료되었거나 정리되었습니다.")
                : NotFound();
        }).RequireLocalSession();

        app.MapPost("/api/session/artifacts/clear", (
            HttpContext context,
            PrintJobManager jobs,
            InMemoryReceiptArtifactStore artifacts,
            HostInfo host) =>
        {
            var session = context.GetLocalSession();

            if (jobs.HasActiveJob(session.SessionId))
            {
                return ApiResults.Error(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.PrinterBusy,
                    "진행 중인 출력이 끝난 뒤에 정리할 수 있습니다.");
            }

            var cleared = artifacts.ClearSession(session.SessionId);

            // 다음 사용자 세션은 remainingJobs == 0을 확인한 뒤에만 시작한다.
            return Results.Ok(new ClearArtifactsResponse(
                ApiSchema.Version,
                cleared,
                artifacts.CountForSession(session.SessionId),
                host.InstanceId));
        }).RequireLocalSession();

        // 가상 출력에는 불명확 상태가 없다. 종이·OS 큐 확인을 요구하지 않으며 재출력도 하지 않는다.
        // 실물(outcome_unknown) 처리는 91번에서 붙인다.
        app.MapPost("/api/print-jobs/{clientJobId}/resolve", (
            string clientJobId,
            HttpContext context,
            PrintJobManager jobs,
            HostInfo host) =>
        {
            var session = context.GetLocalSession();
            var job = jobs.Find(session.SessionId, clientJobId);

            if (job is null)
            {
                return NotFound();
            }

            if (host.Mode != PrinterMode.Physical)
            {
                return ApiResults.Error(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.ResolveNotRequired,
                    "가상 출력은 종이나 프린터 큐 확인이 필요 없습니다. 실패했다면 새 작업으로 다시 시도하세요.",
                    new Dictionary<string, object?> { ["state"] = job.State });
            }

            return ApiResults.NotImplemented("실물 불명확 상태 정리", "tasks/91.md");
        }).RequireLocalSession();

        // 가상 모드 전용 모의 오류 주입. 다음 작업 하나에만 적용된다.
        app.MapGet("/api/virtual-printer/fault", (VirtualFaultStore faults, HostInfo host) =>
            Results.Ok(BuildFaultResponse(faults, host))).RequireLocalSession();

        app.MapPut("/api/virtual-printer/fault", (
            VirtualFaultRequest? request,
            VirtualFaultStore faults,
            HostInfo host) =>
        {
            if (host.Mode != PrinterMode.Virtual)
            {
                return ApiResults.Error(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.PlatformUnsupported,
                    "실패 주입은 가상 모드에서만 사용할 수 있습니다.");
            }

            if (request is null || request.SchemaVersion != ApiSchema.Version)
            {
                return ApiResults.Error(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.SchemaVersionUnsupported,
                    $"지원하는 schemaVersion은 {ApiSchema.Version}입니다.");
            }

            if (string.IsNullOrWhiteSpace(request.Fault))
            {
                faults.Set(null);
                return Results.Ok(BuildFaultResponse(faults, host));
            }

            if (!VirtualFaultKinds.IsKnown(request.Fault))
            {
                return ApiResults.Error(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.ValidationFailed,
                    "알 수 없는 실패 종류입니다.",
                    new Dictionary<string, object?> { ["availableFaults"] = VirtualFaultKinds.All });
            }

            var delayMs = request.DelayMs ?? 0;
            if (delayMs != 0 && (delayMs < VirtualFault.MinDelayMs || delayMs > VirtualFault.MaxDelayMs))
            {
                return ApiResults.Error(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.ValidationFailed,
                    $"지연은 0 또는 {VirtualFault.MinDelayMs}~{VirtualFault.MaxDelayMs}ms 사이여야 합니다.");
            }

            if (request.Fault == VirtualFaultKinds.FailAfterRows && request.FailAfterRows is not > 0)
            {
                return ApiResults.Error(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.ValidationFailed,
                    "fail_after_rows에는 양의 failAfterRows가 필요합니다.");
            }

            faults.Set(new VirtualFault(request.Fault, delayMs, request.FailAfterRows));
            return Results.Ok(BuildFaultResponse(faults, host));
        }).RequireLocalSession();
    }

    private static VirtualFaultResponse BuildFaultResponse(VirtualFaultStore faults, HostInfo host)
    {
        var pending = faults.Pending;
        return new VirtualFaultResponse(
            ApiSchema.Version,
            host.Mode.ToApiValue(),
            pending is null ? null : new VirtualFaultView(pending.Kind, pending.DelayMs, pending.FailAfterRows),
            VirtualFaultKinds.All);
    }

    /// <summary>현재 실행의 다른 세션 작업과 없는 작업을 구분하지 않는다.</summary>
    private static IResult NotFound()
        => ApiResults.Error(
            StatusCodes.Status404NotFound,
            ErrorCodes.ArtifactNotFound,
            "요청한 작업을 찾을 수 없습니다.");

    private static PrintJobStatusResponse ToStatus(PrintJob job, InMemoryReceiptArtifactStore artifacts)
    {
        var stored = artifacts
            .Describe(job.SessionId, job.ClientJobId)
            .ToDictionary(entry => entry.Kind, StringComparer.Ordinal);

        var views = ArtifactOrder.Select(kind =>
        {
            stored.TryGetValue(kind, out var entry);
            return new ArtifactView(
                Kind: kind,
                Path: $"/api/print-jobs/{job.ClientJobId}/artifacts/{kind}",
                Available: entry is not null,
                // 부분 결과를 완료로 표시하지 않는다.
                // 부분 결과는 available=true여도 complete=false다.
                Complete: entry is not null && job.State == PrintJobStates.Rendered && !job.HasPartialArtifacts,
                WidthPx: entry?.WidthPx,
                HeightPx: entry?.HeightPx,
                ByteLength: entry?.Bytes.Length,
                ExpiresAtUtc: entry?.ExpiresAtUtc);
        }).ToList();

        var layout = job.Layout is null
            ? null
            : new ReceiptLayoutView(
                job.Layout.PaperWidthDots,
                job.Layout.PaperHeightDots,
                job.Layout.ContentXDots,
                job.Layout.ContentYDots,
                job.Layout.ContentWidthDots,
                job.Layout.ContentHeightDots,
                job.Layout.LeadingFeedDots,
                job.Layout.TrailingFeedDots,
                job.Layout.DpiX,
                job.Layout.DpiY);

        return new PrintJobStatusResponse(
            SchemaVersion: ApiSchema.Version,
            ClientJobId: job.ClientJobId,
            PrinterMode: job.PrinterMode,
            State: job.State,
            IsPhysical: job.IsPhysical,
            SpoolJobId: job.SpoolJobId,
            QueueBusy: job.QueueBusy,
            ProfileId: job.ProfileSnapshot.ProfileId,
            ProfileRevision: job.ProfileSnapshot.Revision,
            SourceDigest: job.SourceDigest,
            Layout: layout,
            Artifacts: views,
            Simulation: new SimulationView(
                AppearanceRevision: job.AppearanceRevision,
                Seed: job.AppearanceSeed,
                EstimatedEffects: job.EstimatedEffects,
                InjectedFault: job.InjectedFault?.Kind,
                // 가상 출력임을 항상 드러낸다.
                IsSimulated: true),
            Failure: job.FailureCode is null ? null : new PrintJobFailureView(job.FailureCode, job.FailureMessage ?? string.Empty),
            CreatedAtUtc: job.CreatedAtUtc,
            UpdatedAtUtc: job.UpdatedAtUtc);
    }
}
