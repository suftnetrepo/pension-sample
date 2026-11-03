namespace IntellipenImport.Application.Processing;

/// <summary>Writing values into CSV safely.</summary>
public static class CsvText
{
    /// <summary>Quotes a value when it contains the delimiter, a quote or a line break.</summary>
    public static string Escape(string? value, char delimiter = ',')
    {
        value ??= string.Empty;
        var needsQuotes = value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0;
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    /// <summary>
    /// For files people open in Excel: a value starting with = + - @ (or a tab / carriage return) could run as a formula,
    /// so it is prefixed with an apostrophe. Not used for the IntelliPen file, which must carry values unchanged.
    /// </summary>
    public static string EscapeForSpreadsheet(string? value, char delimiter = ',')
    {
        value ??= string.Empty;
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]) && !IsPlainNumber(value)) value = "'" + value;
        return Escape(value, delimiter);
    }

    private static bool IsPlainNumber(string value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _);
}
