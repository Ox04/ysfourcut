using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Tests.Support;

namespace YsFourcut.Host.Tests;

/// <summary>
/// 진행 중 잠금, 진행 중 조회/정리 거부, 재시작 정리처럼 시점이 중요한 규칙.
/// 실제 worker 대신 멈춰 있는 백엔드 대역을 써서 결정적으로 확인한다.
/// </summary>
public sealed class PrintJobConcurrencyTests
{
    [Fact]
    public async Task Only_one_job_runs_at_a_time()
    {
        var backend = new BlockingPrinterBackend();
        using var factory = new PrintHostFactory { BackendOverride = backend };
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        var first = await PrintJobFlowTests.SubmitAsync(client, revision, 576, 64);
        Assert.Equal(HttpStatusCode.Accepted, first.Response.StatusCode);

        var second = await PrintJobFlowTests.SubmitAsync(client, revision, 576, 64);

        Assert.Equal(HttpStatusCode.Conflict, second.Response.StatusCode);
        Assert.Equal("PRINTER_BUSY", second.Body.GetProperty("error").GetProperty("code").GetString());

        backend.Release();
        await WaitUntilIdleAsync(client, first.ClientJobId);
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task Images_are_not_ready_while_rendering()
    {
        var backend = new BlockingPrinterBackend();
        using var factory = new PrintHostFactory { BackendOverride = backend };
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var job = await PrintJobFlowTests.SubmitAsync(client, await ReadRevisionAsync(client), 576, 64);

        var response = await client.GetAsync($"/api/print-jobs/{job.ClientJobId}/artifacts/content");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ARTIFACT_NOT_READY", await PrintHostFactory.ReadErrorCodeAsync(response));

        backend.Release();
        await WaitUntilIdleAsync(client, job.ClientJobId);
    }

    [Fact]
    public async Task Cannot_clear_results_while_a_job_is_running()
    {
        var backend = new BlockingPrinterBackend();
        using var factory = new PrintHostFactory { BackendOverride = backend };
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var job = await PrintJobFlowTests.SubmitAsync(client, await ReadRevisionAsync(client), 576, 64);

        var clear = await client.PostAsync(
            "/api/session/artifacts/clear",
            PrintHostFactory.JsonBody(new { schemaVersion = 1 }));

        Assert.Equal(HttpStatusCode.Conflict, clear.StatusCode);
        Assert.Equal("PRINTER_BUSY", await PrintHostFactory.ReadErrorCodeAsync(clear));

        backend.Release();
        await WaitUntilIdleAsync(client, job.ClientJobId);
    }

    [Fact]
    public async Task Failed_job_reports_the_failure_and_has_no_images()
    {
        var backend = new BlockingPrinterBackend();
        using var factory = new PrintHostFactory { BackendOverride = backend };
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var job = await PrintJobFlowTests.SubmitAsync(client, await ReadRevisionAsync(client), 576, 64);
        backend.Release();

        var status = await WaitUntilIdleAsync(client, job.ClientJobId);

        Assert.Equal("virtual_failed", status.GetProperty("state").GetString());
        Assert.False(status.GetProperty("queueBusy").GetBoolean());
        Assert.Equal("VIRTUAL_RENDER_FAILED", status.GetProperty("failure").GetProperty("code").GetString());
        Assert.All(status.GetProperty("artifacts").EnumerateArray(), a =>
        {
            Assert.False(a.GetProperty("available").GetBoolean());
            Assert.False(a.GetProperty("complete").GetBoolean());
        });

        // 결과가 애초에 없던 실패 작업은 404다(만료가 아니다).
        var image = await client.GetAsync($"/api/print-jobs/{job.ClientJobId}/artifacts/content");
        Assert.Equal(HttpStatusCode.NotFound, image.StatusCode);
    }

    [Fact]
    public async Task Unfinished_records_from_a_previous_run_are_marked_failed_without_re_rendering()
    {
        var stateDirectory = Path.Combine(Path.GetTempPath(), "ysfourcut-tests-restart", Guid.NewGuid().ToString("N"));
        var jobsDirectory = Path.Combine(stateDirectory, "jobs");
        Directory.CreateDirectory(jobsDirectory);

        var clientJobId = Guid.NewGuid().ToString();
        var stale = new PrintJobRecord(
            SchemaVersion: 1,
            ClientJobId: clientJobId,
            RequestDigest: "sha256-stale",
            ProfileId: "virtual-80mm-8dpmm",
            ProfileRevision: "r1-stale",
            PrinterMode: "virtual",
            State: PrintJobStates.Rendering,
            SpoolJobId: null,
            FailureCode: null,
            CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5));

        await File.WriteAllTextAsync(
            Path.Combine(jobsDirectory, clientJobId + ".json"),
            JsonSerializer.Serialize(stale, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        try
        {
            var backend = new BlockingPrinterBackend();
            using (var factory = new PrintHostFactory
                   {
                       BackendOverride = backend,
                       StateDirectory = stateDirectory,
                       KeepStateDirectory = true,
                   })
            {
                // 호스트를 실제로 시작시켜 시작 정리를 수행하게 한다.
                using var client = factory.CreateApiClient();
                var health = await client.GetAsync("/api/health");
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            }

            var text = await File.ReadAllTextAsync(Path.Combine(jobsDirectory, clientJobId + ".json"));
            var cleaned = JsonSerializer.Deserialize<PrintJobRecord>(
                text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            Assert.Equal(PrintJobStates.VirtualFailed, cleaned.State);
            Assert.Equal("HOST_RESTARTED", cleaned.FailureCode);

            // 자동으로 다시 렌더링하지 않는다.
            Assert.Equal(0, backend.Calls);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    /// <summary>
    /// 11번 간헐 실패 재현: 컨테이너가 PrintJobManager를 먼저 Dispose한 뒤에
    /// 호스트 종료(JobMaintenanceService.StopAsync → ShutdownAsync)가 실행되면
    /// 취소 토큰이 이미 정리돼 ObjectDisposedException이 종료 경로를 깨뜨렸다.
    /// 종료는 어떤 순서에서도 예외 없이 끝나야 한다.
    /// </summary>
    [Fact]
    public async Task Shutdown_after_dispose_does_not_throw()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var manager = factory.Services.GetRequiredService<PrintJobManager>();
        manager.Dispose();

        await manager.ShutdownAsync();
    }

    private static async Task<string> ReadRevisionAsync(HttpClient client)
    {
        var body = await PrintHostFactory.ReadJsonAsync(await client.GetAsync("/api/printer-profile"));
        return body.GetProperty("profile").GetProperty("revision").GetString()!;
    }

    private static async Task<JsonElement> WaitUntilIdleAsync(HttpClient client, string clientJobId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var body = await PrintHostFactory.ReadJsonAsync(await client.GetAsync($"/api/print-jobs/{clientJobId}"));
            if (!body.GetProperty("queueBusy").GetBoolean())
            {
                return body;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("작업이 끝나지 않았습니다.");
    }
}
