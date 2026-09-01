namespace YsFourcut.Host.Printing.Virtual;

/// <summary>
/// 가상 모드 전용 실패 주입. **모의 오류**이며 실제 센서·장치 상태가 아니다.
/// 다음 작업 하나에만 적용되고 그 뒤 자동으로 해제된다.
/// </summary>
public static class VirtualFaultKinds
{
    /// <summary>지연만 준다. 렌더 시작 전에 적용하며 렌더 예산에서 제외한다.</summary>
    public const string Delay = "delay";

    public const string OutOfPaper = "out_of_paper";
    public const string CoverOpen = "cover_open";
    public const string Offline = "offline";

    /// <summary>렌더링을 시작하기 전에 실패. 결과 이미지가 하나도 없다.</summary>
    public const string FailBeforeRender = "fail_before_render";

    /// <summary>N행까지 그린 뒤 실패. 진단용 부분 이미지만 남는다.</summary>
    public const string FailAfterRows = "fail_after_rows";

    /// <summary>worker가 응답하지 않는 상황. 부모가 예산 초과로 정리한다.</summary>
    public const string RenderTimeout = "render_timeout";

    public static readonly IReadOnlyList<string> All =
    [
        Delay, OutOfPaper, CoverOpen, Offline, FailBeforeRender, FailAfterRows, RenderTimeout,
    ];

    public static bool IsKnown(string kind) => All.Contains(kind, StringComparer.Ordinal);

    /// <summary>worker까지 전달해야 하는 주입인지. 나머지는 호스트에서 렌더 전에 처리한다.</summary>
    public static bool RunsInWorker(string kind) => kind is FailAfterRows or RenderTimeout;
}

public sealed record VirtualFault(string Kind, int DelayMs, int? FailAfterRows)
{
    public const int MinDelayMs = 500;
    public const int MaxDelayMs = 10_000;
}

/// <summary>
/// 주입된 실패를 한 번만 적용하기 위한 저장소. 작업 시작 시점에 가져가면서 비운다.
/// 화면이 항상 주입 상태를 보여 줄 수 있도록 조회도 제공한다.
/// </summary>
public sealed class VirtualFaultStore
{
    private readonly object _gate = new();
    private VirtualFault? _pending;

    public VirtualFault? Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    public void Set(VirtualFault? fault)
    {
        lock (_gate)
        {
            _pending = fault;
        }
    }

    /// <summary>작업 시작 때 한 번 가져가고 해제한다.</summary>
    public VirtualFault? Take()
    {
        lock (_gate)
        {
            var fault = _pending;
            _pending = null;
            return fault;
        }
    }
}
