using IntellipenImport.Application.Reporting;
using IntellipenImport.Domain.Processing;
using static IntellipenImport.Tests.TestFiles;

namespace IntellipenImport.Tests;

/// <summary>
/// Golden tests: each source's sample through its schema and the IntelliPen mapping must give exactly these counts,
/// these rejections and exactly the expected IntelliPen file. When the real IntelliPen mapping arrives, regenerate the
/// files in Expected/, check them line by line against the specification, and commit them.
/// </summary>
public class SampleFileTests
{
    private static readonly ImportResult Ashwood = Run(Sample("ashwood-payroll_v1_sample.csv"), TestFiles.Ashwood);
    private static readonly ImportResult Legacy = Run(Sample("legacy-admin_v1_sample.csv"), TestFiles.Legacy);

    [Fact]
    public void Ashwood_counts_are_as_expected()
    {
        Assert.Equal((31, 26, 5, 10), (Ashwood.LinesRead, Ashwood.Accepted, Ashwood.Rejected, Ashwood.WarningCount));
        Assert.Equal([(15, "DB014"), (20, "DB019"), (23, "DB022"), (28, "DB027"), (32, "DB019")],
            Ashwood.Members.Where(m => !m.Accepted).Select(m => (m.LineNumber, m.Key)));
    }

    [Fact]
    public void Ashwood_output_matches_the_expected_file_exactly() =>
        Assert.Equal(Lines(Expected("ashwood-payroll_v1.intellipen.csv")), Ashwood.OutputLines);

    [Fact]
    public void Legacy_counts_are_as_expected()
    {
        Assert.Equal((30, 25, 5), (Legacy.LinesRead, Legacy.Accepted, Legacy.Rejected));
        Assert.Equal(["DB005", "DB014", "DB022", "DB024", "DB027"], Legacy.Members.Where(m => !m.Accepted).Select(m => m.Key));
        Assert.Contains(Legacy.Issues, i => i.Field == "Gender" && i.Rule == "Must be one of: Male, Female");
        Assert.Contains(Legacy.Issues, i => i.Field == "MemberStatus" && i.Rule == "Must be one of: A, D");
    }

    [Fact]
    public void Legacy_output_matches_the_expected_file_exactly() =>
        Assert.Equal(Lines(Expected("legacy-admin_v1.intellipen.csv")), Legacy.OutputLines);

    /// <summary>The point of the canonical model: two different source layouts, the same member, the same IntelliPen record.</summary>
    [Fact]
    public void The_same_member_from_two_sources_gives_the_same_IntelliPen_records()
    {
        foreach (var id in new[] { "DB001", "DB002", "DB006", "DB010" })
        {
            var a = Ashwood.Members.Single(m => m.Key == id).Records;
            var b = Legacy.Members.Single(m => m.Key == id).Records;
            Assert.Equal(a[0].Fields, b[0].Fields);                                              // MEM
            Assert.Equal(a.Skip(1).Select(r => r.Fields[3]), b.Skip(1).Select(r => r.Fields[3])); // SAL amounts (periods differ by source)
        }
    }

    [Fact]
    public void Both_samples_reconcile()
    {
        foreach (var r in new[] { Ashwood, Legacy })
        {
            Assert.True(r.Balanced);
            Assert.All(r.Reconciliation, l => Assert.Equal(0m, l.Difference));
            Assert.All(r.RecordChecks, c => Assert.True(c.Ok));
        }
    }

    [Fact]
    public void Report_names_the_source_and_masks_NI_numbers()
    {
        var html = ReconciliationReport.Build(new ReportHeader("IMP-20261102-TEST01", "ReadyForReview", "legacy-admin_v1_sample.csv", "abc",
            "Legacy admin export", "v1", "IntelliPen member import", "0.1", "tester", new DateTimeOffset(2026, 11, 2, 21, 55, 0, TimeSpan.Zero),
            null, null, null), Legacy);
        Assert.Contains("Legacy admin export v1", html);
        Assert.Contains("02/11/2026 21:55 UTC", html);
        Assert.DoesNotContain("AB123456C", html);
        Assert.Contains("Rejected lines: errors (5)", html);
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
}
