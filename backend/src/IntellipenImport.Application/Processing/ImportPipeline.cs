using IntellipenImport.Domain.Canonical;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Domain.Processing;
using IntellipenImport.Domain.Schemas;

namespace IntellipenImport.Application.Processing;

public sealed record ImportContext(string BatchId, DateOnly RunDate, int MaxLines = 50_000);

/// <summary>
/// CSV → (chosen source schema) → canonical members → validation → (the IntelliPen mapping) → IntelliPen file, reconciled.
/// Pure: no I/O, no clock. The same file, schema and mapping always give the same result.
/// </summary>
public static class ImportPipeline
{
    public static ImportResult Process(string csvText, SourceSchema schema, TargetMapping mapping, ImportContext context)
    {
        var result = new ImportResult();
        CsvTable table;
        try
        {
            table = CsvReader.Read(csvText, schema.File.Delimiter[0]);
        }
        catch (CsvFormatException ex)
        {
            return Fail(result, ex.Message);
        }

        // ---- File checks: the file must match the agreed schema exactly ----
        if (table.Headers.Count == 0) return Fail(result, "The file is empty.");
        result.SourceHeaders = table.Headers.ToList();
        if (table.Rows.Count == 0) return Fail(result, "The file has column headings but no data lines.");
        if (table.Rows.Count > context.MaxLines) return Fail(result, $"The file has {table.Rows.Count:N0} data lines; the limit is {context.MaxLines:N0}.");

        var mismatch = SchemaMatch(table.Headers, schema);
        if (mismatch is not null)
        {
            result.Mismatch = mismatch;
            return Fail(result, $"This file doesn't match the agreed layout for {schema.SourceName} {schema.Version}.");
        }

        var reader = new CanonicalReader(table, schema);
        result.LinesRead = table.Rows.Count;
        var keys = table.Rows.Select(reader.KeyOf).Where(k => k.Length > 0)
            .GroupBy(k => k, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // ---- Each line: read → validate → write ----
        var canonical = new List<CanonicalMember>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var key = reader.KeyOf(row);
            var member = new MemberResult { LineNumber = row.LineNumber, Key = key, SourceValues = row.Values.ToList() };
            var issues = new List<ImportIssue>();
            var corrections = new List<Correction>();
            var person = new CanonicalMember();

            if (row.Values.Count != table.Headers.Count)
            {
                var issue = reader.Issue(row, key, "(whole line)", $"Must have {table.Headers.Count} values, one per column heading",
                    "Ask payroll to fix the missing or extra delimiter on this line and send a corrected file", IssueOwner.Payroll);
                issue.Value = $"{row.Values.Count} values";
                issues.Add(issue);
            }
            else
            {
                person = reader.Read(row, issues, corrections);
                if (key.Length > 0 && keys[key] > 1)
                {
                    var others = table.Rows.Where(r => r.LineNumber != row.LineNumber && string.Equals(reader.KeyOf(r), key, StringComparison.OrdinalIgnoreCase))
                        .Select(r => r.LineNumber);
                    issues.Add(reader.Issue(row, key, reader.ColumnOf(CanonicalFields.MemberId),
                        "Must appear only once (also on line " + string.Join(", ", others) + ")", "Ask payroll to remove the duplicate line or correct the member ID and send a corrected file", IssueOwner.Payroll));
                }
                var failedColumns = issues.Select(i => i.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
                MemberRules.Check(person, row, key, reader, failedColumns, issues);
            }

            member.Accepted = issues.All(i => i.Severity != Severity.Error);
            member.HasWarnings = issues.Any(i => i.Severity == Severity.Warning);
            if (member.Accepted) member.Records = IntellipenWriter.Records(person, mapping);
            foreach (var i in issues.OrderBy(i => i.Severity))
            {
                i.Id = result.Issues.Count;
                result.Issues.Add(i);
            }
            // A value that ended up in error was not accepted, so nothing was "corrected" there.
            var errorColumns = issues.Where(i => i.Severity == Severity.Error).Select(i => i.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
            result.Corrections.AddRange(corrections.Where(c => !errorColumns.Contains(c.Column)));
            result.Members.Add(member);
            canonical.Add(person);
        }

        result.Accepted = result.Members.Count(m => m.Accepted);
        result.Rejected = result.Members.Count - result.Accepted;
        result.WarningLines = result.Members.Count(m => m.HasWarnings);

        // ---- Reconciliation: source = transformed + rejected, for every control ----
        var accepted = canonical.Where((_, i) => result.Members[i].Accepted).ToList();
        var rejected = canonical.Where((_, i) => !result.Members[i].Accepted).ToList();
        foreach (var control in mapping.Controls)
        {
            result.Reconciliation.Add(new ReconciliationLine
            {
                Measure = control.Measure,
                IsMoney = control.Type == "sum",
                Source = Measure(control, canonical),
                Transformed = Measure(control, accepted),
                Rejected = Measure(control, rejected),
            });
        }

        var records = result.Members.Where(m => m.Accepted).SelectMany(m => m.Records).ToList();
        foreach (var def in mapping.Records)
        {
            result.RecordChecks.Add(new RecordCheck
            {
                Type = def.Type,
                Expected = (def.PerSalary ? schema.Salaries.Count : 1) * result.Accepted,
                Actual = records.Count(r => r.Type == def.Type),
            });
        }
        result.RecordCounts = mapping.Records.ToDictionary(d => d.Type, d => records.Count(r => r.Type == d.Type));

        var (lines, fileName, trailerOk) = IntellipenWriter.File(records, mapping, schema.Scheme, context);
        result.OutputLines = lines;
        result.MaskedOutputLines = IntellipenWriter.MaskedLines(lines, records, mapping);
        result.OutputFileName = fileName;
        result.TrailerOk = trailerOk;
        result.Balanced = result.Reconciliation.All(r => r.Difference == 0m) && result.RecordChecks.All(c => c.Ok) && trailerOk;
        return result;
    }

    /// <summary>Exact match on headings (ignoring case and order); null when the file matches.</summary>
    public static SchemaMismatch? SchemaMatch(IReadOnlyList<string> headers, SourceSchema schema)
    {
        var present = headers.Where(h => h.Length > 0).ToList();
        var expected = schema.ExpectedColumns;
        var m = new SchemaMismatch
        {
            Missing = expected.Where(e => !present.Contains(e, StringComparer.OrdinalIgnoreCase)).ToList(),
            Unexpected = present.Where(p => !expected.Contains(p, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Duplicated = present.GroupBy(p => p, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList(),
        };
        if (m.Missing.Count + m.Unexpected.Count + m.Duplicated.Count == 0) return null;
        if (present.Count == 1 && expected.Count > 1)
        {
            var used = new[] { ',', '|', ';', '\t' }.FirstOrDefault(c => c != schema.File.Delimiter[0] && present[0].Contains(c));
            if (used != default)
                m.Hint = $"The file appears to use {Name(used)} between values, but {schema.SourceName} {schema.Version} is agreed as {Name(schema.File.Delimiter[0])}.";
        }
        return m;
    }

    /// <summary>True when the file's heading line matches this schema (used to suggest the right source).</summary>
    public static bool HeadingsMatch(string csvText, SourceSchema schema)
    {
        var end = csvText.IndexOfAny(['\r', '\n']);
        try { return SchemaMatch(CsvReader.Read(end < 0 ? csvText : csvText[..end], schema.File.Delimiter[0]).Headers, schema) is null; }
        catch (CsvFormatException) { return false; }
    }

    private static string Name(char c) => c switch { ',' => "commas", '|' => "pipes (|)", ';' => "semicolons", '\t' => "tabs", _ => $"'{c}'" };

    private static decimal Measure(ControlDefinition c, IEnumerable<CanonicalMember> members) => c.Type switch
    {
        "count" => members.Count(),
        "countWhere" => members.Count(m => string.Equals(Convert.ToString(IntellipenWriter.Read(m, c.Field!), System.Globalization.CultureInfo.InvariantCulture),
            c.EqualsValue, StringComparison.OrdinalIgnoreCase)),
        "sum" => members.Sum(m => IntellipenWriter.Read(m, c.Field!) as decimal? ?? 0m),
        _ => 0m,
    };

    private static ImportResult Fail(ImportResult result, string reason)
    {
        result.FileFailed = true;
        result.FailureReason = reason;
        return result;
    }
}
