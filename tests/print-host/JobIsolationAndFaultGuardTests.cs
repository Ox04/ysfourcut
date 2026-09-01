using System.Net;
using System.Text.Json;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Platform;
using YsFourcut.Host.Printing;
using YsFourcut.Host.Tests.Support;

namespace YsFourcut.Host.Tests;

/// <summary>
/// 06번 독립 검수에서 나온 결함에 대한 회귀 검증.
/// physical 거부 분기, 기록 복원의 세션 격리, 만료 응답 일관성.
/// </summary>
public sealed class JobIsolationAndFaultGuardTests
{
    /// <summary>
    /// AC-06-01b. Linux에서는 physical 모드로 호스트를 시작할 수 없으므로
    /// physical 전용 분기는 HostInfo를 주입해 확인한다.
    /// </summary>
    [Fact]
    public async Task Fault_injection_is_refused_in_physical_mode()
    {
        using var factory = new PrintHostFactory { HostInfoOverride = new HostInfo(PrinterMode.Physical) };
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var response = await client.PutAsync(
            "/api/virtual-printer/fault",
            PrintHostFactory.JsonBody(new { schemaVersion = 1, fault = "out_of_paper" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("PLATFORM_UNSUPPORTED", await PrintHostFactory.ReadErrorCodeAsync(response));

        // 거부됐으므로 주입도 남지 않는다.
        var pending = await PrintHostFactory.ReadJsonAsync(await client.GetAsync("/api/virtual-printer/fault"));
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("fault").ValueKind);
        Assert.Equal("physical", pending.GetProperty("printerMode").GetString());
    }

    /// <summary>
    /// 결함 1. 이번 실행이 만든 기록은 메모리에서 사라진 뒤에도 복원되지 않는다.
    ///
    /// 메모리에 작업이 남아 있으면 <c>Find</c>의 첫 분기에서 세션 대조로 걸러지므로 복원 경로에 닿지 않는다.
    /// 그래서 **메모리에 없는** 상태를 만들기 위해 이번 실행의 hostInstanceId를 가진 기록만 직접 놓고 조회한다.
    /// 수정 전에는 이 조회가 200으로 다른 세션에 상태를 노출했다.
    /// </summary>
    [Fact]
    public async Task Records_written_by_the_current_run_are_never_restored()
    {
        using var factory = new PrintHostFactory();
        using var owner = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(owner);

        // 1) 실제 작업의 기록에 이번 실행의 hostInstanceId가 들어가는지 확인한다.
        var health = await PrintHostFactory.ReadJsonAsync(await owner.GetAsync("/api/health"));
        var hostInstanceId = health.GetProperty("hostInstanceId").GetString()!;

        var job = await PrintJobFlowTests.SubmitAsync(
            owner, await VirtualFaultTests.ReadRevisionAsync(owner), 576, 64);
        await VirtualFaultTests.WaitUntilIdleAsync(owner, job.ClientJobId);

        var recordPath = Path.Combine(factory.StateDirectory, "jobs", job.ClientJobId + ".json");
        Assert.Contains(hostInstanceId, await File.ReadAllTextAsync(recordPath));

        // 2) 메모리에 없는 "이번 실행 기록"을 직접 놓는다(오래된 작업이 정리된 상황과 같다).
        var evictedJobId = Guid.NewGuid().ToString();
        var evicted = new PrintJobRecord(
            SchemaVersion: 1,
            ClientJobId: evictedJobId,
            RequestDigest: "sha256-evicted",
            ProfileId: "virtual-80mm-8dpmm",
            ProfileRevision: "r1-evicted",
            PrinterMode: "virtual",
            State: PrintJobStates.Rendered,
            SpoolJobId: null,
            FailureCode: null,
            CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            HostInstanceId: hostInstanceId);

        await File.WriteAllTextAsync(
            Path.Combine(factory.StateDirectory, "jobs", evictedJobId + ".json"),
            JsonSerializer.Serialize(evicted, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        // 소유자 세션도, 다른 세션도 이번 실행의 기록으로는 복원할 수 없다.
        using var stranger = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(stranger);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/print-jobs/{evictedJobId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/print-jobs/{evictedJobId}")).StatusCode);

        // 메모리에 살아 있는 작업도 다른 세션에는 보이지 않는다.
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/print-jobs/{job.ClientJobId}")).StatusCode);
    }

    /// <summary>
    /// 결함 1의 반대편: 이전 실행의 기록은 재시작 뒤 세션이 달라져도 상태만 복원한다.
    /// 이미지는 항상 없으므로 available=false다.
    /// </summary>
    [Fact]
    public async Task Records_from_a_previous_run_are_restored_without_images()
    {
        var stateDirectory = Path.Combine(Path.GetTempPath(), "ysfourcut-tests-prevrun", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(stateDirectory, "jobs"));

        var clientJobId = Guid.NewGuid().ToString();
        var record = new PrintJobRecord(
            SchemaVersion: 1,
            ClientJobId: clientJobId,
            RequestDigest: "sha256-previous",
            ProfileId: "virtual-80mm-8dpmm",
            ProfileRevision: "r1-previous",
            PrinterMode: "virtual",
            State: PrintJobStates.Rendered,
            SpoolJobId: null,
            FailureCode: null,
            CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            HostInstanceId: "previous-host-instance");

        await File.WriteAllTextAsync(
            Path.Combine(stateDirectory, "jobs", clientJobId + ".json"),
            JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        try
        {
            using var factory = new PrintHostFactory { StateDirectory = stateDirectory, KeepStateDirectory = true };
            using var client = factory.CreateApiClient();
            await PrintHostFactory.AuthenticateAsync(client);

            var status = await VirtualFaultTests.ReadStatusAsync(client, clientJobId);

            Assert.Equal("rendered", status.GetProperty("state").GetString());
            Assert.All(status.GetProperty("artifacts").EnumerateArray(), a =>
                Assert.False(a.GetProperty("available").GetBoolean()));

            var image = await client.GetAsync($"/api/print-jobs/{clientJobId}/artifacts/content");
            Assert.Equal(HttpStatusCode.Gone, image.StatusCode);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    /// <summary>결함 4. 진단용 부분 이미지도 만료 뒤 반복 조회에서 404로 바뀌지 않는다.</summary>
    [Fact]
    public async Task Expired_partial_image_keeps_reporting_gone()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        await VirtualFaultTests.InjectAsync(client, "fail_after_rows", failAfterRows: 16);
        var job = await PrintJobFlowTests.SubmitAsync(client, await VirtualFaultTests.ReadRevisionAsync(client), 576, 64);
        var status = await VirtualFaultTests.WaitUntilIdleAsync(client, job.ClientJobId);
        Assert.Equal("virtual_failed", status.GetProperty("state").GetString());

        var path = $"/api/print-jobs/{job.ClientJobId}/artifacts/content";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);

        factory.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        var first = await client.GetAsync(path);
        var second = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Gone, first.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);
        Assert.Equal("ARTIFACT_EXPIRED", await PrintHostFactory.ReadErrorCodeAsync(second));
    }
}
