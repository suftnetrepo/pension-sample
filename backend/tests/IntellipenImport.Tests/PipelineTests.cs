using IntellipenImport.Domain.Processing;
using static IntellipenImport.Tests.TestFiles;

namespace IntellipenImport.Tests;

/// <summary>The processing pipeline against Ashwood payroll v1: file checks, reading, rules, writing.</summary>
public class PipelineTests
{
    // ---- The file must match the agreed schema ----

    [Fact]
    public void Empty_file_fails() => Assert.Equal("The file is empty.", Run("").FailureReason);

    [Fact]
    public void Headings_without_lines_fail() => Assert.True(Run(WithLines()).FileFailed);

    [Fact]
    public void A_missing_column_is_named()
    {
        var r = Run(WithLines(GoodLine).Replace("NI number", "NINO", StringComparison.Ordinal));
        Assert.True(r.FileFailed);
        Assert.Contains("doesn't match the agreed layout for Ashwood payroll v1", r.FailureReason);
        Assert.Equal(["NI number"], r.Mismatch!.Missing);
        Assert.Equal(["NINO"], r.Mismatch.Unexpected);
    }

    [Fact]
    public void An_extra_column_is_a_mismatch_because_the_layout_is_agreed()
    {
        var csv = WithLines(GoodLine + ",x").Replace(",Married\r\n", ",Married,Email\r\n", StringComparison.Ordinal);
        Assert.Equal(["Email"], Run(csv).Mismatch!.Unexpected);
    }

    [Fact]
    public void A_duplicated_heading_is_a_mismatch()
    {
        var csv = WithLines(GoodLine + ",x").Replace(",Married\r\n", ",Married,Name\r\n", StringComparison.Ordinal);
        Assert.Equal(["Name"], Run(csv).Mismatch!.Duplicated);
    }

    [Fact]
    public void A_file_with_another_delimiter_gets_a_hint_and_the_right_schema_is_recognised()
    {
        var legacyFile = Sample("legacy-admin_v1_sample.csv");
        var r = Run(legacyFile, TestFiles.Ashwood);
        Assert.Contains("appears to use pipes (|)", r.Mismatch!.Hint);
        Assert.True(Application.Processing.ImportPipeline.HeadingsMatch(legacyFile, TestFiles.Legacy));
        Assert.False(Application.Processing.ImportPipeline.HeadingsMatch(legacyFile, TestFiles.Ashwood));
    }

    [Fact]
    public void Column_order_does_not_matter()
    {
        var csv = "Married,Member ID,Name,Sex,Date of birth,NI number,Joined scheme,Left service,Status,Salary Y-3,Salary Y-2,Salary Y-1,Part-time fraction\r\n"
                  + "Y,DB001,David Okafor,M,01/02/1967,AB123456C,01/09/1995,31/08/2016,Deferred,46200,47800,50000,1\r\n";
        Assert.Equal(1, Run(csv).Accepted);
    }

    [Fact]
    public void Too_many_lines_fails()
    {
        var r = Run(WithLines(GoodLine, GoodLine.Replace("DB001", "DB002", StringComparison.Ordinal)), context: Context with { MaxLines = 1 });
        Assert.Contains("limit", r.FailureReason);
    }

    // ---- Reading and writing ----

    [Fact]
    public void A_good_line_becomes_one_MEM_and_three_SAL_records()
    {
        var r = Run(WithLines(GoodLine));
        Assert.True(r.Balanced);
        var m = Assert.Single(r.Members);
        Assert.True(m.Accepted);
        Assert.Equal(["MEM", "SAL", "SAL", "SAL"], m.Records.Select(x => x.Type));
        var mem = m.Records[0].Fields.ToDictionary(f => f.Name, f => f.Value);
        Assert.Equal("David", mem["Forename"]);
        Assert.Equal("Okafor", mem["Surname"]);
        Assert.Equal("1967-02-01", mem["DOB"]);
        Assert.Equal("DEF", mem["StatusCode"]);
        Assert.Equal("M", mem["MaritalCode"]);
        Assert.Equal("1.00", mem["PartTimeFraction"]);
        Assert.Equal("50000.00", m.Records[3].Fields.Single(f => f.Name == "Salary").Value);
        Assert.Equal("Y-1", m.Records[3].Fields.Single(f => f.Name == "Period").Value);
    }

    [Theory]
    [InlineData("£52,000", "52000.00")]
    [InlineData("52,000.5", "52000.50")]
    [InlineData("52000", "52000.00")]
    public void Salaries_accept_pounds_and_thousands_separators(string source, string expected)
    {
        var r = Run(WithLines(GoodLine.Replace("50000", $"\"{source}\"", StringComparison.Ordinal)));
        Assert.Equal(expected, r.Members[0].Records[3].Fields.Single(f => f.Name == "Salary").Value);
    }

    [Fact]
    public void A_blank_part_time_fraction_takes_the_schema_default()
    {
        var r = Run(WithLines(GoodLine.Replace(",1,Y", ",,Y", StringComparison.Ordinal)));
        Assert.Equal("1.00", r.Members[0].Records[0].Fields.Single(f => f.Name == "PartTimeFraction").Value);
    }

    // ---- Rules: errors reject the line ----

    [Theory]
    [InlineData("31/02/1967", "Date of birth", "real date")]
    [InlineData("NI:", "NI number", "Must not be blank")]
    [InlineData("NI:QQ12345", "NI number", "expected format")]
    [InlineData("STATUS:Retired", "Status", "Must be one of: Active, Deferred")]
    [InlineData("SEX:X", "Sex", "Must be one of: M, F")]
    public void Errors_reject_the_line(string variant, string field, string ruleText)
    {
        var line = GoodLine;
        if (variant.StartsWith("31/02", StringComparison.Ordinal)) line = line.Replace("01/02/1967", variant, StringComparison.Ordinal);
        if (variant.StartsWith("NI:", StringComparison.Ordinal)) line = line.Replace("AB123456C", variant[3..], StringComparison.Ordinal);
        if (variant.StartsWith("STATUS:", StringComparison.Ordinal)) line = line.Replace("Deferred", variant[7..], StringComparison.Ordinal);
        if (variant.StartsWith("SEX:", StringComparison.Ordinal)) line = line.Replace(",M,", "," + variant[4..] + ",", StringComparison.Ordinal);

        var r = Run(WithLines(line));
        Assert.Equal(0, r.Accepted);
        var issue = Assert.Single(r.Issues, i => i.Severity == Severity.Error);
        Assert.Equal(field, issue.Field);
        Assert.Contains(ruleText, issue.Rule);
        Assert.True(r.Balanced);
    }

    [Fact]
    public void NI_numbers_are_masked_in_issues()
    {
        var r = Run(WithLines(GoodLine.Replace("AB123456C", "ZZ123456Q", StringComparison.Ordinal)));
        Assert.Equal("ZZ12****Q", r.Issues.Single().Value);
    }

    [Fact]
    public void Status_and_leaving_date_must_agree()
    {
        var active = GoodLine.Replace("Deferred", "Active", StringComparison.Ordinal);
        var deferred = GoodLine.Replace("DB001", "DB002", StringComparison.Ordinal).Replace("31/08/2016", "", StringComparison.Ordinal);
        var r = Run(WithLines(active, deferred));
        Assert.Equal(0, r.Accepted);
        Assert.Contains(r.Issues, i => i.Rule.StartsWith("Must be blank when Status is Active", StringComparison.Ordinal));
        Assert.Contains(r.Issues, i => i.Rule.StartsWith("Must not be blank when Status is Deferred", StringComparison.Ordinal));
    }

    [Fact]
    public void Leaving_before_joining_is_an_error_naming_the_source_columns()
    {
        var r = Run(WithLines(GoodLine.Replace("31/08/2016", "01/03/1994", StringComparison.Ordinal)));
        Assert.Equal("Must be after Joined scheme (01/09/1995)", r.Issues.Single().Rule);
    }

    [Fact]
    public void Every_copy_of_a_duplicate_member_is_rejected()
    {
        var r = Run(WithLines(GoodLine, GoodLine));
        Assert.Equal(0, r.Accepted);
        Assert.Equal(2, r.Issues.Count(i => i.Rule.StartsWith("Must appear only once", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_line_with_the_wrong_number_of_values_is_rejected()
    {
        var r = Run(WithLines(GoodLine + ",extra"));
        Assert.Contains("Must have 13 values", r.Issues.Single().Rule);
    }

    // ---- Rules: warnings load the line ----

    [Fact]
    public void Warnings_load_the_line()
    {
        var r = Run(WithLines(GoodLine.Replace("46200,47800,50000,1", "46200,47800,40000,0.6", StringComparison.Ordinal)));
        Assert.Equal(1, r.Accepted);
        Assert.Equal(2, r.WarningCount);
        Assert.Contains(r.Issues, i => i.Rule == "Falls more than 15% on Salary Y-2");
    }
}
