using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Processing;
using IntellipenImport.Application.Reporting;
using IntellipenImport.Domain.Batches;

namespace IntellipenImport.Application.Imports;

/// <summary>Upload, commit, discard: the actions that change a batch.</summary>
public sealed class ImportCommands(
    ISourceSchemaCatalog schemas,
    ITargetMappingCatalog mappings,
    IHeldBatchStore store,
    IBatchRepository repository,
    IIntellipenFileTarget fileTarget,
    IReportSender reportSender,
    BatchAccess access,
    ICurrentUser user,
    ImportOptions options,
    TimeProvider clock,
    IAuditLog audit)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<BatchSummaryDto> UploadAsync(UploadCommand command, CancellationToken ct)
    {
        if (command.Length == 0) throw new ImportProblem(ProblemKind.Invalid, "No file", "Choose a CSV file to upload.");
        if (!command.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            throw new ImportProblem(ProblemKind.Invalid, "Not a CSV file", "Only .csv files can be uploaded.");
        if (command.Length > options.MaxFileBytes)
            throw new ImportProblem(ProblemKind.TooLarge, "File too large",
                $"The file is {command.Length / 1048576.0:0.0} MB; the limit is {options.MaxFileBytes / 1048576.0:0.#} MB.");
        if (string.IsNullOrWhiteSpace(command.Source) || string.IsNullOrWhiteSpace(command.Version))
            throw new ImportProblem(ProblemKind.Invalid, "Source and version needed", "Choose the source and the schema version.");
        var schema = schemas.Find(command.Source, command.Version)
            ?? throw new ImportProblem(ProblemKind.Invalid, "Unknown schema", $"There is no agreed schema {command.Version} for source '{command.Source}'.");
        var mapping = mappings.Current;

        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await command.Content.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }
        var text = Decode(bytes);
        if (text.Contains('\0')) throw new ImportProblem(ProblemKind.Invalid, "Not a text file", "The file is not a text CSV file.");

        var now = clock.GetUtcNow();
        var id = store.NewId();
        var result = ImportPipeline.Process(text, schema, mapping, new ImportContext(id, DateOnly.FromDateTime(now.UtcDateTime), options.MaxLines));
        var fileName = Path.GetFileName(command.FileName);
        if (result.FileFailed)
        {
            if (result.Mismatch is { } mismatch)
                mismatch.MatchesInstead = schemas.All.Where(s => s != schema && ImportPipeline.HeadingsMatch(text, s))
                    .Select(s => $"{s.SourceName} {s.Version}").FirstOrDefault();
            audit.Record("upload-failed", "-", user.UserId, $"{fileName}: {result.FailureReason}");
            throw new ImportProblem(ProblemKind.Unprocessable, "The file cannot be processed", result.FailureReason ?? "The file could not be read.", result.Mismatch);
        }

        var batch = new HeldBatch
        {
            Id = id, Source = schema.Source, SourceName = schema.SourceName, SchemaVersion = schema.Version, Scheme = schema.Scheme,
            MappingName = mapping.Name, MappingVersion = mapping.Version, OutputLineBreak = mapping.Output.LineBreak,
            FileName = fileName, FileSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            UploadedBy = user.UserId, UploadedAtUtc = now, ExpiresAtUtc = now.AddHours(options.BatchExpiryHours), Result = result,
        };
        var record = new BatchRecord
        {
            Id = id, Status = BatchStatus.ReadyForReview, Source = schema.Source, SourceName = schema.SourceName, SchemaVersion = schema.Version,
            FileName = fileName, UploadedBy = user.UserId, UploadedAtUtc = now, ExpiresAtUtc = batch.ExpiresAtUtc,
        };
        await store.SaveBatchAsync(batch, ct);
        await store.SaveRecordAsync(record, ct);
        audit.Record("uploaded", id, user.UserId,
            $"{schema.Source} {schema.Version}: {result.LinesRead} lines, {result.Accepted} accepted, {result.Rejected} rejected, balanced {result.Balanced}");
        return access.Summary(record, batch);
    }

    public async Task<BatchSummaryDto> CommitAsync(string id, bool reviewed, CancellationToken ct)
    {
        if (!reviewed) throw new ImportProblem(ProblemKind.Invalid, "Not reviewed", "Tick \"I have reviewed\" before committing.");
        if (!user.IsCommitter) throw new ImportProblem(ProblemKind.Conflict, "Not allowed", "Committing needs the Import.Committer role.");

        var gate = Locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var record = await access.RecordAsync(id, ct);
            if (record.Status == BatchStatus.Committed) return access.Summary(record, null); // a repeated click gets the same answer
            var (_, batch) = await access.RequireHeldAsync(id, ct);
            Guard(batch.EnsureCommittable);

            var now = clock.GetUtcNow();
            var fileText = FileText(batch);
            var content = Encoding.UTF8.GetBytes(fileText);
            var fileSha = Convert.ToHexStringLower(SHA256.HashData(content));
            var report = ReconciliationReport.Build(Header(batch, "Committed", user.UserId, now, fileSha), batch.Result);
            var outcome = new CommitOutcome
            {
                MembersSaved = batch.Result.Accepted, RecordCounts = batch.Result.RecordCounts, RejectedLines = batch.Result.Rejected,
                OutputFileName = batch.Result.OutputFileName, OutputFileSha256 = fileSha,
            };
            Guard(() => record.MarkCommitted(user.UserId, now, outcome));

            // Save and deliver. If either fails, the record is not stored as committed and the batch stays held for a retry.
            await repository.SaveCommittedAsync(CommittedSnapshot.From(batch, record), batch, report, fileText, ct);
            await fileTarget.WriteAsync(batch.Result.OutputFileName, content, ct);

            // The report: a failure here doesn't undo the commit; the report is already saved with the batch.
            try
            {
                outcome.ReportDestination = await reportSender.SendAsync(batch.Id, $"IntelliPen import {batch.Id}: reconciliation report", report, ct);
                outcome.ReportSent = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                audit.Failure("report-not-sent", batch.Id, ex);
                outcome.ReportDestination = "Not sent";
                outcome.ReportError = "The report could not be sent. It has been saved with the batch.";
            }

            await store.SaveRecordAsync(record, ct);
            await store.RemoveBatchAsync(batch.Id, ct);
            audit.Record("committed", batch.Id, user.UserId, $"{batch.Result.Accepted} members, file {batch.Result.OutputFileName}; report: {outcome.ReportDestination}");
            return access.Summary(record, null);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DiscardAsync(string id, CancellationToken ct)
    {
        var record = await access.RecordAsync(id, ct);
        if (record.Status is BatchStatus.Discarded or BatchStatus.Expired) return;
        if (record.Status == BatchStatus.Committed)
            throw new ImportProblem(ProblemKind.Conflict, "Already committed", "This batch has been committed and can no longer be discarded.");
        Guard(() => record.MarkDiscarded(user.UserId, clock.GetUtcNow()));
        await store.RemoveBatchAsync(id, ct);
        await store.SaveRecordAsync(record, ct);
        audit.Record("discarded", id, user.UserId);
    }

    internal static string FileText(HeldBatch batch) => string.Join(batch.OutputLineBreak, batch.Result.OutputLines) + batch.OutputLineBreak;

    internal static byte[] FileBytes(HeldBatch batch) => Encoding.UTF8.GetBytes(FileText(batch));

    internal static ReportHeader Header(HeldBatch b, string status, string? committedBy, DateTimeOffset? at, string? outputSha) =>
        new(b.Id, status, b.FileName, b.FileSha256, b.SourceName, b.SchemaVersion, b.MappingName, b.MappingVersion, b.UploadedBy, b.UploadedAtUtc, committedBy, at, outputSha);

    /// <summary>UTF-8 (with or without BOM); falls back to Latin-1, which is how Excel often saves "£".</summary>
    private static string Decode(byte[] bytes)
    {
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>A broken domain rule becomes a 409 with the rule's own message.</summary>
    private static void Guard(Action rule)
    {
        try { rule(); }
        catch (BatchRuleException ex) { throw new ImportProblem(ProblemKind.Conflict, "Cannot commit", ex.Message); }
    }
}
