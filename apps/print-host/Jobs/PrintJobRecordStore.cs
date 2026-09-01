using System.Text.Json;
using System.Text.Json.Serialization;

namespace YsFourcut.Host.Jobs;

/// <summary>
/// 사진 없는 작업 기록. clientJobId, 요청 digest, 프로필 revision, 상태, spool ID, 시각만 남긴다.
/// 사진·비트맵·base64·인증값은 절대 넣지 않는다.
/// </summary>
public sealed record PrintJobRecord(
    int SchemaVersion,
    string ClientJobId,
    string RequestDigest,
    string ProfileId,
    string ProfileRevision,
    string PrinterMode,
    string State,
    string? SpoolJobId,
    string? FailureCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    // 이 기록을 만든 호스트 실행. 이전 실행의 기록만 상태 복원에 쓴다.
    // 이 필드가 없는 예전 기록은 필연적으로 이전 실행이므로 null로 읽어 같게 취급한다.
    string? HostInstanceId = null);

/// <summary>
/// 작업 기록을 원자적으로(임시 파일 + rename) 저장한다. 기록에 실패하면 작업을 시작하지 않는다.
/// 완료 기록은 24시간 뒤 정리하고, 해결되지 않은 기록은 자동 삭제하지 않는다.
/// </summary>
public sealed class PrintJobRecordStore
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly ILogger<PrintJobRecordStore> _logger;

    public PrintJobRecordStore(string stateDirectory, ILogger<PrintJobRecordStore> logger)
    {
        _directory = Path.Combine(stateDirectory, "jobs");
        _logger = logger;
    }

    public string Directory => _directory;

    /// <summary>실패하면 예외를 던진다. 호출자는 인쇄를 시작하지 않아야 한다.</summary>
    public void Write(PrintJobRecord record)
    {
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(_directory);

            var path = PathFor(record.ClientJobId);
            var temporary = path + ".tmp";

            File.WriteAllText(temporary, JsonSerializer.Serialize(record, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
    }

    public IReadOnlyList<PrintJobRecord> ReadAll()
    {
        lock (_gate)
        {
            if (!System.IO.Directory.Exists(_directory))
            {
                return [];
            }

            var records = new List<PrintJobRecord>();
            foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<PrintJobRecord>(File.ReadAllText(file), JsonOptions);
                    if (record is not null)
                    {
                        records.Add(record);
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    _logger.LogWarning("작업 기록을 읽지 못했습니다: {Reason}", ex.GetType().Name);
                }
            }

            return records;
        }
    }

    /// <summary>UUID로 파일 하나만 읽는다. 없으면 null이다.</summary>
    public PrintJobRecord? TryRead(string clientJobId)
    {
        if (!Guid.TryParseExact(clientJobId, "D", out _))
        {
            return null;
        }

        lock (_gate)
        {
            var path = PathFor(clientJobId);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<PrintJobRecord>(File.ReadAllText(path), JsonOptions);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning("작업 기록을 읽지 못했습니다: {Reason}", ex.GetType().Name);
                return null;
            }
        }
    }

    public void Delete(string clientJobId)
    {
        lock (_gate)
        {
            var path = PathFor(clientJobId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private string PathFor(string clientJobId)
    {
        // clientJobId는 접수 전에 UUID로 검증한다. 경로 조작 값이 들어올 수 없다.
        if (!Guid.TryParseExact(clientJobId, "D", out _))
        {
            throw new ArgumentException("clientJobId가 UUID 형식이 아닙니다.", nameof(clientJobId));
        }

        return Path.Combine(_directory, clientJobId + ".json");
    }
}
