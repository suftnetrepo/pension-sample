using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Application.Imports;

// The JSON shapes the React app is built on. Mirrored in frontend/src/services/types.ts.

public sealed record SourceVersionDto(string Version, string Description, string Agreement, string Delimiter, string DateFormat, IReadOnlyList<string> Columns);

public sealed record SourceDto(string Source, string Name, string Scheme, IReadOnlyList<SourceVersionDto> Versions);

public sealed record MappingDto(string Name, string Version, string Description);

public sealed record CountsDto(int LinesRead, int Accepted, int Rejected, int Errors, int Warnings, int LinesWithWarnings);

public sealed record ReconciliationDto(string Measure, bool IsMoney, decimal Source, decimal Transformed, decimal Rejected, decimal Difference);

public sealed record RecordCheckDto(string Type, int Expected, int Actual, bool Ok);

public sealed record BatchSummaryDto(
    string Id,
    BatchStatus Status,
    string Source,
    string SourceName,
    string SchemaVersion,
    string FileName,
    string FileSha256,
    MappingDto? Mapping,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    CountsDto? Counts,
    IReadOnlyList<ReconciliationDto> Reconciliation,
    IReadOnlyList<RecordCheckDto> RecordChecks,
    bool TrailerOk,
    bool Balanced,
    bool CanCommit,
    string? ClosedBy,
    DateTimeOffset? ClosedAtUtc,
    CommitOutcome? Commit);

public sealed record SourceValueDto(string Column, string Value);

public sealed record MemberRowDto(int LineNumber, string Key, bool Accepted, bool HasWarnings, IReadOnlyList<SourceValueDto> Source, IReadOnlyList<TargetRecord> Records);

public sealed record PagedDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record IssueDto(string Severity, int LineNumber, string Key, string Field, string Value, string Rule, string Action);

public sealed record FilePreviewDto(string FileName, int TotalLines, IReadOnlyList<string> Lines);

public sealed record DownloadDto(string FileName, string ContentType, byte[] Content);

public sealed record CommittedItemDto(string Id, string Source, string SourceName, string SchemaVersion, string FileName, string CommittedBy, DateTimeOffset CommittedAtUtc,
    int Accepted, int Rejected, string OutputFileName);

/// <summary>What the API hands the upload use case: no web types cross into Application.</summary>
public sealed record UploadCommand(string FileName, long Length, Stream Content, string? Source, string? Version);
