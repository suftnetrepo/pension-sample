using System.Globalization;
using IntellipenImport.Domain.Canonical;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Application.Processing;

/// <summary>Step 3: canonical member → IntelliPen records, and the records → the file's lines. Reads only canonical fields.</summary>
internal static class IntellipenWriter
{
    public static List<TargetRecord> Records(CanonicalMember m, TargetMapping mapping)
    {
        var records = new List<TargetRecord>();
        foreach (var def in mapping.Records)
        {
            IEnumerable<SalaryEntry?> entries = def.PerSalary ? m.Salaries : new SalaryEntry?[] { null };
            foreach (var salary in entries)
            {
                var record = new TargetRecord { Type = def.Type };
                foreach (var f in def.Fields)
                    record.Fields.Add(new TargetField(f.Name, f.Constant ?? Format(Read(m, f.From!, salary), f)));
                records.Add(record);
            }
        }
        return records;
    }

    /// <summary>A canonical field's value (string, DateOnly, decimal or null).</summary>
    public static object? Read(CanonicalMember m, string field, SalaryEntry? salary = null) => field switch
    {
        CanonicalFields.MemberId => m.MemberId,
        CanonicalFields.Forename => m.Forename,
        CanonicalFields.Surname => m.Surname,
        CanonicalFields.Sex => m.Sex,
        CanonicalFields.DateOfBirth => m.DateOfBirth,
        CanonicalFields.NiNumber => m.NiNumber,
        CanonicalFields.DateJoined => m.DateJoined,
        CanonicalFields.DateLeft => m.DateLeft,
        CanonicalFields.Status => m.Status,
        CanonicalFields.MaritalStatus => m.MaritalStatus,
        CanonicalFields.PartTimeFraction => m.PartTimeFraction ?? 1m,
        CanonicalFields.LatestSalary => m.LatestSalary,
        CanonicalFields.SalaryPeriod => salary?.Period,
        CanonicalFields.SalaryAmount => salary?.Amount,
        _ => null,
    };

    private static string Format(object? value, TargetFieldDefinition f) => value switch
    {
        null => string.Empty,
        DateOnly d => d.ToString(f.Format ?? "yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal n => n.ToString("F" + (f.Decimals ?? 2), CultureInfo.InvariantCulture),
        string s when f.Codes is not null => s.Length == 0 ? string.Empty : f.Codes.TryGetValue(s, out var code) ? code : s,
        string s => s,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    public static (List<string> Lines, string FileName, bool TrailerOk) File(List<TargetRecord> records, TargetMapping mapping, string scheme, ImportContext ctx)
    {
        var o = mapping.Output;
        var d = o.Delimiter[0];
        string Line(IEnumerable<string> values) => string.Join(d, values.Select(v => CsvText.Escape(v, d)));
        var placeholders = new Dictionary<string, string>
        {
            ["Scheme"] = scheme,
            ["RunDate"] = ctx.RunDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ["Version"] = mapping.Version,
            ["BatchId"] = ctx.BatchId,
            ["RecordCount"] = records.Count.ToString(CultureInfo.InvariantCulture),
        };
        string Fill(string t)
        {
            foreach (var (k, v) in placeholders) t = t.Replace("{" + k + "}", v, StringComparison.Ordinal);
            return t;
        }

        var lines = new List<string>();
        var hasHeader = o.Header is { Count: > 0 };
        var hasTrailer = o.Trailer is { Count: > 0 };
        if (hasHeader) lines.Add(Line(o.Header!.Select(Fill)));
        lines.AddRange(records.Select(r => Line(r.Fields.Select(f => f.Value))));
        if (hasTrailer) lines.Add(Line(o.Trailer!.Select(Fill)));
        var dataLines = lines.Count - (hasHeader ? 1 : 0) - (hasTrailer ? 1 : 0);
        return (lines, Fill(o.FileName), dataLines == records.Count);
    }
}
