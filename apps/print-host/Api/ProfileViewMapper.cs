using YsFourcut.Host.Contracts;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Profiles;

namespace YsFourcut.Host.Api;

public static class ProfileViewMapper
{
    public static PrinterProfileView ToView(
        PrinterProfile profile,
        ProfileVerification verification,
        bool isSelected)
    {
        var applicable = string.Equals(verification.VerifiedRevision, profile.Revision, StringComparison.Ordinal)
            && verification.Verified;

        return new PrinterProfileView(
            ProfileId: profile.ProfileId,
            Revision: profile.Revision,
            Kind: profile.Kind,
            DisplayName: profile.DisplayName,
            // 가상 프로필의 사용 가능 상태를 hardwareVerified=true로 표현하지 않는다.
            HardwareVerified: applicable && profile.Kind == ProfileKinds.Hardware,
            IsSelected: isSelected,
            Paper: new PaperGeometryView(
                PaperWidthDots: profile.Paper.PaperWidthDots,
                ContentWidthDots: profile.Paper.ContentWidthDots,
                SideMarginDots: profile.Paper.SideMarginDots,
                LeadingFeedDots: profile.Paper.LeadingFeedDots,
                TrailingFeedDots: profile.Paper.TrailingFeedDots,
                DotsPerMmX: profile.Paper.DotsPerMmX,
                DotsPerMmY: profile.Paper.DotsPerMmY,
                DpiX: profile.Paper.DpiX,
                DpiY: profile.Paper.DpiY,
                PaperWidthMm: profile.Paper.PaperWidthMm,
                ContentWidthMm: profile.Paper.ContentWidthMm,
                CutStyle: profile.Paper.CutStyle),
            Limits: new ProfileLimitsView(
                profile.Limits.MaxContentWidthDots,
                profile.Limits.MaxContentHeightDots,
                profile.Limits.MaxDecodedBytes),
            Verification: new VerificationView(
                applicable,
                applicable ? verification.VerifiedRevision : null,
                applicable ? verification.VerifiedAtUtc : null,
                applicable ? verification.TestJobId : null),
            Notes: profile.Notes);
    }

    public static ServiceLimitsView ToView(ServiceLimits limits)
        => new(limits.MaxRequestBodyBytes, limits.MaxWidthDots, limits.MaxHeightDots, limits.MaxDecodedBytes);
}
