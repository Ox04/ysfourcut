using YsFourcut.Host.Profiles;

namespace YsFourcut.Host.Rendering;

/// <summary>
/// 지면 배치. 기준은 정수 dot이며 mm는 표시용으로 계산만 한다.
/// paperHeightDots = leadingFeedDots + contentHeightDots + trailingFeedDots.
/// 라이브러리 기본 프로필(PaperConfiguration)을 쓰지 않고 앱이 명시한 dot로 계산한다.
/// </summary>
public sealed record ReceiptLayout(
    int PaperWidthDots,
    int PaperHeightDots,
    int ContentXDots,
    int ContentYDots,
    int ContentWidthDots,
    int ContentHeightDots,
    int LeadingFeedDots,
    int TrailingFeedDots,
    double DotsPerMmX,
    double DotsPerMmY)
{
    public double DpiX => DotsPerMmX * 25.4;

    public double DpiY => DotsPerMmY * 25.4;

    public double PaperWidthMm => PaperWidthDots / DotsPerMmX;

    public double PaperHeightMm => PaperHeightDots / DotsPerMmY;

    public long PaperPixelCount => (long)PaperWidthDots * PaperHeightDots;

    public long ContentPixelCount => (long)ContentWidthDots * ContentHeightDots;

    /// <summary>
    /// 내용은 인쇄 가능 영역의 원점(좌우 여백만큼 안쪽, 앞 이송만큼 아래)에 1:1로 놓는다.
    /// 인쇄 가능 폭보다 좁은 내용은 왼쪽 정렬한다. 자동 확대/축소는 하지 않는다.
    /// </summary>
    public static ReceiptLayout Create(PrinterProfile profile, int contentWidthDots, int contentHeightDots)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (contentWidthDots <= 0 || contentHeightDots <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentWidthDots), "내용 크기는 양의 정수 dot여야 합니다.");
        }

        var paper = profile.Paper;

        if (contentWidthDots > paper.ContentWidthDots)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentWidthDots),
                $"내용 폭 {contentWidthDots}dot가 프로필의 인쇄 가능 폭 {paper.ContentWidthDots}dot를 넘습니다.");
        }

        var paperHeightDots = paper.LeadingFeedDots + contentHeightDots + paper.TrailingFeedDots;

        var layout = new ReceiptLayout(
            PaperWidthDots: paper.PaperWidthDots,
            PaperHeightDots: paperHeightDots,
            ContentXDots: paper.SideMarginDots,
            ContentYDots: paper.LeadingFeedDots,
            ContentWidthDots: contentWidthDots,
            ContentHeightDots: contentHeightDots,
            LeadingFeedDots: paper.LeadingFeedDots,
            TrailingFeedDots: paper.TrailingFeedDots,
            DotsPerMmX: paper.DotsPerMmX,
            DotsPerMmY: paper.DotsPerMmY);

        layout.EnsureValid();
        return layout;
    }

    /// <summary>음수 여백·내용 초과·이송 중복이 없는지 확인한다.</summary>
    public void EnsureValid()
    {
        if (LeadingFeedDots < 0 || TrailingFeedDots < 0 || ContentXDots < 0 || ContentYDots < 0)
        {
            throw new InvalidOperationException("여백과 이송은 음수일 수 없습니다.");
        }

        if (ContentXDots + ContentWidthDots > PaperWidthDots)
        {
            throw new InvalidOperationException(
                $"내용이 용지 폭을 벗어납니다: {ContentXDots} + {ContentWidthDots} > {PaperWidthDots}");
        }

        if (ContentYDots != LeadingFeedDots ||
            PaperHeightDots != LeadingFeedDots + ContentHeightDots + TrailingFeedDots)
        {
            throw new InvalidOperationException("앞뒤 이송이 지면 높이와 맞지 않습니다.");
        }

        if (DotsPerMmX <= 0 || DotsPerMmY <= 0)
        {
            throw new InvalidOperationException("dot/mm 값은 양수여야 합니다.");
        }
    }
}
