using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YsFourcut.Host.Profiles;

namespace YsFourcut.Host.Worker;

/// <summary>
/// 호스트와 렌더 worker 사이의 파이프 메시지. 크기를 앞에 명시한 이진 프레임이며
/// 표준 출력 로그와 섞이지 않는다. 이미지·base64를 로그로 내보내지 않는다.
/// </summary>
public static class RenderWorkerProtocol
{
    public const int Version = 1;

    public const string WorkerArgument = "--print-worker";
    public const string RequestPipeArgument = "--request-pipe";
    public const string ResponsePipeArgument = "--response-pipe";
    public const string ParentPidArgument = "--parent-pid";

    /// <summary>프레임 하나의 JSON 머리말 상한. 픽셀 데이터는 머리말 뒤에 원시 바이트로 붙는다.</summary>
    public const int MaxHeaderBytes = 64 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task WriteFrameAsync<T>(
        Stream stream,
        T header,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions);
        if (json.Length > MaxHeaderBytes)
        {
            throw new InvalidOperationException("파이프 머리말이 상한을 넘었습니다.");
        }

        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, json.Length);

        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(json, cancellationToken);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadHeaderAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        await ReadExactlyAsync(stream, prefix, cancellationToken);

        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length <= 0 || length > MaxHeaderBytes)
        {
            throw new InvalidOperationException($"파이프 머리말 길이가 올바르지 않습니다: {length}");
        }

        var json = new byte[length];
        await ReadExactlyAsync(stream, json, cancellationToken);

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException("파이프 머리말을 해석하지 못했습니다.");
    }

    public static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = await stream.ReadAsync(buffer[read..], cancellationToken);
            if (chunk == 0)
            {
                throw new EndOfStreamException("파이프가 예상보다 먼저 닫혔습니다.");
            }

            read += chunk;
        }
    }

    public static string Describe(ReadOnlySpan<byte> _) => "<binary>";

    public static Encoding HeaderEncoding => Encoding.UTF8;
}

// ── 메시지 ────────────────────────────────────────────────────────────────────

public sealed record RenderWorkerRequest(
    int ProtocolVersion,
    string HostInstanceId,
    string ClientJobId,
    PrinterProfile Profile,
    RenderWorkerBitmap Bitmap,
    RenderWorkerAppearance Appearance,
    RenderWorkerLimits Limits,
    RenderWorkerFault? Fault = null);

/// <summary>가상 모드의 모의 오류. 실제 장치 상태가 아니다.</summary>
public sealed record RenderWorkerFault(string Kind, int? FailAfterRows);

public sealed record RenderWorkerBitmap(
    int WidthDots,
    int HeightDots,
    int StrideBytes,
    int ByteLength,
    string SourceDigest);

public sealed record RenderWorkerAppearance(long Seed, IReadOnlyList<string> EstimatedEffects);

public sealed record RenderWorkerLimits(int MaxPixelsPerImage, int MaxEncodedBytesPerJob);

/// <summary>
/// worker 결과. <c>Partial</c>은 실패했지만 진단용 부분 이미지가 있다는 뜻이며 정상 결과가 아니다.
/// </summary>
public sealed record RenderWorkerResponse(
    int ProtocolVersion,
    bool Ok,
    string HostInstanceId,
    string ClientJobId,
    RenderWorkerLayout? Layout = null,
    string? SourceDigest = null,
    RenderWorkerAppearanceResult? Appearance = null,
    IReadOnlyList<RenderWorkerArtifact>? Artifacts = null,
    string? Code = null,
    string? Message = null,
    bool Partial = false);

public sealed record RenderWorkerLayout(
    int PaperWidthDots,
    int PaperHeightDots,
    int ContentXDots,
    int ContentYDots,
    int ContentWidthDots,
    int ContentHeightDots,
    int LeadingFeedDots,
    int TrailingFeedDots,
    double DpiX,
    double DpiY);

public sealed record RenderWorkerAppearanceResult(
    string Revision,
    long Seed,
    IReadOnlyList<string> EstimatedEffects);

public sealed record RenderWorkerArtifact(string Kind, int ByteLength, int WidthPx, int HeightPx);
