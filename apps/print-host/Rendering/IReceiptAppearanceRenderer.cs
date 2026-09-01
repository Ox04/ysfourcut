using CrossEscPos.Graphics;

namespace YsFourcut.Host.Rendering;

/// <summary>
/// 04번이 구현할 실물 느낌 렌더러의 입력. 지면 원본(Paper)과 배치 메타데이터를 그대로 넘긴다.
/// Paper 이미지의 수명은 호출자가 관리하며 Render 호출 동안에만 유효하다.
/// 내용/지면 원본을 수정하면 안 된다. 별도 캔버스에 그린다.
/// </summary>
public sealed record ReceiptAppearanceRequest(
    ReceiptLayout Layout,
    IReceiptImage Paper,
    string CutStyle,
    string AppearanceRevision,
    long Seed,
    IReadOnlyList<string> EstimatedEffects);

public sealed record ReceiptAppearanceResult(
    byte[] PngBytes,
    int WidthPx,
    int HeightPx,
    string AppearanceRevision,
    long Seed,
    IReadOnlyList<string> EstimatedEffects);

public interface IReceiptAppearanceRenderer
{
    /// <summary>false면 렌더러가 appearance를 만들지 않고, 결과에서 미제공으로 표시한다.</summary>
    bool IsImplemented { get; }

    /// <summary>이 외관 revision이 만드는 캔버스 크기. 그리기 전에 픽셀 한도를 검사하기 위해 쓴다.</summary>
    (int WidthPx, int HeightPx) Measure(ReceiptLayout layout);

    ReceiptAppearanceResult Render(ReceiptAppearanceRequest request);
}

/// <summary>
/// 03번 시점의 기본 구현. appearance는 **아직 구현되지 않았다**.
/// 빈 이미지나 지면 복사본을 대신 돌려주어 성공처럼 보이게 하지 않는다.
/// </summary>
public sealed class UnimplementedAppearanceRenderer : IReceiptAppearanceRenderer
{
    public const string NotImplementedReason = "appearance는 tasks/04.md에서 구현합니다.";

    public bool IsImplemented => false;

    public (int WidthPx, int HeightPx) Measure(ReceiptLayout layout)
        => throw new NotImplementedException(NotImplementedReason);

    public ReceiptAppearanceResult Render(ReceiptAppearanceRequest request)
        => throw new NotImplementedException(NotImplementedReason);
}
