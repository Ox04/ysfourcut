using System.Net;
using System.Text.Json;
using SkiaSharp;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Printing;
using YsFourcut.Host.Rendering;
using YsFourcut.Host.Tests.Support;

namespace YsFourcut.Host.Tests;

/// <summary>
/// 실제 .NET API를 거쳐 접수 → 상태 조회 → 세 PNG 조회까지의 흐름.
/// 렌더링은 진짜 worker 프로세스가 수행한다.
/// </summary>
public sealed class PrintJobFlowTests
{
    [Fact]
    public async Task Submitting_a_job_produces_three_downloadable_pngs()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var submit = await SubmitAsync(client, await ReadRevisionAsync(client), 576, 160);
        Assert.Equal(HttpStatusCode.Accepted, submit.Response.StatusCode);

        var accepted = submit.Body;
        Assert.Equal("virtual", accepted.GetProperty("printerMode").GetString());
        Assert.False(accepted.GetProperty("isPhysical").GetBoolean());
        Assert.True(accepted.GetProperty("spoolJobId").ValueKind == JsonValueKind.Null);
        Assert.True(accepted.GetProperty("queueBusy").GetBoolean());
        Assert.Contains(accepted.GetProperty("state").GetString(), new[] { "accepted", "rendering" });

        var status = await WaitForTerminalAsync(client, submit.ClientJobId);
        Assert.Equal("rendered", status.GetProperty("state").GetString());
        Assert.False(status.GetProperty("queueBusy").GetBoolean());
        Assert.True(status.GetProperty("simulation").GetProperty("isSimulated").GetBoolean());
        Assert.Equal(640, status.GetProperty("layout").GetProperty("paperWidthDots").GetInt32());
        Assert.Equal(24 + 160 + 64, status.GetProperty("layout").GetProperty("paperHeightDots").GetInt32());

        var artifacts = status.GetProperty("artifacts").EnumerateArray().ToList();
        Assert.Equal(3, artifacts.Count);
        Assert.All(artifacts, a =>
        {
            Assert.True(a.GetProperty("available").GetBoolean());
            Assert.True(a.GetProperty("complete").GetBoolean());
        });

        foreach (var kind in new[] { "content", "paper", "appearance" })
        {
            var response = await client.GetAsync($"/api/print-jobs/{submit.ClientJobId}/artifacts/{kind}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var decoded = SKBitmap.Decode(bytes);
            Assert.True(decoded.Width > 0 && decoded.Height > 0);

            if (kind == "paper")
            {
                Assert.Equal(640, decoded.Width);
                Assert.Equal(24 + 160 + 64, decoded.Height);
            }
        }
    }

    [Fact]
    public async Task Same_uuid_with_same_content_reuses_the_job_without_rendering_again()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        var first = await SubmitAsync(client, revision, 576, 96);
        var rendered = await WaitForTerminalAsync(client, first.ClientJobId);
        var firstExpiry = rendered.GetProperty("artifacts")[0].GetProperty("expiresAtUtc").GetString();

        // 같은 UUID + 같은 비트맵을 다시 보낸다.
        var again = await SubmitAsync(client, revision, 576, 96, first.ClientJobId);

        Assert.Equal(HttpStatusCode.Accepted, again.Response.StatusCode);
        Assert.Equal("rendered", again.Body.GetProperty("state").GetString());
        Assert.Equal(
            rendered.GetProperty("createdAtUtc").GetString(),
            again.Body.GetProperty("createdAtUtc").GetString());

        // 다시 렌더링했다면 만료 시각이 새로 잡힌다. 그대로여야 한 번만 만든 것이다.
        Assert.Equal(firstExpiry, again.Body.GetProperty("artifacts")[0].GetProperty("expiresAtUtc").GetString());
    }

    [Fact]
    public async Task Same_uuid_with_different_content_is_a_conflict()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        var first = await SubmitAsync(client, revision, 576, 96);
        await WaitForTerminalAsync(client, first.ClientJobId);

        var conflict = await SubmitAsync(client, revision, 576, 120, first.ClientJobId);

        Assert.Equal(HttpStatusCode.Conflict, conflict.Response.StatusCode);
        Assert.Equal("JOB_ID_CONFLICT", conflict.Body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Another_session_cannot_see_the_job_or_its_images()
    {
        using var factory = new PrintHostFactory();
        using var owner = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(owner);

        var submit = await SubmitAsync(owner, await ReadRevisionAsync(owner), 576, 96);
        await WaitForTerminalAsync(owner, submit.ClientJobId);

        using var stranger = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(stranger);

        var status = await stranger.GetAsync($"/api/print-jobs/{submit.ClientJobId}");
        var image = await stranger.GetAsync($"/api/print-jobs/{submit.ClientJobId}/artifacts/content");

        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, image.StatusCode);
        Assert.Equal("ARTIFACT_NOT_FOUND", await PrintHostFactory.ReadErrorCodeAsync(image));
    }

    [Fact]
    public async Task Unknown_job_is_not_found()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var response = await client.GetAsync($"/api/print-jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Images_expire_after_ten_minutes()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var submit = await SubmitAsync(client, await ReadRevisionAsync(client), 576, 96);
        await WaitForTerminalAsync(client, submit.ClientJobId);

        factory.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        var response = await client.GetAsync($"/api/print-jobs/{submit.ClientJobId}/artifacts/content");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("ARTIFACT_EXPIRED", await PrintHostFactory.ReadErrorCodeAsync(response));

        var status = await PrintHostFactory.ReadJsonAsync(await client.GetAsync($"/api/print-jobs/{submit.ClientJobId}"));
        Assert.All(
            status.GetProperty("artifacts").EnumerateArray(),
            a => Assert.False(a.GetProperty("available").GetBoolean()));
    }

    [Fact]
    public async Task Clearing_the_session_removes_the_images()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var submit = await SubmitAsync(client, await ReadRevisionAsync(client), 576, 96);
        await WaitForTerminalAsync(client, submit.ClientJobId);

        var clear = await client.PostAsync("/api/session/artifacts/clear", PrintHostFactory.JsonBody(new { schemaVersion = 1 }));
        var cleared = await PrintHostFactory.ReadJsonAsync(clear);

        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Equal(1, cleared.GetProperty("clearedJobs").GetInt32());

        var response = await client.GetAsync($"/api/print-jobs/{submit.ClientJobId}/artifacts/paper");
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Job_record_is_written_without_any_photo_data()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var submit = await SubmitAsync(client, await ReadRevisionAsync(client), 576, 96);
        await WaitForTerminalAsync(client, submit.ClientJobId);

        var path = Path.Combine(factory.StateDirectory, "jobs", submit.ClientJobId + ".json");
        Assert.True(File.Exists(path));

        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("\"state\": \"rendered\"", text);
        Assert.Contains("virtual-80mm-8dpmm", text);
        Assert.DoesNotContain("dataBase64", text);
        Assert.DoesNotContain(submit.DataBase64[..32], text);
        Assert.False(File.Exists(path + ".tmp"));
    }

    private static async Task<string> ReadRevisionAsync(HttpClient client)
    {
        var body = await PrintHostFactory.ReadJsonAsync(await client.GetAsync("/api/printer-profile"));
        return body.GetProperty("profile").GetProperty("revision").GetString()!;
    }

    private static async Task<JsonElement> WaitForTerminalAsync(HttpClient client, string clientJobId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var body = await PrintHostFactory.ReadJsonAsync(await client.GetAsync($"/api/print-jobs/{clientJobId}"));
            if (!body.GetProperty("queueBusy").GetBoolean())
            {
                return body;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("작업이 끝나지 않았습니다.");
    }

    internal static async Task<(HttpResponseMessage Response, JsonElement Body, string ClientJobId, string DataBase64)>
        SubmitAsync(HttpClient client, string revision, int width, int height, string? clientJobId = null)
    {
        var stride = (width + 7) / 8;
        var data = new byte[stride * height];
        for (var i = 0; i < data.Length; i += 5)
        {
            data[i] = 0b1100_1100;
        }

        var dataBase64 = Convert.ToBase64String(data);
        var jobId = clientJobId ?? Guid.NewGuid().ToString();

        var response = await client.PostAsync("/api/print-jobs", PrintHostFactory.JsonBody(new
        {
            schemaVersion = 1,
            clientJobId = jobId,
            profileId = "virtual-80mm-8dpmm",
            profileRevision = revision,
            bitmap = new
            {
                widthDots = width,
                heightDots = height,
                strideBytes = stride,
                bitOrder = "msb-first",
                blackBit = 1,
                dataBase64,
            },
        }));

        return (response, await PrintHostFactory.ReadJsonAsync(response), jobId, dataBase64);
    }
}

/// <summary>진행 중 상태를 결정적으로 만들기 위한 백엔드 대역.</summary>
internal sealed class BlockingPrinterBackend : IPrinterBackend
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PrinterMode Mode => PrinterMode.Virtual;

    public int Calls { get; private set; }

    public void Release() => _release.TrySetResult();

    public async Task<PrinterBackendResult> PrintAsync(
        PrinterBackendRequest request,
        CancellationToken cancellationToken)
    {
        Calls++;
        await _release.Task.WaitAsync(cancellationToken);

        return PrinterBackendResult.Failure("VIRTUAL_RENDER_FAILED", "테스트 대역", request.AppearanceSeed);
    }
}
