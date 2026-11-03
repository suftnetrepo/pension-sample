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
    private readonly Dictionary<string, Sensitivity> _sensitive = Masking.Columns(schema);

    public string Get(CsvRow row, string column)
    {
        if (!_index.TryGetValue(column, out var i)) _index[column] = i = table.IndexOf(column);
        return i >= 0 && i < row.Values.Count ? row.Values[i].Trim() : string.Empty;
    }

    /// <summary>The value as it may be shown: masked for sensitive columns.</summary>
    public string Shown(CsvRow row, string column)
    {
        return Mask(column, Get(row, column));
    }

    private string Mask(string column, string value) => _sensitive.TryGetValue(column.Trim(), out var s) ? Masking.Value(s, value) : value;

    /// <summary>The source column a canonical field comes from (what the user sees in their file).</summary>
    public string ColumnOf(string field) => schema.Fields.TryGetValue(field, out var m) ? m.Column : field;

    public string SalaryColumn(int index) => schema.Salaries[index].Column;
    public string LatestSalaryColumn => schema.Salaries[^1].Column;

    /// <summary>The cell exactly as written, including any leading or trailing spaces.</summary>
    private string Raw(CsvRow row, string column)
    {
        var i = table.IndexOf(column);
        var values = row.Untrimmed ?? row.Values;
        return i >= 0 && i < values.Count ? values[i] : string.Empty;
    }

    /// <summary>
    /// Reads the line. Safe format fixes (spaces, case, £ and thousands separators, unpadded dates) are made only when the
    /// fixed value is then accepted, and each is noted in <paramref name="corrections"/>. Nothing else is ever guessed.
    /// </summary>
    public CanonicalMember Read(CsvRow row, List<ImportIssue> issues, List<Correction> corrections)
    {
        var member = new CanonicalMember();
        var key = KeyOf(row);

        // One correction per cell, with every fix made to it described together. Not noted if the cell ended up with an error.
        void Note(string column, string after, List<string> notes)
        {
            if (notes.Count == 0 || issues.Any(i => i.Field == column) || corrections.Any(c => c.Column == column)) return; // a column read twice (a split name) is noted once
            var before = Raw(row, column);
            corrections.Add(new Correction(row.LineNumber, key, column, Mask(column, before), Mask(column, after), string.Join("; ", notes)));
        }
        static List<string> Spaces(string raw) => raw != raw.Trim() ? ["Leading or trailing spaces removed"] : [];

        foreach (var (field, map) in schema.Fields)
        {
            var cell = Raw(row, map.Column);
            var raw = cell.Trim();
            var fromFile = raw.Length > 0;
            if (raw.Length == 0 && map.Default is not null) raw = map.Default;
            raw = Split(raw, map.Split);
            var notes = fromFile ? Spaces(cell) : [];
            var spec = CanonicalFields.All[field];
            switch (spec.Kind)
            {
                case FieldKind.Text:
                    SetText(member, field, raw);
                    Note(map.Column, map.Split is null ? raw : cell.Trim(), notes);
                    break;
                case FieldKind.NiNumber:
                    var compact = raw.Replace(" ", string.Empty, StringComparison.Ordinal);
                    member.NiNumber = compact.ToUpperInvariant();
                    notes = [];
                    if (fromFile && raw.Contains(' ')) notes.Add("Spaces removed");
                    else if (fromFile && cell != raw) notes.Add("Leading or trailing spaces removed");
                    if (member.NiNumber != compact) notes.Add("Letters upper-cased");
                    Note(map.Column, member.NiNumber, notes);
                    break;
                case FieldKind.Date:
                    if (raw.Length == 0) break;
                    if (Values.TryDate(raw, schema.File.DateFormat, out var d))
                    {
                        SetDate(member, field, d);
                        Note(map.Column, raw, notes);
                    }
                    else if (Values.TryUnpaddedDate(raw, schema.File.DateFormat, out var u))
                    {
                        SetDate(member, field, u);
                        notes.Add($"Day or month written with one digit; read as the same date in {schema.File.DateFormat.ToLowerInvariant()}");
                        Note(map.Column, Values.Date(u, schema.File.DateFormat), notes);
                    }
                    else issues.Add(Issue(row, key, map.Column, $"Must be a real date in the format {schema.File.DateFormat.ToLowerInvariant()}",
                        "Ask the employer to confirm the date and send a corrected file", IssueOwner.Employer));
                    break;
                case FieldKind.Decimal:
                    if (raw.Length == 0) break;
                    if (Values.TryNumber(raw, out var n))
                    {
                        member.PartTimeFraction = n;
                        Note(map.Column, Values.Plain(n), [.. notes, .. NumberNotes(raw)]);
                    }
                    else issues.Add(Issue(row, key, map.Column, "Must be a number", "Ask payroll to correct the value and send a corrected file", IssueOwner.Payroll));
                    break;
                case FieldKind.Code:
                    if (raw.Length == 0) break;
                    var codes = map.Codes ?? spec.Codes!.ToDictionary(c => c, c => c);
                    var hit = codes.Where(kv => string.Equals(kv.Key, raw, StringComparison.Ordinal)).Concat(codes.Where(kv => string.Equals(kv.Key, raw, StringComparison.OrdinalIgnoreCase)))
                        .FirstOrDefault();
                    if (hit.Key is not null)
                    {
                        SetCode(member, field, hit.Value);
                        if (hit.Key != raw) notes.Add($"Matched the agreed value '{hit.Key}' ignoring upper/lower case");
                        Note(map.Column, hit.Key, notes);
                    }
                    else issues.Add(Issue(row, key, map.Column, "Must be one of: " + string.Join(", ", codes.Keys),
                        $"Ask payroll to use a value agreed for {schema.SourceName} {schema.Version} and send a corrected file. A new value must be agreed with the scheme administrator first",
                        IssueOwner.Payroll));
                    break;
            }
        }

        foreach (var s in schema.Salaries)
        {
            var cell = Raw(row, s.Column);
            var raw = cell.Trim();
            decimal? amount = null;
            if (raw.Length > 0)
            {
                if (Values.TryNumber(raw, out var n))
                {
                    amount = n;
                    Note(s.Column, Values.Plain(n), [.. Spaces(cell), .. NumberNotes(raw)]);
                }
                else issues.Add(Issue(row, key, s.Column, "Must be a number", "Ask payroll to correct the salary and send a corrected file", IssueOwner.Payroll));
            }
            member.Salaries.Add(new SalaryEntry(s.Period, amount));
        }
        return member;
    }

    private static List<string> NumberNotes(string raw)
    {
        var notes = new List<string>();
        if (raw.Contains('£')) notes.Add("£ sign removed");
        if (raw.Contains(',')) notes.Add("Thousands separator removed");
        return notes;
    }

    public string KeyOf(CsvRow row) => Get(row, ColumnOf(CanonicalFields.MemberId));

    public ImportIssue Issue(CsvRow row, string key, string column, string rule, string action, IssueOwner owner, Severity severity = Severity.Error) => new()
    {
        Severity = severity, LineNumber = row.LineNumber, Key = key, Field = column, Value = Shown(row, column), Rule = rule, Action = action, Owner = owner,
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
