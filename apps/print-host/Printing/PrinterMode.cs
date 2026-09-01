using YsFourcut.Host.Contracts;

namespace YsFourcut.Host.Printing;

public enum PrinterMode
{
    Virtual,
    Physical,
}

public static class PrinterModeNames
{
    public const string Virtual = "virtual";
    public const string Physical = "physical";

    public static string ToApiValue(this PrinterMode mode)
        => mode == PrinterMode.Physical ? Physical : Virtual;
}

/// <summary>
/// 서버 시작 설정에서 출력 모드를 정한다. 클라이언트가 요청마다 실제 출력을 지정할 수 없다.
/// Linux에서 physical 선택은 거부한다 (WSL_DEVELOPMENT.md 5절). 자동으로 virtual로 바꾸지 않는다.
/// </summary>
public static class PrinterModeResolver
{
    public static PrinterMode Resolve(string? configuredMode, bool isWindows)
    {
        var value = string.IsNullOrWhiteSpace(configuredMode)
            ? PrinterModeNames.Virtual
            : configuredMode.Trim().ToLowerInvariant();

        return value switch
        {
            PrinterModeNames.Virtual => PrinterMode.Virtual,
            PrinterModeNames.Physical when isWindows => PrinterMode.Physical,
            PrinterModeNames.Physical => throw new PrinterModeUnsupportedException(
                $"{ErrorCodes.PlatformUnsupported}: physical 모드는 Windows에서만 사용할 수 있습니다. " +
                "현재 OS에서는 실물 출력 어댑터를 시작하지 않습니다. PrinterMode=virtual로 실행하세요."),
            _ => throw new PrinterModeUnsupportedException(
                $"알 수 없는 PrinterMode 값입니다: '{configuredMode}'. virtual 또는 physical만 사용합니다."),
        };
    }
}

public sealed class PrinterModeUnsupportedException(string message) : Exception(message);
