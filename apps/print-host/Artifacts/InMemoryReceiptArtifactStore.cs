using YsFourcut.Host.Rendering;

namespace YsFourcut.Host.Artifacts;

public enum ArtifactLookup
{
    Found,
    NotFound,
    Expired,
}

public sealed record ArtifactEntry(
    string Kind,
    byte[] Bytes,
    int WidthPx,
    int HeightPx,
    DateTimeOffset ExpiresAtUtc);

public sealed record ArtifactCacheLimits(
    TimeSpan Ttl,
    int MaxJobsPerSession = 3,
    long MaxBytesPerSession = 64L * 1024 * 1024,
    long MaxBytesPerHost = 128L * 1024 * 1024,
    long MaxBytesPerJob = 32L * 1024 * 1024)
{
    public static ArtifactCacheLimits Default { get; } = new(TimeSpan.FromMinutes(10));
}

/// <summary>
/// 인증 세션에 묶인 메모리 전용 결과 캐시. 디스크에 쓰지 않고 공개 파일 경로도 만들지 않는다.
/// 절대 TTL 10분이며 조회로 연장하지 않는다. 세션/호스트 한도를 넘으면 오래된 완료 결과부터 만료시킨다.
/// </summary>
public sealed class InMemoryReceiptArtifactStore(
    TimeProvider clock,
    ArtifactCacheLimits limits,
    ILogger<InMemoryReceiptArtifactStore> logger)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, JobArtifacts> _jobs = new(StringComparer.Ordinal);
    // 세션별 정리 세대. **비우지 않는다.** 세대를 0으로 되돌리면 정리된 세션의 늦은 게시가 다시 통과한다.
    // 항목은 실제로 정리를 호출한 세션 수만큼만 늘고(세션 ID는 재사용되지 않는다) 호스트 재시작에 사라진다.
    private readonly Dictionary<string, long> _clearGenerations = new(StringComparer.Ordinal);

    public ArtifactCacheLimits Limits => limits;

    /// <summary>
    /// 세션의 정리 횟수. 작업 시작 때 값을 받아 두고 게시할 때 확인하면,
    /// 정리 뒤에 도착한 늦은 결과가 이전 이미지를 다시 게시하지 못한다.
    /// </summary>
    public long ClearGeneration(string sessionId)
    {
        lock (_gate)
        {
            return _clearGenerations.TryGetValue(sessionId, out var generation) ? generation : 0;
        }
    }

    /// <summary>세 이미지를 한 번에 게시한다. 하나라도 빠지면 게시하지 않는다.</summary>
    public bool TryPublish(
        string sessionId,
        string clientJobId,
        IReadOnlyList<RenderedPng> artifacts,
        long expectedClearGeneration,
        out string? failureReason)
    {
        if (artifacts.Count == 0 || artifacts.Any(a => a.Bytes.Length == 0))
        {
            failureReason = "결과 이미지가 비어 있습니다.";
            return false;
        }

        var totalBytes = artifacts.Sum(a => (long)a.Bytes.Length);
        if (totalBytes > limits.MaxBytesPerJob)
        {
            failureReason = "작업의 결과 이미지 합계가 한도를 넘었습니다.";
            return false;
        }

        lock (_gate)
        {
            if (ClearGenerationUnlocked(sessionId) != expectedClearGeneration)
            {
                // 작업이 도는 동안 세션이 정리됐다. 지운 결과를 되살리지 않는다.
                failureReason = "결과를 만드는 동안 세션이 정리되어 게시하지 않았습니다.";
                return false;
            }

            var now = clock.GetUtcNow();
            SweepUnlocked(now);

            var expiresAt = now + limits.Ttl;
            var entry = new JobArtifacts(
                sessionId,
                clientJobId,
                now,
                expiresAt,
                artifacts.ToDictionary(
                    a => a.Kind,
                    a => new ArtifactEntry(a.Kind, a.Bytes, a.WidthPx, a.HeightPx, expiresAt),
                    StringComparer.Ordinal),
                totalBytes);

            _jobs[clientJobId] = entry;

            EnforceSessionLimitsUnlocked(sessionId);
            EnforceHostLimitUnlocked();

            if (!_jobs.ContainsKey(clientJobId))
            {
                // 자기 자신이 한도에 밀려 정리됐다면 게시 실패로 처리한다.
                failureReason = "결과 이미지 캐시 한도를 넘어 보관하지 못했습니다.";
                return false;
            }

            failureReason = null;
            return true;
        }
    }

    public ArtifactLookup TryGet(string sessionId, string clientJobId, string kind, out ArtifactEntry? entry)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            entry = null;

            // 요청한 작업을 먼저 확인해야 "만료"와 "없음"을 구분해 알릴 수 있다.
            if (!_jobs.TryGetValue(clientJobId, out var job))
            {
                SweepUnlocked(now);
                return ArtifactLookup.NotFound;
            }

            if (!string.Equals(job.SessionId, sessionId, StringComparison.Ordinal))
            {
                // 다른 세션에는 존재 자체를 알리지 않는다.
                SweepUnlocked(now);
                return ArtifactLookup.NotFound;
            }

            // 조회로 TTL을 연장하지 않는다.
            if (job.ExpiresAtUtc <= now)
            {
                _jobs.Remove(clientJobId);
                SweepUnlocked(now);
                return ArtifactLookup.Expired;
            }

            SweepUnlocked(now);

            if (!job.Entries.TryGetValue(kind, out var found))
            {
                return ArtifactLookup.NotFound;
            }

            entry = found;
            return ArtifactLookup.Found;
        }
    }

    /// <summary>작업 조회 응답에 넣을 목록. 캐시가 비었으면 available=false로 표시한다.</summary>
    public IReadOnlyList<ArtifactEntry> Describe(string sessionId, string clientJobId)
    {
        lock (_gate)
        {
            SweepUnlocked(clock.GetUtcNow());

            if (!_jobs.TryGetValue(clientJobId, out var job) ||
                !string.Equals(job.SessionId, sessionId, StringComparison.Ordinal))
            {
                return [];
            }

            return [.. job.Entries.Values];
        }
    }

    /// <summary>세션의 완료 결과를 모두 지운다. 반환값은 지운 작업 수다.</summary>
    public int ClearSession(string sessionId)
    {
        lock (_gate)
        {
            var removed = _jobs
                .Where(pair => string.Equals(pair.Value.SessionId, sessionId, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToList();

            foreach (var key in removed)
            {
                _jobs.Remove(key);
            }

            _clearGenerations[sessionId] = ClearGenerationUnlocked(sessionId) + 1;
            return removed.Count;
        }
    }

    public void Sweep()
    {
        lock (_gate)
        {
            SweepUnlocked(clock.GetUtcNow());
        }
    }

    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return _jobs.Values.Sum(j => j.TotalBytes);
            }
        }
    }

    public int JobCount
    {
        get
        {
            lock (_gate)
            {
                return _jobs.Count;
            }
        }
    }

    /// <summary>세션에 남아 있는 작업 수. 정리 성공을 확인하는 데 쓴다.</summary>
    public int CountForSession(string sessionId)
    {
        lock (_gate)
        {
            SweepUnlocked(clock.GetUtcNow());
            return _jobs.Values.Count(j => string.Equals(j.SessionId, sessionId, StringComparison.Ordinal));
        }
    }

    private long ClearGenerationUnlocked(string sessionId)
        => _clearGenerations.TryGetValue(sessionId, out var generation) ? generation : 0;

    private void SweepUnlocked(DateTimeOffset now)
    {
        var expired = _jobs.Where(pair => pair.Value.ExpiresAtUtc <= now).Select(pair => pair.Key).ToList();
        foreach (var key in expired)
        {
            _jobs.Remove(key);
        }

        if (expired.Count > 0)
        {
            logger.LogDebug("만료된 결과 이미지 {Count}건을 정리했습니다.", expired.Count);
        }
    }

    private void EnforceSessionLimitsUnlocked(string sessionId)
    {
        while (true)
        {
            var sessionJobs = _jobs.Values
                .Where(j => string.Equals(j.SessionId, sessionId, StringComparison.Ordinal))
                .OrderBy(j => j.CreatedAtUtc)
                .ToList();

            if (sessionJobs.Count <= limits.MaxJobsPerSession &&
                sessionJobs.Sum(j => j.TotalBytes) <= limits.MaxBytesPerSession)
            {
                return;
            }

            _jobs.Remove(sessionJobs[0].ClientJobId);
        }
    }

    private void EnforceHostLimitUnlocked()
    {
        while (_jobs.Values.Sum(j => j.TotalBytes) > limits.MaxBytesPerHost && _jobs.Count > 0)
        {
            var oldest = _jobs.Values.OrderBy(j => j.CreatedAtUtc).First();
            _jobs.Remove(oldest.ClientJobId);
        }
    }

    private sealed record JobArtifacts(
        string SessionId,
        string ClientJobId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        Dictionary<string, ArtifactEntry> Entries,
        long TotalBytes);
}
