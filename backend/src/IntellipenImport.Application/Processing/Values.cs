using System.Globalization;

namespace IntellipenImport.Application.Processing;

/// <summary>Strict parsing and formatting shared by every step, so they can never disagree.</summary>
internal static class Values
{
    public static bool TryDate(string value, string format, out DateOnly date) =>
        DateOnly.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Accepts 50000, 50,000.00 and £50,000; rejects anything else.</summary>
    public static bool TryNumber(string value, out decimal number)
    {
        var cleaned = value.Trim().Replace("£", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out number);
    }

    public static string Money(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);

    public static string Date(DateOnly date, string format) => date.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>QQ123456A becomes QQ12****A. Anything else that isn't blank is masked whole.</summary>
    public static string MaskNi(string value)
    {
        var v = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        return v.Length == 9 ? v[..4] + "****" + v[^1..] : (v.Length == 0 ? string.Empty : "****");
    }
}
