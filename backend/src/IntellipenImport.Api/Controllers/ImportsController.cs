using IntellipenImport.Api.Auth;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Imports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace IntellipenImport.Api.Controllers;

/// <summary>Thin HTTP layer over the use cases. No rules live here.</summary>
[ApiController]
[Route("api/v1")]
[Authorize(Policy = Policies.Upload)]
public sealed class ImportsController(ImportCommands commands, ImportQueries queries, ImportOptions options) : ControllerBase
{
    /// <summary>The sources and their agreed schema versions, for the Source and Version dropdowns.</summary>
    [HttpGet("sources")]
    public IReadOnlyList<SourceDto> Sources() => queries.Sources();

    /// <summary>
    /// Upload and process a CSV against the chosen source and version. Nothing is saved: the result is held for review.
    /// The multipart body is streamed straight into memory with <see cref="MultipartReader"/>. There is deliberately no IFormFile
    /// or [FromForm] here, and [DisableFormValueModelBinding] stops MVC parsing the body itself first: ASP.NET's form binding spills any file over 64 KB to a temporary file on disk, and member data must
    /// never touch disk before commit.
    /// </summary>
    [HttpPost("imports")]
    [DisableFormValueModelBinding]
    [ProducesResponseType<BatchSummaryDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        var upload = await ReadUploadAsync(ct);
        await using var content = upload.Content;
        var summary = await commands.UploadAsync(new UploadCommand(upload.FileName, content.Length, content, upload.Source, upload.Version), ct);
        return CreatedAtAction(nameof(Get), new { id = summary.Id }, summary);
    }

    private sealed record UploadParts(string FileName, MemoryStream Content, string? Source, string? Version);

    private async Task<UploadParts> ReadUploadAsync(CancellationToken ct)
    {
        const int MaxFieldBytes = 1024;
        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var type)
            || !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(type.Boundary).Value is not { Length: > 0 } boundary)
            throw new ImportProblem(ProblemKind.Invalid, "No file", "Choose a CSV file to upload.");

        string? source = null, version = null, fileName = string.Empty;
        var content = new MemoryStream();
        try
        {
            var reader = new MultipartReader(boundary, Request.Body) { BodyLengthLimit = options.MaxFileBytes + 1024 * 1024 };
            while (await reader.ReadNextSectionAsync(ct) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)) continue;
                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                if (name == "file" && (disposition.FileName.HasValue || disposition.FileNameStar.HasValue))
                {
                    fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? string.Empty;
                    content.SetLength(0);
                    var chunk = new byte[81920];
                    int read;
                    while ((read = await section.Body.ReadAsync(chunk, ct)) > 0)
                    {
                        if (content.Length + read > options.MaxFileBytes) throw TooLarge();
                        content.Write(chunk, 0, read);
                    }
                }
                else if (name is "source" or "version")
                {
                    var text = new byte[MaxFieldBytes + 1];
                    var length = 0;
                    int read;
                    while (length <= MaxFieldBytes && (read = await section.Body.ReadAsync(text.AsMemory(length, text.Length - length), ct)) > 0) length += read;
                    if (length > MaxFieldBytes) throw new ImportProblem(ProblemKind.Invalid, "Source and version needed", "Choose the source and the schema version.");
                    var value = System.Text.Encoding.UTF8.GetString(text, 0, length).Trim();
                    if (name == "source") source = value; else version = value;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            await content.DisposeAsync();
            throw ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } or InvalidDataException ? TooLarge() : new ImportProblem(ProblemKind.Invalid, "No file", "The upload could not be read.");
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
        content.Position = 0;
        return new UploadParts(fileName, content, source, version);
    }

    private ImportProblem TooLarge() =>
        new(ProblemKind.TooLarge, "File too large", $"The file is larger than the {options.MaxFileBytes / 1048576.0:0.#} MB limit.");

    [HttpGet("imports/{id}")]
    public Task<BatchSummaryDto> Get(string id, CancellationToken ct) => queries.GetAsync(id, ct);

    /// <summary>Transformed members, paged. show = accepted (default) | rejected | warnings | all.</summary>
    [HttpGet("imports/{id}/rows")]
    public Task<PagedDto<MemberRowDto>> Rows(string id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null, [FromQuery] string? show = null, CancellationToken ct = default) =>
        queries.RowsAsync(id, page, pageSize, search, show, ct);

    /// <summary>One member's unmasked values. Needs Import.RevealSensitive or Import.Committer; audited.</summary>
    [HttpGet("imports/{id}/rows/{lineNumber:int}/reveal")]
    [Authorize(Policy = Policies.Reveal)]
    public Task<MemberRowDto> Reveal(string id, int lineNumber, CancellationToken ct) => queries.RevealAsync(id, lineNumber, ct);

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

    /// <summary>Preview of the IntelliPen file: the first lines, with sensitive values masked.</summary>
    [HttpGet("imports/{id}/file")]
    public async Task<IActionResult> OutputFile(string id, [FromQuery] int lines = 50, CancellationToken ct = default) =>
        Ok(await queries.FilePreviewAsync(id, lines, ct));

    /// <summary>The whole real IntelliPen file. The DownloadFile policy decides who may (see Program.cs); every download is audited.</summary>
    [HttpGet("imports/{id}/file/download")]
    [Authorize(Policy = Policies.DownloadFile)]
    public async Task<FileContentResult> OutputFileDownload(string id, CancellationToken ct = default) => Download(await queries.FileAsync(id, ct));

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
