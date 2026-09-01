using System.IO.Pipes;
using YsFourcut.Host.Contracts;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Printing.Virtual;
using YsFourcut.Host.Rendering;

namespace YsFourcut.Host.Worker;

/// <summary>
/// `--print-worker`로 시작하는 렌더 전용 프로세스. 웹 호스트를 만들지 않고
/// Windows 인쇄 모듈도 로드하지 않는다. 부모가 사라지면 스스로 종료한다.
/// 표준 출력에 이미지·base64를 쓰지 않는다(짧은 오류 문구만 표준 오류로).
/// </summary>
public static class RenderWorkerEntryPoint
{
    public static bool IsWorkerInvocation(string[] args)
        => args.Contains(RenderWorkerProtocol.WorkerArgument, StringComparer.Ordinal);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var requestHandle = GetArgument(args, RenderWorkerProtocol.RequestPipeArgument);
            var responseHandle = GetArgument(args, RenderWorkerProtocol.ResponsePipeArgument);
            var parentPid = int.Parse(GetArgument(args, RenderWorkerProtocol.ParentPidArgument));

            using var shutdown = new CancellationTokenSource();
            using var watchdog = StartParentWatchdog(parentPid, shutdown);

            using var requestPipe = new AnonymousPipeClientStream(PipeDirection.In, requestHandle);
            using var responsePipe = new AnonymousPipeClientStream(PipeDirection.Out, responseHandle);

            var request = await RenderWorkerProtocol.ReadHeaderAsync<RenderWorkerRequest>(requestPipe, shutdown.Token);

            if (request.ProtocolVersion != RenderWorkerProtocol.Version)
            {
                await WriteFailureAsync(responsePipe, request, ErrorCodes.InternalError, "worker 프로토콜 버전이 다릅니다.");
                return 2;
            }

            var pixels = new byte[request.Bitmap.ByteLength];
            await RenderWorkerProtocol.ReadExactlyAsync(requestPipe, pixels, shutdown.Token);

            try
            {
                // 모의 무응답. 부모가 렌더 예산을 넘기면 worker를 정리한다.
                if (request.Fault?.Kind == VirtualFaultKinds.RenderTimeout)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
                }

                if (request.Fault?.Kind == VirtualFaultKinds.FailAfterRows)
                {
                    await WritePartialAsync(responsePipe, request, pixels, shutdown.Token);
                    return 6;
                }

                var result = Render(request, pixels);
                await WriteSuccessAsync(responsePipe, request, result, shutdown.Token);
                return 0;
            }
            catch (RenderLimitExceededException ex)
            {
                await WriteFailureAsync(responsePipe, request, ex.Code, ex.Message);
                return 3;
            }
            catch (Exception ex)
            {
                // 예외 메시지에 픽셀 데이터가 섞이지 않도록 형식 이름만 전달한다.
                await WriteFailureAsync(responsePipe, request, ErrorCodes.InternalError, $"렌더링 실패: {ex.GetType().Name}");
                return 4;
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[print-worker] 시작 실패: {ex.GetType().Name}");
            return 1;
        }
    }

    private static ReceiptRenderResult Render(RenderWorkerRequest request, byte[] pixels)
    {
        request.Profile.EnsureValid();

        var bitmap = new DecodedBitmap(
            request.Bitmap.WidthDots,
            request.Bitmap.HeightDots,
            request.Bitmap.StrideBytes,
            pixels,
            request.Bitmap.SourceDigest);

        var backend = new CrossEscPosSkiaAdapter();
        var renderer = new ReceiptRenderer(
            backend,
            new ReceiptPaperComposer(backend),
            new ReceiptAppearanceRenderer(backend),
            new RenderLimits(request.Limits.MaxPixelsPerImage, request.Limits.MaxEncodedBytesPerJob));

        return renderer.Render(
            bitmap,
            request.Profile,
            request.Appearance.Seed,
            request.Appearance.EstimatedEffects);
    }

    private static async Task WriteSuccessAsync(
        Stream responsePipe,
        RenderWorkerRequest request,
        ReceiptRenderResult result,
        CancellationToken cancellationToken)
    {
        var artifacts = result.Artifacts;
        var payload = new byte[artifacts.Sum(a => a.ByteLength)];
        var offset = 0;
        foreach (var artifact in artifacts)
        {
            artifact.Bytes.CopyTo(payload, offset);
            offset += artifact.ByteLength;
        }

        var response = new RenderWorkerResponse(
            ProtocolVersion: RenderWorkerProtocol.Version,
            Ok: true,
            HostInstanceId: request.HostInstanceId,
            ClientJobId: request.ClientJobId,
            Layout: new RenderWorkerLayout(
                result.Layout.PaperWidthDots,
                result.Layout.PaperHeightDots,
                result.Layout.ContentXDots,
                result.Layout.ContentYDots,
                result.Layout.ContentWidthDots,
                result.Layout.ContentHeightDots,
                result.Layout.LeadingFeedDots,
                result.Layout.TrailingFeedDots,
                result.Layout.DpiX,
                result.Layout.DpiY),
            SourceDigest: result.SourceDigest,
            Appearance: new RenderWorkerAppearanceResult(
                ReceiptAppearanceRenderer.Revision,
                request.Appearance.Seed,
                request.Appearance.EstimatedEffects),
            Artifacts: [.. artifacts.Select(a => new RenderWorkerArtifact(a.Kind, a.ByteLength, a.WidthPx, a.HeightPx))]);

        await RenderWorkerProtocol.WriteFrameAsync(responsePipe, response, payload, cancellationToken);
    }

    /// <summary>
    /// N행까지만 그린 진단용 부분 이미지를 만들고 실패로 보고한다.
    /// 정상 결과가 아니므로 content 한 장만 보내고 지면/외관은 만들지 않는다.
    /// </summary>
    private static async Task WritePartialAsync(
        Stream responsePipe,
        RenderWorkerRequest request,
        byte[] pixels,
        CancellationToken cancellationToken)
    {
        var rows = Math.Clamp(request.Fault?.FailAfterRows ?? 0, 0, request.Bitmap.HeightDots);

        // 실패 지점 이후 행은 그리지 않는다(흰색으로 남긴다).
        var partialPixels = (byte[])pixels.Clone();
        Array.Clear(partialPixels, rows * request.Bitmap.StrideBytes, (request.Bitmap.HeightDots - rows) * request.Bitmap.StrideBytes);

        var backend = new CrossEscPosSkiaAdapter();
        var partialBitmap = new DecodedBitmap(
            request.Bitmap.WidthDots,
            request.Bitmap.HeightDots,
            request.Bitmap.StrideBytes,
            partialPixels,
            request.Bitmap.SourceDigest);

        using var image = backend.FromPixels(
            partialBitmap.WidthDots,
            partialBitmap.HeightDots,
            MonoBitmapDecoder.ToRowMajorPixels(partialBitmap));

        var png = backend.EncodePng(image);

        var response = new RenderWorkerResponse(
            ProtocolVersion: RenderWorkerProtocol.Version,
            Ok: false,
            HostInstanceId: request.HostInstanceId,
            ClientJobId: request.ClientJobId,
            Artifacts: [new RenderWorkerArtifact(ArtifactKinds.Content, png.Length, image.Width, image.Height)],
            Code: ErrorCodes.VirtualRenderFailed,
            Message: $"모의 오류: {rows}행까지 그린 뒤 실패했습니다.",
            Partial: true);

        await RenderWorkerProtocol.WriteFrameAsync(responsePipe, response, png, cancellationToken);
    }

    private static async Task WriteFailureAsync(
        Stream responsePipe,
        RenderWorkerRequest request,
        string code,
        string message)
    {
        var response = new RenderWorkerResponse(
            ProtocolVersion: RenderWorkerProtocol.Version,
            Ok: false,
            HostInstanceId: request.HostInstanceId,
            ClientJobId: request.ClientJobId,
            Code: code,
            Message: message);

        await RenderWorkerProtocol.WriteFrameAsync(responsePipe, response, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
    }

    /// <summary>부모 호스트가 사라지면 렌더를 멈추고 종료한다.</summary>
    private static Timer StartParentWatchdog(int parentPid, CancellationTokenSource shutdown)
        => new(
            _ =>
            {
                try
                {
                    using var parent = System.Diagnostics.Process.GetProcessById(parentPid);
                    if (parent.HasExited)
                    {
                        Environment.Exit(5);
                    }
                }
                catch (ArgumentException)
                {
                    Environment.Exit(5);
                }
            },
            state: null,
            dueTime: TimeSpan.FromMilliseconds(500),
            period: TimeSpan.FromMilliseconds(500));

    private static string GetArgument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length)
        {
            throw new ArgumentException($"필수 인수가 없습니다: {name}");
        }

        return args[index + 1];
    }
}
