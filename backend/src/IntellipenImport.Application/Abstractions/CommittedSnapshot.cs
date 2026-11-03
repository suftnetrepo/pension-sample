using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Application.Abstractions;

/// <summary>
/// What is kept about a committed batch for the read-only view: who, what, when, and the figures the summary shows.
/// Built once at commit, so the view shows exactly what the committer approved, whatever changes in config later.
/// </summary>
public sealed class CommittedSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string MappingName { get; set; } = string.Empty;
    public string MappingVersion { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileSha256 { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; set; }
    public string CommittedBy { get; set; } = string.Empty;
    public DateTimeOffset CommittedAtUtc { get; set; }
    public string OutputFileName { get; set; } = string.Empty;
    public string OutputFileSha256 { get; set; } = string.Empty;
    public CommittedFigures Figures { get; set; } = new();

    public static CommittedSnapshot From(HeldBatch batch, BatchRecord record)
    {
        var r = batch.Result;
        return new CommittedSnapshot
        {
            Id = batch.Id, Source = batch.Source, SourceName = batch.SourceName, SchemaVersion = batch.SchemaVersion,
            MappingName = batch.MappingName, MappingVersion = batch.MappingVersion, FileName = batch.FileName, FileSha256 = batch.FileSha256,
            UploadedBy = batch.UploadedBy, UploadedAtUtc = batch.UploadedAtUtc,
            CommittedBy = record.ClosedBy ?? string.Empty, CommittedAtUtc = record.ClosedAtUtc ?? DateTimeOffset.UtcNow,
            OutputFileName = r.OutputFileName, OutputFileSha256 = record.Commit?.OutputFileSha256 ?? string.Empty,
            Figures = new CommittedFigures
            {
                LinesRead = r.LinesRead, Accepted = r.Accepted, Rejected = r.Rejected, Errors = r.ErrorCount, Warnings = r.WarningCount,
                LinesWithWarnings = r.WarningLines, Reconciliation = r.Reconciliation, RecordChecks = r.RecordChecks, TrailerOk = r.TrailerOk,
                Balanced = r.Balanced, RecordCounts = r.RecordCounts,
                WarningsChecked = batch.Acknowledgements.Count, Corrections = r.Corrections.Count,
            },
        };
    }

    /// <summary>The delivered file's text with sensitive values masked, kept beside the real file so previews never need the real one.</summary>
    public static string MaskedFileText(HeldBatch batch) =>
        string.Join(batch.OutputLineBreak, batch.Result.MaskedOutputLines) + batch.OutputLineBreak;

    /// <summary>The accepted members as they were saved.</summary>
    public static IEnumerable<CommittedMember> Members(HeldBatch batch) =>
        batch.Result.Members.Where(m => m.Accepted).Select(m => new CommittedMember(m.LineNumber, m.Key, m.HasWarnings, m.Records));
}

public sealed class CommittedFigures
{
    public int LinesRead { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }
    public int LinesWithWarnings { get; set; }
    public int WarningsChecked { get; set; }
    public int Corrections { get; set; }
    public List<ReconciliationLine> Reconciliation { get; set; } = [];
    public List<RecordCheck> RecordChecks { get; set; } = [];
    public bool TrailerOk { get; set; }
    public bool Balanced { get; set; }
    public Dictionary<string, int> RecordCounts { get; set; } = [];
}
