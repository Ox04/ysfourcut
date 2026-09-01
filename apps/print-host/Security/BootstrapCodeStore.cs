using System.Collections.Concurrent;

namespace YsFourcut.Host.Security;

public enum BootstrapExchange
{
    Ok,
    Invalid,
    Expired,
    AlreadyUsed,
}

/// <summary>
/// 256비트 난수 일회용 코드. 60초 안에 한 번만 교환할 수 있다 (SERVICE_DESIGN.md 10절).
/// 코드는 메모리에만 두고 로그·응답·파일에 남기지 않는다. URL fragment로만 브라우저에 전달한다.
/// </summary>
public sealed class BootstrapCodeStore(TimeProvider clock)
{
    private const int MaxLiveCodes = 4;

    private readonly ConcurrentDictionary<string, CodeEntry> _codes = new(StringComparer.Ordinal);

    public TimeSpan Ttl { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>만료·사용된 코드를 이 시간까지는 남겨 두어 만료/재사용을 구분해 응답한다.</summary>
    public TimeSpan RetentionGrace { get; init; } = TimeSpan.FromMinutes(5);

    public string Issue()
    {
        Prune();

        if (CountUsable() >= MaxLiveCodes)
        {
            var oldest = _codes.OrderBy(pair => pair.Value.IssuedAtUtc).FirstOrDefault();
            if (oldest.Key is not null)
            {
                _codes.TryRemove(oldest.Key, out _);
            }
        }

        var code = SessionStore.CreateOpaqueToken();
        _codes[code] = new CodeEntry(clock.GetUtcNow());
        return code;
    }

    public BootstrapExchange TryConsume(string? code)
    {
        Prune();

        if (string.IsNullOrEmpty(code) || !_codes.TryGetValue(code, out var entry))
        {
            return BootstrapExchange.Invalid;
        }

        if (entry.Used)
        {
            return BootstrapExchange.AlreadyUsed;
        }

        if (clock.GetUtcNow() - entry.IssuedAtUtc > Ttl)
        {
            return BootstrapExchange.Expired;
        }

        _codes[code] = entry with { Used = true };
        return BootstrapExchange.Ok;
    }

    /// <summary>아직 교환할 수 있는 코드 수.</summary>
    public int UsableCodeCount => CountUsable();

    private int CountUsable()
    {
        var now = clock.GetUtcNow();
        return _codes.Count(pair => !pair.Value.Used && now - pair.Value.IssuedAtUtc <= Ttl);
    }

    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (var pair in _codes)
        {
            if (now - pair.Value.IssuedAtUtc > Ttl + RetentionGrace)
            {
                _codes.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record CodeEntry(DateTimeOffset IssuedAtUtc)
    {
        public bool Used { get; init; }
    }
}
