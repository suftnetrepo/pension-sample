using IntellipenImport.Domain.Canonical;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Domain.Schemas;

namespace IntellipenImport.Application.Processing;

/// <summary>
/// Checks schema and mapping files when they are loaded, so a mistake stops start-up with its reasons
/// instead of surfacing during a user's upload.
/// </summary>
public static class ConfigRules
{
    public static IReadOnlyList<string> CheckSchema(SourceSchema s)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(s.SourceName)) problems.Add("sourceName is missing");
        if (string.IsNullOrWhiteSpace(s.Scheme)) problems.Add("scheme is missing");
        if (s.File.Delimiter.Length != 1) problems.Add("file.delimiter must be one character");
        if (string.IsNullOrWhiteSpace(s.File.DateFormat)) problems.Add("file.dateFormat is missing");

        foreach (var (field, map) in s.Fields)
        {
            if (!CanonicalFields.All.TryGetValue(field, out var spec)) { problems.Add($"'{field}' is not a canonical field"); continue; }
            if (string.IsNullOrWhiteSpace(map.Column)) problems.Add($"{field} has no column");
            if (map.Split is not null and not "first" and not "rest") problems.Add($"{field}: split must be 'first' or 'rest'");
            if (map.Codes is not null)
            {
                if (spec.Kind != FieldKind.Code) problems.Add($"{field} is not a code field, so it cannot have codes");
                foreach (var target in map.Codes.Values.Where(v => !spec.Codes!.Contains(v, StringComparer.Ordinal)))
                    problems.Add($"{field}: code value '{target}' is not one of {string.Join(", ", spec.Codes!)}");
            }
        }
        foreach (var (field, _) in CanonicalFields.All.Where(f => f.Value.Required && !s.Fields.ContainsKey(f.Key)))
            problems.Add($"required canonical field '{field}' is not mapped");
        if (s.Salaries.Count == 0) problems.Add("at least one salary column is required");
        if (s.Salaries.Any(x => string.IsNullOrWhiteSpace(x.Column) || string.IsNullOrWhiteSpace(x.Period))) problems.Add("every salary needs a period and a column");
        return problems;
    }

    private static readonly HashSet<string> MappingFields =
        [.. CanonicalFields.All.Keys, CanonicalFields.SalaryPeriod, CanonicalFields.SalaryAmount, CanonicalFields.LatestSalary];

    public static IReadOnlyList<string> CheckMapping(TargetMapping m)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(m.Name)) problems.Add("name is missing");
        if (string.IsNullOrWhiteSpace(m.Version)) problems.Add("version is missing");
        if (m.Records.Count == 0) problems.Add("records is empty");
        if (m.Output.Delimiter.Length != 1) problems.Add("output.delimiter must be one character");

        foreach (var r in m.Records)
        foreach (var f in r.Fields)
        {
            if (f.From is null && f.Constant is null) { problems.Add($"{r.Type}.{f.Name} needs 'from' or 'constant'"); continue; }
            if (f.From is null) continue;
            if (!MappingFields.Contains(f.From)) problems.Add($"{r.Type}.{f.Name}: '{f.From}' is not a canonical field");
            if (f.From.StartsWith("Salary.", StringComparison.Ordinal) && !r.PerSalary) problems.Add($"{r.Type}.{f.Name}: {f.From} needs perSalary: true");
            // Every canonical code must have an IntelliPen code, so a validated member can always be written.
            if (f.Codes is not null && CanonicalFields.All.TryGetValue(f.From, out var spec) && spec.Codes is not null)
                foreach (var code in spec.Codes.Where(c => !f.Codes.ContainsKey(c)))
                    problems.Add($"{r.Type}.{f.Name}: no IntelliPen code for '{code}'");
        }
        foreach (var c in m.Controls)
        {
            if (c.Type is not ("count" or "countWhere" or "sum")) problems.Add($"control '{c.Measure}': type '{c.Type}' is not known");
            if (c.Type != "count" && (c.Field is null || !MappingFields.Contains(c.Field))) problems.Add($"control '{c.Measure}' needs a canonical field");
        }
        return problems;
    }
}
