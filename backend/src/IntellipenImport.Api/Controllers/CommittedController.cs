using IntellipenImport.Api.Auth;
using IntellipenImport.Application.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IntellipenImport.Api.Controllers;

/// <summary>
/// Committed batches, read-only. Only GET endpoints: nothing here can change, recommit or discard a committed batch.
/// Open to Import.Viewer as well as uploaders and committers.
/// </summary>
[ApiController]
[Route("api/v1/committed")]
[Authorize(Policy = Policies.View)]
public sealed class CommittedController(CommittedQueries queries) : ControllerBase
{
    /// <summary>Committed batches, newest first. search matches batch ID, file name, source or who committed.</summary>
    [HttpGet]
    public Task<PagedDto<CommittedItemDto>> List([FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default) => queries.ListAsync(search, page, pageSize, ct);

    [HttpGet("{id}")]
    public Task<BatchSummaryDto> Get(string id, CancellationToken ct) => queries.GetAsync(id, ct);

    /// <summary>Saved members, paged. show = accepted (default) | warnings.</summary>
    [HttpGet("{id}/rows")]
    public Task<PagedDto<MemberRowDto>> Rows(string id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null, [FromQuery] string? show = null, CancellationToken ct = default) =>
        queries.RowsAsync(id, page, pageSize, search, show, ct);

    /// <summary>One saved member's unmasked values. Needs Import.RevealSensitive or Import.Committer; audited.</summary>
    [HttpGet("{id}/rows/{lineNumber:int}/reveal")]
    [Authorize(Policy = Policies.Reveal)]
    public Task<MemberRowDto> Reveal(string id, int lineNumber, CancellationToken ct) => queries.RevealAsync(id, lineNumber, ct);

    [HttpGet("{id}/exceptions")]
    public Task<IReadOnlyList<IssueDto>> Exceptions(string id, CancellationToken ct) => queries.IssuesAsync(id, ct);

    [HttpGet("{id}/exceptions.csv")]
    public async Task<FileContentResult> ExceptionsCsv(string id, CancellationToken ct) => Download(await queries.IssuesCsvAsync(id, ct));

    [HttpGet("{id}/corrections")]
    public Task<IReadOnlyList<CorrectionDto>> Corrections(string id, CancellationToken ct) => queries.CorrectionsAsync(id, ct);

    [HttpGet("{id}/corrections.csv")]
    public async Task<FileContentResult> CorrectionsCsv(string id, CancellationToken ct) => Download(await queries.CorrectionsCsvAsync(id, ct));

    /// <summary>Preview of the IntelliPen file: the first lines, with sensitive values masked.</summary>
    [HttpGet("{id}/file")]
    public async Task<IActionResult> OutputFile(string id, [FromQuery] int lines = 50, CancellationToken ct = default) =>
        Ok(await queries.FilePreviewAsync(id, lines, ct));

    /// <summary>The whole IntelliPen file exactly as delivered. The DownloadFile policy decides who may (see Program.cs); every download is audited.</summary>
    [HttpGet("{id}/file/download")]
    [Authorize(Policy = Policies.DownloadFile)]
    public async Task<FileContentResult> OutputFileDownload(string id, CancellationToken ct = default) => Download(await queries.FileDownloadAsync(id, ct));

    /// <summary>The reconciliation report saved at commit, as a page or (download=true) a file.</summary>
    [HttpGet("{id}/report")]
    public async Task<IActionResult> Report(string id, [FromQuery] bool download = false, CancellationToken ct = default) =>
        download ? Download(await queries.ReportDownloadAsync(id, ct)) : Content(await queries.ReportAsync(id, ct), "text/html; charset=utf-8");

    private FileContentResult Download(DownloadDto d) => File(d.Content, d.ContentType, d.FileName);
}
