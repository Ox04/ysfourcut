using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace YsFourcut.Host.Tests.Support;

/// <summary>
/// 실제 호스트를 그대로 띄우되 설정 폴더와 시계만 테스트용으로 바꾼다.
/// 사용자의 ~/.config는 건드리지 않는다.
/// </summary>
public sealed class PrintHostFactory : WebApplicationFactory<Program>
{
    public const string DevScreenOrigin = "http://127.0.0.1:5173";
    public const string HostOrigin = "http://127.0.0.1:4317";
    public const string ClientHeaderName = "X-YSFourcut-Client";
    public const string ClientHeaderValue = "web";
    public const string SessionCookieName = "ysfourcut_session";

    public string ConfigDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "ysfourcut-tests", Guid.NewGuid().ToString("N"));

    /// <summary>작업 기록 저장 위치. 사용자의 ~/.local/state는 건드리지 않는다.</summary>
    public string StateDirectory { get; init; } =
        Path.Combine(Path.GetTempPath(), "ysfourcut-tests-state", Guid.NewGuid().ToString("N"));

    /// <summary>지정한 state 폴더를 테스트가 직접 관리하는 경우 정리를 건너뛴다.</summary>
    public bool KeepStateDirectory { get; init; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

    public CapturedLogs Logs { get; } = new();

    /// <summary>로그 수준 필터를 모두 걷어내고 프레임워크 로그까지 받는다(로그 유출 검사용).</summary>
    public bool VerboseLogs { get; init; }

    /// <summary>실제 worker 대신 쓸 백엔드. 경합·진행 중 상태를 결정적으로 시험할 때 지정한다.</summary>
    public YsFourcut.Host.Printing.IPrinterBackend? BackendOverride { get; init; }

    /// <summary>렌더 예산을 짧게 줄여 시간 초과를 빠르게 확인할 때 지정한다.</summary>
    public YsFourcut.Host.Printing.Virtual.VirtualPrinterOptions? VirtualPrinterOptionsOverride { get; init; }

    /// <summary>
    /// 호스트 정보 교체. Linux에서는 physical 모드로 시작할 수 없으므로
    /// physical 전용 분기를 확인할 때만 주입한다.
    /// </summary>
    public YsFourcut.Host.Platform.HostInfo? HostInfoOverride { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:ConfigDirectory", ConfigDirectory);
        builder.UseSetting("Storage:StateDirectory", StateDirectory);

        builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(new CapturingLoggerProvider(Logs));

            // 로그 수준 설정에 기대지 않고 프레임워크 로그까지 전부 받는다.
            // 일회용 코드·쿠키가 "설정을 낮췄을 때만" 새는지 확인할 때 쓴다.
            if (VerboseLogs)
            {
                logging.Services.Configure<Microsoft.Extensions.Logging.LoggerFilterOptions>(options =>
                {
                    options.Rules.Clear();
                    options.MinLevel = LogLevel.Trace;
                });
            }
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            if (HostInfoOverride is not null)
            {
                services.RemoveAll<YsFourcut.Host.Platform.HostInfo>();
                services.AddSingleton(HostInfoOverride);
            }

            if (VirtualPrinterOptionsOverride is not null)
            {
                services.RemoveAll<YsFourcut.Host.Printing.Virtual.VirtualPrinterOptions>();
                services.AddSingleton(VirtualPrinterOptionsOverride);
            }

            if (BackendOverride is not null)
            {
                services.RemoveAll<YsFourcut.Host.Printing.IPrinterBackend>();
                services.AddSingleton(BackendOverride);
            }
        });
    }

    /// <summary>기본 클라이언트: 루프백 Host, 전용 헤더, 개발 화면 Origin, 쿠키 유지.</summary>
    public HttpClient CreateApiClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(HostOrigin + "/"),
            HandleCookies = true,
        });

        client.DefaultRequestHeaders.Add(ClientHeaderName, ClientHeaderValue);
        client.DefaultRequestHeaders.Add("Origin", DevScreenOrigin);
        return client;
    }

    /// <summary>페어링 화면 → 일회용 코드 → 세션 쿠키. 실제 흐름 그대로 사용한다.</summary>
    public static async Task<string> IssueBootstrapCodeAsync(HttpClient client)
    {
        var pairing = await client.GetAsync("/pair");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, pairing.StatusCode);

        var location = pairing.Headers.Location?.ToString() ?? string.Empty;
        const string marker = "#bootstrap=";
        var index = location.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"페어링 위치에 코드 fragment가 없습니다: {location}");

        return location[(index + marker.Length)..];
    }

    public static async Task<string> AuthenticateAsync(HttpClient client)
    {
        var code = await IssueBootstrapCodeAsync(client);
        var response = await client.PostAsync(
            "/api/bootstrap",
            JsonBody(new { schemaVersion = 1, code }));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return code;
    }

    public static StringContent JsonBody(object value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadJsonAsync(response);
        return body.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code)
            ? code.GetString()
            : null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            var directories = KeepStateDirectory ? new[] { ConfigDirectory } : [ConfigDirectory, StateDirectory];
            foreach (var directory in directories)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // 임시 폴더 정리 실패는 테스트 결과에 영향을 주지 않는다.
                }
            }
        }
    }
}

public sealed class CapturedLogs
{
    private readonly ConcurrentQueue<string> _entries = new();

    public void Add(string entry) => _entries.Enqueue(entry);

    public IReadOnlyList<string> Entries => [.. _entries];

    public string AllText => string.Join('\n', _entries);
}

public sealed class CapturingLoggerProvider(CapturedLogs logs) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, logs);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, CapturedLogs logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => logs.Add($"[{logLevel}] {category}: {formatter(state, exception)} {exception}");
    }
}
