using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Platform;
using YsFourcut.Host.Printing;
using YsFourcut.Host.Printing.Virtual;
using YsFourcut.Host.Profiles;
using YsFourcut.Host.Rendering;

namespace YsFourcut.Host.Tests;

/// <summary>
/// 실제 별도 프로세스를 띄워 파이프로 PNG를 받는 경로를 확인한다.
/// 프로세스를 진짜로 실행하므로 다른 단위 테스트보다 느리다.
/// </summary>
public sealed class VirtualPrinterWorkerTests
{
    private static readonly PrinterProfile Profile80 =
        PrinterProfileStore.LoadPresets(Path.Combine(AppContext.BaseDirectory, "Profiles"))
            .Single(p => p.ProfileId == "virtual-80mm-8dpmm");

    [Fact]
    public async Task Worker_process_returns_three_pngs()
    {
        var printer = CreatePrinter(out var hostInfo);
        var request = CreateRequest(576, 240);

        var result = await printer.PrintAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Null(result.FailureCode);
        Assert.Equal(3, result.Artifacts.Count);
        Assert.Equal(["content", "paper", "appearance"], result.Artifacts.Select(a => a.Kind));

        var paper = result.Artifacts.Single(a => a.Kind == "paper");
        Assert.Equal(640, paper.WidthPx);
        Assert.Equal(24 + 240 + 64, paper.HeightPx);

        // 받은 바이트가 실제로 디코딩되는 PNG인지 확인한다.
        foreach (var artifact in result.Artifacts)
        {
            using var decoded = SKBitmap.Decode(artifact.Bytes);
            Assert.Equal(artifact.WidthPx, decoded.Width);
            Assert.Equal(artifact.HeightPx, decoded.Height);
        }

        Assert.NotNull(result.Layout);
        Assert.Equal(640, result.Layout!.PaperWidthDots);
        Assert.Equal(ReceiptAppearanceRenderer.Revision, result.AppearanceRevision);
        Assert.NotEmpty(hostInfo.InstanceId);
    }

    [Fact]
    public async Task Worker_result_matches_in_process_rendering()
    {
        var printer = CreatePrinter(out _);
        var request = CreateRequest(576, 96);

        var viaWorker = await printer.PrintAsync(request, CancellationToken.None);

        var backend = new CrossEscPosSkiaAdapter();
        var inProcess = new ReceiptRenderer(
            backend,
            new ReceiptPaperComposer(backend),
            new ReceiptAppearanceRenderer(backend),
            RenderLimits.Default)
            .Render(request.Bitmap, request.Profile, request.AppearanceSeed, request.EstimatedEffects);

        Assert.True(viaWorker.Succeeded);
        Assert.Equal(inProcess.Content.Bytes, viaWorker.Artifacts.Single(a => a.Kind == "content").Bytes);
        Assert.Equal(inProcess.Paper.Bytes, viaWorker.Artifacts.Single(a => a.Kind == "paper").Bytes);
        Assert.Equal(inProcess.Appearance!.Bytes, viaWorker.Artifacts.Single(a => a.Kind == "appearance").Bytes);
    }

    [Fact]
    public async Task Render_timeout_kills_the_worker_and_reports_failure()
    {
        var printer = CreatePrinter(out _, new VirtualPrinterOptions { RenderTimeout = TimeSpan.FromMilliseconds(1) });

        var result = await printer.PrintAsync(CreateRequest(576, 1788), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("RENDER_TIMEOUT", result.FailureCode);
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task Limit_failure_from_the_worker_is_reported_with_its_code()
    {
        var printer = CreatePrinter(out _, limits: new RenderLimits(MaxPixelsPerImage: 1000));

        var result = await printer.PrintAsync(CreateRequest(576, 240), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("RENDER_LIMIT_EXCEEDED", result.FailureCode);
        Assert.Empty(result.Artifacts);
    }

    private static VirtualReceiptPrinter CreatePrinter(
        out HostInfo hostInfo,
        VirtualPrinterOptions? options = null,
        RenderLimits? limits = null)
    {
        hostInfo = new HostInfo(PrinterMode.Virtual);
        return new VirtualReceiptPrinter(
            hostInfo,
            limits ?? RenderLimits.Default,
            options ?? new VirtualPrinterOptions(),
            NullLogger<VirtualReceiptPrinter>.Instance);
    }

    private static PrinterBackendRequest CreateRequest(int width, int height)
    {
        var stride = (width + 7) / 8;
        var data = new byte[stride * height];
        for (var i = 0; i < data.Length; i += 3)
        {
            data[i] = 0b1010_1010;
        }

        return new PrinterBackendRequest(
            ClientJobId: Guid.NewGuid().ToString(),
            Profile: Profile80,
            Bitmap: new DecodedBitmap(width, height, stride, data,
                PrintJobRequestValidator.ComputeDigest(data, width, height)),
            AppearanceSeed: 12345,
            EstimatedEffects: []);
    }
}
