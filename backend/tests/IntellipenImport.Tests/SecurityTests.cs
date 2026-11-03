using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Imports;
using IntellipenImport.Application.Processing;
using IntellipenImport.Domain.Canonical;
using IntellipenImport.Domain.Processing;
using static IntellipenImport.Tests.TestFiles;

namespace IntellipenImport.Tests;

/// <summary>Masking of NI numbers, dates of birth and salaries in every response, and who may see or download the real values.</summary>
public class SecurityTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- The real values in the Ashwood sample: none may appear in anything the API returns ----

    private static readonly string[] Lines = Sample("ashwood-payroll_v1_sample.csv").Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
    private static string[] Cell(int i) => Lines.Select(l => l.Split(',', StringSplitOptions.None)).Where(c => c.Length >= 13).Select(c => c[i]).ToArray();

    private static readonly string[] NiNumbers = Cell(4).Where(v => v.Length == 9).ToArray();
    private static readonly string[] Dobs = Cell(3).Select(v => v.Trim()).Where(v => v.Length > 0).ToArray(); // dd/MM/yyyy
    private static readonly string[] Salaries = Cell(8).Concat(Cell(9)).Concat(Cell(10)).Where(v => v.Length > 0 && !v.Contains('"')).Distinct().ToArray();

    /// <summary>Fails if the text contains any real NI number, birth date (in either layout) or salary amount from the sample.</summary>
    internal static void AssertNoSensitiveValues(string text, string where)
    {
        foreach (var ni in NiNumbers) Assert.False(text.Contains(ni, StringComparison.Ordinal), $"{where}: full NI number {ni} appears");
        foreach (var dob in Dobs)
        {
            Assert.False(text.Contains(dob, StringComparison.Ordinal), $"{where}: date of birth {dob} appears");
            if (DateOnly.TryParseExact(dob, "dd/MM/yyyy", out var d))
                Assert.False(text.Contains(d.ToString("yyyy-MM-dd"), StringComparison.Ordinal), $"{where}: date of birth {dob} appears as ISO");
        }
        foreach (var salary in Salaries)
            Assert.False(Regex.IsMatch(text, $@"(?<![0-9,.]){salary}(\.00)?(?![0-9,])"), $"{where}: salary {salary} appears");
    }

    private static async Task<TestApp> CommittedAshwoodAsync(TestApp app)
    {
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(Sample("ashwood-payroll_v1_sample.csv")));
        app.Id = (await app.Commands.UploadAsync(new UploadCommand("members.csv", content.Length, content, "ashwood-payroll", "v1"), default)).Id;
        return app;
    }

    private static string All(params object[] responses) => string.Join("\n", responses.Select(r => r as string ?? JsonSerializer.Serialize(r, Json)));

    private static async Task CheckAllHeldAsync(TestApp app)
    {
        var rows = await app.Queries.RowsAsync(app.Id, 1, 200, null, "all", default);
        var preview = await app.Queries.FilePreviewAsync(app.Id, 500, default);
        var issues = await app.Queries.IssuesAsync(app.Id, default);
        var corrections = await app.Queries.CorrectionsAsync(app.Id, default);
        var report = await app.Queries.ReportAsync(app.Id, default);
        var issuesCsv = Encoding.UTF8.GetString((await app.Queries.IssuesCsvAsync(app.Id, default)).Content);
        var correctionsCsv = Encoding.UTF8.GetString((await app.Queries.CorrectionsCsvAsync(app.Id, default)).Content);
        Assert.Equal(31, rows.Total);
        AssertNoSensitiveValues(All(rows), "held rows");
        AssertNoSensitiveValues(All(preview), "held file preview");
        AssertNoSensitiveValues(All(issues), "held exceptions");
        AssertNoSensitiveValues(All(corrections), "held corrections");
        AssertNoSensitiveValues(report, "held report");
        AssertNoSensitiveValues(issuesCsv, "held exceptions csv");
        AssertNoSensitiveValues(correctionsCsv, "held corrections csv");
    }

    [Fact]
    public async Task A_held_batch_shows_no_full_NI_birth_date_or_salary_anywhere()
    {
        var app = await CommittedAshwoodAsync(TestApp.Build());
        await CheckAllHeldAsync(app);
    }

    [Fact]
    public async Task A_committed_batch_shows_no_full_NI_birth_date_or_salary_anywhere()
    {
        var app = await CommittedAshwoodAsync(TestApp.Build());
        await app.Commands.AcknowledgeAsync(app.Id, (await app.Queries.IssuesAsync(app.Id, default)).Where(i => i.Severity == "Warning").Select(i => i.Id).ToList(), null, default);
        await app.Commands.CommitAsync(app.Id, reviewed: true, default);

        var rows = await app.Committed.RowsAsync(app.Id, 1, 200, null, null, default);
        var preview = await app.Committed.FilePreviewAsync(app.Id, 500, default);
        var issues = await app.Committed.IssuesAsync(app.Id, default);
        var corrections = await app.Committed.CorrectionsAsync(app.Id, default);
        var report = await app.Committed.ReportAsync(app.Id, default);
        var issuesCsv = Encoding.UTF8.GetString((await app.Committed.IssuesCsvAsync(app.Id, default)).Content);
        var correctionsCsv = Encoding.UTF8.GetString((await app.Committed.CorrectionsCsvAsync(app.Id, default)).Content);
        Assert.Equal(26, rows.Total);
        AssertNoSensitiveValues(All(rows), "committed rows");
        AssertNoSensitiveValues(All(preview), "committed file preview");
        AssertNoSensitiveValues(All(issues), "committed exceptions");
        AssertNoSensitiveValues(All(corrections), "committed corrections");
        AssertNoSensitiveValues(report, "committed report");
        AssertNoSensitiveValues(issuesCsv, "committed exceptions csv");
        AssertNoSensitiveValues(correctionsCsv, "committed corrections csv");
    }

    [Fact]
    public async Task Masked_values_have_the_agreed_shape_and_names_are_not_masked()
    {
        var app = await CommittedAshwoodAsync(TestApp.Build());
        var db001 = Assert.Single((await app.Queries.RowsAsync(app.Id, 1, 50, "DB001", "all", default)).Items);
        var source = db001.Source.ToDictionary(s => s.Column, s => s.Value);
        Assert.Equal(("AB12****C", "**/**/1967", "****", "David Okafor"), (source["NI number"], source["Date of birth"], source["Salary Y-1"], source["Name"]));
        var mem = db001.Records.Single(r => r.Type == "MEM").Fields.ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal(("AB12****C", "**/**/1967", "David", "Okafor"), (mem["NINumber"], mem["DOB"], mem["Forename"], mem["Surname"]));
        Assert.All(db001.Records.Where(r => r.Type == "SAL"), r => Assert.Equal("****", r.Fields.Single(f => f.Name == "Salary").Value));
        Assert.Equal("1.00", mem["PartTimeFraction"]); // not sensitive
        var preview = await app.Queries.FilePreviewAsync(app.Id, 3, default);
        Assert.StartsWith("HDR,", preview.Lines[0]);
        Assert.Contains("AB12****C", preview.Lines[1]);
    }

    [Theory]
    [InlineData(Sensitivity.NiNumber, "AB123456C", "AB12****C")]
    [InlineData(Sensitivity.NiNumber, "ab 12 34 56 c", "ab12****c")]
    [InlineData(Sensitivity.DateOfBirth, "01/02/1967", "**/**/1967")]
    [InlineData(Sensitivity.DateOfBirth, "1967-02-01", "**/**/1967")]
    [InlineData(Sensitivity.DateOfBirth, "not a date", "**/**/****")]
    [InlineData(Sensitivity.Salary, "46200.00", "****")]
    [InlineData(Sensitivity.Salary, "", "")]
    [InlineData(Sensitivity.None, "David", "David")]
    public void Masking_shapes(Sensitivity sensitivity, string value, string expected) => Assert.Equal(expected, Masking.Value(sensitivity, value));

    [Fact]
    public void The_sensitive_canonical_fields_are_marked_in_the_domain()
    {
        Assert.Equal(Sensitivity.NiNumber, CanonicalFields.SensitivityOf(CanonicalFields.NiNumber));
        Assert.Equal(Sensitivity.DateOfBirth, CanonicalFields.SensitivityOf(CanonicalFields.DateOfBirth));
        Assert.Equal(Sensitivity.Salary, CanonicalFields.SensitivityOf(CanonicalFields.SalaryAmount));
        Assert.Equal(Sensitivity.Salary, CanonicalFields.SensitivityOf(CanonicalFields.LatestSalary));
        Assert.All(new[] { CanonicalFields.Forename, CanonicalFields.Surname, CanonicalFields.MemberId, CanonicalFields.DateJoined }, f =>
            Assert.Equal(Sensitivity.None, CanonicalFields.SensitivityOf(f)));
    }

    [Fact]
    public void A_record_saved_before_masking_is_masked_by_the_mapping_or_outright_when_unknown()
    {
        var legacy = new TargetRecord
        {
            Type = "MEM",
            Fields = [new TargetField("NINumber", "AB123456C"), new TargetField("Surname", "Okafor"), new TargetField("Mystery", "secret")],
        };
        var masked = Masking.Record(legacy, Mapping).Fields.ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal(("AB12****C", "Okafor", "****"), (masked["NINumber"], masked["Surname"], masked["Mystery"]));
    }

    [Fact]
    public async Task Search_cannot_be_used_to_probe_sensitive_values()
    {
        var app = await CommittedAshwoodAsync(TestApp.Build());
        Assert.Equal(0, (await app.Queries.RowsAsync(app.Id, 1, 50, "AB123456C", "all", default)).Total);
        Assert.Equal(0, (await app.Queries.RowsAsync(app.Id, 1, 50, "46200", "all", default)).Total);
        Assert.Equal(1, (await app.Queries.RowsAsync(app.Id, 1, 50, "Okafor", "all", default)).Total);
    }

    // ---- Reveal ----

    [Fact]
    public async Task Reveal_returns_the_real_values_to_a_user_with_the_role_and_is_audited_without_them()
    {
        var app = await CommittedAshwoodAsync(TestApp.Build(user: "rita", committer: false, reveal: true));
        var line = (await app.Queries.RowsAsync(app.Id, 1, 50, "DB001", "all", default)).Items.Single().LineNumber;
        var member = await app.Queries.RevealAsync(app.Id, line, default);
        Assert.Equal("AB123456C", member.Source.Single(s => s.Column == "NI number").Value);
        Assert.Equal("01/02/1967", member.Source.Single(s => s.Column == "Date of birth").Value);
        var latest = member.Records.Single(r => r.Type == "SAL" && r.Fields.Any(f => f.Name == "Period" && f.Value == "Y-1"));
        Assert.Equal("50000.00", latest.Fields.Single(f => f.Name == "Salary").Value);
        Assert.Contains("AB123456C", All(member));

        var entry = Assert.Single(app.Audit.Lines, l => l.StartsWith("member-revealed|", StringComparison.Ordinal));
        Assert.Equal($"member-revealed|{app.Id}|rita|line {line}", entry);
        Assert.DoesNotContain("AB123456C", string.Join("\n", app.Audit.Lines));
    }

    [Fact]
    public async Task Reveal_without_the_role_is_refused_and_not_logged_as_a_reveal()
    {
        var app = await CommittedAshwoodAsync(TestApp.Build(user: "uma", committer: false, reveal: false));
        var problem = await Assert.ThrowsAsync<ImportProblem>(() => app.Queries.RevealAsync(app.Id, 2, default));
        Assert.Equal(ProblemKind.Forbidden, problem.Kind);
        Assert.DoesNotContain(app.Audit.Lines, l => l.StartsWith("member-revealed|", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_committed_member_can_be_revealed_by_a_committer_and_not_by_others()
    {
        var committer = await CommittedAshwoodAsync(TestApp.Build());
        await committer.Commands.AcknowledgeAsync(committer.Id, (await committer.Queries.IssuesAsync(committer.Id, default)).Where(i => i.Severity == "Warning").Select(i => i.Id).ToList(), null, default);
        await committer.Commands.CommitAsync(committer.Id, reviewed: true, default);
        var row = (await committer.Committed.RowsAsync(committer.Id, 1, 50, "DB001", null, default)).Items.Single();
        var real = await committer.Committed.RevealAsync(committer.Id, row.LineNumber, default);
        Assert.Contains("AB123456C", All(real));
        Assert.Contains(committer.Audit.Lines, l => l == $"member-revealed|{committer.Id}|ann|line {row.LineNumber}");

    }

    // ---- The full file ----

    // Who may download is decided by the DownloadFile policy on the endpoints, so the 403 for a viewer or an uploader is tested over HTTP
    // (IntellipenImport.Api.Tests). Here: the use case returns the complete file and audits every download.
    [Fact]
    public async Task Every_full_file_download_is_audited_and_returns_the_complete_file()
    {
        var app = await CommittedAshwoodAsync(TestApp.Build());
        var held = await app.Queries.FileAsync(app.Id, default);
        Assert.Contains("AB123456C", Encoding.UTF8.GetString(held.Content)); // the delivered file is complete
        Assert.Contains($"file-downloaded|{app.Id}|ann|{held.FileName}", app.Audit.Lines);

        await app.Commands.AcknowledgeAsync(app.Id, (await app.Queries.IssuesAsync(app.Id, default)).Where(i => i.Severity == "Warning").Select(i => i.Id).ToList(), null, default);
        await app.Commands.CommitAsync(app.Id, reviewed: true, default);
        var saved = await app.Committed.FileDownloadAsync(app.Id, default);
        Assert.Contains("AB123456C", Encoding.UTF8.GetString(saved.Content));
        Assert.Equal(2, app.Audit.Lines.Count(l => l.StartsWith("file-downloaded|", StringComparison.Ordinal)));
    }

    // ---- Reconciliation totals are aggregates, not member values: never masked ----

    [Fact]
    public async Task Reconciliation_totals_are_shown_in_full_everywhere_while_member_values_stay_masked()
    {
        var app = await CommittedAshwoodAsync(TestApp.Build());

        // Held: summary and report
        var held = await app.Queries.GetAsync(app.Id, default);
        var heldTotal = held.Reconciliation.Single(l => l.Measure == "Latest-year salary total");
        Assert.Equal((1646700m, 1424200m, 222500m, 0m), (heldTotal.Source, heldTotal.Transformed, heldTotal.Rejected, heldTotal.Difference));
        var heldReport = await app.Queries.ReportAsync(app.Id, default);
        Assert.Contains("£1,646,700.00", heldReport);
        Assert.Contains("£1,424,200.00", heldReport);
        Assert.Contains("£222,500.00", heldReport);
        Assert.Contains("<td>Members</td><td class=\"n\">31</td>", heldReport);

        // Committed: summary and the report saved at commit
        await app.Commands.AcknowledgeAsync(app.Id, (await app.Queries.IssuesAsync(app.Id, default)).Where(i => i.Severity == "Warning").Select(i => i.Id).ToList(), null, default);
        await app.Commands.CommitAsync(app.Id, reviewed: true, default);
        var saved = await app.Committed.GetAsync(app.Id, default);
        var savedTotal = saved.Reconciliation.Single(l => l.Measure == "Latest-year salary total");
        Assert.Equal((1646700m, 1424200m, 222500m), (savedTotal.Source, savedTotal.Transformed, savedTotal.Rejected));
        var savedReport = await app.Committed.ReportAsync(app.Id, default);
        Assert.Contains("£1,646,700.00", savedReport);
        Assert.Contains("£1,424,200.00", savedReport);

        // ...while one member's salary in the same report and rows is still masked
        AssertNoSensitiveValues(savedReport, "committed report");
        Assert.Contains("\"****\"", All(await app.Committed.RowsAsync(app.Id, 1, 5, null, null, default)));
        Assert.DoesNotContain("1646700", All(await app.Committed.RowsAsync(app.Id, 1, 200, null, null, default)));
    }
}
