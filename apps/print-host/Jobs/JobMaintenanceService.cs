using YsFourcut.Host.Artifacts;
using YsFourcut.Host.Contracts;

namespace YsFourcut.Host.Jobs;

/// <summary>
/// 시작 시 지난 실행의 미완료 기록을 실패로 정리하고(자동 재렌더링 없음),
/// 실행 중에는 타이머로 결과 이미지 TTL을 정리한다. 프런트가 열려 있어야 정리되는 구조를 피한다.
/// 종료 시에는 진행 중 렌더와 worker를 정리한다.
/// </summary>
public sealed class JobMaintenanceService(
    PrintJobRecordStore records,
    InMemoryReceiptArtifactStore artifacts,
    PrintJobManager jobs,
    TimeProvider clock,
    ILogger<JobMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    /// <summary>완료 기록 보관 기간. 해결되지 않은 기록은 자동으로 지우지 않는다.</summary>
    private static readonly TimeSpan CompletedRecordRetention = TimeSpan.FromHours(24);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        CleanUpRecordsFromPreviousRun();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                artifacts.Sweep();
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await jobs.ShutdownAsync();
        await base.StopAsync(cancellationToken);
    }

    private void CleanUpRecordsFromPreviousRun()
    {
        var now = clock.GetUtcNow();
        var unfinished = 0;
        var removed = 0;

        foreach (var record in records.ReadAll())
        {
            try
            {
                if (!PrintJobStates.IsTerminal(record.State))
                {
                    // 이전 실행이 남긴 미완료 작업. 자동으로 다시 렌더링하지 않는다.
                    records.Write(record with
                    {
                        State = PrintJobStates.VirtualFailed,
                        FailureCode = ErrorCodes.HostRestarted,
                        UpdatedAtUtc = now,
                    });
                    unfinished++;
                    continue;
                }

                if (now - record.UpdatedAtUtc > CompletedRecordRetention)
                {
                    records.Delete(record.ClientJobId);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                logger.LogWarning("작업 기록 정리 중 건너뛴 항목이 있습니다: {Reason}", ex.GetType().Name);
            }
        }

        if (unfinished > 0 || removed > 0)
        {
            logger.LogInformation(
                "시작 정리: 미완료 {Unfinished}건을 실패로 표시하고 오래된 완료 {Removed}건을 지웠습니다.",
                unfinished,
                removed);
        }
    }
}
