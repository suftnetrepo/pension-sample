using System.Text;
using IntellipenImport.Application.Processing;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Application.Imports;

/// <summary>Downloads shared by held and committed batches, so both give the same files.</summary>
internal static class Exports
{
    public static IReadOnlyList<IssueDto> Issues(IEnumerable<ImportIssue> issues) =>
        issues.OrderBy(i => i.LineNumber).ThenBy(i => i.Severity)
            .Select(i => new IssueDto(i.Severity.ToString(), i.LineNumber, i.Key, i.Field, i.Value, i.Rule, i.Action)).ToList();

    /// <summary>Exceptions as CSV for Excel: UTF-8 with BOM, and values that could run as formulas are neutralised.</summary>
    public static DownloadDto ExceptionsCsv(string id, IEnumerable<IssueDto> issues)
    {
        var sb = new StringBuilder("Severity,Line,Member,Field,Value,Rule,What to do\r\n");
        foreach (var i in issues)
            sb.Append(string.Join(',', new[] { i.Severity, i.LineNumber.ToString(), i.Key, i.Field, i.Value, i.Rule, i.Action }.Select(v => CsvText.EscapeForSpreadsheet(v))))
              .Append("\r\n");
        return new DownloadDto($"{id}_exceptions.csv", "text/csv", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())]);
    }

    public static DownloadDto Report(string id, string html) =>
        new($"{id}_reconciliation-report.html", "text/html", Encoding.UTF8.GetBytes(html));
}
