using IntellipenImport.Domain.Canonical;
using IntellipenImport.Domain.Processing;
using IntellipenImport.Domain.Schemas;

namespace IntellipenImport.Application.Processing;

/// <summary>
/// Step 1 of a line: read it into the canonical member using the chosen source schema.
/// Values that can't be read (a bad date, an unagreed code) become errors against the source column the user knows.
/// </summary>
internal sealed class CanonicalReader(CsvTable table, SourceSchema schema)
{
    private readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _masked = new(schema.File.MaskedColumns, StringComparer.OrdinalIgnoreCase);

    public string Get(CsvRow row, string column)
    {
        if (!_index.TryGetValue(column, out var i)) _index[column] = i = table.IndexOf(column);
        return i >= 0 && i < row.Values.Count ? row.Values[i].Trim() : string.Empty;
    }

    /// <summary>The value as it may be shown: masked for sensitive columns.</summary>
    public string Shown(CsvRow row, string column)
    {
        var v = Get(row, column);
        return _masked.Contains(column) ? Values.MaskNi(v) : v;
    }

    /// <summary>The source column a canonical field comes from (what the user sees in their file).</summary>
    public string ColumnOf(string field) => schema.Fields.TryGetValue(field, out var m) ? m.Column : field;

    public string SalaryColumn(int index) => schema.Salaries[index].Column;
    public string LatestSalaryColumn => schema.Salaries[^1].Column;

    public CanonicalMember Read(CsvRow row, List<ImportIssue> issues)
    {
        var member = new CanonicalMember();
        var key = KeyOf(row);
        foreach (var (field, map) in schema.Fields)
        {
            var raw = Get(row, map.Column);
            if (raw.Length == 0 && map.Default is not null) raw = map.Default;
            raw = Split(raw, map.Split);
            var spec = CanonicalFields.All[field];
            switch (spec.Kind)
            {
                case FieldKind.Text:
                    SetText(member, field, raw);
                    break;
                case FieldKind.NiNumber:
                    member.NiNumber = raw.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
                    break;
                case FieldKind.Date:
                    if (raw.Length == 0) break;
                    if (Values.TryDate(raw, schema.File.DateFormat, out var d)) SetDate(member, field, d);
                    else issues.Add(Issue(row, key, map.Column, $"Must be a real date in the format {schema.File.DateFormat.ToLowerInvariant()}",
                        "Correct the date in the source file and upload again"));
                    break;
                case FieldKind.Decimal:
                    if (raw.Length == 0) break;
                    if (Values.TryNumber(raw, out var n)) member.PartTimeFraction = n;
                    else issues.Add(Issue(row, key, map.Column, "Must be a number", "Correct the value in the source file"));
                    break;
                case FieldKind.Code:
                    if (raw.Length == 0) break;
                    var codes = map.Codes ?? spec.Codes!.ToDictionary(c => c, c => c);
                    var hit = codes.FirstOrDefault(kv => string.Equals(kv.Key, raw, StringComparison.OrdinalIgnoreCase));
                    if (hit.Key is not null) SetCode(member, field, hit.Value);
                    else issues.Add(Issue(row, key, map.Column, "Must be one of: " + string.Join(", ", codes.Keys),
                        $"Use a value agreed for {schema.SourceName} {schema.Version}"));
                    break;
            }
        }

        foreach (var s in schema.Salaries)
        {
            var raw = Get(row, s.Column);
            decimal? amount = null;
            if (raw.Length > 0)
            {
                if (Values.TryNumber(raw, out var n)) amount = n;
                else issues.Add(Issue(row, key, s.Column, "Must be a number", "Correct the salary"));
            }
            member.Salaries.Add(new SalaryEntry(s.Period, amount));
        }
        return member;
    }

    public string KeyOf(CsvRow row) => Get(row, ColumnOf(CanonicalFields.MemberId));

    public ImportIssue Issue(CsvRow row, string key, string column, string rule, string action, Severity severity = Severity.Error) => new()
    {
        Severity = severity, LineNumber = row.LineNumber, Key = key, Field = column, Value = Shown(row, column), Rule = rule, Action = action,
    };

    private static string Split(string raw, string? split)
    {
        if (split is null) return raw;
        var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return split == "first"
            ? (words.Length > 1 ? words[0] : string.Empty)
            : (words.Length > 1 ? string.Join(' ', words.Skip(1)) : (words.Length == 1 ? words[0] : string.Empty));
    }

    private static void SetText(CanonicalMember m, string field, string v)
    {
        switch (field)
        {
            case CanonicalFields.MemberId: m.MemberId = v; break;
            case CanonicalFields.Forename: m.Forename = v; break;
            case CanonicalFields.Surname: m.Surname = v; break;
        }
    }

    private static void SetDate(CanonicalMember m, string field, DateOnly v)
    {
        switch (field)
        {
            case CanonicalFields.DateOfBirth: m.DateOfBirth = v; break;
            case CanonicalFields.DateJoined: m.DateJoined = v; break;
            case CanonicalFields.DateLeft: m.DateLeft = v; break;
        }
    }

    private static void SetCode(CanonicalMember m, string field, string v)
    {
        switch (field)
        {
            case CanonicalFields.Sex: m.Sex = v; break;
            case CanonicalFields.Status: m.Status = v; break;
            case CanonicalFields.MaritalStatus: m.MaritalStatus = v; break;
        }
    }
}
