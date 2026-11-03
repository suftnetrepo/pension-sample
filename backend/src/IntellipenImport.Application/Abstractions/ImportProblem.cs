using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Application.Abstractions;

public enum ProblemKind { Invalid, NotFound, Conflict, Gone, TooLarge, Unprocessable }

/// <summary>An expected failure with a plain-English message for the user. The API turns it into problem details.</summary>
public sealed class ImportProblem(ProblemKind kind, string title, string detail, SchemaMismatch? mismatch = null) : Exception(detail)
{
    public ProblemKind Kind { get; } = kind;
    public string Title { get; } = title;
    /// <summary>Set when a file doesn't match its schema, so the screen can list the differences.</summary>
    public SchemaMismatch? Mismatch { get; } = mismatch;
}

/// <summary>Limits from configuration section "Import".</summary>
public sealed class ImportOptions
{
    public const string Section = "Import";
    public int BatchExpiryHours { get; set; } = 24;
    public int MarkerDays { get; set; } = 7;
    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxLines { get; set; } = 50_000;
}
