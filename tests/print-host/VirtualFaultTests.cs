using System.Net;
using System.Text.Json;
using SkiaSharp;
using YsFourcut.Host.Printing.Virtual;
using YsFourcut.Host.Tests.Support;

namespace YsFourcut.Host.Tests;

/// <summary>
/// 가상 모드의 모의 오류 주입. 다음 작업 하나에만 적용되고, 실제 센서 감지처럼 표시하지 않는다.
/// 지연은 조절 가능한 시계로 확인해 실제로 기다리지 않는다.
/// </summary>
public sealed class VirtualFaultTests
{
    [Theory]
    [InlineData(VirtualFaultKinds.OutOfPaper, "VIRTUAL_OUT_OF_PAPER")]
    [InlineData(VirtualFaultKinds.CoverOpen, "VIRTUAL_COVER_OPEN")]
    [InlineData(VirtualFaultKinds.Offline, "VIRTUAL_PRINTER_OFFLINE")]
    [InlineData(VirtualFaultKinds.FailBeforeRender, "VIRTUAL_RENDER_FAILED")]
    public async Task Device_faults_fail_the_job_without_any_image(string fault, string expectedCode)
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        await InjectAsync(client, fault);
        var job = await PrintJobFlowTests.SubmitAsync(client, await ReadRevisionAsync(client), 576, 64);
        var status = await WaitUntilIdleAsync(client, job.ClientJobId);

        Assert.Equal("virtual_failed", status.GetProperty("state").GetString());
        Assert.Equal(expectedCode, status.GetProperty("failure").GetProperty("code").GetString());
        Assert.Contains("모의 오류", status.GetProperty("failure").GetProperty("message").GetString());
        Assert.All(status.GetProperty("artifacts").EnumerateArray(), a =>
            Assert.False(a.GetProperty("available").GetBoolean()));

        // 가상 출력임을 항상 드러내고, 주입한 오류를 상태에 남긴다.
        Assert.True(status.GetProperty("simulation").GetProperty("isSimulated").GetBoolean());
        Assert.Equal(fault, status.GetProperty("simulation").GetProperty("injectedFault").GetString());

        // 한 번만 적용하고 해제된다.
        var pending = await PrintHostFactory.ReadJsonAsync(await client.GetAsync("/api/virtual-printer/fault"));
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("fault").ValueKind);
    }

    [Fact]
    public async Task Next_job_after_a_fault_succeeds_because_injection_is_one_shot()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        await InjectAsync(client, VirtualFaultKinds.OutOfPaper);
        var failed = await PrintJobFlowTests.SubmitAsync(client, revision, 576, 64);
        Assert.Equal("virtual_failed", (await WaitUntilIdleAsync(client, failed.ClientJobId)).GetProperty("state").GetString());

        var second = await PrintJobFlowTests.SubmitAsync(client, revision, 576, 64);
        var status = await WaitUntilIdleAsync(client, second.ClientJobId);

        Assert.Equal("rendered", status.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("simulation").GetProperty("injectedFault").ValueKind);
    }

    [Fact]
    public async Task Injected_delay_runs_before_rendering_and_outside_the_render_budget()
    {
        using var factory = new PrintHostFactory
        {
            // 렌더 예산을 짧게 줘도 지연 때문에 실패하지 않아야 한다.
            VirtualPrinterOptionsOverride = new VirtualPrinterOptions { RenderTimeout = TimeSpan.FromSeconds(30) },
        };
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        await InjectAsync(client, VirtualFaultKinds.Delay, delayMs: 10_000);
        var job = await PrintJobFlowTests.SubmitAsync(client, await ReadRevisionAsync(client), 576, 64);

        // 시계를 움직이기 전에는 렌더가 시작되지 않는다.
        await Task.Delay(50);
        var waiting = await ReadStatusAsync(client, job.ClientJobId);
        Assert.Equal("accepted", waiting.GetProperty("state").GetString());
        Assert.True(waiting.GetProperty("queueBusy").GetBoolean());

        // 실제로 10초를 기다리지 않고 시계만 앞으로 돌린다.
        var status = await WaitUntilIdleAsync(client, job.ClientJobId, factory);
        Assert.Equal("rendered", status.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Failing_after_n_rows_keeps_a_partial_diagnostic_image()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        await InjectAsync(client, VirtualFaultKinds.FailAfterRows, failAfterRows: 20);
        var job = await PrintJobFlowTests.SubmitAsync(client, await ReadRevisionAsync(client), 576, 64);
        var status = await WaitUntilIdleAsync(client, job.ClientJobId);

        // 실패 상태를 유지한다. rendered로 바꾸지 않는다.
        Assert.Equal("virtual_failed", status.GetProperty("state").GetString());
        Assert.Equal("VIRTUAL_RENDER_FAILED", status.GetProperty("failure").GetProperty("code").GetString());

        var artifacts = status.GetProperty("artifacts").EnumerateArray().ToList();
        var content = artifacts.Single(a => a.GetProperty("kind").GetString() == "content");

        // 진단용 부분 이미지는 available=true여도 complete=false다.
        Assert.True(content.GetProperty("available").GetBoolean());
        Assert.False(content.GetProperty("complete").GetBoolean());
        Assert.All(
            artifacts.Where(a => a.GetProperty("kind").GetString() != "content"),
            a => Assert.False(a.GetProperty("available").GetBoolean()));

        var image = await client.GetAsync($"/api/print-jobs/{job.ClientJobId}/artifacts/content");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);

        using var decoded = SKBitmap.Decode(await image.Content.ReadAsByteArrayAsync());
        Assert.Equal(576, decoded.Width);
        Assert.Equal(64, decoded.Height);

        // 20행 이후는 그리지 않았다.
        var white = new SKColor(255, 255, 255);
        for (var x = 0; x < decoded.Width; x++)
        {
            Assert.Equal(white, decoded.GetPixel(x, 30));
        }

        // 지면/외관은 만들어지지 않았다.
        var paper = await client.GetAsync($"/api/print-jobs/{job.ClientJobId}/artifacts/paper");
        Assert.Equal(HttpStatusCode.NotFound, paper.StatusCode);
    }

    [Fact]
    public async Task Unresponsive_worker_is_cleaned_up_and_the_lock_is_released()
    {
        using var factory = new PrintHostFactory
        {
            VirtualPrinterOptionsOverride = new VirtualPrinterOptions { RenderTimeout = TimeSpan.FromMilliseconds(400) },
        };
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        await InjectAsync(client, VirtualFaultKinds.RenderTimeout);
        var stuck = await PrintJobFlowTests.SubmitAsync(client, revision, 576, 64);
        var status = await WaitUntilIdleAsync(client, stuck.ClientJobId);

        Assert.Equal("virtual_failed", status.GetProperty("state").GetString());
        Assert.Equal("RENDER_TIMEOUT", status.GetProperty("failure").GetProperty("code").GetString());
        Assert.False(status.GetProperty("queueBusy").GetBoolean());

        // 확정 실패 뒤에는 잠금이 풀려 다음 작업을 받을 수 있다. 자동 재실행은 하지 않는다.
        var next = await PrintJobFlowTests.SubmitAsync(client, revision, 576, 64);
        Assert.Equal(HttpStatusCode.Accepted, next.Response.StatusCode);
        Assert.Equal("rendered", (await WaitUntilIdleAsync(client, next.ClientJobId)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Later_injection_does_not_change_a_job_that_already_started()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        await InjectAsync(client, VirtualFaultKinds.Delay, delayMs: 5_000);
        var job = await PrintJobFlowTests.SubmitAsync(client, revision, 576, 64);

        // 진행 중에 다른 오류를 주입해도 이 작업에는 적용되지 않는다.
        await InjectAsync(client, VirtualFaultKinds.OutOfPaper);

        var status = await WaitUntilIdleAsync(client, job.ClientJobId, factory);
        Assert.Equal("rendered", status.GetProperty("state").GetString());
        Assert.Equal(VirtualFaultKinds.Delay, status.GetProperty("simulation").GetProperty("injectedFault").GetString());

        // 새로 주입한 오류는 다음 작업에 적용된다.
        var next = await PrintJobFlowTests.SubmitAsync(client, revision, 576, 64);
        var nextStatus = await WaitUntilIdleAsync(client, next.ClientJobId);
        Assert.Equal("VIRTUAL_OUT_OF_PAPER", nextStatus.GetProperty("failure").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Injection_can_be_read_and_cleared()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var set = await InjectAsync(client, VirtualFaultKinds.CoverOpen, delayMs: 1_000);
        Assert.Equal("cover_open", set.GetProperty("fault").GetProperty("kind").GetString());
        Assert.Equal(1_000, set.GetProperty("fault").GetProperty("delayMs").GetInt32());
        Assert.True(set.GetProperty("fault").GetProperty("isSimulated").GetBoolean());
        Assert.Contains("render_timeout", set.GetProperty("availableFaults").EnumerateArray().Select(v => v.GetString()));

        var cleared = await PrintHostFactory.ReadJsonAsync(await client.PutAsync(
            "/api/virtual-printer/fault",
            PrintHostFactory.JsonBody(new { schemaVersion = 1, fault = (string?)null })));

        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("fault").ValueKind);
    }

    [Fact]
    public async Task Rejects_unknown_fault_and_out_of_range_delay()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var unknown = await client.PutAsync(
            "/api/virtual-printer/fault",
            PrintHostFactory.JsonBody(new { schemaVersion = 1, fault = "set_on_fire" }));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await PrintHostFactory.ReadErrorCodeAsync(unknown));

        var tooLong = await client.PutAsync(
            "/api/virtual-printer/fault",
            PrintHostFactory.JsonBody(new { schemaVersion = 1, fault = "delay", delayMs = 20_000 }));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var tooShort = await client.PutAsync(
            "/api/virtual-printer/fault",
            PrintHostFactory.JsonBody(new { schemaVersion = 1, fault = "delay", delayMs = 100 }));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var missingRows = await client.PutAsync(
            "/api/virtual-printer/fault",
            PrintHostFactory.JsonBody(new { schemaVersion = 1, fault = "fail_after_rows" }));
        Assert.Equal(HttpStatusCode.BadRequest, missingRows.StatusCode);
    }

    [Fact]
    public async Task Virtual_failures_do_not_require_paper_or_queue_confirmation()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        await InjectAsync(client, VirtualFaultKinds.Offline);
        var job = await PrintJobFlowTests.SubmitAsync(client, await ReadRevisionAsync(client), 576, 64);
        await WaitUntilIdleAsync(client, job.ClientJobId);

        var resolve = await client.PostAsync(
            $"/api/print-jobs/{job.ClientJobId}/resolve",
            PrintHostFactory.JsonBody(new { schemaVersion = 1 }));

        Assert.Equal(HttpStatusCode.Conflict, resolve.StatusCode);
        Assert.Equal("RESOLVE_NOT_REQUIRED", await PrintHostFactory.ReadErrorCodeAsync(resolve));
    }

    internal static async Task<JsonElement> InjectAsync(
        HttpClient client,
        string fault,
        int delayMs = 0,
        int? failAfterRows = null)
    {
        var response = await client.PutAsync(
            "/api/virtual-printer/fault",
            PrintHostFactory.JsonBody(new { schemaVersion = 1, fault, delayMs, failAfterRows }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await PrintHostFactory.ReadJsonAsync(response);
    }

    internal static async Task<string> ReadRevisionAsync(HttpClient client)
    {
        var body = await PrintHostFactory.ReadJsonAsync(await client.GetAsync("/api/printer-profile"));
        return body.GetProperty("profile").GetProperty("revision").GetString()!;
    }

    internal static async Task<JsonElement> ReadStatusAsync(HttpClient client, string clientJobId)
        => await PrintHostFactory.ReadJsonAsync(await client.GetAsync($"/api/print-jobs/{clientJobId}"));

    /// <summary>
    /// 주입 지연이 있으면 가짜 시계를 조금씩 앞으로 돌리며 기다린다.
    /// 지연 타이머가 언제 등록되는지에 의존하지 않으므로 부하가 있어도 멈추지 않는다.
    /// </summary>
    internal static async Task<JsonElement> WaitUntilIdleAsync(
        HttpClient client,
        string clientJobId,
        PrintHostFactory? factory = null)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var body = await ReadStatusAsync(client, clientJobId);
            if (!body.GetProperty("queueBusy").GetBoolean())
            {
                return body;
            }

            factory?.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50);
        }

        throw new TimeoutException("작업이 끝나지 않았습니다.");
    }
}
