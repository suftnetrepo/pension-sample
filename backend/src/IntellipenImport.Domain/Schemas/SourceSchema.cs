namespace IntellipenImport.Domain.Schemas;

/// <summary>
/// The agreed layout of one source's CSV at one version (config/schemas/{source}/{version}.json). The contractual
/// agreement with the source fixes it, so the file must match it exactly. A new agreed version is a new file.
/// </summary>
public sealed class SourceSchema
{
    /// <summary>Folder name, e.g. "ashwood-payroll".</summary>
    public string Source { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    /// <summary>File name without .json, e.g. "v1".</summary>
    public string Version { get; set; } = string.Empty;
    public string Scheme { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>The agreement this layout comes from, e.g. a contract reference and date.</summary>
    public string Agreement { get; set; } = string.Empty;
    public FileFormat File { get; set; } = new();
    /// <summary>Canonical field name → how to read it from this source.</summary>
    public Dictionary<string, FieldMapping> Fields { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Salary columns, oldest first.</summary>
    public List<SalaryColumn> Salaries { get; set; } = [];
    /// <summary>Columns the agreement includes that we don't use. They must be present, and are ignored.</summary>
    public List<string> IgnoredColumns { get; set; } = [];

    /// <summary>Every heading the file must contain, in schema order.</summary>
    public IReadOnlyList<string> ExpectedColumns =>
        Fields.Values.Select(f => f.Column)
            .Concat(Salaries.Select(s => s.Column))
            .Concat(IgnoredColumns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public sealed class FileFormat
{
    public string Delimiter { get; set; } = ",";
    /// <summary>.NET date format used by every date column, e.g. "dd/MM/yyyy".</summary>
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    /// <summary>NI numbers and other identifiers in these columns are masked wherever a value is shown.</summary>
    public List<string> MaskedColumns { get; set; } = [];
}

public sealed class FieldMapping
{
    public string Column { get; set; } = string.Empty;
    /// <summary>"first" = first word, "rest" = everything after it. For a single full-name column.</summary>
    public string? Split { get; set; }
    /// <summary>Source value → canonical value, for code fields (matched ignoring case).</summary>
    public Dictionary<string, string>? Codes { get; set; }
    /// <summary>Used when the source value is blank.</summary>
    public string? Default { get; set; }
}

public sealed class SalaryColumn
{
    public string Period { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
}
