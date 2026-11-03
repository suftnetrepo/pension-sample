using System.Text.RegularExpressions;
using IntellipenImport.Domain.Canonical;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Application.Processing;

/// <summary>
/// Step 2 of a line: the scheme's validation rules, written once against the canonical member, so they apply
/// identically to every source and schema version. Errors reject the line; warnings load it but flag it for a check.
/// </summary>
internal static partial class MemberRules
{
    public const int MaxMemberIdLength = 10;
    public const decimal SalaryFallWarningPercent = 15m;

    [GeneratedRegex("^[A-CEGHJ-PR-TW-Z]{2}[0-9]{6}[A-D]$", RegexOptions.CultureInvariant)]
    private static partial Regex NiPattern();

    /// <param name="alreadyFailed">Source columns that already have a reading error on this line (not reported twice).</param>
    public static void Check(CanonicalMember m, CsvRow row, string key, CanonicalReader r, HashSet<string> alreadyFailed, List<ImportIssue> issues)
    {
        void Error(string field, string rule, string action, IssueOwner owner)
        {
            var column = r.ColumnOf(field);
            if (!alreadyFailed.Contains(column)) issues.Add(r.Issue(row, key, column, rule, action, owner));
        }
        void Warning(string column, string rule, string action, IssueOwner owner) => issues.Add(r.Issue(row, key, column, rule, action, owner, Severity.Warning));

        // Required fields
        if (m.MemberId.Length == 0) Error(CanonicalFields.MemberId, "Must not be blank", "Ask payroll to add the member ID and send a corrected file", IssueOwner.Payroll);
        if (m.Surname.Length == 0) Error(CanonicalFields.Surname, "Must not be blank", "Ask the employer for the member's name and send a corrected file", IssueOwner.Employer);
        if (m.Sex.Length == 0) Error(CanonicalFields.Sex, "Must not be blank", "Ask the employer to confirm the member's sex and send a corrected file", IssueOwner.Employer);
        if (m.DateOfBirth is null) Error(CanonicalFields.DateOfBirth, "Must not be blank", "Ask the employer to confirm the date of birth and send a corrected file", IssueOwner.Employer);
        if (m.NiNumber.Length == 0) Error(CanonicalFields.NiNumber, "Must not be blank", "Ask the employer's HR team for the NI number and send a corrected file", IssueOwner.Employer);
        if (m.DateJoined is null) Error(CanonicalFields.DateJoined, "Must not be blank", "Ask the employer to confirm the joining date and send a corrected file", IssueOwner.Employer);
        if (m.Status.Length == 0) Error(CanonicalFields.Status, "Must not be blank", "Ask payroll to add the member's status and send a corrected file", IssueOwner.Payroll);

        // Formats
        if (m.MemberId.Length > MaxMemberIdLength)
            Error(CanonicalFields.MemberId, $"Must be no longer than {MaxMemberIdLength} characters", "Ask payroll to use the scheme's member ID and send a corrected file", IssueOwner.Payroll);
        if (m.NiNumber.Length > 0 && !NiPattern().IsMatch(m.NiNumber))
            Error(CanonicalFields.NiNumber, "Is not in the expected format", "Ask the employer's HR team to confirm the NI number and send a corrected file", IssueOwner.Employer);

        // Dates that must make sense together
        if (m.DateOfBirth is { } dob && m.DateJoined is { } joined && joined <= dob)
            Error(CanonicalFields.DateJoined, $"Must be after {r.ColumnOf(CanonicalFields.DateOfBirth)} ({r.Shown(row, r.ColumnOf(CanonicalFields.DateOfBirth))})",
                "Ask the employer to confirm the date of birth and joining date and send a corrected file", IssueOwner.Employer);
        if (m.DateJoined is { } j && m.DateLeft is { } left && left <= j)
            Error(CanonicalFields.DateLeft, $"Must be after {r.ColumnOf(CanonicalFields.DateJoined)} ({r.Shown(row, r.ColumnOf(CanonicalFields.DateJoined))})",
                "Ask the employer to confirm both dates and send a corrected file", IssueOwner.Employer);
        if (m.Status == "Deferred" && m.DateLeft is null && !alreadyFailed.Contains(r.ColumnOf(CanonicalFields.DateLeft)))
            Error(CanonicalFields.DateLeft, $"Must not be blank when {r.ColumnOf(CanonicalFields.Status)} is Deferred", "Ask the employer to confirm the leaving date and send a corrected file", IssueOwner.Employer);
        if (m.Status == "Active" && m.DateLeft is not null)
            Error(CanonicalFields.DateLeft, $"Must be blank when {r.ColumnOf(CanonicalFields.Status)} is Active", "Ask the employer whether the member has left; send a corrected file with the status or leaving date fixed", IssueOwner.Employer);

        // Numbers
        if (m.PartTimeFraction is { } pt && (pt <= 0 || pt > 1))
            Error(CanonicalFields.PartTimeFraction, "Must be more than 0 and no more than 1", "Ask payroll to send the part-time fraction as a number such as 0.6", IssueOwner.Payroll);
        var latest = m.Salaries.Count == 0 ? null : m.Salaries[^1];
        if (latest is not null && latest.Amount is null)
        {
            var column = r.LatestSalaryColumn;
            if (!alreadyFailed.Contains(column)) issues.Add(r.Issue(row, key, column, "Must not be blank", "Ask payroll for the latest pensionable salary and send a corrected file", IssueOwner.Payroll));
        }
        foreach (var (s, i) in m.Salaries.Select((s, i) => (s, i)).Where(x => x.s.Amount < 0))
            issues.Add(r.Issue(row, key, r.SalaryColumn(i), "Must be at least 0", "Ask payroll to correct the salary and send a corrected file", IssueOwner.Payroll));

        // Warnings: loaded, but someone should check
        if (m.PartTimeFraction is { } p && p > 0 && p < 1)
            Warning(r.ColumnOf(CanonicalFields.PartTimeFraction), "Is not 1", "Ask the employer to confirm the part-time period dates, then tick this as checked", IssueOwner.Employer);
        if (m.LatestSalary is { } now && m.PreviousSalary is { } before && before > 0 && (before - now) / before * 100m > SalaryFallWarningPercent)
            Warning(r.LatestSalaryColumn, $"Falls more than {SalaryFallWarningPercent:0}% on {r.SalaryColumn(m.Salaries.Count - 2)}", "Ask the employer to confirm the salary is right, then tick this as checked", IssueOwner.Employer);
    }
}
