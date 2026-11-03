using System.Text;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Processing;
using IntellipenImport.Domain.Batches;

namespace IntellipenImport.Application.Imports;

/// <summary>
/// The read-only view of committed batches, from the database. There are no commands here on purpose:
/// a committed batch cannot be changed, recommitted or discarded. Exports return exactly what was saved at commit.
/// </summary>
public sealed class CommittedQueries(ICommittedBatchReader reader, ITargetMappingCatalog mappings, ICurrentUser user, IAuditLog audit)
{
    public async Task<PagedDto<CommittedItemDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);
        var result = await reader.ListAsync(search?.Trim(), page, pageSize, ct);
        return new PagedDto<CommittedItemDto>(
            result.Items.Select(b => new CommittedItemDto(b.Id, b.Source, b.SourceName, b.SchemaVersion, b.FileName, b.CommittedBy, b.CommittedAtUtc,
                b.Accepted, b.Rejected, b.OutputFileName)).ToList(),
            page, pageSize, result.Total);
    }

    /// <summary>The same summary shape as a batch under review, with status Committed and CanCommit always false.</summary>
    public async Task<BatchSummaryDto> GetAsync(string id, CancellationToken ct)
    {
        var s = await RequireAsync(id, ct);
        var f = s.Figures;
        var current = mappings.Current;
        var description = current.Name == s.MappingName && current.Version == s.MappingVersion ? current.Description : string.Empty;
        return new BatchSummaryDto(
            s.Id, BatchStatus.Committed, s.Source, s.SourceName, s.SchemaVersion, s.FileName, s.FileSha256,
            new MappingDto(s.MappingName, s.MappingVersion, description),
            s.UploadedBy, s.UploadedAtUtc, ExpiresAtUtc: s.CommittedAtUtc,
            new CountsDto(f.LinesRead, f.Accepted, f.Rejected, f.Errors, f.Warnings, f.LinesWithWarnings, f.WarningsChecked, f.Corrections),
            f.Reconciliation.Select(l => new ReconciliationDto(l.Measure, l.IsMoney, l.Source, l.Transformed, l.Rejected, l.Difference)).ToList(),
            f.RecordChecks.Select(c => new RecordCheckDto(c.Type, c.Expected, c.Actual, c.Ok)).ToList(),
            f.TrailerOk, f.Balanced, WarningsPending: 0, CanCommit: false, s.CommittedBy, s.CommittedAtUtc,
            new CommitOutcome
            {
                MembersSaved = f.Accepted, RecordCounts = f.RecordCounts, RejectedLines = f.Rejected,
                OutputFileName = s.OutputFileName, OutputFileSha256 = s.OutputFileSha256,
                ReportSent = true, ReportDestination = "Saved with the batch",
            });
    }

    /// <summary>Accepted members as saved. show = accepted (default) or warnings; rejected lines were never saved, so see the exceptions.</summary>
    public async Task<PagedDto<MemberRowDto>> RowsAsync(string id, int page, int pageSize, string? search, string? show, CancellationToken ct)
    {
        await RequireAsync(id, ct);
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var warningsOnly = string.Equals(show, "warnings", StringComparison.OrdinalIgnoreCase);
        var result = await reader.MembersAsync(id, search?.Trim(), warningsOnly, page, pageSize, ct);
        var items = result.Items.Select(m => new MemberRowDto(m.LineNumber, m.Key, true, m.HasWarnings, [], Masking.Records(m.Records, mappings.Current))).ToList();
        return new PagedDto<MemberRowDto>(items, page, pageSize, result.Total);
    }

    /// <summary>One saved member's real values. Needs Import.RevealSensitive (or Import.Committer); audited without the values.</summary>
    public async Task<MemberRowDto> RevealAsync(string id, int lineNumber, CancellationToken ct)
    {
        if (!user.CanReveal) throw new ImportProblem(ProblemKind.Forbidden, "Not allowed", "Showing the full values needs the Import.RevealSensitive role.");
        await RequireAsync(id, ct);
        var m = await reader.MemberAsync(id, lineNumber, ct)
            ?? throw new ImportProblem(ProblemKind.NotFound, "Line not found", $"Committed batch {id} has no saved member on line {lineNumber}.");
        audit.Record("member-revealed", id, user.UserId, $"line {lineNumber}");
        return new MemberRowDto(m.LineNumber, m.Key, true, m.HasWarnings, [], m.Records);
    }

    public async Task<IReadOnlyList<IssueDto>> IssuesAsync(string id, CancellationToken ct)
    {
        await RequireAsync(id, ct);
        return Exports.Issues(await reader.IssuesAsync(id, ct), await reader.AcknowledgementsAsync(id, ct));
    }

    public async Task<IReadOnlyList<CorrectionDto>> CorrectionsAsync(string id, CancellationToken ct)
    {
        await RequireAsync(id, ct);
        return Exports.Corrections(await reader.CorrectionsAsync(id, ct));
    }

    public async Task<DownloadDto> CorrectionsCsvAsync(string id, CancellationToken ct) => Exports.CorrectionsCsv(id, await CorrectionsAsync(id, ct));

    public async Task<DownloadDto> IssuesCsvAsync(string id, CancellationToken ct) => Exports.ExceptionsCsv(id, await IssuesAsync(id, ct));

    /// <summary>A preview of the file with sensitive values masked. A batch committed before masking existed has no masked copy, so it has no preview.</summary>
    public async Task<FilePreviewDto> FilePreviewAsync(string id, int lines, CancellationToken ct)
    {
        var s = await RequireAsync(id, ct);
        var text = await reader.MaskedOutputFileAsync(id, ct);
        if (string.IsNullOrEmpty(text))
            throw new ImportProblem(ProblemKind.NotFound, "Preview not available", $"{id} was committed before masked previews existed. A committer can download the file.");
        var all = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (all.Count > 0 && all[^1].Length == 0) all.RemoveAt(all.Count - 1);
        return new FilePreviewDto(s.OutputFileName, all.Count, all.Take(Math.Clamp(lines, 1, 500)).ToList());
    }

    /// <summary>The IntelliPen file byte-for-byte as delivered at commit (its fingerprint matches the report). Who may call this is decided by the DownloadFile policy on the endpoint; every download is audited.</summary>
    public async Task<DownloadDto> FileDownloadAsync(string id, CancellationToken ct)
    {
        var s = await RequireAsync(id, ct);
        var text = await reader.OutputFileAsync(id, ct);
        if (string.IsNullOrEmpty(text))
            throw new ImportProblem(ProblemKind.NotFound, "File not kept", $"The IntelliPen file for {id} was not kept in the database (committed before files were stored).");
        audit.Record("file-downloaded", id, user.UserId, s.OutputFileName);
        return new DownloadDto(s.OutputFileName, "text/csv", Encoding.UTF8.GetBytes(text));
    }

    /// <summary>The reconciliation report saved at commit (not rebuilt), so it is exactly what was sent.</summary>
    public async Task<string> ReportAsync(string id, CancellationToken ct) =>
        await reader.ReportAsync(id, ct) ?? throw NotFound(id);

    public async Task<DownloadDto> ReportDownloadAsync(string id, CancellationToken ct) => Exports.Report(id, await ReportAsync(id, ct));

    private async Task<CommittedSnapshot> RequireAsync(string id, CancellationToken ct) => await reader.FindAsync(id, ct) ?? throw NotFound(id);

    private static ImportProblem NotFound(string id) =>
        new(ProblemKind.NotFound, "Committed batch not found", $"There is no committed batch {id}.");
}
