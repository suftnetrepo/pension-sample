using IntellipenImport.Domain.Processing;
using static IntellipenImport.Tests.TestFiles;

namespace IntellipenImport.Tests;

/// <summary>Automatic corrections: each safe fix is noted, nothing else is guessed, and the output never changes.</summary>
public class CorrectionTests
{
    private static ImportResult Line(string line) => Run(WithLines(line));

    private static Correction Only(ImportResult r) => Assert.Single(r.Corrections);

    [Fact]
    public void A_clean_line_has_no_corrections() => Assert.Empty(Line(GoodLine).Corrections);

    [Fact]
    public void NI_number_with_lower_case_and_spaces_is_corrected_and_masked()
    {
        var r = Line(GoodLine.Replace("AB123456C", "ab 12 34 56 c", StringComparison.Ordinal));
        var c = Only(r);
        Assert.Equal(("NI number", "DB001", 1 + 1), (c.Column, c.Key, c.LineNumber));
        Assert.Equal("ab12****c", c.Before);
        Assert.Equal("AB12****C", c.After);
        Assert.Contains("Spaces removed", c.Description);
        Assert.Contains("upper-cased", c.Description);
        Assert.DoesNotContain("3456", c.Before + c.After);
        Assert.Equal(1, r.Accepted);
    }

    [Fact]
    public void NI_number_only_upper_cased()
    {
        var c = Only(Line(GoodLine.Replace("AB123456C", "ab123456c", StringComparison.Ordinal)));
        Assert.Equal("Letters upper-cased", c.Description);
    }

    [Fact]
    public void A_pound_sign_and_thousands_separator_are_removed_from_a_salary()
    {
        var c = Only(Line(GoodLine.Replace("50000", "\"£50,000\"", StringComparison.Ordinal)));
        Assert.Equal(("Salary Y-1", "****", "****"), (c.Column, c.Before, c.After)); // salaries are masked; the description says what was done
        Assert.Contains("£ sign removed", c.Description);
        Assert.Contains("Thousands separator removed", c.Description);
    }

    [Fact]
    public void A_code_matched_with_different_case_is_corrected()
    {
        var c = Only(Line(GoodLine.Replace("Deferred", "deferred", StringComparison.Ordinal)));
        Assert.Equal(("Status", "deferred", "Deferred"), (c.Column, c.Before, c.After));
        Assert.Contains("upper/lower case", c.Description);
    }

    [Fact]
    public void Leading_and_trailing_spaces_are_corrected()
    {
        var c = Only(Line(GoodLine.Replace("David Okafor", "  David Okafor ", StringComparison.Ordinal)));
        Assert.Equal(("Name", "  David Okafor ", "David Okafor"), (c.Column, c.Before, c.After));
        Assert.Equal("Leading or trailing spaces removed", c.Description);
    }

    [Fact]
    public void A_date_with_single_digit_day_or_month_is_read_as_the_same_date()
    {
        var r = Line(GoodLine.Replace("01/02/1967", "1/2/1967", StringComparison.Ordinal));
        var c = Only(r);
        Assert.Equal(("Date of birth", "**/**/1967", "**/**/1967"), (c.Column, c.Before, c.After));
        Assert.Contains("one digit", c.Description);
        Assert.Equal(1, r.Accepted);
        Assert.Equal(Run(WithLines(GoodLine)).OutputLines.Skip(1), r.OutputLines.Skip(1)); // same 1 February 1967, same output
    }

    [Theory]
    [InlineData("13/25/1990")]   // not a date in this order: day and month are never swapped
    [InlineData("1/2/67")]       // two-digit years are never guessed
    [InlineData("31/2/1990")]    // single digits, but 31 February does not exist
    public void Dates_that_cannot_be_read_safely_stay_errors_with_no_correction(string value)
    {
        var r = Line(GoodLine.Replace("01/02/1967", value, StringComparison.Ordinal));
        Assert.Equal(0, r.Accepted);
        Assert.Contains(r.Issues, i => i.Field == "Date of birth" && i.Severity == Severity.Error);
        Assert.Empty(r.Corrections);
    }

    [Fact]
    public void A_blank_is_never_filled_in()
    {
        var r = Line(GoodLine.Replace("AB123456C", string.Empty, StringComparison.Ordinal));
        Assert.Equal(0, r.Accepted);
        Assert.Empty(r.Corrections);
    }

    [Fact]
    public void A_corrected_value_that_is_still_rejected_is_not_reported_as_corrected()
    {
        var r = Line(GoodLine.Replace("AB123456C", "zz 99", StringComparison.Ordinal)); // upper-cased, but still not an NI number
        Assert.Equal(0, r.Accepted);
        Assert.Empty(r.Corrections);
    }

    [Fact]
    public void Corrections_never_change_the_result_or_the_output()
    {
        var messy = GoodLine.Replace("AB123456C", " ab 123456 c ", StringComparison.Ordinal)
            .Replace("Deferred", "deferred", StringComparison.Ordinal).Replace("50000", "\"£50,000\"", StringComparison.Ordinal)
            .Replace("01/09/1995", "1/9/1995", StringComparison.Ordinal);
        var clean = Run(WithLines(GoodLine));
        var fixedUp = Run(WithLines(messy));
        Assert.Equal(clean.OutputLines.Skip(1), fixedUp.OutputLines.Skip(1)); // line 1 carries the run name/date only
        Assert.Equal((clean.Accepted, clean.Rejected, clean.WarningCount), (fixedUp.Accepted, fixedUp.Rejected, fixedUp.WarningCount));
        Assert.Equal(4, fixedUp.Corrections.Count);
    }

    [Fact]
    public void The_samples_still_give_the_agreed_counts_and_list_their_corrections()
    {
        var ashwood = Run(Sample("ashwood-payroll_v1_sample.csv"));
        Assert.Equal((31, 26, 5, 10), (ashwood.LinesRead, ashwood.Accepted, ashwood.Rejected, ashwood.WarningCount));
        Assert.Contains(ashwood.Corrections, c => c.Key == "DB006" && c.Column == "Salary Y-1" && c.Description.Contains("£ sign removed"));

        var legacy = Run(Sample("legacy-admin_v1_sample.csv"), Legacy);
        Assert.Equal((30, 25, 5, 10), (legacy.LinesRead, legacy.Accepted, legacy.Rejected, legacy.WarningCount));
    }

    [Fact]
    public void Issue_ids_are_the_index_in_the_issue_list_and_every_issue_names_who_acts()
    {
        var r = Run(Sample("ashwood-payroll_v1_sample.csv"));
        Assert.Equal(Enumerable.Range(0, r.Issues.Count), r.Issues.Select(i => i.Id));
        Assert.All(r.Issues, i => Assert.StartsWith("Ask", i.Action));
        Assert.All(Run(Sample("legacy-admin_v1_sample.csv"), Legacy).Issues, i => Assert.StartsWith("Ask", i.Action));
        Assert.All(r.Issues.Where(i => i.Rule == "Must not be blank" && i.Field == "NI number"), i => Assert.Equal(IssueOwner.Employer, i.Owner));
    }
}
