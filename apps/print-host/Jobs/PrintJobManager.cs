using YsFourcut.Host.Artifacts;
using YsFourcut.Host.Contracts;
using YsFourcut.Host.Platform;
using YsFourcut.Host.Printing;
using YsFourcut.Host.Printing.Virtual;
using YsFourcut.Host.Profiles;

namespace YsFourcut.Host.Jobs;

public sealed record JobSubmission(PrintJob Job, bool IsExisting);

public sealed record JobError(int StatusCode, string Code, string Message, IReadOnlyDictionary<string, object?>? Details = null);

/// <summary>
/// 서비스 전체에서 진행 중인 작업을 하나로 제한하고, 같은 UUID·같은 내용의 재요청을 흡수한다.
/// 실제 렌더링은 별도 worker 프로세스(가상 백엔드)가 수행하며 여기서는 상태와 기록만 관리한다.
/// </summary>
public sealed class PrintJobManager(
    IPrinterBackend backend,
    InMemoryReceiptArtifactStore artifacts,
    PrintJobRecordStore records,
    VirtualFaultStore faults,
    HostInfo hostInfo,
    TimeProvider clock,
    ILogger<PrintJobManager> logger) : IDisposable
{
    private const int MaxRememberedJobs = 32;

    private readonly object _gate = new();
    private readonly Dictionary<string, PrintJob> _jobs = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();

    private string? _activeJobId;
    private Task _activeRender = Task.CompletedTask;

    /// <summary>접수. 성공하면 202로 돌려줄 작업을 반환한다. 접수는 렌더 완료를 기다리지 않는다.</summary>
    public JobSubmission? Submit(
        string sessionId,
        string clientJobId,
        DecodedBitmap bitmap,
        PrinterProfile profile,
        out JobError? error)
    {
        var requestDigest = PrintJob.ComputeRequestDigest(bitmap.SourceDigest, profile.ProfileId, profile.Revision);

        lock (_gate)
        {
            if (_jobs.TryGetValue(clientJobId, out var existing))
            {
                if (!string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal) ||
                    !string.Equals(existing.RequestDigest, requestDigest, StringComparison.Ordinal))
                {
                    error = new JobError(
                        StatusCodes.Status409Conflict,
                        ErrorCodes.JobIdConflict,
                        "같은 작업 번호로 다른 내용을 보냈습니다. 새 작업 번호를 만들어 주세요.");
                    return null;
                }

                // 같은 UUID + 같은 내용이면 기존 작업을 그대로 돌려준다. 이미지를 다시 만들지 않는다.
                error = null;
                return new JobSubmission(existing, IsExisting: true);
            }

            if (_activeJobId is not null && _jobs.TryGetValue(_activeJobId, out var active) && active.QueueBusy)
            {
                error = new JobError(
                    StatusCodes.Status409Conflict,
                    ErrorCodes.PrinterBusy,
                    "이미 진행 중인 출력이 있습니다.");
                return null;
            }

            var now = clock.GetUtcNow();

            // 주입된 모의 오류는 이 작업 하나에만 적용하고 바로 해제한다. 가상 모드에서만 소비한다.
            var fault = hostInfo.Mode == PrinterMode.Virtual ? faults.Take() : null;

            var job = new PrintJob
            {
                ClientJobId = clientJobId,
                SessionId = sessionId,
                RequestDigest = requestDigest,
                SourceDigest = bitmap.SourceDigest,
                ProfileSnapshot = profile,
                PrinterMode = backend.Mode.ToApiValue(),
                AppearanceSeed = PrintJob.ComputeAppearanceSeed(clientJobId),
                AppearanceRevision = Rendering.ReceiptAppearanceRenderer.Revision,
                EstimatedEffects = [],
                InjectedFault = fault,
                ClearGeneration = artifacts.ClearGeneration(sessionId),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                State = PrintJobStates.Accepted,
            };

            // 출력을 시작하기 전에 기록부터 남긴다. 기록에 실패하면 작업을 시작하지 않는다.
            try
            {
                records.Write(ToRecord(job));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "작업 기록 저장 실패로 접수를 거부했습니다.");
                error = new JobError(
                    StatusCodes.Status500InternalServerError,
                    ErrorCodes.JobRecordFailed,
                    "작업 기록을 저장하지 못해 출력을 시작하지 않았습니다.");
                return null;
            }

            _jobs[clientJobId] = job;
            _activeJobId = clientJobId;
            TrimRememberedJobsUnlocked();

            _activeRender = Task.Run(() => RenderAsync(job, bitmap), CancellationToken.None);

            error = null;
            return new JobSubmission(job, IsExisting: false);
        }
    }

    public PrintJob? Find(string sessionId, string clientJobId)
    {
        lock (_gate)
        {
            if (_jobs.TryGetValue(clientJobId, out var job))
            {
                return string.Equals(job.SessionId, sessionId, StringComparison.Ordinal) ? job : null;
            }
        }

        // 현재 실행 중 만든 작업은 위에서 세션을 대조한다(다른 세션이면 존재를 알리지 않는다).
        // 호스트를 다시 시작하면 세션 쿠키도 함께 사라지므로 소유자를 대조할 수 없다.
        // 그래서 **이전 실행의 기록만** 상태를 복원하고, 결과 이미지는 없으므로 available=false가 된다.
        return RestoreFromPreviousRunRecord(sessionId, clientJobId);
    }

    private PrintJob? RestoreFromPreviousRunRecord(string sessionId, string clientJobId)
    {
        var record = records.TryRead(clientJobId);
        if (record is null)
        {
            return null;
        }

        // 이번 실행이 만든 기록이면 메모리에서 사라졌더라도(오래된 작업 정리 등) 복원하지 않는다.
        // 그렇지 않으면 같은 실행 안에서 다른 세션의 작업 상태가 노출될 수 있다.
        if (string.Equals(record.HostInstanceId, hostInfo.InstanceId, StringComparison.Ordinal))
        {
            return null;
        }

        return new PrintJob
        {
            ClientJobId = record.ClientJobId,
            SessionId = sessionId,
            RequestDigest = record.RequestDigest,
            SourceDigest = string.Empty,
            ProfileSnapshot = new Profiles.PrinterProfile(
                record.ProfileId,
                Profiles.ProfileKinds.Virtual,
                record.ProfileId,
                new Profiles.PaperGeometry(8, 8, 0, 0, 0, 8, 8, Profiles.CutStyles.Straight),
                new Profiles.ProfileLimits(8, 8, 8),
                "기록에서 복원한 작업입니다."),
            PrinterMode = record.PrinterMode,
            AppearanceSeed = 0,
            AppearanceRevision = string.Empty,
            EstimatedEffects = [],
            ClearGeneration = 0,
            CreatedAtUtc = record.CreatedAtUtc,
            UpdatedAtUtc = record.UpdatedAtUtc,
            State = record.State,
            FailureCode = record.FailureCode,
            FailureMessage = record.FailureCode is null ? null : "이전 실행에서 끝난 작업입니다.",
            RestoredFromRecord = true,
        };
    }

    public bool HasActiveJob(string sessionId)
    {
        lock (_gate)
        {
            return _activeJobId is not null &&
                   _jobs.TryGetValue(_activeJobId, out var job) &&
                   job.QueueBusy &&
                   string.Equals(job.SessionId, sessionId, StringComparison.Ordinal);
        }
    }

    private async Task RenderAsync(PrintJob job, DecodedBitmap bitmap)
    {
        try
        {
            await RenderCoreAsync(job, bitmap);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "가상 출력 처리 중 처리하지 못한 오류");

            // 이미 확정된 작업(예: 게시 후 기록 갱신에서 난 예외)을 실패로 뒤집지 않는다.
            if (job.QueueBusy)
            {
                Fail(job, ErrorCodes.VirtualRenderFailed, "가상 출력에 실패했습니다.");
            }
        }
        finally
        {
            // 어떤 경로로 빠져나가도 잠금이 남지 않게 한다. 확정 종료 전에는 풀지 않는다.
            if (job.QueueBusy)
            {
                Fail(job, ErrorCodes.VirtualRenderFailed, "가상 출력이 확정되지 않은 채 종료되었습니다.");
            }
        }
    }

    private async Task RenderCoreAsync(PrintJob job, DecodedBitmap bitmap)
    {
        // 주입 지연은 렌더 시작 **전에** 적용하고 렌더링 예산에서 제외한다.
        if (job.InjectedFault is { DelayMs: > 0 } delayed)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayed.DelayMs), clock, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                Fail(job, ErrorCodes.VirtualRenderFailed, "호스트가 종료되어 가상 출력을 중단했습니다.");
                return;
            }
        }

        // 렌더링을 시작하기 전에 확정되는 모의 장치 오류. worker를 띄우지 않는다.
        if (job.InjectedFault is not null &&
            TryPreRenderFault(job.InjectedFault.Kind, out var faultCode, out var faultMessage))
        {
            Fail(job, faultCode, faultMessage);
            return;
        }

        UpdateState(job, PrintJobStates.Rendering, failureCode: null, failureMessage: null);

        PrinterBackendResult result;
        try
        {
            result = await backend.PrintAsync(
                new PrinterBackendRequest(
                    job.ClientJobId,
                    job.ProfileSnapshot,
                    bitmap,
                    job.AppearanceSeed,
                    job.EstimatedEffects,
                    job.InjectedFault),
                _shutdown.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "가상 출력 처리 중 오류");
            Fail(job, ErrorCodes.VirtualRenderFailed, "가상 출력에 실패했습니다.");
            return;
        }

        if (!result.Succeeded)
        {
            // 진단용 부분 이미지가 있으면 보관하되 rendered로 바꾸지 않는다.
            if (result.IsPartial && result.Artifacts.Count > 0 &&
                artifacts.TryPublish(job.SessionId, job.ClientJobId, result.Artifacts, job.ClearGeneration, out _))
            {
                lock (_gate)
                {
                    job.HasPartialArtifacts = true;
                    job.PublishedArtifactKinds = [.. result.Artifacts.Select(a => a.Kind)];
                }
            }

            Fail(job, result.FailureCode ?? ErrorCodes.VirtualRenderFailed, result.FailureMessage ?? "가상 출력에 실패했습니다.");
            return;
        }

        // 세 결과를 모두 받은 뒤 한 번에 게시한다. 하나라도 없으면 완료로 바꾸지 않는다.
        if (result.Artifacts.Count != 3)
        {
            Fail(job, ErrorCodes.VirtualRenderFailed, "필수 결과 이미지가 부족합니다.");
            return;
        }

        if (!artifacts.TryPublish(job.SessionId, job.ClientJobId, result.Artifacts, job.ClearGeneration, out var publishFailure))
        {
            Fail(job, ErrorCodes.RenderLimitExceeded, publishFailure ?? "결과 이미지를 보관하지 못했습니다.");
            return;
        }

        lock (_gate)
        {
            job.Layout = result.Layout;
            job.PublishedArtifactKinds = [.. result.Artifacts.Select(a => a.Kind)];
            job.State = PrintJobStates.Rendered;
            job.FailureCode = null;
            job.FailureMessage = null;
            job.UpdatedAtUtc = clock.GetUtcNow();
            ReleaseActiveUnlocked(job);
        }

        WriteRecordSafely(job);
    }

    private void Fail(PrintJob job, string code, string message)
    {
        UpdateState(job, PrintJobStates.VirtualFailed, code, message);
    }

    /// <summary>렌더 전에 확정되는 모의 오류. 실제 센서 감지가 아니라는 뜻을 문구에 남긴다.</summary>
    private static bool TryPreRenderFault(string kind, out string code, out string message)
    {
        (code, message) = kind switch
        {
            VirtualFaultKinds.OutOfPaper => (ErrorCodes.VirtualOutOfPaper, "모의 오류: 가상 용지가 없습니다."),
            VirtualFaultKinds.CoverOpen => (ErrorCodes.VirtualCoverOpen, "모의 오류: 가상 덮개가 열려 있습니다."),
            VirtualFaultKinds.Offline => (ErrorCodes.VirtualPrinterOffline, "모의 오류: 가상 프린터가 오프라인입니다."),
            VirtualFaultKinds.FailBeforeRender => (ErrorCodes.VirtualRenderFailed, "모의 오류: 렌더링을 시작하기 전에 실패했습니다."),
            _ => (string.Empty, string.Empty),
        };

        return code.Length > 0;
    }

    private void UpdateState(PrintJob job, string state, string? failureCode, string? failureMessage)
    {
        lock (_gate)
        {
            job.State = state;
            job.FailureCode = failureCode;
            job.FailureMessage = failureMessage;
            job.UpdatedAtUtc = clock.GetUtcNow();

            if (PrintJobStates.IsTerminal(state))
            {
                ReleaseActiveUnlocked(job);
            }
        }

        WriteRecordSafely(job);
    }

    private void ReleaseActiveUnlocked(PrintJob job)
    {
        if (string.Equals(_activeJobId, job.ClientJobId, StringComparison.Ordinal))
        {
            _activeJobId = null;
        }
    }

    private void WriteRecordSafely(PrintJob job)
    {
        try
        {
            records.Write(ToRecord(job));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 이미 출력은 끝났으므로 상태를 되돌리지 않는다. 기록 실패만 남긴다.
            logger.LogError(ex, "작업 기록 갱신 실패");
        }
    }

    private PrintJobRecord ToRecord(PrintJob job)
        => new(
            PrintJobRecordStore.SchemaVersion,
            job.ClientJobId,
            job.RequestDigest,
            job.ProfileSnapshot.ProfileId,
            job.ProfileSnapshot.Revision,
            job.PrinterMode,
            job.State,
            job.SpoolJobId,
            job.FailureCode,
            job.CreatedAtUtc,
            job.UpdatedAtUtc,
            hostInfo.InstanceId);

    private void TrimRememberedJobsUnlocked()
    {
        if (_jobs.Count <= MaxRememberedJobs)
        {
            return;
        }

        foreach (var key in _jobs.Values
                     .Where(j => !j.QueueBusy)
                     .OrderBy(j => j.UpdatedAtUtc)
                     .Take(_jobs.Count - MaxRememberedJobs)
                     .Select(j => j.ClientJobId)
                     .ToList())
        {
            _jobs.Remove(key);
        }
    }

    /// <summary>호스트 종료 시 진행 중 렌더를 취소해 worker를 정리한다.</summary>
    public async Task ShutdownAsync()
    {
        try
        {
            await _shutdown.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // 컨테이너가 이 매니저를 먼저 정리한 뒤에 호스트 종료가 실행될 수 있다.
            // 취소 토큰이 이미 사라진 경우이므로 종료 경로를 예외로 깨뜨리지 않는다.
        }

        Task pending;
        lock (_gate)
        {
            pending = _activeRender;
        }

        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            logger.LogWarning("종료 중 진행 작업 정리를 기다리지 못했습니다.");
        }
    }

    public string HostInstanceId => hostInfo.InstanceId;

    public void Dispose() => _shutdown.Dispose();
}
