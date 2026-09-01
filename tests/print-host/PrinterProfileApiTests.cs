using System.Net;
using System.Text.Json;
using YsFourcut.Host.Tests.Support;

namespace YsFourcut.Host.Tests;

/// <summary>가상 프리셋 제공, 프로필 조회·저장, revision 충돌 거부.</summary>
public sealed class PrinterProfileApiTests
{
    [Fact]
    public async Task Lists_virtual_presets_without_querying_os_queues()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var body = await PrintHostFactory.ReadJsonAsync(await client.GetAsync("/api/printers"));

        Assert.Equal("virtual", body.GetProperty("printerMode").GetString());
        Assert.False(body.GetProperty("osQueryPerformed").GetBoolean());

        var printers = body.GetProperty("printers").EnumerateArray().ToList();
        Assert.Equal(2, printers.Count);
        Assert.Contains(printers, p => p.GetProperty("profileId").GetString() == "virtual-80mm-8dpmm");
        Assert.Contains(printers, p => p.GetProperty("profileId").GetString() == "virtual-58mm-8dpmm");

        foreach (var printer in printers)
        {
            Assert.Equal("virtual", printer.GetProperty("kind").GetString());
            // 가상 프로필을 실물 검증된 것으로 표시하지 않는다.
            Assert.False(printer.GetProperty("hardwareVerified").GetBoolean());
            Assert.False(printer.GetProperty("verification").GetProperty("verified").GetBoolean());
            Assert.Contains("AHAPOS 미검증", printer.GetProperty("notes").GetString());
        }
    }

    [Fact]
    public async Task Default_profile_is_the_eighty_millimetre_preset()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var body = await PrintHostFactory.ReadJsonAsync(await client.GetAsync("/api/printer-profile"));
        var profile = body.GetProperty("profile");
        var paper = profile.GetProperty("paper");

        Assert.Equal("virtual-80mm-8dpmm", profile.GetProperty("profileId").GetString());
        Assert.StartsWith("r1-", profile.GetProperty("revision").GetString());
        Assert.Equal(640, paper.GetProperty("paperWidthDots").GetInt32());
        Assert.Equal(576, paper.GetProperty("contentWidthDots").GetInt32());
        Assert.Equal(32, paper.GetProperty("sideMarginDots").GetInt32());
        Assert.Equal(24, paper.GetProperty("leadingFeedDots").GetInt32());
        Assert.Equal(64, paper.GetProperty("trailingFeedDots").GetInt32());
        Assert.Equal(203.2, paper.GetProperty("dpiX").GetDouble(), 3);
        Assert.Equal("straight", paper.GetProperty("cutStyle").GetString());

        var limits = body.GetProperty("serviceLimits");
        Assert.Equal(1024 * 1024, limits.GetProperty("maxRequestBodyBytes").GetInt64());
        Assert.Equal(1024, limits.GetProperty("maxWidthDots").GetInt32());
        Assert.Equal(4096, limits.GetProperty("maxHeightDots").GetInt32());
        Assert.Equal(512 * 1024, limits.GetProperty("maxDecodedBytes").GetInt32());
    }

    [Fact]
    public async Task Saves_selected_profile_and_keeps_it_after_reload()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var current = await ReadProfileAsync(client);
        var response = await client.PutAsync("/api/printer-profile", PrintHostFactory.JsonBody(new
        {
            schemaVersion = 1,
            profileId = "virtual-58mm-8dpmm",
            expectedRevision = current.GetProperty("revision").GetString(),
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await PrintHostFactory.ReadJsonAsync(response)).GetProperty("profile");
        Assert.Equal("virtual-58mm-8dpmm", saved.GetProperty("profileId").GetString());
        Assert.Equal(464, saved.GetProperty("paper").GetProperty("paperWidthDots").GetInt32());

        // 설정 파일에 남고 다시 조회해도 유지된다.
        var settingsPath = Path.Combine(factory.ConfigDirectory, "printer-profile.json");
        Assert.True(File.Exists(settingsPath));
        Assert.Contains("virtual-58mm-8dpmm", await File.ReadAllTextAsync(settingsPath));
        Assert.False(File.Exists(settingsPath + ".tmp"));

        var reread = await ReadProfileAsync(client);
        Assert.Equal("virtual-58mm-8dpmm", reread.GetProperty("profileId").GetString());
    }

    [Fact]
    public async Task Changing_print_critical_setting_changes_revision()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var before = await ReadProfileAsync(client);
        var beforeRevision = before.GetProperty("revision").GetString();

        var response = await client.PutAsync("/api/printer-profile", PrintHostFactory.JsonBody(new
        {
            schemaVersion = 1,
            profileId = "virtual-80mm-8dpmm",
            expectedRevision = beforeRevision,
            cutStyle = "tear",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await PrintHostFactory.ReadJsonAsync(response)).GetProperty("profile");

        Assert.Equal("tear", after.GetProperty("paper").GetProperty("cutStyle").GetString());
        Assert.NotEqual(beforeRevision, after.GetProperty("revision").GetString());
    }

    [Fact]
    public async Task Rejects_save_with_stale_revision()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var response = await client.PutAsync("/api/printer-profile", PrintHostFactory.JsonBody(new
        {
            schemaVersion = 1,
            profileId = "virtual-58mm-8dpmm",
            expectedRevision = "r1-0000000000000000",
        }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("PROFILE_CHANGED", await PrintHostFactory.ReadErrorCodeAsync(response));

        // 거부됐으므로 활성 프로필은 그대로다.
        var profile = await ReadProfileAsync(client);
        Assert.Equal("virtual-80mm-8dpmm", profile.GetProperty("profileId").GetString());
    }

    [Fact]
    public async Task Rejects_unknown_profile_id()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var current = await ReadProfileAsync(client);
        var response = await client.PutAsync("/api/printer-profile", PrintHostFactory.JsonBody(new
        {
            schemaVersion = 1,
            profileId = "ahapos-real-printer",
            expectedRevision = current.GetProperty("revision").GetString(),
        }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("PRINTER_NOT_FOUND", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rejects_invalid_cut_style()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();
        await PrintHostFactory.AuthenticateAsync(client);

        var current = await ReadProfileAsync(client);
        var response = await client.PutAsync("/api/printer-profile", PrintHostFactory.JsonBody(new
        {
            schemaVersion = 1,
            profileId = "virtual-80mm-8dpmm",
            expectedRevision = current.GetProperty("revision").GetString(),
            cutStyle = "zigzag",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await PrintHostFactory.ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Printer_endpoints_require_session()
    {
        using var factory = new PrintHostFactory();
        using var client = factory.CreateApiClient();

        foreach (var path in new[] { "/api/printers", "/api/printer-profile" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("SESSION_REQUIRED", await PrintHostFactory.ReadErrorCodeAsync(response));
        }
    }

    private static async Task<JsonElement> ReadProfileAsync(HttpClient client)
    {
        var body = await PrintHostFactory.ReadJsonAsync(await client.GetAsync("/api/printer-profile"));
        return body.GetProperty("profile");
    }
}
