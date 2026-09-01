using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace YsFourcut.Host.Security;

public sealed record LocalSession(string SessionId, DateTimeOffset CreatedAtUtc)
{
    public DateTimeOffset LastSeenUtc { get; set; } = CreatedAtUtc;
}

/// <summary>
/// 호스트 프로세스 수명 안에서만 유효한 메모리 세션. 쿠키 값(토큰)은 로그에 남기지 않는다.
/// 세션 ID는 결과 이미지 소유권 확인에 쓰기 위해 별도로 둔다(03/05번).
/// </summary>
public sealed class SessionStore(TimeProvider clock)
{
    private const int MaxSessions = 16;

    private readonly ConcurrentDictionary<string, LocalSession> _sessions = new(StringComparer.Ordinal);

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromHours(12);

    public (string Token, LocalSession Session) Create()
    {
        PruneExpired();

        if (_sessions.Count >= MaxSessions)
        {
            var oldest = _sessions.OrderBy(pair => pair.Value.LastSeenUtc).FirstOrDefault();
            if (oldest.Key is not null)
            {
                _sessions.TryRemove(oldest.Key, out _);
            }
        }

        var now = clock.GetUtcNow();
        var token = CreateOpaqueToken();
        var session = new LocalSession(CreateOpaqueToken(), now);
        _sessions[token] = session;
        return (token, session);
    }

    public LocalSession? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_sessions.TryGetValue(token, out var session))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        if (now - session.LastSeenUtc > IdleTimeout)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        session.LastSeenUtc = now;
        return session;
    }

    public void Remove(string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            _sessions.TryRemove(token, out _);
        }
    }

    public int Count => _sessions.Count;

    private void PruneExpired()
    {
        var now = clock.GetUtcNow();
        foreach (var pair in _sessions)
        {
            if (now - pair.Value.LastSeenUtc > IdleTimeout)
            {
                _sessions.TryRemove(pair.Key, out _);
            }
        }
    }

    internal static string CreateOpaqueToken()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
