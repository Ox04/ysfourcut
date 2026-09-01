namespace YsFourcut.Host.Platform;

/// <summary>
/// 프로필/작업 기록 저장 위치. 저장소·웹 루트·공유 폴더에는 두지 않는다.
/// Linux는 XDG 경로, Windows는 %LOCALAPPDATA%를 사용한다 (WSL_DEVELOPMENT.md 5절).
/// 사진과 결과 PNG는 어느 쪽에도 저장하지 않는다. 메모리 전용이다.
/// </summary>
public static class AppDirectories
{
    private const string AppFolderName = "YSFourcut";

    /// <summary>프로필 등 설정. 설정 키 <c>Storage:ConfigDirectory</c>로 덮어쓸 수 있다(테스트용).</summary>
    public static string ResolveConfigDirectory(IConfiguration configuration)
    {
        var configured = configuration["Storage:ConfigDirectory"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);
        }

        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var baseDir = string.IsNullOrWhiteSpace(xdgConfig)
            ? Path.Combine(HomeDirectory(), ".config")
            : xdgConfig;

        return Path.Combine(baseDir, AppFolderName);
    }

    /// <summary>사진이 없는 작업 기록용. 05/06번에서 사용한다.</summary>
    public static string ResolveStateDirectory(IConfiguration configuration)
    {
        var configured = configuration["Storage:StateDirectory"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName);
        }

        var xdgState = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var baseDir = string.IsNullOrWhiteSpace(xdgState)
            ? Path.Combine(HomeDirectory(), ".local", "state")
            : xdgState;

        return Path.Combine(baseDir, AppFolderName);
    }

    private static string HomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? Directory.GetCurrentDirectory() : home;
    }
}
