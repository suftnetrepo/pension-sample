namespace IntellipenImport.Domain.Processing;

/// <summary>Everything one processed file produced. Held (encrypted) until the batch is committed, discarded or expires.</summary>
public sealed class ImportResult
{
    public bool FileFailed { get; set; }
    public string? FailureReason { get; set; }
    /// <summary>When the headings don't match the chosen schema: exactly what differs.</summary>
    public SchemaMismatch? Mismatch { get; set; }

    public int LinesRead { get; set; }
    public int Accepted { get; set; }
    public int Rejected { get; set; }
    public int WarningLines { get; set; }

    public List<string> SourceHeaders { get; set; } = [];
    public List<MemberResult> Members { get; set; } = [];
    public List<ImportIssue> Issues { get; set; } = [];
    public List<ReconciliationLine> Reconciliation { get; set; } = [];
    public List<RecordCheck> RecordChecks { get; set; } = [];
    public bool TrailerOk { get; set; }
    public bool Balanced { get; set; }

    public Dictionary<string, int> RecordCounts { get; set; } = [];
    public string OutputFileName { get; set; } = string.Empty;
    public List<string> OutputLines { get; set; } = [];

    public int WarningCount => Issues.Count(i => i.Severity == Severity.Warning);
    public int ErrorCount => Issues.Count(i => i.Severity == Severity.Error);
}

public sealed class SchemaMismatch
{
    public List<string> Missing { get; set; } = [];
    public List<string> Unexpected { get; set; } = [];
    public List<string> Duplicated { get; set; } = [];
    /// <summary>A plain-English hint, e.g. that the file uses a different delimiter.</summary>
    public string? Hint { get; set; }
    /// <summary>Another agreed schema these headings do match, e.g. "Legacy admin export v1".</summary>
    public string? MatchesInstead { get; set; }
}

public enum Severity { Error, Warning }

/// <summary>One source line: its values as supplied, whether it was accepted, and the IntelliPen records it became.</summary>
public sealed class MemberResult
{
    public int LineNumber { get; set; }
    public string Key { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public bool HasWarnings { get; set; }
    public List<string> SourceValues { get; set; } = [];
    public List<TargetRecord> Records { get; set; } = [];
}

public sealed class TargetRecord
{
    public string Type { get; set; } = string.Empty;
    public List<TargetField> Fields { get; set; } = [];
}

public sealed record TargetField(string Name, string Value);

public sealed class ImportIssue
{
    public Severity Severity { get; set; }
    public int LineNumber { get; set; }
    public string Key { get; set; } = string.Empty;
    /// <summary>The source column the problem is in (what the user sees in their file).</summary>
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}

public sealed class ReconciliationLine
{
    public string Measure { get; set; } = string.Empty;
    public bool IsMoney { get; set; }
    public decimal Source { get; set; }
    public decimal Transformed { get; set; }
    public decimal Rejected { get; set; }
    public decimal Difference => Source - Transformed - Rejected;
}

public sealed class RecordCheck
{
    public string Type { get; set; } = string.Empty;
    public int Expected { get; set; }
    public int Actual { get; set; }
    public bool Ok => Expected == Actual;
}
