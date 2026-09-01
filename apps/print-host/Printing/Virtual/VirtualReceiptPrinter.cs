using System.Diagnostics;
using System.IO.Pipes;
using YsFourcut.Host.Contracts;
using YsFourcut.Host.Platform;
using YsFourcut.Host.Rendering;
using YsFourcut.Host.Worker;

namespace YsFourcut.Host.Printing.Virtual;

public sealed record VirtualPrinterOptions
{
    /// <summary>가상 렌더링 예산. 주입 지연(06번)은 이 예산에서 제외한다.</summary>
    public TimeSpan RenderTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// 별도 렌더 worker 프로세스를 띄워 PNG를 받는다. Windows 인쇄 모듈이나 OS 큐를 호출하지 않는다.
/// 실행 경로에 .exe/cmd.exe를 고정하지 않고 현재 프로세스 실행 방식(dotnet 또는 apphost)을 따른다.
/// 늦은 결과는 host instance/job ID로 거부하고, 시간 초과나 호스트 종료 시 worker를 정리한다.
/// </summary>
public sealed class VirtualReceiptPrinter(
    HostInfo hostInfo,
    RenderLimits limits,
    VirtualPrinterOptions options,
    ILogger<VirtualReceiptPrinter> logger) : IPrinterBackend
{
    public PrinterMode Mode => PrinterMode.Virtual;

    public async Task<PrinterBackendResult> PrintAsync(
        PrinterBackendRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.RenderTimeout);

        using var requestPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        using var responsePipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

        using var process = new Process { StartInfo = BuildStartInfo(requestPipe, responsePipe) };

        try
        {
            if (!process.Start())
            {
                return PrinterBackendResult.Failure(
                    ErrorCodes.InternalError, "렌더 worker를 시작하지 못했습니다.", request.AppearanceSeed);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "렌더 worker 시작 실패");
            return PrinterBackendResult.Failure(
                ErrorCodes.InternalError, "렌더 worker를 시작하지 못했습니다.", request.AppearanceSeed);
        }

        // 자식이 상속한 뒤에는 부모 쪽 클라이언트 핸들을 닫아야 파이프 종료를 감지할 수 있다.
        requestPipe.DisposeLocalCopyOfClientHandle();
        responsePipe.DisposeLocalCopyOfClientHandle();

        try
        {
            await SendRequestAsync(requestPipe, request, timeout.Token);
            var result = await ReadResponseAsync(responsePipe, request, timeout.Token);
            await WaitForExitAsync(process, timeout.Token);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("가상 렌더링이 {Seconds}초 예산을 넘겨 worker를 정리합니다.", options.RenderTimeout.TotalSeconds);
            return PrinterBackendResult.Failure(
                ErrorCodes.RenderTimeout, "가상 출력이 제한 시간을 넘겼습니다.", request.AppearanceSeed);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidOperationException)
        {
            logger.LogWarning("렌더 worker 통신 실패: {Reason}", ex.GetType().Name);
            return PrinterBackendResult.Failure(
                ErrorCodes.VirtualRenderFailed, "가상 출력에 실패했습니다.", request.AppearanceSeed);
        }
        finally
        {
            KillIfRunning(process);
        }
    }

    private ProcessStartInfo BuildStartInfo(
        AnonymousPipeServerStream requestPipe,
        AnonymousPipeServerStream responsePipe)
    {
        var startInfo = new ProcessStartInfo
        {
            // 셸을 거치지 않고 인수를 하나씩 전달한다.
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        // 배포판의 apphost로 실행 중이면 자기 자신을, 그 밖에는 `dotnet <host.dll>`을 쓴다.
        // .exe나 cmd.exe를 고정하지 않는다.
        var hostAssemblyPath = typeof(VirtualReceiptPrinter).Assembly.Location;
        var hostName = Path.GetFileNameWithoutExtension(hostAssemblyPath);
        var processPath = Environment.ProcessPath;
        var processName = Path.GetFileNameWithoutExtension(processPath) ?? string.Empty;

        if (processPath is not null && string.Equals(processName, hostName, StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = processPath;
        }
        else
        {
            if (string.IsNullOrEmpty(hostAssemblyPath))
            {
                throw new InvalidOperationException("호스트 어셈블리 경로를 찾지 못했습니다.");
            }

            startInfo.FileName = ResolveDotnetMuxer();
            startInfo.ArgumentList.Add(hostAssemblyPath);
        }

        startInfo.ArgumentList.Add(RenderWorkerProtocol.WorkerArgument);
        startInfo.ArgumentList.Add(RenderWorkerProtocol.RequestPipeArgument);
        startInfo.ArgumentList.Add(requestPipe.GetClientHandleAsString());
        startInfo.ArgumentList.Add(RenderWorkerProtocol.ResponsePipeArgument);
        startInfo.ArgumentList.Add(responsePipe.GetClientHandleAsString());
        startInfo.ArgumentList.Add(RenderWorkerProtocol.ParentPidArgument);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

        return startInfo;
    }

    /// <summary>현재 실행 중인 .NET의 dotnet 실행 파일. PATH 검색에만 의존하지 않는다.</summary>
    private static string ResolveDotnetMuxer()
    {
        var processPath = Environment.ProcessPath;
        if (processPath is not null &&
            string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var fileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var fromRoot = Path.Combine(root, fileName);
            if (File.Exists(fromRoot))
            {
                return fromRoot;
            }
        }

        // …/shared/Microsoft.NETCore.App/<버전>/ 에서 세 단계 위가 dotnet 설치 루트다.
        var runtimeDirectory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        var candidate = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..", fileName));
        return File.Exists(candidate) ? candidate : fileName;
    }

    private async Task SendRequestAsync(
        Stream pipe,
        PrinterBackendRequest request,
        CancellationToken cancellationToken)
    {
        var header = new RenderWorkerRequest(
            ProtocolVersion: RenderWorkerProtocol.Version,
            HostInstanceId: hostInfo.InstanceId,
            ClientJobId: request.ClientJobId,
            Profile: request.Profile,
            Bitmap: new RenderWorkerBitmap(
                request.Bitmap.WidthDots,
                request.Bitmap.HeightDots,
                request.Bitmap.StrideBytes,
                request.Bitmap.Data.Length,
                request.Bitmap.SourceDigest),
            Appearance: new RenderWorkerAppearance(request.AppearanceSeed, request.EstimatedEffects),
            Limits: new RenderWorkerLimits(limits.MaxPixelsPerImage, limits.MaxEncodedBytesPerJob),
            Fault: request.InjectedFault is null
                ? null
                : new RenderWorkerFault(request.InjectedFault.Kind, request.InjectedFault.FailAfterRows));

        await RenderWorkerProtocol.WriteFrameAsync(pipe, header, request.Bitmap.Data, cancellationToken);
    }

    private async Task<PrinterBackendResult> ReadResponseAsync(
        Stream pipe,
        PrinterBackendRequest request,
        CancellationToken cancellationToken)
    {
        var response = await RenderWorkerProtocol.ReadHeaderAsync<RenderWorkerResponse>(pipe, cancellationToken);

        // 늦게 도착한 다른 작업/다른 호스트의 결과는 받지 않는다.
        if (response.ProtocolVersion != RenderWorkerProtocol.Version ||
            !string.Equals(response.HostInstanceId, hostInfo.InstanceId, StringComparison.Ordinal) ||
            !string.Equals(response.ClientJobId, request.ClientJobId, StringComparison.Ordinal))
        {
            logger.LogWarning("다른 호스트/작업의 렌더 결과를 무시했습니다.");
            return PrinterBackendResult.Failure(
                ErrorCodes.VirtualRenderFailed, "가상 출력 결과를 확인하지 못했습니다.", request.AppearanceSeed);
        }

        if (!response.Ok)
        {
            // 진단용 부분 이미지가 있으면 함께 받아 두되 성공으로 표시하지 않는다.
            if (response is { Partial: true, Artifacts.Count: > 0 })
            {
                var partial = await ReadArtifactsAsync(pipe, response.Artifacts, request, cancellationToken);
                if (partial is not null)
                {
                    return PrinterBackendResult.PartialFailure(
                        response.Code ?? ErrorCodes.VirtualRenderFailed,
                        response.Message ?? "가상 출력에 실패했습니다.",
                        request.AppearanceSeed,
                        partial);
                }
            }

            return PrinterBackendResult.Failure(
                response.Code ?? ErrorCodes.VirtualRenderFailed,
                response.Message ?? "가상 출력에 실패했습니다.",
                request.AppearanceSeed);
        }

        if (response.Artifacts is not { Count: 3 } declared || response.Layout is null)
        {
            return PrinterBackendResult.Failure(
                ErrorCodes.VirtualRenderFailed, "필수 결과 이미지가 부족합니다.", request.AppearanceSeed);
        }

        var totalBytes = declared.Sum(a => (long)a.ByteLength);
        if (totalBytes <= 0 || totalBytes > limits.MaxEncodedBytesPerJob)
        {
            return PrinterBackendResult.Failure(
                ErrorCodes.RenderLimitExceeded, "결과 이미지 합계가 한도를 넘었습니다.", request.AppearanceSeed);
        }

        var artifacts = await ReadArtifactsAsync(pipe, declared, request, cancellationToken);
        if (artifacts is null)
        {
            return PrinterBackendResult.Failure(
                ErrorCodes.RenderLimitExceeded, "결과 이미지 크기가 한도를 넘었습니다.", request.AppearanceSeed);
        }

        var layout = new ReceiptLayout(
            response.Layout.PaperWidthDots,
            response.Layout.PaperHeightDots,
            response.Layout.ContentXDots,
            response.Layout.ContentYDots,
            response.Layout.ContentWidthDots,
            response.Layout.ContentHeightDots,
            response.Layout.LeadingFeedDots,
            response.Layout.TrailingFeedDots,
            request.Profile.Paper.DotsPerMmX,
            request.Profile.Paper.DotsPerMmY);

        return new PrinterBackendResult(
            Succeeded: true,
            Layout: layout,
            Artifacts: artifacts,
            AppearanceRevision: response.Appearance?.Revision,
            AppearanceSeed: request.AppearanceSeed,
            EstimatedEffects: response.Appearance?.EstimatedEffects ?? [],
            FailureCode: null,
            FailureMessage: null);
    }

    /// <summary>선언한 크기만큼만 읽는다. 한도를 넘으면 받지 않는다.</summary>
    private async Task<List<RenderedPng>?> ReadArtifactsAsync(
        Stream pipe,
        IReadOnlyList<RenderWorkerArtifact> declared,
        PrinterBackendRequest request,
        CancellationToken cancellationToken)
    {
        var total = declared.Sum(a => (long)a.ByteLength);
        if (total <= 0 || total > limits.MaxEncodedBytesPerJob)
        {
            return null;
        }

        var artifacts = new List<RenderedPng>(declared.Count);
        foreach (var artifact in declared)
        {
            if (artifact.ByteLength <= 0 || artifact.ByteLength > limits.MaxEncodedBytesPerJob)
            {
                return null;
            }

            var bytes = new byte[artifact.ByteLength];
            await RenderWorkerProtocol.ReadExactlyAsync(pipe, bytes, cancellationToken);
            artifacts.Add(new RenderedPng(artifact.Kind, bytes, artifact.WidthPx, artifact.HeightPx));
        }

        return artifacts;
    }

    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        using var grace = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        grace.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await process.WaitForExitAsync(grace.Token);
        }
        catch (OperationCanceledException)
        {
            // 아래 finally에서 정리한다.
        }
    }

    private void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            logger.LogDebug("worker 정리 중 무시한 오류: {Reason}", ex.GetType().Name);
        }
    }
}
