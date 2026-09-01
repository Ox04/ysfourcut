using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using YsFourcut.Host.Contracts;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Printing;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Security;

namespace YsFourcut.Host.Tests;

public sealed class PrinterModeResolverTests
{
    [Fact]
    public void Defaults_to_virtual()
    {
        Assert.Equal(PrinterMode.Virtual, PrinterModeResolver.Resolve(null, isWindows: false));
        Assert.Equal(PrinterMode.Virtual, PrinterModeResolver.Resolve("virtual", isWindows: false));
    }

    [Fact]
    public void Refuses_physical_mode_on_non_windows()
    {
        var error = Assert.Throws<PrinterModeUnsupportedException>(
            () => PrinterModeResolver.Resolve("physical", isWindows: false));

        // 자동으로 virtual로 바꾸지 않고 시작을 거부한다.
        Assert.Contains(ErrorCodes.PlatformUnsupported, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Allows_physical_mode_on_windows()
        => Assert.Equal(PrinterMode.Physical, PrinterModeResolver.Resolve("physical", isWindows: true));

    [Fact]
    public void Rejects_unknown_mode()
        => Assert.Throws<PrinterModeUnsupportedException>(() => PrinterModeResolver.Resolve("real", isWindows: true));
}

public sealed class PrintJobRequestValidatorTests
{
    private static readonly PrinterProfile Profile = TestProfiles.Virtual80mm;

    [Fact]
    public void Accepts_exact_stride_and_white_padding()
    {
        // 폭 9 → stride 2, 마지막 바이트의 하위 7비트는 패딩.
        var data = new byte[] { 0b1010_1010, 0b1000_0000, 0b1111_1111, 0b0000_0000 };
        var bitmap = new PrintJobBitmap(9, 2, 2, "msb-first", 1, Convert.ToBase64String(data));

        var ok = PrintJobRequestValidator.TryValidateBitmap(
            bitmap, Profile, ServiceLimits.Default, out var decoded, out var failure);

        Assert.True(ok);
        Assert.Null(failure);
        Assert.NotNull(decoded);
        Assert.Equal(9, decoded!.WidthDots);
        Assert.Equal(2, decoded.StrideBytes);
        Assert.StartsWith("sha256-", decoded.SourceDigest);
    }

    [Fact]
    public void Rejects_black_padding_in_last_row()
    {
        // 마지막 행의 패딩 비트만 검게 만든다.
        var data = new byte[] { 0b0000_0000, 0b0000_0000, 0b0000_0000, 0b0100_0000 };
        var bitmap = new PrintJobBitmap(9, 2, 2, "msb-first", 1, Convert.ToBase64String(data));

        var ok = PrintJobRequestValidator.TryValidateBitmap(
            bitmap, Profile, ServiceLimits.Default, out _, out var failure);

        Assert.False(ok);
        Assert.Equal(ErrorCodes.InvalidBitmap, failure!.Detail.Code);
        Assert.Equal("padding_not_white", failure.Detail.Details!["reason"]);
        Assert.Equal(1, failure.Detail.Details["row"]);
    }

    [Fact]
    public void Accepts_full_byte_width_without_padding_checks()
    {
        var bitmap = new PrintJobBitmap(16, 2, 2, "msb-first", 1, Convert.ToBase64String(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }));

        var ok = PrintJobRequestValidator.TryValidateBitmap(
            bitmap, Profile, ServiceLimits.Default, out _, out var failure);

        Assert.True(ok);
        Assert.Null(failure);
    }

    [Fact]
    public void Rejects_unsupported_bit_order_and_black_bit()
    {
        var lsb = new PrintJobBitmap(8, 1, 1, "lsb-first", 1, Convert.ToBase64String(new byte[1]));
        Assert.False(PrintJobRequestValidator.TryValidateBitmap(lsb, Profile, ServiceLimits.Default, out _, out var f1));
        Assert.Equal("bit_order_unsupported", f1!.Detail.Details!["reason"]);

        var zeroBlack = new PrintJobBitmap(8, 1, 1, "msb-first", 0, Convert.ToBase64String(new byte[1]));
        Assert.False(PrintJobRequestValidator.TryValidateBitmap(zeroBlack, Profile, ServiceLimits.Default, out _, out var f2));
        Assert.Equal("black_bit_unsupported", f2!.Detail.Details!["reason"]);
    }

    [Fact]
    public void Rejects_decoded_data_over_limit_before_allocating()
    {
        var limits = new ServiceLimits(MaxDecodedBytes: 1024);
        var bitmap = new PrintJobBitmap(576, 100, 72, "msb-first", 1, "AA==");

        var ok = PrintJobRequestValidator.TryValidateBitmap(bitmap, Profile, limits, out _, out var failure);

        Assert.False(ok);
        Assert.Equal(ErrorCodes.PageSizeUnsupported, failure!.Detail.Code);
        Assert.Equal("decodedBytes", failure.Detail.Details!["limit"]);
    }

    [Fact]
    public void Digest_depends_on_dimensions_and_content()
    {
        var data = new byte[] { 0x0F, 0xF0 };
        var a = PrintJobRequestValidator.ComputeDigest(data, 16, 1);
        var b = PrintJobRequestValidator.ComputeDigest(data, 8, 2);
        var c = PrintJobRequestValidator.ComputeDigest(data, 16, 1);

        Assert.Equal(a, c);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Envelope_requires_schema_version_and_uuid()
    {
        var bitmap = new PrintJobBitmap(8, 1, 1, "msb-first", 1, "AA==");

        Assert.False(PrintJobRequestValidator.TryValidateEnvelope(
            new PrintJobRequest(2, Guid.NewGuid().ToString(), "p", "r", bitmap), out var versionFailure));
        Assert.Equal(ErrorCodes.SchemaVersionUnsupported, versionFailure!.Detail.Code);

        Assert.False(PrintJobRequestValidator.TryValidateEnvelope(
            new PrintJobRequest(1, "not-a-uuid", "p", "r", bitmap), out var idFailure));
        Assert.Equal(ErrorCodes.ValidationFailed, idFailure!.Detail.Code);

        Assert.True(PrintJobRequestValidator.TryValidateEnvelope(
            new PrintJobRequest(1, Guid.NewGuid().ToString(), "p", "r", bitmap), out var ok));
        Assert.Null(ok);
    }

    [Fact]
    public void Virtual_mode_refuses_hardware_profile()
    {
        var hardware = TestProfiles.Virtual80mm with { ProfileId = "ahapos-x", Kind = ProfileKinds.Hardware };

        var ok = PrintJobRequestValidator.TryValidateProfileCompatibility(
            PrinterMode.Virtual, hardware, ProfileVerification.None, out var failure);

        Assert.False(ok);
        Assert.Equal(ErrorCodes.ProfileKindMismatch, failure!.Detail.Code);
    }

    [Fact]
    public void Physical_mode_refuses_unverified_profile()
    {
        var hardware = TestProfiles.Virtual80mm with { ProfileId = "ahapos-x", Kind = ProfileKinds.Hardware };

        var ok = PrintJobRequestValidator.TryValidateProfileCompatibility(
            PrinterMode.Physical, hardware, ProfileVerification.None, out var failure);

        Assert.False(ok);
        Assert.Equal(ErrorCodes.ProfileUnverified, failure!.Detail.Code);
    }
}

public sealed class PrinterProfileTests
{
    [Fact]
    public void Revision_is_deterministic_for_same_print_settings()
    {
        var a = TestProfiles.Virtual80mm;
        var b = TestProfiles.Virtual80mm;

        Assert.Equal(a.Revision, b.Revision);
        Assert.StartsWith("r1-", a.Revision);
    }

    [Fact]
    public void Revision_ignores_display_only_fields()
    {
        var renamed = TestProfiles.Virtual80mm with { DisplayName = "다른 이름", Notes = "다른 메모" };

        Assert.Equal(TestProfiles.Virtual80mm.Revision, renamed.Revision);
    }

    [Fact]
    public void Revision_changes_with_print_critical_fields()
    {
        var profile = TestProfiles.Virtual80mm;
        var cutChanged = profile with { Paper = profile.Paper with { CutStyle = CutStyles.Tear } };
        var feedChanged = profile with { Paper = profile.Paper with { TrailingFeedDots = 96 } };

        Assert.NotEqual(profile.Revision, cutChanged.Revision);
        Assert.NotEqual(profile.Revision, feedChanged.Revision);
    }

    [Fact]
    public void Rejects_geometry_that_does_not_add_up()
    {
        var broken = TestProfiles.Virtual80mm with
        {
            Paper = TestProfiles.Virtual80mm.Paper with { SideMarginDots = 31 },
        };

        Assert.Throws<InvalidOperationException>(broken.EnsureValid);
    }

    [Fact]
    public void Shipped_presets_are_valid_and_match_the_design_numbers()
    {
        var presets = PrinterProfileStore.LoadPresets(Path.Combine(AppContext.BaseDirectory, "Profiles"));

        Assert.Equal(2, presets.Count);

        var eighty = presets.Single(p => p.ProfileId == "virtual-80mm-8dpmm");
        Assert.Equal(640, eighty.Paper.PaperWidthDots);
        Assert.Equal(576, eighty.Paper.ContentWidthDots);
        Assert.Equal(80, eighty.Paper.PaperWidthMm);
        Assert.Equal(72, eighty.Paper.ContentWidthMm);

        var fiftyEight = presets.Single(p => p.ProfileId == "virtual-58mm-8dpmm");
        Assert.Equal(464, fiftyEight.Paper.PaperWidthDots);
        Assert.Equal(384, fiftyEight.Paper.ContentWidthDots);
        Assert.Equal(58, fiftyEight.Paper.PaperWidthMm);

        Assert.All(presets, p => Assert.Equal(ProfileKinds.Virtual, p.Kind));
    }
}

public sealed class PrinterProfileStoreTests
{
    [Fact]
    public void Saves_atomically_and_reloads_selection()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            var before = store.Active;

            Assert.True(store.TryUpdate("virtual-58mm-8dpmm", before.Revision, cutStyle: null, out var updated, out var error));
            Assert.Null(error);
            Assert.Equal("virtual-58mm-8dpmm", updated.ProfileId);
            Assert.False(File.Exists(Path.Combine(directory, "printer-profile.json.tmp")));

            // 새 프로세스가 다시 읽어도 같은 선택이 남아 있다.
            var reloaded = CreateStore(directory);
            Assert.Equal("virtual-58mm-8dpmm", reloaded.Active.ProfileId);
            Assert.Equal(updated.Revision, reloaded.Active.Revision);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Rejects_update_with_stale_revision()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);

            Assert.False(store.TryUpdate("virtual-58mm-8dpmm", "r1-stale", cutStyle: null, out _, out var error));
            Assert.Equal(ErrorCodes.ProfileChanged, error!.Code);
            Assert.Equal("virtual-80mm-8dpmm", store.Active.ProfileId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Cut_style_override_changes_active_revision()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = CreateStore(directory);
            var before = store.Active;

            Assert.True(store.TryUpdate(before.ProfileId, before.Revision, CutStyles.Tear, out var updated, out _));

            Assert.Equal(CutStyles.Tear, updated.Paper.CutStyle);
            Assert.NotEqual(before.Revision, updated.Revision);
            Assert.Equal(updated.Revision, store.Active.Revision);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Starts_from_defaults_when_settings_file_is_corrupt()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "printer-profile.json"), "{ 깨진 파일");

            var store = CreateStore(directory);

            // 파일을 지우지 않고 기본 프리셋으로 시작한다.
            Assert.Equal("virtual-80mm-8dpmm", store.Active.ProfileId);
            Assert.True(File.Exists(Path.Combine(directory, "printer-profile.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PrinterProfileStore CreateStore(string directory)
        => new(
            PrinterProfileStore.LoadPresets(Path.Combine(AppContext.BaseDirectory, "Profiles")),
            directory,
            NullLogger<PrinterProfileStore>.Instance);

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ysfourcut-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}

public sealed class SessionAndCodeStoreTests
{
    [Fact]
    public void Session_expires_after_idle_timeout()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));
        var sessions = new SessionStore(clock) { IdleTimeout = TimeSpan.FromMinutes(30) };

        var (token, _) = sessions.Create();
        Assert.NotNull(sessions.Validate(token));

        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Null(sessions.Validate(token));
    }

    [Fact]
    public void Session_activity_extends_idle_window()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));
        var sessions = new SessionStore(clock) { IdleTimeout = TimeSpan.FromMinutes(30) };

        var (token, _) = sessions.Create();

        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.NotNull(sessions.Validate(token));

        clock.Advance(TimeSpan.FromMinutes(20));
        Assert.NotNull(sessions.Validate(token));
    }

    [Fact]
    public void Tokens_are_distinct_and_opaque()
    {
        var sessions = new SessionStore(TimeProvider.System);

        var (first, firstSession) = sessions.Create();
        var (second, _) = sessions.Create();

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, firstSession.SessionId);
        Assert.True(first.Length >= 40);
    }

    [Fact]
    public void Code_is_single_use()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));
        var codes = new BootstrapCodeStore(clock);

        var code = codes.Issue();

        Assert.Equal(BootstrapExchange.Ok, codes.TryConsume(code));
        Assert.Equal(BootstrapExchange.AlreadyUsed, codes.TryConsume(code));
    }

    [Fact]
    public void Code_expires_after_sixty_seconds()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));
        var codes = new BootstrapCodeStore(clock);

        var code = codes.Issue();
        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.Equal(BootstrapExchange.Expired, codes.TryConsume(code));
        Assert.Equal(0, codes.UsableCodeCount);
    }

    [Fact]
    public void Unknown_code_is_invalid()
        => Assert.Equal(BootstrapExchange.Invalid, new BootstrapCodeStore(TimeProvider.System).TryConsume("nope"));
}

internal static class TestProfiles
{
    public static PrinterProfile Virtual80mm { get; } = new(
        ProfileId: "virtual-80mm-8dpmm",
        Kind: ProfileKinds.Virtual,
        DisplayName: "가상 영수증 80mm",
        Paper: new PaperGeometry(640, 576, 32, 24, 64, 8, 8, CutStyles.Straight),
        Limits: new ProfileLimits(576, 4096, 512 * 1024),
        Notes: "테스트용");
}
