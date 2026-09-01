using System.Security.Cryptography;
using System.Text;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Rendering;

namespace YsFourcut.Host.Jobs;

public static class PrintJobStates
{
    public const string Accepted = "accepted";
    public const string Rendering = "rendering";
    public const string Rendered = "rendered";
    public const string VirtualFailed = "virtual_failed";

    // 실물 경로(91번)에서 사용할 상태. 가상에서는 쓰지 않는다.
    public const string Submitting = "submitting";
    public const string Submitted = "submitted";
    public const string FailedBeforeSubmit = "failed_before_submit";
    public const string OutcomeUnknown = "outcome_unknown";

    public static bool IsTerminal(string state)
        => state is Rendered or VirtualFailed or Submitted or FailedBeforeSubmit or OutcomeUnknown;
}

/// <summary>
/// 진행 중인 작업 하나의 메모리 상태. 접수 시점에 프로필·모드·외형 revision·seed를 스냅샷으로 고정해
/// 대기 중 설정이 바뀌어도 결과가 달라지지 않게 한다.
/// </summary>
public sealed class PrintJob
{
    public required string ClientJobId { get; init; }

    /// <summary>결과 이미지 소유권. 다른 세션은 이 작업을 조회할 수 없다.</summary>
    public required string SessionId { get; init; }

    /// <summary>같은 UUID로 같은 내용을 다시 보냈는지 판단하는 값.</summary>
    public required string RequestDigest { get; init; }

    public required string SourceDigest { get; init; }

    public required PrinterProfile ProfileSnapshot { get; init; }

    public required string PrinterMode { get; init; }

    public required long AppearanceSeed { get; init; }

    public required string AppearanceRevision { get; init; }

    public required IReadOnlyList<string> EstimatedEffects { get; init; }

    /// <summary>이 작업에 적용한 모의 오류. 접수 시점에 고정되며 이후 주입 변경의 영향을 받지 않는다.</summary>
    public Printing.Virtual.VirtualFault? InjectedFault { get; init; }

    /// <summary>세션 정리 세대. 작업이 도는 동안 정리되면 결과를 게시하지 않는다.</summary>
    public required long ClearGeneration { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public string State { get; set; } = PrintJobStates.Accepted;

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ReceiptLayout? Layout { get; set; }

    public string? FailureCode { get; set; }

    public string? FailureMessage { get; set; }

    /// <summary>진단용 부분 이미지만 있는 상태. 정상 완료가 아니다.</summary>
    public bool HasPartialArtifacts { get; set; }

    /// <summary>실제로 게시한 결과 종류. 만료 뒤에도 "있었다가 사라짐"과 "애초에 없음"을 구분한다.</summary>
    public IReadOnlyList<string> PublishedArtifactKinds { get; set; } = [];

    /// <summary>기록에서 복원한 작업(호스트 재시작 뒤)은 결과 이미지가 없다.</summary>
    public bool RestoredFromRecord { get; init; }

    /// <summary>가상은 항상 false / null이다.</summary>
    public bool IsPhysical => false;

    public string? SpoolJobId => null;

    /// <summary>terminal 상태면 잠금이 풀린다.</summary>
    public bool QueueBusy => !PrintJobStates.IsTerminal(State);

    /// <summary>같은 UUID + 같은 내용인지 확인하는 digest. 사진 자체는 담기지 않는다.</summary>
    public static string ComputeRequestDigest(string sourceDigest, string profileId, string profileRevision)
    {
        var canonical = string.Join('|', "job1", sourceDigest, profileId, profileRevision);
        return "sha256-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>작업 ID에서 결정적으로 만드는 외형 seed. 같은 작업을 다시 렌더해도 질감이 같다.</summary>
    public static long ComputeAppearanceSeed(string clientJobId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("seed1|" + clientJobId));
        return BitConverter.ToInt64(hash, 0) & long.MaxValue;
    }
}
