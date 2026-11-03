using System.Text;
using IntellipenImport.Application.Processing;
using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Application.Imports;

/// <summary>Downloads shared by held and committed batches, so both give the same files.</summary>
internal static class Exports
{
    public static IReadOnlyList<IssueDto> Issues(IEnumerable<ImportIssue> issues, IEnumerable<Acknowledgement> acknowledgements)
    {
        var checkedBy = acknowledgements.ToDictionary(a => a.IssueId);
        return issues.OrderBy(i => i.LineNumber).ThenBy(i => i.Severity)
            .Select(i => new IssueDto(i.Id, i.Severity.ToString(), i.LineNumber, i.Key, i.Field, i.Value, i.Rule, i.Action, OwnerText(i.Owner),
                checkedBy.TryGetValue(i.Id, out var a) ? new AcknowledgementDto(a.By, a.AtUtc, a.Note) : null)).ToList();
    }

    public static IReadOnlyList<CorrectionDto> Corrections(IEnumerable<Correction> corrections) =>
        corrections.OrderBy(c => c.LineNumber)
            .Select(c => new CorrectionDto(c.LineNumber, c.Key, c.Column, c.Before, c.After, c.Description)).ToList();

    public static string OwnerText(IssueOwner owner) => owner == IssueOwner.SchemeAdministrator ? "Scheme administrator" : owner.ToString();

    /// <summary>Exceptions as CSV for Excel: UTF-8 with BOM, and values that could run as formulas are neutralised.</summary>
    public static DownloadDto ExceptionsCsv(string id, IEnumerable<IssueDto> issues) =>
        Csv($"{id}_exceptions.csv", "Severity,Line,Member,Field,Value,Rule,What to do,Who acts,Checked by,Checked at (UTC),Check note",
            issues.Select(i => new[] { i.Severity, i.LineNumber.ToString(), i.Key, i.Field, i.Value, i.Rule, i.Action, i.Owner,
                i.Checked?.By ?? string.Empty, i.Checked?.AtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty, i.Checked?.Note ?? string.Empty }));

    /// <summary>The automatic corrections as CSV, with the same rules as the exceptions file.</summary>
    public static DownloadDto CorrectionsCsv(string id, IEnumerable<CorrectionDto> corrections) =>
        Csv($"{id}_corrections.csv", "Line,Member,Column,Before,After,What was done",
            corrections.Select(c => new[] { c.LineNumber.ToString(), c.Key, c.Column, c.Before, c.After, c.Description }));

    private static DownloadDto Csv(string fileName, string header, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder(header).Append("\r\n");
        foreach (var row in rows) sb.Append(string.Join(',', row.Select(v => CsvText.EscapeForSpreadsheet(v)))).Append("\r\n");
        return new DownloadDto(fileName, "text/csv", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())]);
    }

    public static DownloadDto Report(string id, string html) =>
        new($"{id}_reconciliation-report.html", "text/html", Encoding.UTF8.GetBytes(html));
}
