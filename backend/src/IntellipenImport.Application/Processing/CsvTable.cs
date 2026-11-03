namespace IntellipenImport.Application.Processing;

/// <summary>One data line of the uploaded file. <see cref="LineNumber"/> is the physical line it starts on (headings = line 1).</summary>
public sealed record CsvRow(int LineNumber, IReadOnlyList<string> Values);

/// <summary>The uploaded file as headings plus data lines. Fully blank lines are skipped.</summary>
public sealed class CsvTable
{
    public CsvTable(IReadOnlyList<string> headers, IReadOnlyList<CsvRow> rows)
    {
        Headers = headers;
        Rows = rows;
    }

    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<CsvRow> Rows { get; }

    /// <summary>Column position by heading, ignoring case and surrounding spaces; -1 when absent.</summary>
    public int IndexOf(string heading)
    {
        for (var i = 0; i < Headers.Count; i++)
            if (string.Equals(Headers[i].Trim(), heading.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }
}
