using System.Reflection;
using YsFourcut.Host.Printing;

namespace YsFourcut.Host.Platform;

/// <summary>
/// 호스트 프로세스 단위 정보. 재시작하면 InstanceId가 바뀌므로 웹이 이전 결과 캐시가
/// 사라진 것을 알 수 있다 (VIRTUAL_PRINTER_DESIGN.md 9절).
/// </summary>
public sealed class HostInfo(PrinterMode mode)
{
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");

    public PrinterMode Mode { get; } = mode;

    public string Version { get; } =
        typeof(HostInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? typeof(HostInfo).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";
}
