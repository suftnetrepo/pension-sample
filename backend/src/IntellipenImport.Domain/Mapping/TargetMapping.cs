namespace IntellipenImport.Domain.Mapping;

/// <summary>
/// The one IntelliPen mapping (config/intellipen/*.json): canonical fields → IntelliPen records.
/// It reads only canonical fields, so a source change never touches it and an IntelliPen change never touches a source.
/// </summary>
public sealed class TargetMapping
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<TargetRecordDefinition> Records { get; set; } = [];
    public OutputDefinition Output { get; set; } = new();
    public List<ControlDefinition> Controls { get; set; } = [];
}

public sealed class TargetRecordDefinition
{
    public string Type { get; set; } = string.Empty;
    /// <summary>When true, one record per salary entry; fields may read Salary.Period and Salary.Amount.</summary>
    public bool PerSalary { get; set; }
    public List<TargetFieldDefinition> Fields { get; set; } = [];
}

public sealed class TargetFieldDefinition
{
    public string Name { get; set; } = string.Empty;
    /// <summary>A canonical field name (or Salary.Period / Salary.Amount in a per-salary record).</summary>
    public string? From { get; set; }
    public string? Constant { get; set; }
    /// <summary>Output format for dates, e.g. "yyyy-MM-dd".</summary>
    public string? Format { get; set; }
    /// <summary>Decimal places for numbers.</summary>
    public int? Decimals { get; set; }
    /// <summary>Canonical value → IntelliPen code.</summary>
    public Dictionary<string, string>? Codes { get; set; }
}

public sealed class OutputDefinition
{
    public string Delimiter { get; set; } = ",";
    /// <summary>Placeholders: {Scheme} {RunDate} {Version} {BatchId}.</summary>
    public List<string>? Header { get; set; }
    /// <summary>Adds {RecordCount}: the number of data records.</summary>
    public List<string>? Trailer { get; set; }
    public string FileName { get; set; } = "{Scheme}_{RunDate}_{BatchId}.csv";
    /// <summary>"CRLF" (default) or "LF".</summary>
    public string LineEnding { get; set; } = "CRLF";

    public string LineBreak => string.Equals(LineEnding, "LF", StringComparison.OrdinalIgnoreCase) ? "\n" : "\r\n";
}

/// <summary>A reconciliation measure over canonical members: source = transformed + rejected.</summary>
public sealed class ControlDefinition
{
    public string Measure { get; set; } = string.Empty;
    /// <summary>count | countWhere | sum</summary>
    public string Type { get; set; } = "count";
    /// <summary>Canonical field (countWhere, sum).</summary>
    public string? Field { get; set; }
    public string? EqualsValue { get; set; }
}
