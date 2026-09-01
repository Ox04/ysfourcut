using System.Net;
using System.Text.Json;
using YsFourcut.Host.Tests.Support;

namespace YsFourcut.Host.Tests;

/// <summary>
/// 접수 전 요청 검증 경계. 크기·stride·padding·revision 거부를 실제 API로 확인한다.
/// 접수 뒤의 흐름은 PrintJobFlowTests가 맡는다.
/// </summary>
public sealed class PrintJobApiTests
{
    /// <summary>검증을 통과한 요청은 202로 접수된다.</summary>
    [Fact]
    public async Task Valid_request_is_accepted()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        var response = await PostJobAsync(client, revision, Bitmap(576, 8));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await PrintHostFactory.ReadJsonAsync(response);

        Assert.Equal("virtual", body.GetProperty("printerMode").GetString());
        Assert.Contains(body.GetProperty("state").GetString(), new[] { "accepted", "rendering", "rendered" });
        Assert.False(body.GetProperty("isPhysical").GetBoolean());
    }

    [Fact]
    public async Task Rejects_stride_that_does_not_match_width()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        var bitmap = Bitmap(100, 4) with { StrideBytes = 12 }; // ceil(100/8) = 13
        var response = await PostJobAsync(client, revision, bitmap);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await PrintHostFactory.ReadJsonAsync(response)).GetProperty("error");
        Assert.Equal("INVALID_BITMAP", error.GetProperty("code").GetString());
        Assert.Equal("stride_mismatch", error.GetProperty("details").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Rejects_black_padding_bits_at_row_end()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        // 폭 100 → 마지막 바이트의 하위 4비트는 패딩이며 흰색(0)이어야 한다.
        var stride = 13;
        var data = new byte[stride * 3];
        data[stride - 1] = 0b0000_0001;

        var bitmap = new BitmapPayload(100, 3, stride, "msb-first", 1, Convert.ToBase64String(data));
        var response = await PostJobAsync(client, revision, bitmap);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await PrintHostFactory.ReadJsonAsync(response)).GetProperty("error");
        Assert.Equal("INVALID_BITMAP", error.GetProperty("code").GetString());
        Assert.Equal("padding_not_white", error.GetProperty("details").GetProperty("reason").GetString());
        Assert.Equal(0, error.GetProperty("details").GetProperty("row").GetInt32());
    }

    [Fact]
    public async Task Rejects_data_length_that_does_not_match_stride_times_height()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        var bitmap = Bitmap(64, 4) with { DataBase64 = Convert.ToBase64String(new byte[8 * 3]) };
        var response = await PostJobAsync(client, revision, bitmap);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await PrintHostFactory.ReadJsonAsync(response)).GetProperty("error");
        Assert.Equal("INVALID_BITMAP", error.GetProperty("code").GetString());
        Assert.Equal("data_length_mismatch", error.GetProperty("details").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Rejects_invalid_base64()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        var bitmap = Bitmap(64, 2) with { DataBase64 = "이건 base64가 아닙니다!!" };
        var response = await PostJobAsync(client, revision, bitmap);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await PrintHostFactory.ReadJsonAsync(response)).GetProperty("error");
        Assert.Equal("INVALID_BITMAP", error.GetProperty("code").GetString());
        Assert.Equal("base64_invalid", error.GetProperty("details").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Rejects_width_wider_than_profile_content_width()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        // 80mm 프리셋의 인쇄 내용 폭은 576dot. 서비스 상한(1024)보다는 작다.
        var response = await PostJobAsync(client, revision, Bitmap(600, 4));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await PrintHostFactory.ReadJsonAsync(response)).GetProperty("error");
        Assert.Equal("PAGE_SIZE_UNSUPPORTED", error.GetProperty("code").GetString());
        Assert.Equal("profile", error.GetProperty("details").GetProperty("limit").GetString());
    }

    [Fact]
    public async Task Rejects_size_over_service_limit()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        var bitmap = new BitmapPayload(2000, 10, 250, "msb-first", 1, "AA==");
        var response = await PostJobAsync(client, revision, bitmap);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await PrintHostFactory.ReadJsonAsync(response)).GetProperty("error");
        Assert.Equal("PAGE_SIZE_UNSUPPORTED", error.GetProperty("code").GetString());
        Assert.Equal("service", error.GetProperty("details").GetProperty("limit").GetString());
    }

    [Fact]
    public async Task Rejects_stale_profile_revision()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var response = await PostJobAsync(client, "r1-0000000000000000", Bitmap(576, 4));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("PROFILE_CHANGED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_unknown_profile()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var response = await client.PostAsync("/api/print-jobs", PrintHostFactory.JsonBody(new
        {
            schemaVersion = 1,
            clientJobId = Guid.NewGuid().ToString(),
            profileId = "ahapos-real",
            profileRevision = "r1-0000000000000000",
            bitmap = Bitmap(576, 4),
        }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("PRINTER_NOT_FOUND", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_non_uuid_client_job_id()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);
        var revision = await ReadRevisionAsync(client);

        var response = await client.PostAsync("/api/print-jobs", PrintHostFactory.JsonBody(new
        {
            schemaVersion = 1,
            clientJobId = "job-1",
            profileId = "virtual-80mm-8dpmm",
            profileRevision = revision,
            bitmap = Bitmap(576, 4),
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Print_job_endpoints_require_session()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var response = await PostJobAsync(client, "r1-0000000000000000", Bitmap(576, 4));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("SESSION_REQUIRED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Unknown_job_is_not_found_everywhere()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var jobId = Guid.NewGuid().ToString();
        foreach (var path in new[]
                 {
                     $"/api/print-jobs/{jobId}",
                     $"/api/print-jobs/{jobId}/artifacts/content",
                 })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // 결과가 없어도 세션 정리는 성공한다.
        var clear = await client.PostAsync("/api/session/artifacts/clear", PrintHostFactory.JsonBody(new { schemaVersion = 1 }));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Equal(0, (await PrintHostFactory.ReadJsonAsync(clear)).GetProperty("clearedJobs").GetInt32());

        // 없는 작업의 정리 요청도 404다.
        var resolve = await client.PostAsync($"/api/print-jobs/{jobId}/resolve", PrintHostFactory.JsonBody(new { schemaVersion = 1 }));
        Assert.Equal(HttpStatusCode.NotFound, resolve.StatusCode);
    }

    private static async Task<string> ReadRevisionAsync(HttpClient client)
    {
        var body = await PrintHostFactory.ReadJsonAsync(await client.GetAsync("/api/printer-profile"));
        return body.GetProperty("profile").GetProperty("revision").GetString()!;
    }

    private static Task<HttpResponseMessage> PostJobAsync(HttpClient client, string revision, BitmapPayload bitmap)
        => client.PostAsync("/api/print-jobs", PrintHostFactory.JsonBody(new
        {
            schemaVersion = 1,
            clientJobId = Guid.NewGuid().ToString(),
            profileId = "virtual-80mm-8dpmm",
            profileRevision = revision,
            bitmap,
        }));

    /// <summary>패딩이 흰색인 정상 비트맵.</summary>
    private static BitmapPayload Bitmap(int width, int height)
    {
        var stride = (width + 7) / 8;
        return new BitmapPayload(width, height, stride, "msb-first", 1, Convert.ToBase64String(new byte[stride * height]));
    }

    private sealed record BitmapPayload(
        int WidthDots,
        int HeightDots,
        int StrideBytes,
        string BitOrder,
        int BlackBit,
        string DataBase64);
}
