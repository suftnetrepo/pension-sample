using IntellipenImport.Api.Auth;
using IntellipenImport.Application.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IntellipenImport.Api.Controllers;

/// <summary>Thin HTTP layer over the use cases. No rules live here.</summary>
[ApiController]
[Route("api/v1")]
[Authorize(Policy = Policies.Upload)]
public sealed class ImportsController(ImportCommands commands, ImportQueries queries) : ControllerBase
{
    /// <summary>The sources and their agreed schema versions, for the Source and Version dropdowns.</summary>
    [HttpGet("sources")]
    public IReadOnlyList<SourceDto> Sources() => queries.Sources();

    /// <summary>Upload and process a CSV against the chosen source and version. Nothing is saved: the result is held for review.</summary>
    [HttpPost("imports")]
    [RequestFormLimits(MultipartBodyLengthLimit = 64 * 1024 * 1024)]
    [ProducesResponseType<BatchSummaryDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(IFormFile? file, [FromForm] string? source, [FromForm] string? version, CancellationToken ct)
    {
        await using var content = file?.OpenReadStream() ?? Stream.Null;
        var summary = await commands.UploadAsync(new UploadCommand(file?.FileName ?? string.Empty, file?.Length ?? 0, content, source, version), ct);
        return CreatedAtAction(nameof(Get), new { id = summary.Id }, summary);
    }

    [HttpGet("imports/{id}")]
    public Task<BatchSummaryDto> Get(string id, CancellationToken ct) => queries.GetAsync(id, ct);

    /// <summary>Transformed members, paged. show = accepted (default) | rejected | warnings | all.</summary>
    [HttpGet("imports/{id}/rows")]
    public Task<PagedDto<MemberRowDto>> Rows(string id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null, [FromQuery] string? show = null, CancellationToken ct = default) =>
        queries.RowsAsync(id, page, pageSize, search, show, ct);

    [HttpGet("imports/{id}/exceptions")]
    public Task<IReadOnlyList<IssueDto>> Exceptions(string id, CancellationToken ct) => queries.IssuesAsync(id, ct);

    [HttpGet("imports/{id}/exceptions.csv")]
    public async Task<FileContentResult> ExceptionsCsv(string id, CancellationToken ct) => Download(await queries.IssuesCsvAsync(id, ct));

    /// <summary>Automatic format corrections made while reading the file.</summary>
    [HttpGet("imports/{id}/corrections")]
    public Task<IReadOnlyList<CorrectionDto>> Corrections(string id, CancellationToken ct) => queries.CorrectionsAsync(id, ct);

    [HttpGet("imports/{id}/corrections.csv")]
    public async Task<FileContentResult> CorrectionsCsv(string id, CancellationToken ct) => Download(await queries.CorrectionsCsvAsync(id, ct));

    /// <summary>Marks several warnings as checked by the signed-in user. Returns the updated summary.</summary>
    [HttpPost("imports/{id}/acknowledgements")]
    public Task<BatchSummaryDto> Acknowledge(string id, AcknowledgeRequest request, CancellationToken ct) =>
        commands.AcknowledgeAsync(id, request.IssueIds ?? [], request.Note, ct);

    /// <summary>Takes back one check. Returns the updated summary.</summary>
    [HttpDelete("imports/{id}/acknowledgements/{issueId:int}")]
    public Task<BatchSummaryDto> Unacknowledge(string id, int issueId, CancellationToken ct) => commands.UnacknowledgeAsync(id, issueId, ct);

    /// <summary>Preview of the IntelliPen file (first lines), or the whole file with download=true.</summary>
    [HttpGet("imports/{id}/file")]
    public async Task<IActionResult> OutputFile(string id, [FromQuery] int lines = 50, [FromQuery] bool download = false, CancellationToken ct = default) =>
        download ? Download(await queries.FileAsync(id, ct)) : Ok(await queries.FilePreviewAsync(id, lines, ct));

    /// <summary>The reconciliation report as a page, or as a file with download=true.</summary>
    [HttpGet("imports/{id}/report")]
    public async Task<IActionResult> Report(string id, [FromQuery] bool download = false, CancellationToken ct = default) =>
        download ? Download(await queries.ReportDownloadAsync(id, ct)) : Content(await queries.ReportAsync(id, ct), "text/html; charset=utf-8");

    /// <summary>Save, write the IntelliPen file, send the report. Repeating it returns the same result.</summary>
    [HttpPost("imports/{id}/commit")]
    [Authorize(Policy = Policies.Commit)]
    public Task<BatchSummaryDto> Commit(string id, CommitRequest request, CancellationToken ct) => commands.CommitAsync(id, request.Reviewed, ct);

    [HttpDelete("imports/{id}")]
    public async Task<NoContentResult> Discard(string id, CancellationToken ct)
    {
        await commands.DiscardAsync(id, ct);
        return NoContent();
    }

    private FileContentResult Download(DownloadDto d) => File(d.Content, d.ContentType, d.FileName);
}

public sealed record CommitRequest(bool Reviewed);

public sealed record AcknowledgeRequest(int[]? IssueIds, string? Note);
