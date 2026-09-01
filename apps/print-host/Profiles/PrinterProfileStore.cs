using System.Text.Json;
using System.Text.Json.Serialization;
using YsFourcut.Host.Platform;

namespace YsFourcut.Host.Profiles;

/// <summary>디스크에 저장하는 사용자 설정. 사진·인증값은 절대 넣지 않는다.</summary>
public sealed record PrinterProfileSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string? SelectedProfileId { get; init; }
    public Dictionary<string, string> CutStyles { get; init; } = [];
    public ProfileVerification? Verification { get; init; }
}

public sealed record ProfileUpdateError(string Code, string Message, IReadOnlyDictionary<string, object?>? Details = null);

/// <summary>
/// 프리셋(읽기 전용 JSON)과 사용자 설정(설정 폴더의 작은 JSON)을 합쳐 현재 프로필을 만든다.
/// 인쇄 핵심 설정이 바뀌면 revision이 바뀌고 검증 기록은 해제된다.
/// </summary>
public sealed class PrinterProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly IReadOnlyList<PrinterProfile> _presets;
    private readonly string _settingsPath;
    private readonly ILogger<PrinterProfileStore> _logger;
    private PrinterProfileSettings _settings;

    public PrinterProfileStore(
        IReadOnlyList<PrinterProfile> presets,
        string configDirectory,
        ILogger<PrinterProfileStore> logger)
    {
        if (presets.Count == 0)
        {
            throw new InvalidOperationException("프린터 프리셋을 하나도 읽지 못했습니다.");
        }

        _presets = presets;
        _settingsPath = Path.Combine(configDirectory, "printer-profile.json");
        _logger = logger;
        _settings = LoadSettings();
    }

    /// <summary>기본 프리셋. VIRTUAL_PRINTER_DESIGN.md 5절의 80mm가 기본이다.</summary>
    public PrinterProfile DefaultPreset =>
        _presets.FirstOrDefault(p => p.ProfileId == "virtual-80mm-8dpmm") ?? _presets[0];

    public IReadOnlyList<PrinterProfile> Presets => _presets;

    public string SettingsPath => _settingsPath;

    public PrinterProfile Active
    {
        get
        {
            lock (_gate)
            {
                return ResolveActiveUnlocked();
            }
        }
    }

    public ProfileVerification Verification
    {
        get
        {
            lock (_gate)
            {
                return NormalizeVerificationUnlocked(_settings.Verification, ResolveActiveUnlocked());
            }
        }
    }

    public string SelectedProfileId => Active.ProfileId;

    /// <summary>프리셋에 사용자 설정(절취 방식)을 적용한 목록. OS 큐를 조회하지 않는다.</summary>
    public IReadOnlyList<PrinterProfile> ListProfiles()
    {
        lock (_gate)
        {
            return [.. _presets.Select(ApplyOverridesUnlocked)];
        }
    }

    public PrinterProfile? Find(string profileId)
        => ListProfiles().FirstOrDefault(p => string.Equals(p.ProfileId, profileId, StringComparison.Ordinal));

    /// <summary>
    /// 큐·출력 설정 저장. expectedRevision은 클라이언트가 마지막으로 본 **현재 활성 프로필**의 revision이다.
    /// </summary>
    public bool TryUpdate(
        string profileId,
        string expectedRevision,
        string? cutStyle,
        out PrinterProfile updated,
        out ProfileUpdateError? error)
    {
        lock (_gate)
        {
            var active = ResolveActiveUnlocked();
            if (!string.Equals(active.Revision, expectedRevision, StringComparison.Ordinal))
            {
                updated = active;
                error = new ProfileUpdateError(
                    Contracts.ErrorCodes.ProfileChanged,
                    "프로필이 이미 변경되었습니다. 최신 프로필을 다시 조회한 뒤 저장하세요.",
                    new Dictionary<string, object?>
                    {
                        ["currentProfileId"] = active.ProfileId,
                        ["currentRevision"] = active.Revision,
                    });
                return false;
            }

            var target = _presets.FirstOrDefault(p => string.Equals(p.ProfileId, profileId, StringComparison.Ordinal));
            if (target is null)
            {
                updated = active;
                error = new ProfileUpdateError(
                    Contracts.ErrorCodes.PrinterNotFound,
                    "요청한 프로필을 찾을 수 없습니다.",
                    new Dictionary<string, object?> { ["profileId"] = profileId });
                return false;
            }

            var newCutStyles = new Dictionary<string, string>(_settings.CutStyles, StringComparer.Ordinal);
            if (cutStyle is not null)
            {
                if (!CutStyles.IsValid(cutStyle))
                {
                    updated = active;
                    error = new ProfileUpdateError(
                        Contracts.ErrorCodes.ValidationFailed,
                        "cutStyle은 straight, tear, none 중 하나여야 합니다.",
                        new Dictionary<string, object?> { ["cutStyle"] = cutStyle });
                    return false;
                }

                newCutStyles[target.ProfileId] = cutStyle;
            }

            var next = _settings with
            {
                SelectedProfileId = target.ProfileId,
                CutStyles = newCutStyles,
            };

            var nextActive = ApplyOverridesUnlocked(target, next);

            // 인쇄 핵심 설정이 바뀌면(=revision 변경) 검증 기록을 해제한다.
            var verification = NormalizeVerificationUnlocked(next.Verification, nextActive);
            next = next with { Verification = verification.Verified ? verification : null };

            SaveSettingsUnlocked(next);
            _settings = next;

            updated = nextActive;
            error = null;
            return true;
        }
    }

    private PrinterProfile ResolveActiveUnlocked()
    {
        var selected = _presets.FirstOrDefault(p =>
            string.Equals(p.ProfileId, _settings.SelectedProfileId, StringComparison.Ordinal)) ?? DefaultPreset;
        return ApplyOverridesUnlocked(selected);
    }

    private PrinterProfile ApplyOverridesUnlocked(PrinterProfile preset)
        => ApplyOverridesUnlocked(preset, _settings);

    private static PrinterProfile ApplyOverridesUnlocked(PrinterProfile preset, PrinterProfileSettings settings)
    {
        if (!settings.CutStyles.TryGetValue(preset.ProfileId, out var cutStyle) ||
            !CutStyles.IsValid(cutStyle) ||
            string.Equals(cutStyle, preset.Paper.CutStyle, StringComparison.Ordinal))
        {
            return preset;
        }

        return preset with { Paper = preset.Paper with { CutStyle = cutStyle } };
    }

    private static ProfileVerification NormalizeVerificationUnlocked(
        ProfileVerification? stored,
        PrinterProfile active)
    {
        if (stored is null || !stored.Verified)
        {
            return ProfileVerification.None;
        }

        // 다른 프로필이거나 인쇄 핵심 설정이 바뀌었으면 검증은 유효하지 않다.
        return string.Equals(stored.VerifiedRevision, active.Revision, StringComparison.Ordinal)
            ? stored
            : ProfileVerification.None;
    }

    private PrinterProfileSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new PrinterProfileSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<PrinterProfileSettings>(json, JsonOptions);
            return loaded ?? new PrinterProfileSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 설정을 못 읽으면 기본 프리셋으로 시작한다. 파일을 지우지는 않는다.
            _logger.LogWarning("프로필 설정을 읽지 못해 기본값으로 시작합니다: {Reason}", ex.GetType().Name);
            return new PrinterProfileSettings();
        }
    }

    /// <summary>임시 파일에 쓰고 이름을 바꾼다. 중간 상태의 파일을 남기지 않는다.</summary>
    private void SaveSettingsUnlocked(PrinterProfileSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _settingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tempPath, _settingsPath, overwrite: true);
    }

    /// <summary>프리셋 JSON을 읽는다. 잘못된 프리셋은 시작 시점에 실패로 드러낸다.</summary>
    public static IReadOnlyList<PrinterProfile> LoadPresets(string presetDirectory)
    {
        if (!Directory.Exists(presetDirectory))
        {
            throw new DirectoryNotFoundException($"프린터 프리셋 폴더를 찾지 못했습니다: {presetDirectory}");
        }

        var profiles = new List<PrinterProfile>();
        foreach (var file in Directory.EnumerateFiles(presetDirectory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            var profile = JsonSerializer.Deserialize<PrinterProfile>(File.ReadAllText(file), JsonOptions)
                ?? throw new InvalidOperationException($"프리셋을 읽지 못했습니다: {Path.GetFileName(file)}");
            profile.EnsureValid();
            profiles.Add(profile);
        }

        return profiles;
    }
}
