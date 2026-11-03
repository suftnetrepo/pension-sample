using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Processing;
using IntellipenImport.Application.Reporting;
using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Application.Imports;

/// <summary>Everything the review screens read. Nothing here changes a batch (except marking an overdue one expired).</summary>
public sealed class ImportQueries(ISourceSchemaCatalog schemas, BatchAccess access, ICurrentUser user, IAuditLog audit)
{
    public IReadOnlyList<SourceDto> Sources() =>
        schemas.All
            .GroupBy(s => s.Source, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SourceDto(g.Key, g.First().SourceName, g.First().Scheme,
                g.OrderBy(s => s.Version, StringComparer.OrdinalIgnoreCase)
                 .Select(s => new SourceVersionDto(s.Version, s.Description, s.Agreement, s.File.Delimiter, s.File.DateFormat, s.ExpectedColumns))
                 .ToList()))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<BatchSummaryDto> GetAsync(string id, CancellationToken ct)
    {
        var record = await access.RecordAsync(id, ct);
        return access.Summary(record, await access.HeldOrExpireAsync(record, ct));
    }

    /// <summary>Members as transformed, always masked. Search looks at the member ID and non-sensitive columns only, so it can't be used to probe NI numbers, birth dates or salaries.</summary>
    public async Task<PagedDto<MemberRowDto>> RowsAsync(string id, int page, int pageSize, string? search, string? show, CancellationToken ct)
    {
        var (_, batch) = await access.RequireHeldAsync(id, ct);
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var headers = batch.Result.SourceHeaders;
        var schema = schemas.Find(batch.Source, batch.SchemaVersion);
        var sensitive = schema is null ? null : Masking.Columns(schema);

        IEnumerable<MemberResult> members = (show ?? "accepted").ToLowerInvariant() switch
        {
            "all" => batch.Result.Members,
            "rejected" => batch.Result.Members.Where(m => !m.Accepted),
            "warnings" => batch.Result.Members.Where(m => m.HasWarnings),
            _ => batch.Result.Members.Where(m => m.Accepted),
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            members = members.Where(m => m.Key.Contains(s, StringComparison.OrdinalIgnoreCase)
                || (sensitive is not null && m.SourceValues.Where((_, i) => i < headers.Count && !sensitive.ContainsKey(headers[i].Trim()))
                    .Any(v => v.Contains(s, StringComparison.OrdinalIgnoreCase))));
        }
        var list = members.ToList();
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).Select(m => Row(m, headers, schema, reveal: false)).ToList();
        return new PagedDto<MemberRowDto>(items, page, pageSize, list.Count);
    }

    /// <summary>One member's real values. Needs Import.RevealSensitive (or Import.Committer); every reveal is audited with the batch, line and user, never the values.</summary>
    public async Task<MemberRowDto> RevealAsync(string id, int lineNumber, CancellationToken ct)
    {
        if (!user.CanReveal) throw new ImportProblem(ProblemKind.Forbidden, "Not allowed", "Showing the full values needs the Import.RevealSensitive role.");
        var (_, batch) = await access.RequireHeldAsync(id, ct);
        var member = batch.Result.Members.FirstOrDefault(m => m.LineNumber == lineNumber)
            ?? throw new ImportProblem(ProblemKind.NotFound, "Line not found", $"Batch {id} has no line {lineNumber}.");
        audit.Record("member-revealed", id, user.UserId, $"line {lineNumber}");
        return Row(member, batch.Result.SourceHeaders, schemas.Find(batch.Source, batch.SchemaVersion), reveal: true);
    }

    private static MemberRowDto Row(MemberResult m, IReadOnlyList<string> headers, Domain.Schemas.SourceSchema? schema, bool reveal) => new(
        m.LineNumber, m.Key, m.Accepted, m.HasWarnings,
        Masking.Source(headers, m.SourceValues, schema, reveal).Select(v => new SourceValueDto(v.Column, v.Value)).ToList(),
        reveal ? m.Records : Masking.Records(m.Records, null));

    public async Task<IReadOnlyList<IssueDto>> IssuesAsync(string id, CancellationToken ct)
    {
        var (_, batch) = await access.RequireHeldAsync(id, ct);
        return Exports.Issues(batch.Result.Issues, batch.Acknowledgements);
    }

    public async Task<DownloadDto> IssuesCsvAsync(string id, CancellationToken ct) => Exports.ExceptionsCsv(id, await IssuesAsync(id, ct));

    public async Task<IReadOnlyList<CorrectionDto>> CorrectionsAsync(string id, CancellationToken ct)
    {
        var (_, batch) = await access.RequireHeldAsync(id, ct);
        return Exports.Corrections(batch.Result.Corrections);
    }

    public async Task<DownloadDto> CorrectionsCsvAsync(string id, CancellationToken ct) => Exports.CorrectionsCsv(id, await CorrectionsAsync(id, ct));

    public async Task<FilePreviewDto> FilePreviewAsync(string id, int lines, CancellationToken ct)
    {
        var (_, batch) = await access.RequireHeldAsync(id, ct);
        var all = batch.Result.OutputLines;
        var masked = batch.Result.MaskedOutputLines; // never the delivered lines: the preview is always masked
        return new FilePreviewDto(batch.Result.OutputFileName, all.Count, masked.Take(Math.Clamp(lines, 1, 500)).ToList());
    }

    public async Task<DownloadDto> FileAsync(string id, CancellationToken ct)
    {
        var (_, batch) = await access.RequireHeldAsync(id, ct);
        audit.Record("file-downloaded", id, user.UserId, batch.Result.OutputFileName);
        return new DownloadDto(batch.Result.OutputFileName, "text/csv", ImportCommands.FileBytes(batch));
    }

    public async Task<string> ReportAsync(string id, CancellationToken ct)
    {
        var (record, batch) = await access.RequireHeldAsync(id, ct);
        return ReconciliationReport.Build(ImportCommands.Header(batch, record.Status.ToString(), null, null, null), batch.Result, batch.Acknowledgements);
    }

    public async Task<DownloadDto> ReportDownloadAsync(string id, CancellationToken ct) => Exports.Report(id, await ReportAsync(id, ct));
}
