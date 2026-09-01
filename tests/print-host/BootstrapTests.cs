using System.Net;
using YsFourcut.Host.Tests.Support;

namespace YsFourcut.Host.Tests;

/// <summary>일회용 코드 전달·교환·만료·재사용, 세션 쿠키 속성.</summary>
public sealed class BootstrapTests
{
    [Fact]
    public async Task Pairing_screen_hands_code_to_browser_in_url_fragment()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/pair");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();

        // 개발에서는 Vite 화면으로 돌려보내고, 코드는 fragment로만 전달한다.
        Assert.StartsWith(PrintHostFactory.DevScreenOrigin, location, StringComparison.Ordinal);
        Assert.Contains("#bootstrap=", location, StringComparison.Ordinal);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        // fragment는 서버로 전송되지 않으므로 질의 문자열에 코드가 새지 않아야 한다.
        var uri = new Uri(location);
        Assert.Equal(string.Empty, uri.Query);
    }

    [Fact]
    public async Task Exchange_sets_httponly_strict_session_cookie()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var code = await PrintHostFactory.IssueBootstrapCodeAsync(client);
        var response = await client.PostAsync(
            "/api/bootstrap",
            PrintHostFactory.JsonBody(new { schemaVersion = 1, code }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(PrintHostFactory.SessionCookieName + "=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        // 루프백 HTTP이므로 Secure에 의존하지 않고, Domain은 생략해 호스트 전용으로 둔다.
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);

        var body = await PrintHostFactory.ReadJsonAsync(response);
        Assert.True(body.GetProperty("session").GetProperty("authenticated").GetBoolean());

        // 응답 본문에 코드나 쿠키 값이 들어가지 않는다.
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(code, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_cookie_unlocks_protected_api()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var before = await client.GetAsync("/api/printer-profile");
        Assert.Equal(HttpStatusCode.Unauthorized, before.StatusCode);
        Assert.Equal("SESSION_REQUIRED", await PrintHostFactory.ReadErrorCodeAsync(before));

        await PrintHostFactory.AuthenticateAsync(client);

        var after = await client.GetAsync("/api/printer-profile");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);

        var health = await PrintHostFactory.ReadJsonAsync(await client.GetAsync("/api/health"));
        Assert.True(health.GetProperty("session").GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task Rejects_reused_code()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var code = await PrintHostFactory.IssueBootstrapCodeAsync(client);
        var first = await client.PostAsync("/api/bootstrap", PrintHostFactory.JsonBody(new { schemaVersion = 1, code }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsync("/api/bootstrap", PrintHostFactory.JsonBody(new { schemaVersion = 1, code }));

        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal("BOOTSTRAP_CODE_ALREADY_USED", await PrintHostFactory.ReadErrorCodeAsync(second));
    }

    [Fact]
    public async Task Rejects_code_after_sixty_seconds()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var code = await PrintHostFactory.IssueBootstrapCodeAsync(client);
        factory.Clock.Advance(TimeSpan.FromSeconds(61));

        var response = await client.PostAsync("/api/bootstrap", PrintHostFactory.JsonBody(new { schemaVersion = 1, code }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("BOOTSTRAP_CODE_EXPIRED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_unknown_code()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var response = await client.PostAsync(
            "/api/bootstrap",
            PrintHostFactory.JsonBody(new { schemaVersion = 1, code = "not-a-real-code" }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("BOOTSTRAP_CODE_INVALID", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_unsupported_schema_version()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var code = await PrintHostFactory.IssueBootstrapCodeAsync(client);
        var response = await client.PostAsync("/api/bootstrap", PrintHostFactory.JsonBody(new { schemaVersion = 2, code }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SCHEMA_VERSION_UNSUPPORTED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Code_and_session_cookie_never_reach_logs()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        var code = await PrintHostFactory.IssueBootstrapCodeAsync(client);
        var response = await client.PostAsync("/api/bootstrap", PrintHostFactory.JsonBody(new { schemaVersion = 1, code }));
        var setCookie = response.Headers.GetValues("Set-Cookie").Single();
        var cookieValue = setCookie[(setCookie.IndexOf('=') + 1)..setCookie.IndexOf(';')];

        // 실패 경로도 확인한다.
        await client.PostAsync("/api/bootstrap", PrintHostFactory.JsonBody(new { schemaVersion = 1, code }));
        await client.GetAsync("/api/printer-profile");

        var logs = factory.Logs.AllText;
        Assert.NotEqual(string.Empty, logs);
        Assert.DoesNotContain(code, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(cookieValue, logs, StringComparison.Ordinal);
    }

    /// <summary>
    /// 11번 실측에서 찾은 결함의 회귀 검사. `/pair`가 Results.Redirect를 쓰면
    /// 프레임워크가 Location(=일회용 코드)을 Information 로그로 남기고,
    /// 로그 수준을 Warning으로 낮춰 둔 설정만이 그것을 가리고 있었다.
    /// 코드는 로그 설정과 무관하게 어디에도 남지 않아야 한다.
    /// </summary>
    [Fact]
    public async Task Code_never_reaches_logs_even_with_every_filter_removed()
    {
        using var factory = new PrintHostFactory { VerboseLogs = true };
        using var client = factory.CreateApiClient();

        var code = await PrintHostFactory.IssueBootstrapCodeAsync(client);
        var response = await client.PostAsync("/api/bootstrap", PrintHostFactory.JsonBody(new { schemaVersion = 1, code }));
        var setCookie = response.Headers.GetValues("Set-Cookie").Single();
        var cookieValue = setCookie[(setCookie.IndexOf('=') + 1)..setCookie.IndexOf(';')];

        var logs = factory.Logs.AllText;

        // 필터를 걷어냈으므로 프레임워크의 Information 로그가 실제로 들어와야 한다(시험 유효성).
        Assert.Contains("Microsoft.AspNetCore", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(code, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(cookieValue, logs, StringComparison.Ordinal);
    }
}
