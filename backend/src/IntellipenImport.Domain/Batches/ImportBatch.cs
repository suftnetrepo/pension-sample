using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Domain.Batches;

public enum BatchStatus { ReadyForReview, Committed, Discarded, Expired }

/// <summary>
/// The status record of a batch, and the rules for moving between statuses. Holds no member data, so it can be kept
/// after the batch closes: a refresh or a repeated commit then gets a clear answer.
/// </summary>
public sealed class BatchRecord
{
    public string Id { get; set; } = string.Empty;
    public BatchStatus Status { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string? ClosedBy { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public CommitOutcome? Commit { get; set; }

    public bool IsOpen => Status == BatchStatus.ReadyForReview;
    public bool HasExpired(DateTimeOffset now) => IsOpen && now >= ExpiresAtUtc;

    public void MarkCommitted(string by, DateTimeOffset at, CommitOutcome outcome)
    {
        EnsureOpen("committed");
        Status = BatchStatus.Committed;
        ClosedBy = by;
        ClosedAtUtc = at;
        Commit = outcome;
    }

    public void MarkDiscarded(string by, DateTimeOffset at)
    {
        EnsureOpen("discarded");
        Status = BatchStatus.Discarded;
        ClosedBy = by;
        ClosedAtUtc = at;
    }

    public void MarkExpired()
    {
        EnsureOpen("expired");
        Status = BatchStatus.Expired;
        ClosedAtUtc = ExpiresAtUtc;
    }

    private void EnsureOpen(string action)
    {
        if (!IsOpen) throw new BatchRuleException($"Batch {Id} is {Status.ToString().ToLowerInvariant()} and cannot be {action}.");
    }
}

/// <summary>The whole processed batch, held encrypted in the temporary store until it closes. Never in the database before commit.</summary>
public sealed class HeldBatch
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string Scheme { get; set; } = string.Empty;
    public string MappingName { get; set; } = string.Empty;
    public string MappingVersion { get; set; } = string.Empty;
    public string OutputLineBreak { get; set; } = "\r\n";
    public string FileName { get; set; } = string.Empty;
    public string FileSha256 { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public ImportResult Result { get; set; } = new();

    /// <summary>The commit rules: what must be true before anything is saved.</summary>
    public void EnsureCommittable()
    {
        if (!Result.Balanced) throw new BatchRuleException("The reconciliation does not balance, so this batch cannot be committed. Contact support.");
        if (Result.Accepted == 0) throw new BatchRuleException("Every line was rejected. Correct the file and upload it again.");
    }
}

public sealed class CommitOutcome
{
    public int MembersSaved { get; set; }
    public Dictionary<string, int> RecordCounts { get; set; } = [];
    public int RejectedLines { get; set; }
    public string OutputFileName { get; set; } = string.Empty;
    public string OutputFileSha256 { get; set; } = string.Empty;
    public bool ReportSent { get; set; }
    public string ReportDestination { get; set; } = string.Empty;
    public string? ReportError { get; set; }
}

/// <summary>A business rule stopped the action (shown to the user as-is).</summary>
public sealed class BatchRuleException(string message) : Exception(message);
