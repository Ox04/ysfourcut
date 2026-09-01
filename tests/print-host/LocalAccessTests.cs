using System.Net;
using System.Net.Http.Headers;
using System.Text;
using YsFourcut.Host.Tests.Support;

namespace YsFourcut.Host.Tests;

/// <summary>루프백 Host, 전용 헤더, Origin, Content-Type, 본문 크기 경계.</summary>
public sealed class LocalAccessTests
{
    [Fact]
    public async Task Health_without_session_returns_minimal_status()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/health");
        var body = await PrintHostFactory.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal("virtual", body.GetProperty("printerMode").GetString());
        Assert.False(body.GetProperty("session").GetProperty("authenticated").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("hostInstanceId").GetString()));

        // 사진·파일 경로·프로필 상세는 인증 전 응답에 없다.
        Assert.False(body.TryGetProperty("profile", out _));
        Assert.False(body.TryGetProperty("settingsPath", out _));
    }

    [Fact]
    public async Task Api_responses_are_not_cached()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Rejects_request_with_foreign_host_header()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Host = "evil.example";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("HOST_NOT_ALLOWED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_api_request_without_client_header()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(PrintHostFactory.HostOrigin + "/"),
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("CLIENT_HEADER_REQUIRED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_mutation_from_unknown_origin()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bootstrap")
        {
            Content = PrintHostFactory.JsonBody(new { schemaVersion = 1, code = "whatever" }),
        };
        request.Headers.Add("Origin", "http://evil.example");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ORIGIN_NOT_ALLOWED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_mutation_without_origin_header()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(PrintHostFactory.HostOrigin + "/"),
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(PrintHostFactory.ClientHeaderName, PrintHostFactory.ClientHeaderValue);

        var response = await client.PostAsync("/api/bootstrap", PrintHostFactory.JsonBody(new { schemaVersion = 1, code = "x" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("ORIGIN_NOT_ALLOWED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_mutation_with_non_json_content_type()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var content = new StringContent("code=abc", Encoding.UTF8, new MediaTypeHeaderValue("application/x-www-form-urlencoded"));
        var response = await client.PostAsync("/api/bootstrap", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("CONTENT_TYPE_UNSUPPORTED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_body_over_one_mebibyte()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var oversized = new string('A', (1024 * 1024) + 1024);
        var response = await client.PostAsync(
            "/api/print-jobs",
            PrintHostFactory.JsonBody(new { schemaVersion = 1, filler = oversized }));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("BODY_TOO_LARGE", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_malformed_json_body()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var content = new StringContent("{ not json", Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        var response = await client.PostAsync("/api/print-jobs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("MALFORMED_JSON", await PrintHostFactory.ReadErrorCodeAsync(response));
    }
}
