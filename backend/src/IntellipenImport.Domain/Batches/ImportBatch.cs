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

    public void EnsureOpen(string action)
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
    /// <summary>Warnings a reviewer has checked. Only warnings can be here, at most once each.</summary>
    public List<Acknowledgement> Acknowledgements { get; set; } = [];

    public int WarningsTotal => Result.WarningCount;
    public int WarningsPending => Result.Issues.Count(i => i.Severity == Severity.Warning && !IsAcknowledged(i.Id));
    public bool IsAcknowledged(int issueId) => Acknowledgements.Any(a => a.IssueId == issueId);

    /// <summary>The commit rules: what must be true before anything is saved.</summary>
    public void EnsureCommittable(bool requireWarningsAcknowledged = true)
    {
        if (!Result.Balanced) throw new BatchRuleException("The reconciliation does not balance, so this batch cannot be committed. Contact support.");
        if (Result.Accepted == 0) throw new BatchRuleException("Every line was rejected. Correct the file and upload it again.");
        if (requireWarningsAcknowledged && WarningsPending is > 0 and var n)
            throw new BatchRuleException($"{n} {(n == 1 ? "warning is" : "warnings are")} still to be checked. Check every warning before committing.");
    }

    /// <summary>
    /// Marks warnings as checked. All or nothing: if any id is unknown or is an error, nothing changes.
    /// A warning that is already checked keeps its first acknowledgement.
    /// </summary>
    public void Acknowledge(BatchRecord record, IEnumerable<int> issueIds, string by, DateTimeOffset at, string? note)
    {
        record.EnsureOpen("acknowledged");
        var ids = issueIds.Distinct().ToList();
        foreach (var id in ids)
        {
            var issue = Result.Issues.ElementAtOrDefault(id);
            if (id < 0 || issue is null) throw new BatchRuleException($"There is no issue {id} in batch {Id}.");
            if (issue.Severity != Severity.Warning) throw new BatchRuleException($"Issue {id} is an error. Only warnings can be checked; errors are corrected in the source file.");
        }
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        foreach (var id in ids.Where(id => !IsAcknowledged(id)))
            Acknowledgements.Add(new Acknowledgement(id, by, at, text));
    }

    /// <summary>Removes a check, until the batch is committed. Nothing happens if the warning wasn't checked.</summary>
    public void Unacknowledge(BatchRecord record, int issueId)
    {
        record.EnsureOpen("changed");
        Acknowledgements.RemoveAll(a => a.IssueId == issueId);
    }
}

/// <summary>A reviewer's "I have checked this warning".</summary>
public sealed record Acknowledgement(int IssueId, string By, DateTimeOffset AtUtc, string? Note);

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
