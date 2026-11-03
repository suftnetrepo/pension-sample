using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Processing;
using IntellipenImport.Application.Reporting;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Application.Imports;

/// <summary>Everything the review screens read. Nothing here changes a batch (except marking an overdue one expired).</summary>
public sealed class ImportQueries(ISourceSchemaCatalog schemas, BatchAccess access)
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

    public async Task<PagedDto<MemberRowDto>> RowsAsync(string id, int page, int pageSize, string? search, string? show, CancellationToken ct)
    {
        var (_, batch) = await access.RequireHeldAsync(id, ct);
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var headers = batch.Result.SourceHeaders;

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
            members = members.Where(m => m.Key.Contains(s, StringComparison.OrdinalIgnoreCase) || m.SourceValues.Any(v => v.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }
        var list = members.ToList();
        var items = list.Skip((page - 1) * pageSize).Take(pageSize).Select(m => new MemberRowDto(
            m.LineNumber, m.Key, m.Accepted, m.HasWarnings,
            headers.Select((h, i) => new SourceValueDto(h, i < m.SourceValues.Count ? m.SourceValues[i] : string.Empty)).ToList(),
            m.Records)).ToList();
        return new PagedDto<MemberRowDto>(items, page, pageSize, list.Count);
    }

    public async Task<IReadOnlyList<IssueDto>> IssuesAsync(string id, CancellationToken ct)
    {
        var (_, batch) = await access.RequireHeldAsync(id, ct);
        return Exports.Issues(batch.Result.Issues);
    }

    public async Task<DownloadDto> IssuesCsvAsync(string id, CancellationToken ct) => Exports.ExceptionsCsv(id, await IssuesAsync(id, ct));

    public async Task<FilePreviewDto> FilePreviewAsync(string id, int lines, CancellationToken ct)
    {
        var (_, batch) = await access.RequireHeldAsync(id, ct);
        var all = batch.Result.OutputLines;
        return new FilePreviewDto(batch.Result.OutputFileName, all.Count, all.Take(Math.Clamp(lines, 1, 500)).ToList());
    }

    public async Task<DownloadDto> FileAsync(string id, CancellationToken ct)
    {
        var (_, batch) = await access.RequireHeldAsync(id, ct);
        return new DownloadDto(batch.Result.OutputFileName, "text/csv", ImportCommands.FileBytes(batch));
    }

    public async Task<string> ReportAsync(string id, CancellationToken ct)
    {
        var (record, batch) = await access.RequireHeldAsync(id, ct);
        return ReconciliationReport.Build(ImportCommands.Header(batch, record.Status.ToString(), null, null, null), batch.Result);
    }

    public async Task<DownloadDto> ReportDownloadAsync(string id, CancellationToken ct) => Exports.Report(id, await ReportAsync(id, ct));
}
