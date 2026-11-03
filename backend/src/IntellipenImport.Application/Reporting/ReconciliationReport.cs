using System.Globalization;
using System.Net;
using System.Text;
using IntellipenImport.Application.Processing;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Application.Reporting;

/// <summary>What the report needs to know about the batch, beyond the processing result.</summary>
public sealed record ReportHeader(
    string BatchId,
    string Status,
    string FileName,
    string FileSha256,
    string SourceName,
    string SchemaVersion,
    string MappingName,
    string MappingVersion,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc,
    string? CommittedBy,
    DateTimeOffset? CommittedAtUtc,
    string? OutputFileSha256);

/// <summary>Builds the reconciliation report as one self-contained HTML page (shown on screen, saved, and emailed).</summary>
public static class ReconciliationReport
{
    private static readonly CultureInfo Uk = CultureInfo.InvariantCulture; // fixed formats below give UK-style dd/MM/yyyy and 1,234.56

    public static string Build(ReportHeader h, ImportResult r)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        string When(DateTimeOffset? t) => t is null ? "-" : t.Value.ToUniversalTime().ToString("dd/MM/yyyy HH:mm 'UTC'", Uk);
        string Num(ReconciliationLine l, decimal v) => l.IsMoney ? "£" + Values.Money(v) : v.ToString("N0", Uk);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Reconciliation report ").Append(E(h.BatchId)).Append("</title>");
        sb.Append("<style>body{font-family:Arial,Helvetica,sans-serif;color:#17202B;margin:24px;font-size:14px}h1{font-size:20px}h2{font-size:16px;margin-top:28px}")
          .Append("table{border-collapse:collapse;margin-top:8px}th,td{border:1px solid #D9DEE5;padding:6px 10px;text-align:left}th{background:#F3F5F8}")
          .Append("td.n{text-align:right}.ok{color:#157F3C;font-weight:bold}.bad{color:#B42318;font-weight:bold}.muted{color:#4A5463}</style></head><body>");

        sb.Append("<h1>Reconciliation report: ").Append(E(h.BatchId)).Append("</h1>");
        sb.Append("<p class=\"").Append(r.Balanced ? "ok" : "bad").Append("\">")
          .Append(r.Balanced ? "Balanced: every control measure satisfies source = transformed + rejected." : "NOT BALANCED: do not load this file. Contact the support team.")
          .Append("</p>");

        sb.Append("<table><tbody>");
        void Row(string k, string v) => sb.Append("<tr><th>").Append(E(k)).Append("</th><td>").Append(E(v)).Append("</td></tr>");
        Row("Status", h.Status);
        Row("Source file", h.FileName);
        Row("Source file SHA-256", h.FileSha256);
        Row("Source and schema", $"{h.SourceName} {h.SchemaVersion}");
        Row("IntelliPen mapping", $"{h.MappingName} v{h.MappingVersion}");
        Row("Uploaded", $"{h.UploadedBy}, {When(h.UploadedAtUtc)}");
        Row("Committed", h.CommittedBy is null ? "Not committed" : $"{h.CommittedBy}, {When(h.CommittedAtUtc)}");
        Row("IntelliPen file", r.OutputFileName);
        if (h.OutputFileSha256 is not null) Row("IntelliPen file SHA-256", h.OutputFileSha256);
        sb.Append("</tbody></table>");

        sb.Append("<h2>Counts</h2><table><tbody>");
        Row("Lines read", r.LinesRead.ToString("N0", Uk));
        Row("Accepted", r.Accepted.ToString("N0", Uk));
        Row("Rejected (errors)", r.Rejected.ToString("N0", Uk));
        Row("Warnings", r.WarningCount.ToString("N0", Uk));
        foreach (var c in r.RecordChecks) Row($"{c.Type} records", $"{c.Actual:N0} written, {c.Expected:N0} expected {(c.Ok ? "(OK)" : "(MISMATCH)")}");
        Row("Trailer record count", r.TrailerOk ? "Agrees with the file" : "DOES NOT AGREE");
        sb.Append("</tbody></table>");

        sb.Append("<h2>Control measures</h2><table><thead><tr><th>Measure</th><th>Source</th><th>Transformed</th><th>Rejected</th><th>Difference</th></tr></thead><tbody>");
        foreach (var l in r.Reconciliation)
        {
            sb.Append("<tr><td>").Append(E(l.Measure)).Append("</td><td class=\"n\">").Append(Num(l, l.Source))
              .Append("</td><td class=\"n\">").Append(Num(l, l.Transformed)).Append("</td><td class=\"n\">").Append(Num(l, l.Rejected))
              .Append("</td><td class=\"n ").Append(l.Difference == 0 ? "ok" : "bad").Append("\">").Append(Num(l, l.Difference)).Append("</td></tr>");
        }
        sb.Append("</tbody></table>");

        void Issues(string title, Severity severity)
        {
            var list = r.Issues.Where(i => i.Severity == severity).OrderBy(i => i.LineNumber).ToList();
            sb.Append("<h2>").Append(E(title)).Append(" (").Append(list.Count).Append(")</h2>");
            if (list.Count == 0) { sb.Append("<p class=\"muted\">None.</p>"); return; }
            sb.Append("<table><thead><tr><th>Line</th><th>Member</th><th>Field</th><th>Value</th><th>Rule</th><th>What to do</th></tr></thead><tbody>");
            foreach (var i in list)
                sb.Append("<tr><td class=\"n\">").Append(i.LineNumber).Append("</td><td>").Append(E(i.Key)).Append("</td><td>").Append(E(i.Field))
                  .Append("</td><td>").Append(E(i.Value)).Append("</td><td>").Append(E(i.Rule)).Append("</td><td>").Append(E(i.Action)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        Issues("Rejected lines: errors", Severity.Error);
        Issues("Warnings: loaded, check these", Severity.Warning);

        sb.Append("<p class=\"muted\">NI numbers are masked in this report.</p></body></html>");
        return sb.ToString();
    }
}
