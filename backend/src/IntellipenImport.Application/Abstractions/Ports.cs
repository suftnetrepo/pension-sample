using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Domain.Processing;
using IntellipenImport.Domain.Schemas;

namespace IntellipenImport.Application.Abstractions;

// The outside world, as the use cases see it. Implementations live in Infrastructure (and Api for the current user).

/// <summary>The agreed source layouts (config/schemas/{source}/{version}.json).</summary>
public interface ISourceSchemaCatalog
{
    IReadOnlyList<SourceSchema> All { get; }
    SourceSchema? Find(string source, string version);
}

/// <summary>The single IntelliPen mapping in use.</summary>
public interface ITargetMappingCatalog
{
    TargetMapping Current { get; }
}

/// <summary>The temporary store for batches awaiting review. Never the database.</summary>
public interface IHeldBatchStore
{
    string NewId();
    Task SaveBatchAsync(HeldBatch batch, CancellationToken ct);
    Task<HeldBatch?> GetBatchAsync(string id, CancellationToken ct);
    Task RemoveBatchAsync(string id, CancellationToken ct);
    Task SaveRecordAsync(BatchRecord record, CancellationToken ct);
    Task<BatchRecord?> GetRecordAsync(string id, CancellationToken ct);
}

/// <summary>Where committed batches are saved (SQLite in this build). Write side: called only by the commit use case.</summary>
public interface IBatchRepository
{
    /// <summary>
    /// Saves the batch, its member records, issues, reconciliation, the report and the exact IntelliPen file as delivered.
    /// Must be safe to call twice for the same batch: a retried commit replaces the earlier save.
    /// </summary>
    Task SaveCommittedAsync(CommittedSnapshot snapshot, HeldBatch batch, string reportHtml, string outputFile, CancellationToken ct);
}

/// <summary>Read side for committed batches. Only reads: there is no way to change a committed batch through it.</summary>
public interface ICommittedBatchReader
{
    Task<Page<CommittedBatch>> ListAsync(string? search, int page, int pageSize, CancellationToken ct);
    Task<CommittedSnapshot?> FindAsync(string id, CancellationToken ct);
    Task<Page<CommittedMember>> MembersAsync(string id, string? search, bool warningsOnly, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<ImportIssue>> IssuesAsync(string id, CancellationToken ct);
    /// <summary>Who checked which warning (by issue id).</summary>
    Task<IReadOnlyList<Acknowledgement>> AcknowledgementsAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<Correction>> CorrectionsAsync(string id, CancellationToken ct);
    Task<string?> ReportAsync(string id, CancellationToken ct);
    Task<string?> OutputFileAsync(string id, CancellationToken ct);
}

public sealed record Page<T>(IReadOnlyList<T> Items, int Total);

/// <summary>One line in the committed-batches list.</summary>
public sealed record CommittedBatch(
    string Id, string Source, string SourceName, string SchemaVersion, string FileName, string CommittedBy, DateTimeOffset CommittedAtUtc,
    int Accepted, int Rejected, string OutputFileName);

/// <summary>An accepted member as saved: the IntelliPen records only (the raw source line is not kept after commit).</summary>
public sealed record CommittedMember(int LineNumber, string Key, bool HasWarnings, IReadOnlyList<TargetRecord> Records);

/// <summary>Delivers the IntelliPen import file. Returns a description of where it went.</summary>
public interface IIntellipenFileTarget
{
    Task<string> WriteAsync(string fileName, byte[] content, CancellationToken ct);
}

/// <summary>Sends the reconciliation report. Returns a description of where it went, for the user.</summary>
public interface IReportSender
{
    Task<string> SendAsync(string batchId, string subject, string reportHtml, CancellationToken ct);
}

public interface ICurrentUser
{
    string UserId { get; }
    bool IsCommitter { get; }
}

/// <summary>Audit events: batch ID, user and counts only, never member data.</summary>
public interface IAuditLog
{
    void Record(string action, string batchId, string user, string detail = "");
    void Failure(string action, string batchId, Exception error);
}
