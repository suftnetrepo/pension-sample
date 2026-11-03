using IntellipenImport.Application.Abstractions;
using IntellipenImport.Domain.Batches;

namespace IntellipenImport.Application.Imports;

/// <summary>Loads batches for the current user, applies ownership and expiry, and builds the summary. Shared by commands and queries.</summary>
public sealed class BatchAccess(IHeldBatchStore store, ITargetMappingCatalog mappings, ICurrentUser user, ImportOptions options, TimeProvider clock, IAuditLog audit)
{
    /// <summary>The record, if this user may see it. Someone else's batch looks the same as a missing one.</summary>
    public async Task<BatchRecord> RecordAsync(string id, CancellationToken ct)
    {
        var record = await store.GetRecordAsync(id, ct);
        if (record is null || (!user.IsCommitter && !string.Equals(record.UploadedBy, user.UserId, StringComparison.OrdinalIgnoreCase)))
            throw new ImportProblem(ProblemKind.NotFound, "Batch not found", $"There is no batch {id}, or it was closed more than {options.MarkerDays} days ago.");
        return record;
    }

    /// <summary>The held batch, or null after marking an overdue batch expired.</summary>
    public async Task<HeldBatch?> HeldOrExpireAsync(BatchRecord record, CancellationToken ct)
    {
        if (!record.IsOpen) return null;
        var batch = record.HasExpired(clock.GetUtcNow()) ? null : await store.GetBatchAsync(record.Id, ct);
        if (batch is not null) return batch;
        record.MarkExpired();
        await store.RemoveBatchAsync(record.Id, ct);
        await store.SaveRecordAsync(record, ct);
        audit.Record("expired", record.Id, record.UploadedBy);
        return null;
    }

    /// <summary>The held batch, or a clear "gone" problem saying why it is no longer available.</summary>
    public async Task<(BatchRecord Record, HeldBatch Batch)> RequireHeldAsync(string id, CancellationToken ct)
    {
        var record = await RecordAsync(id, ct);
        if (!record.IsOpen)
            throw new ImportProblem(ProblemKind.Gone, $"Batch {Word(record.Status)}",
                $"Batch {record.Id} was {Word(record.Status)}" + (record.ClosedBy is null ? "." : $" by {record.ClosedBy}."));
        var batch = await HeldOrExpireAsync(record, ct)
            ?? throw new ImportProblem(ProblemKind.Gone, "Batch expired",
                $"Batch {record.Id} was not committed within {options.BatchExpiryHours} hours and has been deleted. Upload the file again.");
        return (record, batch);
    }

    public BatchSummaryDto Summary(BatchRecord record, HeldBatch? batch)
    {
        var r = batch?.Result;
        var m = mappings.Current;
        return new BatchSummaryDto(
            record.Id, record.Status, record.Source, record.SourceName, record.SchemaVersion, record.FileName, batch?.FileSha256 ?? string.Empty,
            batch is null ? null : new MappingDto(batch.MappingName, batch.MappingVersion, m.Description),
            record.UploadedBy, record.UploadedAtUtc, record.ExpiresAtUtc,
            r is null ? null : new CountsDto(r.LinesRead, r.Accepted, r.Rejected, r.ErrorCount, r.WarningCount, r.WarningLines, batch!.Acknowledgements.Count, r.Corrections.Count),
            r?.Reconciliation.Select(l => new ReconciliationDto(l.Measure, l.IsMoney, l.Source, l.Transformed, l.Rejected, l.Difference)).ToList() ?? [],
            r?.RecordChecks.Select(c => new RecordCheckDto(c.Type, c.Expected, c.Actual, c.Ok)).ToList() ?? [],
            r?.TrailerOk ?? false,
            r?.Balanced ?? false,
            WarningsPending: batch?.WarningsPending ?? 0,
            CanCommit: record.IsOpen && r is { Balanced: true, Accepted: > 0 } && user.IsCommitter
                && (!options.RequireWarningsAcknowledged || batch!.WarningsPending == 0),
            record.ClosedBy, record.ClosedAtUtc, record.Commit);
    }

    private static string Word(BatchStatus s) => s.ToString().ToLowerInvariant();
}
