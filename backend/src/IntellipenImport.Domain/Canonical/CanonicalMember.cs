namespace IntellipenImport.Domain.Canonical;

/// <summary>
/// Our fixed internal view of a member. Every source schema translates INTO this; the IntelliPen mapping reads FROM it;
/// validation rules are written against it. A new source or schema version never changes it. A new field is added
/// only when IntelliPen needs information no source sends today, and then as optional so existing schemas still work.
/// </summary>
public sealed class CanonicalMember
{
    public string MemberId { get; set; } = string.Empty;
    public string Forename { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    /// <summary>One of <see cref="CanonicalCodes.Sex"/>.</summary>
    public string Sex { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    /// <summary>Upper case, no spaces.</summary>
    public string NiNumber { get; set; } = string.Empty;
    public DateOnly? DateJoined { get; set; }
    public DateOnly? DateLeft { get; set; }
    /// <summary>One of <see cref="CanonicalCodes.Status"/>.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>One of <see cref="CanonicalCodes.MaritalStatus"/>, or blank when the source doesn't send it.</summary>
    public string MaritalStatus { get; set; } = string.Empty;
    public decimal? PartTimeFraction { get; set; }
    /// <summary>Oldest first, in the order the source schema lists them.</summary>
    public List<SalaryEntry> Salaries { get; set; } = [];

    public decimal? LatestSalary => Salaries.Count == 0 ? null : Salaries[^1].Amount;
    public decimal? PreviousSalary => Salaries.Count < 2 ? null : Salaries[^2].Amount;
}

public sealed record SalaryEntry(string Period, decimal? Amount);

/// <summary>The canonical fields a source schema can fill, with their type and whether every source must supply them.</summary>
public static class CanonicalFields
{
    public const string MemberId = nameof(CanonicalMember.MemberId);
    public const string Forename = nameof(CanonicalMember.Forename);
    public const string Surname = nameof(CanonicalMember.Surname);
    public const string Sex = nameof(CanonicalMember.Sex);
    public const string DateOfBirth = nameof(CanonicalMember.DateOfBirth);
    public const string NiNumber = nameof(CanonicalMember.NiNumber);
    public const string DateJoined = nameof(CanonicalMember.DateJoined);
    public const string DateLeft = nameof(CanonicalMember.DateLeft);
    public const string Status = nameof(CanonicalMember.Status);
    public const string MaritalStatus = nameof(CanonicalMember.MaritalStatus);
    public const string PartTimeFraction = nameof(CanonicalMember.PartTimeFraction);

    public static readonly IReadOnlyDictionary<string, FieldSpec> All = new Dictionary<string, FieldSpec>(StringComparer.Ordinal)
    {
        [MemberId] = new(FieldKind.Text, Required: true),
        [Forename] = new(FieldKind.Text, Required: false),
        [Surname] = new(FieldKind.Text, Required: true),
        [Sex] = new(FieldKind.Code, Required: true, CanonicalCodes.Sex),
        [DateOfBirth] = new(FieldKind.Date, Required: true, Sensitivity: Sensitivity.DateOfBirth),
        [NiNumber] = new(FieldKind.NiNumber, Required: true, Sensitivity: Sensitivity.NiNumber),
        [DateJoined] = new(FieldKind.Date, Required: true),
        [DateLeft] = new(FieldKind.Date, Required: false),
        [Status] = new(FieldKind.Code, Required: true, CanonicalCodes.Status),
        [MaritalStatus] = new(FieldKind.Code, Required: false, CanonicalCodes.MaritalStatus),
        [PartTimeFraction] = new(FieldKind.Decimal, Required: false),
    };

    /// <summary>How a canonical field (or a salary one) must be masked wherever a member's values are shown.</summary>
    public static Sensitivity SensitivityOf(string field) => field switch
    {
        SalaryAmount or LatestSalary => Sensitivity.Salary,
        _ => All.TryGetValue(field, out var spec) ? spec.Sensitivity : Sensitivity.None,
    };

    /// <summary>Field names the IntelliPen mapping may read, including the per-salary ones.</summary>
    public const string SalaryPeriod = "Salary.Period";
    public const string SalaryAmount = "Salary.Amount";
    public const string LatestSalary = nameof(CanonicalMember.LatestSalary);
}

public enum FieldKind { Text, Date, Decimal, Code, NiNumber }

public sealed record FieldSpec(FieldKind Kind, bool Required, IReadOnlyList<string>? Codes = null, Sensitivity Sensitivity = Sensitivity.None);

/// <summary>
/// Member data that is masked in every screen, export preview and response unless a user with the reveal role asks for it.
/// Names are not sensitive. Salary covers every salary column and amount.
/// </summary>
public enum Sensitivity { None, NiNumber, DateOfBirth, Salary }

/// <summary>The only values canonical code fields may hold. Source schemas translate their own codes into these.</summary>
public static class CanonicalCodes
{
    public static readonly IReadOnlyList<string> Sex = ["M", "F"];
    public static readonly IReadOnlyList<string> Status = ["Active", "Deferred"];
    public static readonly IReadOnlyList<string> MaritalStatus = ["Married", "Single"];
}
