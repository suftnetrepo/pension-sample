using System.Text.RegularExpressions;
using IntellipenImport.Domain.Canonical;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Domain.Processing;
using IntellipenImport.Domain.Schemas;

namespace IntellipenImport.Application.Processing;

/// <summary>
/// The one place that decides how sensitive member data looks when it is shown: NI number AB12****C, date of birth
/// **/**/1967 (year only), salary ****. Everything that leaves the API about a member goes through here;
/// only the reveal use case returns the real values.
/// </summary>
public static partial class Masking
{
    [GeneratedRegex(@"\d{4}")]
    private static partial Regex Year();

    public static string Value(Sensitivity sensitivity, string value) => value.Length == 0 ? value : sensitivity switch
    {
        Sensitivity.None => value,
        Sensitivity.NiNumber => Values.MaskNi(value),
        Sensitivity.DateOfBirth => "**/**/" + (Year().Match(value) is { Success: true } y ? y.Value : "****"),
        _ => "****",
    };

    /// <summary>Sensitivity by source column: the canonical field each column fills, and every salary column.</summary>
    public static Dictionary<string, Sensitivity> Columns(SourceSchema schema)
    {
        var map = new Dictionary<string, Sensitivity>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, m) in schema.Fields)
            if (CanonicalFields.SensitivityOf(field) is var s and not Sensitivity.None) map[m.Column.Trim()] = s;
        foreach (var salary in schema.Salaries) map[salary.Column.Trim()] = Sensitivity.Salary;
        foreach (var column in schema.File.MaskedColumns) map.TryAdd(column.Trim(), Sensitivity.NiNumber);
        return map;
    }

    /// <summary>A source line as shown: each value masked by its column. With no schema to go by, every value is masked.</summary>
    public static IReadOnlyList<SourceValue> Source(IReadOnlyList<string> headers, IReadOnlyList<string> values, SourceSchema? schema, bool reveal = false)
    {
        var columns = schema is null ? null : Columns(schema);
        return headers.Select((h, i) =>
        {
            var value = i < values.Count ? values[i] : string.Empty;
            if (reveal) return new SourceValue(h, value);
            if (columns is null) return new SourceValue(h, Value(Sensitivity.Salary, value));
            return new SourceValue(h, columns.TryGetValue(h.Trim(), out var s) ? Value(s, value) : value);
        }).ToList();
    }

    /// <summary>
    /// A record with its sensitive fields masked. A field saved before masking existed has no recorded sensitivity: it is looked up in
    /// the mapping by record type and name, and masked outright if the mapping doesn't know it.
    /// </summary>
    public static TargetRecord Record(TargetRecord record, TargetMapping? mapping)
    {
        var definition = mapping?.Records.FirstOrDefault(r => r.Type == record.Type);
        return new TargetRecord
        {
            Type = record.Type,
            Fields = record.Fields.Select(f =>
            {
                var s = f.Sensitivity
                    ?? (definition?.Fields.FirstOrDefault(d => d.Name == f.Name) is { } d
                        ? (d.From is null ? Sensitivity.None : CanonicalFields.SensitivityOf(d.From))
                        : Sensitivity.Salary);
                return f with { Value = Value(s, f.Value), Sensitivity = s };
            }).ToList(),
        };
    }

    public static List<TargetRecord> Records(IEnumerable<TargetRecord> records, TargetMapping? mapping) => records.Select(r => Record(r, mapping)).ToList();
}

/// <summary>One source cell: its column heading and value.</summary>
public sealed record SourceValue(string Column, string Value);
