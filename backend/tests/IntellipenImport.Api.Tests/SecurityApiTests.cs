using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntellipenImport.Api.Controllers;
using Microsoft.AspNetCore.Http;

namespace IntellipenImport.Api.Tests;

/// <summary>The real HTTP API: masking on every endpoint, reveal and download roles, audit, headers, and an upload that never touches disk.</summary>
public sealed class SecurityApiTests : IClassFixture<ApiFactory>
{
    private const string Committer = "Import.Uploader,Import.Committer";
    private readonly ApiFactory _factory;

    public SecurityApiTests(ApiFactory factory) => _factory = factory;

    private static string BackendFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("backend/samples not found");
    }

    private static readonly string SampleText = File.ReadAllText(Path.Combine(BackendFolder(), "samples", "ashwood-payroll_v1_sample.csv"));

    private static MultipartFormDataContent Form(string csv)
    {
        var form = new MultipartFormDataContent { { new StringContent(csv), "file", "members.csv" }, { new StringContent("ashwood-payroll"), "source" }, { new StringContent("v1"), "version" } };
        return form;
    }

    private async Task<string> UploadAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/v1/imports", Form(SampleText));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private async Task<string> UploadAndCommitAsync(HttpClient committer)
    {
        var id = await UploadAsync(committer);
        var issues = await committer.GetFromJsonAsync<JsonElement>($"/api/v1/imports/{id}/exceptions");
        var warnings = issues.EnumerateArray().Where(i => i.GetProperty("severity").GetString() == "Warning").Select(i => i.GetProperty("id").GetInt32()).ToArray();
        Assert.Equal(HttpStatusCode.OK, (await committer.PostAsJsonAsync($"/api/v1/imports/{id}/acknowledgements", new { issueIds = warnings })).StatusCode);
        var commit = await committer.PostAsJsonAsync($"/api/v1/imports/{id}/commit", new { reviewed = true });
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        return id;
    }

    private static readonly string[] Reads =
    [
        "rows?show=all&pageSize=200", "exceptions", "exceptions.csv", "corrections", "corrections.csv", "file?lines=500", "report", "report?download=true",
    ];

    // ---- Masking on every endpoint, both scopes ----

    [Fact]
    public async Task No_endpoint_returns_a_full_NI_number_birth_date_or_salary_for_a_held_or_committed_batch()
    {
        var committer = _factory.As("carl", Committer);
        var held = await UploadAsync(committer);
        var saved = await UploadAndCommitAsync(committer);

        foreach (var (scope, id) in new[] { ("imports", held), ("committed", saved) })
            foreach (var read in Reads.Where(r => scope == "imports" || !r.StartsWith("rows?show=all", StringComparison.Ordinal)).Append(scope == "imports" ? "rows?show=all&pageSize=200" : "rows?pageSize=200"))
            {
                var response = await committer.GetAsync($"/api/v1/{scope}/{id}/{read}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                Assert.NotEmpty(body);
                SecurityAssert.NoSensitiveValues(body, $"{scope} {read}");
            }

        var rows = await committer.GetStringAsync($"/api/v1/imports/{held}/rows?show=all&pageSize=200");
        Assert.Contains("AB12****C", rows);
        Assert.Contains("**/**/1967", rows);
        Assert.Contains("\"****\"", rows);
        Assert.Contains("David", rows); // names are not masked
    }

    [Fact]
    public async Task A_viewer_sees_masked_committed_data_but_cannot_reveal_or_download_the_full_file()
    {
        var committer = _factory.As("carl", Committer);
        var id = await UploadAndCommitAsync(committer);
        var viewer = _factory.As("vera", "Import.Viewer");

        var rows = await viewer.GetAsync($"/api/v1/committed/{id}/rows?pageSize=200");
        Assert.Equal(HttpStatusCode.OK, rows.StatusCode);
        SecurityAssert.NoSensitiveValues(await rows.Content.ReadAsStringAsync(), "viewer rows");
        var preview = await viewer.GetAsync($"/api/v1/committed/{id}/file?lines=500");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        SecurityAssert.NoSensitiveValues(await preview.Content.ReadAsStringAsync(), "viewer preview");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/v1/committed/{id}/file/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/v1/committed/{id}/rows/2/reveal")).StatusCode);
        Assert.DoesNotContain(_factory.Audit.Lines, l => l.StartsWith($"file-downloaded|{id}|vera", StringComparison.Ordinal));
    }

    // ---- Reveal ----

    [Fact]
    public async Task Reveal_needs_the_role_returns_one_members_real_values_and_is_audited_without_them()
    {
        var committer = _factory.As("carl", Committer);
        var revealer = _factory.As("rita", "Import.Uploader,Import.RevealSensitive"); // owns the batch: uploaders only see their own
        var id = await UploadAsync(revealer);
        var line = (await committer.GetFromJsonAsync<JsonElement>($"/api/v1/imports/{id}/rows?search=DB001&show=all")).GetProperty("items")[0].GetProperty("lineNumber").GetInt32();

        var plain = _factory.As("uma", "Import.Uploader");
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync($"/api/v1/imports/{id}/rows/{line}/reveal")).StatusCode);
        Assert.DoesNotContain(_factory.Audit.Lines, l => l.StartsWith($"member-revealed|{id}|uma", StringComparison.Ordinal));

        var ok = await revealer.GetAsync($"/api/v1/imports/{id}/rows/{line}/reveal");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadAsStringAsync();
        Assert.Contains("AB123456C", body);
        Assert.Contains("01/02/1967", body);
        Assert.Contains("50000", body);

        Assert.Contains($"member-revealed|{id}|rita|line {line}", _factory.Audit.Lines);
        Assert.DoesNotContain("AB123456C", string.Join("\n", _factory.Audit.Lines));

        // A committer may reveal too; a line that doesn't exist is a clear 404.
        Assert.Equal(HttpStatusCode.OK, (await committer.GetAsync($"/api/v1/imports/{id}/rows/{line}/reveal")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await committer.GetAsync($"/api/v1/imports/{id}/rows/9999/reveal")).StatusCode);
    }

    [Fact]
    public async Task The_full_file_download_is_for_committers_and_audited()
    {
        var committer = _factory.As("carl", Committer);
        var uploader = _factory.As("uma", "Import.Uploader,Import.RevealSensitive");
        var id = await UploadAsync(uploader); // uma owns the batch, so the 403 below is the policy, not "not found"
        Assert.Equal(HttpStatusCode.Forbidden, (await uploader.GetAsync($"/api/v1/imports/{id}/file/download")).StatusCode);

        // The old "?download=true" form is no way round it: it is just the masked preview now.
        var viaQuery = await uploader.GetAsync($"/api/v1/imports/{id}/file?download=true");
        Assert.Equal(HttpStatusCode.OK, viaQuery.StatusCode);
        Assert.DoesNotContain("AB123456C", await viaQuery.Content.ReadAsStringAsync());

        var file = await committer.GetAsync($"/api/v1/imports/{id}/file/download");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Contains("AB123456C", await file.Content.ReadAsStringAsync());
        Assert.Contains(_factory.Audit.Lines, l => l.StartsWith($"file-downloaded|{id}|carl|", StringComparison.Ordinal));
    }

    // ---- Headers ----

    [Theory]
    [InlineData("/api/v1/sources")]
    [InlineData("/api/v1/committed")]
    [InlineData("/api/v1/imports/IMP-UNKNOWN")] // errors too
    public async Task Every_api_response_is_not_cacheable_and_carries_the_security_headers(string url)
    {
        var response = await _factory.As("carl", Committer).GetAsync(url);
        AssertHeaders(response);
    }

    [Fact]
    public async Task Downloads_are_not_cacheable_either()
    {
        var committer = _factory.As("carl", Committer);
        var id = await UploadAsync(committer);
        foreach (var url in new[] { $"/api/v1/imports/{id}/file/download", $"/api/v1/imports/{id}/exceptions.csv", $"/api/v1/imports/{id}/report?download=true" })
        {
            var response = await committer.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertHeaders(response);
        }
    }

    private static void AssertHeaders(HttpResponseMessage response)
    {
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("no-cache", response.Headers.Pragma.ToString());
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
    }

    // ---- Upload: streamed into memory, never to disk ----

    [Fact]
    public void The_upload_endpoint_has_no_form_binding_so_nothing_can_be_buffered_to_a_temporary_file()
    {
        var upload = typeof(ImportsController).GetMethod(nameof(ImportsController.Upload))!;
        Assert.DoesNotContain(upload.GetParameters(), p => p.ParameterType == typeof(IFormFile) || p.ParameterType == typeof(IFormCollection) || p.ParameterType == typeof(IFormFileCollection));
        Assert.Null(upload.GetCustomAttribute<Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute>());
    }

    /// <summary>
    /// ASP.NET's temporary folder for buffered request bodies is pointed at a folder that does not exist (see TempArea). Form binding of an
    /// IFormFile over 64 KB spills to a temporary file there, so it fails with DirectoryNotFoundException (checked while writing this test with a
    /// throwaway endpoint that used ReadFormAsync). The streamed upload succeeds, which shows it never writes the CSV to disk.
    /// </summary>
    [Fact]
    public async Task A_large_upload_succeeds_with_no_temporary_folder_so_the_csv_never_touches_disk()
    {
        var big = SampleText + string.Concat(Enumerable.Range(0, 2500).Select(i => $"BIG{i:D5},Pat Example,F,01/02/1970,AB{i % 1000:D3}456C,01/09/1995,,Active,40000,41000,42000,1,Y\r\n"));
        Assert.True(big.Length > 200_000); // far over the 64 KB form buffer
        Assert.False(Directory.Exists(TempArea.Path));

        var response = await _factory.As("carl", Committer).PostAsync("/api/v1/imports", Form(big));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(Directory.Exists(TempArea.Path));
        var summary = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(summary.GetProperty("counts").GetProperty("linesRead").GetInt32() > 2500);
    }

    [Fact]
    public async Task Upload_keeps_its_friendly_errors()
    {
        var client = _factory.As("carl", Committer);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/imports", new StringContent("x"))).StatusCode); // not multipart
        var noFile = await client.PostAsync("/api/v1/imports", new MultipartFormDataContent { { new StringContent("ashwood-payroll"), "source" }, { new StringContent("v1"), "version" } });
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
        Assert.Contains("Choose a CSV file", await noFile.Content.ReadAsStringAsync());
        var wrongType = await client.PostAsync("/api/v1/imports", new MultipartFormDataContent { { new StringContent("a,b"), "file", "notes.txt" }, { new StringContent("ashwood-payroll"), "source" }, { new StringContent("v1"), "version" } });
        Assert.Contains("Only .csv files", await wrongType.Content.ReadAsStringAsync());
        var noVersion = await client.PostAsync("/api/v1/imports", new MultipartFormDataContent { { new StringContent("a,b"), "file", "x.csv" } });
        Assert.Contains("Choose the source", await noVersion.Content.ReadAsStringAsync());

        var tooBig = new MultipartFormDataContent { { new ByteArrayContent(new byte[10 * 1024 * 1024 + 10]), "file", "huge.csv" }, { new StringContent("ashwood-payroll"), "source" }, { new StringContent("v1"), "version" } };
        var large = await client.PostAsync("/api/v1/imports", tooBig);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        Assert.Contains("File too large", await large.Content.ReadAsStringAsync());
        Assert.False(Directory.Exists(TempArea.Path));
    }
}

/// <summary>The real sample values that must never appear in a response. The same scan as the use-case tests, over HTTP bodies.</summary>
internal static class SecurityAssert
{
    private static readonly string[] Rows = File.ReadAllLines(Path.Combine(Backend(), "samples", "ashwood-payroll_v1_sample.csv")).Skip(1).ToArray();
    private static string Backend()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "samples"))) dir = dir.Parent;
        return dir!.FullName;
    }

    public static void NoSensitiveValues(string text, string where)
    {
        foreach (var cells in Rows.Select(r => r.Split(',')).Where(c => c.Length >= 13))
        {
            if (cells[4].Length == 9) Assert.False(text.Contains(cells[4], StringComparison.Ordinal), $"{where}: NI number {cells[4]} appears");
            Assert.False(text.Contains(cells[3], StringComparison.Ordinal), $"{where}: date of birth {cells[3]} appears");
            if (DateOnly.TryParseExact(cells[3], "dd/MM/yyyy", out var d))
                Assert.False(text.Contains(d.ToString("yyyy-MM-dd"), StringComparison.Ordinal), $"{where}: date of birth {cells[3]} appears as ISO");
            foreach (var salary in new[] { cells[8], cells[9], cells[10] }.Where(s => s.Length > 0 && !s.Contains('"')))
                Assert.False(Regex.IsMatch(text, $@"(?<![0-9,.]){salary}(\.00)?(?![0-9,])"), $"{where}: salary {salary} appears");
        }
    }
}

/// <summary>/api/v1/me: what the signed-in user may do, from the same policies the endpoints enforce.</summary>
public sealed class MeApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<JsonElement> MeAsync(string roles)
    {
        var response = await factory.As("someone", roles).GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static (bool, bool, bool, bool, bool) Can(JsonElement me)
    {
        var c = me.GetProperty("can");
        return (c.GetProperty("upload").GetBoolean(), c.GetProperty("commit").GetBoolean(), c.GetProperty("reveal").GetBoolean(),
            c.GetProperty("downloadFile").GetBoolean(), c.GetProperty("viewCommitted").GetBoolean());
    }

    [Theory]
    //                          upload commit reveal download view
    [InlineData("Import.Committer",                          true,  true,  true,  true,  true)]
    [InlineData("Import.Uploader,Import.Committer",          true,  true,  true,  true,  true)]
    [InlineData("Import.Uploader",                           true,  false, false, false, true)]
    [InlineData("Import.Uploader,Import.RevealSensitive",    true,  false, true,  false, true)]
    [InlineData("Import.Viewer",                             false, false, false, false, true)]
    [InlineData("Import.Viewer,Import.RevealSensitive",      false, false, true,  false, true)]
    public async Task Each_permission_follows_the_policy_the_endpoint_uses(string roles, bool upload, bool commit, bool reveal, bool download, bool view)
    {
        var me = await MeAsync(roles);
        Assert.Equal((upload, commit, reveal, download, view), Can(me));
        Assert.Equal("someone", me.GetProperty("userId").GetString());
        Assert.Equal(roles.Split(',').Order(), me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!));
    }

    [Fact]
    public async Task What_me_says_is_what_the_endpoints_do()
    {
        // A viewer-only user: /me says no to upload, and the upload endpoint agrees.
        var viewer = factory.As("vera", "Import.Viewer");
        Assert.False(Can(await MeAsync("Import.Viewer")).Item1);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/v1/imports", new MultipartFormDataContent())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/v1/committed")).StatusCode);
    }
}
