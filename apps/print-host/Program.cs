// YS Fourcut print-host.
// 01번: WSL 실행 뼈대. 02번: API 계약·프로필·로컬 인증 경계.
// 렌더러/작업 큐/실제 인쇄는 03번 이후에 붙인다. 이 파일은 조립만 담당한다.

using YsFourcut.Host.Api;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Platform;
using YsFourcut.Host.Artifacts;
using YsFourcut.Host.Printing;
using YsFourcut.Host.Printing.Virtual;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Rendering;
using YsFourcut.Host.Security;
using YsFourcut.Host.Worker;

const string ListenUrl = "http://127.0.0.1:4317";
const string DevScreenOrigin = "http://127.0.0.1:5173";
const string HostOrigin = "http://127.0.0.1:4317";

// 렌더 worker로 실행된 경우에는 웹 호스트를 만들지 않는다.
if (RenderWorkerEntryPoint.IsWorkerInvocation(args))
{
    return await RenderWorkerEntryPoint.RunAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(ListenUrl);

var serviceLimits = ServiceLimits.Default;
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = serviceLimits.MaxRequestBodyBytes;
});

// 출력 모드는 서버 설정에서만 정한다. Linux에서 physical은 시작 시점에 거부한다.
var printerMode = PrinterModeResolver.Resolve(
    builder.Configuration["PrinterMode"],
    OperatingSystem.IsWindows());

var localAccess = new LocalAccessOptions
{
    AllowedOrigins = builder.Environment.IsDevelopment()
        ? [DevScreenOrigin, HostOrigin]
        : [HostOrigin],
    AppUrl = builder.Environment.IsDevelopment() ? DevScreenOrigin + "/" : "/",
    MaxRequestBodyBytes = serviceLimits.MaxRequestBodyBytes,
};

builder.Services.AddSingleton(localAccess);
builder.Services.AddSingleton(serviceLimits);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new HostInfo(printerMode));
builder.Services.AddSingleton(sp => new SessionStore(sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(sp => new BootstrapCodeStore(sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<SessionEndpointFilter>();

// 렌더링(03번) + 종이 외관(04번). 작업 큐·HTTP 연결은 05번에서 붙인다.
builder.Services.AddSingleton(RenderLimits.Default);
builder.Services.AddSingleton<IReceiptImageBackend, CrossEscPosSkiaAdapter>();
builder.Services.AddSingleton<IReceiptAppearanceRenderer, ReceiptAppearanceRenderer>();
builder.Services.AddSingleton<ReceiptPaperComposer>();
builder.Services.AddSingleton<ReceiptRenderer>();

// 출력 작업(05번). 가상 백엔드만 등록한다. Windows 어댑터는 91번에서 별도로 붙인다.
builder.Services.AddSingleton(new VirtualPrinterOptions());
builder.Services.AddSingleton<VirtualFaultStore>();
builder.Services.AddSingleton(ArtifactCacheLimits.Default);
builder.Services.AddSingleton<IPrinterBackend, VirtualReceiptPrinter>();
builder.Services.AddSingleton<InMemoryReceiptArtifactStore>();
builder.Services.AddSingleton(sp => new PrintJobRecordStore(
    AppDirectories.ResolveStateDirectory(sp.GetRequiredService<IConfiguration>()),
    sp.GetRequiredService<ILogger<PrintJobRecordStore>>()));
builder.Services.AddSingleton<PrintJobManager>();
builder.Services.AddHostedService<JobMaintenanceService>();
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var environment = sp.GetRequiredService<IHostEnvironment>();

    var presetDirectory = Path.Combine(AppContext.BaseDirectory, "Profiles");
    if (!Directory.Exists(presetDirectory))
    {
        presetDirectory = Path.Combine(environment.ContentRootPath, "Profiles");
    }

    return new PrinterProfileStore(
        PrinterProfileStore.LoadPresets(presetDirectory),
        AppDirectories.ResolveConfigDirectory(configuration),
        sp.GetRequiredService<ILogger<PrinterProfileStore>>());
});

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<LocalAccessMiddleware>(localAccess);

// 개발 환경은 Vite(5173)가 화면을 서빙한다. 배포판(13번)에서는 이 호스트가
// wwwroot의 웹 빌드 결과를 직접 서빙해 별도 Node 없이도 화면이 뜨게 한다.
if (!app.Environment.IsDevelopment())
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapHealthEndpoints();
app.MapBootstrapEndpoints();
app.MapPrinterEndpoints();
app.MapPrintJobEndpoints();

app.Logger.LogInformation(
    "printerMode={PrinterMode}, 프로필 설정 파일={SettingsPath}",
    printerMode.ToApiValue(),
    app.Services.GetRequiredService<PrinterProfileStore>().SettingsPath);

app.Run();
return 0;

/// <summary>통합 테스트가 이 호스트를 그대로 띄우기 위해 필요한 진입점 노출.</summary>
public partial class Program;
