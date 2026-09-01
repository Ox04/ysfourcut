using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using YsFourcut.Host.Artifacts;
using YsFourcut.Host.Jobs;
using YsFourcut.Host.Rendering;
using YsFourcut.Host.Tests.Support;

namespace YsFourcut.Host.Tests;

/// <summary>
/// TTL·건수·바이트 한도와 정리/늦은 완료 경합. 조절 가능한 시계로 확인하며 실제로 기다리지 않는다.
/// </summary>
public sealed class ArtifactStoreLifecycleTests
{
    [Fact]
    public void Reading_does_not_extend_the_ttl()
    {
        var (store, clock) = CreateStore();
        Assert.True(store.TryPublish("s1", Job(), Artifacts(1000), 0, out _));
        var jobId = store.Describe("s1", LastJobId).Count;

        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(ArtifactLookup.Found, store.TryGet("s1", LastJobId, "content", out _));

        // 읽었다고 수명이 늘지 않는다.
        clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        Assert.Equal(ArtifactLookup.Expired, store.TryGet("s1", LastJobId, "content", out _));

        // 만료 뒤에는 다시 읽어도 이미지가 살아나지 않는다.
        Assert.Equal(ArtifactLookup.NotFound, store.TryGet("s1", LastJobId, "content", out _));
        Assert.Equal(1, jobId);
        Assert.Equal(0, store.JobCount);
    }

    [Fact]
    public void Session_keeps_at_most_three_jobs()
    {
        var (store, clock) = CreateStore();
        var ids = new List<string>();

        for (var i = 0; i < 4; i++)
        {
            var id = Job();
            ids.Add(id);
            Assert.True(store.TryPublish("s1", id, Artifacts(1000), 0, out _));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(3, store.CountForSession("s1"));
        Assert.Equal(ArtifactLookup.NotFound, store.TryGet("s1", ids[0], "content", out _));
        Assert.Equal(ArtifactLookup.Found, store.TryGet("s1", ids[3], "content", out _));
    }

    [Fact]
    public void Host_byte_limit_evicts_the_oldest_results()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryReceiptArtifactStore(
            clock,
            new ArtifactCacheLimits(TimeSpan.FromMinutes(10), MaxBytesPerHost: 2_500, MaxBytesPerJob: 2_000),
            NullLogger<InMemoryReceiptArtifactStore>.Instance);

        var first = Job();
        Assert.True(store.TryPublish("s1", first, Artifacts(1_000), 0, out _));
        clock.Advance(TimeSpan.FromSeconds(1));

        var second = Job();
        Assert.True(store.TryPublish("s2", second, Artifacts(2_000), 0, out _));

        Assert.Equal(ArtifactLookup.NotFound, store.TryGet("s1", first, "content", out _));
        Assert.Equal(ArtifactLookup.Found, store.TryGet("s2", second, "content", out _));
    }

    [Fact]
    public void Publish_after_a_clear_is_rejected_so_old_images_never_come_back()
    {
        var (store, _) = CreateStore();
        var jobId = Job();

        var generationAtStart = store.ClearGeneration("s1");
        Assert.True(store.TryPublish("s1", jobId, Artifacts(500), generationAtStart, out _));

        // 사용자가 다음 촬영을 눌러 결과를 지운다.
        Assert.Equal(1, store.ClearSession("s1"));

        // 늦게 끝난 작업이 같은 세대 값으로 다시 게시하려 하면 거부한다.
        Assert.False(store.TryPublish("s1", jobId, Artifacts(500), generationAtStart, out var reason));
        Assert.Contains("정리", reason);
        Assert.Equal(0, store.CountForSession("s1"));
        Assert.Equal(ArtifactLookup.NotFound, store.TryGet("s1", jobId, "content", out _));
    }

    [Fact]
    public void Job_over_the_per_job_byte_limit_is_not_stored()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryReceiptArtifactStore(
            clock,
            new ArtifactCacheLimits(TimeSpan.FromMinutes(10), MaxBytesPerJob: 100),
            NullLogger<InMemoryReceiptArtifactStore>.Instance);

        Assert.False(store.TryPublish("s1", Job(), Artifacts(500), 0, out var reason));
        Assert.Contains("한도", reason);
        Assert.Equal(0, store.JobCount);
    }

    private static string LastJobId = string.Empty;

    private static string Job()
    {
        LastJobId = Guid.NewGuid().ToString();
        return LastJobId;
    }

    private static IReadOnlyList<RenderedPng> Artifacts(int bytesEach)
        => [new RenderedPng("content", new byte[bytesEach], 10, 10)];

    private static (InMemoryReceiptArtifactStore Store, FakeTimeProvider Clock) CreateStore()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));
        return (
            new InMemoryReceiptArtifactStore(
                clock, ArtifactCacheLimits.Default, NullLogger<InMemoryReceiptArtifactStore>.Instance),
            clock);
    }
}

/// <summary>동시 요청·설정 변경·재시작이 진행 중 작업과 결과에 미치는 영향.</summary>
public sealed class JobRaceAndRestartTests
{
    [Fact]
    public async Task Hammering_the_same_uuid_creates_only_one_job()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await VirtualFaultTests.ReadRevisionAsync(client);
        var clientJobId = Guid.NewGuid().ToString();

        // 연타: 같은 UUID·같은 내용으로 동시에 5번 보낸다.
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            PrintJobFlowTests.SubmitAsync(client, revision, 576, 64, clientJobId)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.Response.StatusCode));

        var status = await VirtualFaultTests.WaitUntilIdleAsync(client, clientJobId);
        Assert.Equal("rendered", status.GetProperty("state").GetString());

        // 작업 기록도 하나만 남는다.
        var records = Directory.GetFiles(Path.Combine(factory.StateDirectory, "jobs"), "*.json");
        Assert.Single(records);
        Assert.Contains(clientJobId, records[0]);
    }

    [Fact]
    public async Task Lost_response_is_recovered_by_asking_for_the_same_uuid()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        // 접수 응답을 못 받았다고 가정하고 같은 UUID로 상태만 조회한다.
        var job = await PrintJobFlowTests.SubmitAsync(client, await VirtualFaultTests.ReadRevisionAsync(client), 576, 64);
        var recovered = await VirtualFaultTests.WaitUntilIdleAsync(client, job.ClientJobId);

        Assert.Equal(job.ClientJobId, recovered.GetProperty("clientJobId").GetString());
        Assert.Equal("rendered", recovered.GetProperty("state").GetString());

        // 새 UUID로 자동 재접수하지 않았음을 기록 수로 확인한다.
        Assert.Single(Directory.GetFiles(Path.Combine(factory.StateDirectory, "jobs"), "*.json"));
    }

    [Fact]
    public async Task Changing_the_profile_does_not_change_a_running_job()
    {
        var backend = new BlockingPrinterBackend();
        using var factory = new PrintHostFactory { BackendOverride = backend };
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await VirtualFaultTests.ReadRevisionAsync(client);

        var job = await PrintJobFlowTests.SubmitAsync(client, revision, 576, 64);

        // 진행 중에 58mm로 바꾼다.
        var save = await client.PutAsync("/api/printer-profile", PrintHostFactory.JsonBody(new
        {
            schemaVersion = 1,
            profileId = "virtual-58mm-8dpmm",
            expectedRevision = revision,
        }));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        backend.Release();
        var status = await VirtualFaultTests.WaitUntilIdleAsync(client, job.ClientJobId);

        // 접수 시점 스냅샷이 유지된다.
        Assert.Equal("virtual-80mm-8dpmm", status.GetProperty("profileId").GetString());
        Assert.Equal(revision, status.GetProperty("profileRevision").GetString());
    }

    [Fact]
    public async Task After_a_restart_the_job_shows_metadata_with_no_images()
    {
        var stateDirectory = Path.Combine(Path.GetTempPath(), "ysfourcut-tests-restart2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(stateDirectory, "jobs"));

        var clientJobId = Guid.NewGuid().ToString();
        var record = new PrintJobRecord(
            SchemaVersion: 1,
            ClientJobId: clientJobId,
            RequestDigest: "sha256-old",
            ProfileId: "virtual-80mm-8dpmm",
            ProfileRevision: "r1-old",
            PrinterMode: "virtual",
            State: PrintJobStates.Rendered,
            SpoolJobId: null,
            FailureCode: null,
            CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-3),
            UpdatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-3));

        await File.WriteAllTextAsync(
            Path.Combine(stateDirectory, "jobs", clientJobId + ".json"),
            JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        try
        {
            using var factory = new PrintHostFactory { StateDirectory = stateDirectory, KeepStateDirectory = true };
            using var client = factory.CreateApiClient();
            await PrintHostFactory.AuthenticateAsync(client);

            var status = await VirtualFaultTests.ReadStatusAsync(client, clientJobId);

            // 기록은 rendered지만 이미지는 사라졌다. 완성 이미지를 보여 주는 척하지 않는다.
            Assert.Equal("rendered", status.GetProperty("state").GetString());
            Assert.All(status.GetProperty("artifacts").EnumerateArray(), a =>
            {
                Assert.False(a.GetProperty("available").GetBoolean());
                Assert.False(a.GetProperty("complete").GetBoolean());
            });

            var image = await client.GetAsync($"/api/print-jobs/{clientJobId}/artifacts/content");
            Assert.Equal(HttpStatusCode.Gone, image.StatusCode);
            Assert.Equal("ARTIFACT_EXPIRED", await PrintHostFactory.ReadErrorCodeAsync(image));
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Clear_reports_what_remains_so_the_next_session_can_check_it()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var job = await PrintJobFlowTests.SubmitAsync(client, await VirtualFaultTests.ReadRevisionAsync(client), 576, 64);
        await VirtualFaultTests.WaitUntilIdleAsync(client, job.ClientJobId);

        var body = await PrintHostFactory.ReadJsonAsync(await client.PostAsync(
            "/api/session/artifacts/clear",
            PrintHostFactory.JsonBody(new { schemaVersion = 1 })));

        Assert.Equal(1, body.GetProperty("clearedJobs").GetInt32());
        Assert.Equal(0, body.GetProperty("remainingJobs").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("hostInstanceId").GetString()));

        // 정리 뒤 오래된 GET이 이미지를 되살리지 않는다.
        var image = await client.GetAsync($"/api/print-jobs/{job.ClientJobId}/artifacts/paper");
        Assert.Equal(HttpStatusCode.Gone, image.StatusCode);
    }
}
