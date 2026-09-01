using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YsFourcut.Host.Profiles;

public static class ProfileKinds
{
    public const string Virtual = "virtual";
    public const string Hardware = "hardware";
}

public static class CutStyles
{
    public const string Straight = "straight";
    public const string Tear = "tear";
    public const string None = "none";

    public static bool IsValid(string value)
        => value is Straight or Tear or None;
}

/// <summary>
/// 용지 배치의 기준은 정수 dot다. mm 값은 설명용으로 계산해 표시만 한다.
/// paperWidthDots = contentWidthDots + sideMarginDots * 2 를 항상 만족해야 한다.
/// </summary>
public sealed record PaperGeometry(
    int PaperWidthDots,
    int ContentWidthDots,
    int SideMarginDots,
    int LeadingFeedDots,
    int TrailingFeedDots,
    double DotsPerMmX,
    double DotsPerMmY,
    string CutStyle)
{
    public double DpiX => DotsPerMmX * 25.4;
    public double DpiY => DotsPerMmY * 25.4;
    public double PaperWidthMm => PaperWidthDots / DotsPerMmX;
    public double ContentWidthMm => ContentWidthDots / DotsPerMmX;
}

public sealed record ProfileLimits(
    int MaxContentWidthDots,
    int MaxContentHeightDots,
    int MaxDecodedBytes);

public sealed record ProfileVerification(
    bool Verified,
    string? VerifiedRevision,
    DateTimeOffset? VerifiedAtUtc,
    string? TestJobId)
{
    public static ProfileVerification None { get; } = new(false, null, null, null);
}

public sealed record PrinterProfile(
    string ProfileId,
    string Kind,
    string DisplayName,
    PaperGeometry Paper,
    ProfileLimits Limits,
    string Notes)
{
    /// <summary>
    /// 인쇄 핵심 설정만으로 계산하는 결정적 revision. 표시 이름·메모는 포함하지 않는다.
    /// 같은 설정이면 호스트를 재시작해도 같은 값이므로 후속 작업의 프로필 스냅샷과 정확히 비교할 수 있다.
    /// 인쇄 핵심 설정이 바뀌면 값이 바뀌고, 검증 기록은 해제된다.
    /// </summary>
    public string Revision => ComputeRevision(this);

    public static string ComputeRevision(PrinterProfile profile)
    {
        var canonical = string.Join('|',
            "v1",
            profile.ProfileId,
            profile.Kind,
            profile.Paper.PaperWidthDots.ToString(CultureInfo.InvariantCulture),
            profile.Paper.ContentWidthDots.ToString(CultureInfo.InvariantCulture),
            profile.Paper.SideMarginDots.ToString(CultureInfo.InvariantCulture),
            profile.Paper.LeadingFeedDots.ToString(CultureInfo.InvariantCulture),
            profile.Paper.TrailingFeedDots.ToString(CultureInfo.InvariantCulture),
            profile.Paper.DotsPerMmX.ToString("R", CultureInfo.InvariantCulture),
            profile.Paper.DotsPerMmY.ToString("R", CultureInfo.InvariantCulture),
            profile.Paper.CutStyle,
            profile.Limits.MaxContentWidthDots.ToString(CultureInfo.InvariantCulture),
            profile.Limits.MaxContentHeightDots.ToString(CultureInfo.InvariantCulture),
            profile.Limits.MaxDecodedBytes.ToString(CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "r1-" + Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    /// <summary>프리셋 JSON을 읽은 직후 확인한다. 음수 여백·내용 초과를 허용하지 않는다.</summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(ProfileId))
        {
            throw new InvalidOperationException("프로필 ID가 비어 있습니다.");
        }

        if (Kind is not (ProfileKinds.Virtual or ProfileKinds.Hardware))
        {
            throw new InvalidOperationException($"'{ProfileId}': kind는 virtual 또는 hardware여야 합니다.");
        }

        if (Paper.ContentWidthDots <= 0 || Paper.SideMarginDots < 0 ||
            Paper.LeadingFeedDots < 0 || Paper.TrailingFeedDots < 0)
        {
            throw new InvalidOperationException($"'{ProfileId}': dot 값은 음수일 수 없고 내용 폭은 양수여야 합니다.");
        }

        if (Paper.PaperWidthDots != Paper.ContentWidthDots + (Paper.SideMarginDots * 2))
        {
            throw new InvalidOperationException(
                $"'{ProfileId}': paperWidthDots({Paper.PaperWidthDots})가 " +
                $"contentWidthDots + sideMarginDots*2({Paper.ContentWidthDots + (Paper.SideMarginDots * 2)})와 다릅니다.");
        }

        if (Paper.DotsPerMmX <= 0 || Paper.DotsPerMmY <= 0)
        {
            throw new InvalidOperationException($"'{ProfileId}': dot/mm 값은 양수여야 합니다.");
        }

        if (!CutStyles.IsValid(Paper.CutStyle))
        {
            throw new InvalidOperationException($"'{ProfileId}': cutStyle은 straight/tear/none 중 하나여야 합니다.");
        }

        if (Limits.MaxContentWidthDots <= 0 || Limits.MaxContentHeightDots <= 0 || Limits.MaxDecodedBytes <= 0)
        {
            throw new InvalidOperationException($"'{ProfileId}': limits 값은 양수여야 합니다.");
        }

        if (Limits.MaxContentWidthDots > Paper.ContentWidthDots)
        {
            throw new InvalidOperationException(
                $"'{ProfileId}': maxContentWidthDots가 인쇄 내용 폭보다 큽니다.");
        }
    }
}
