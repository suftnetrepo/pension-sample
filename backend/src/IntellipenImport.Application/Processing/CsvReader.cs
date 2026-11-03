using System.Text;

namespace IntellipenImport.Application.Processing;

/// <summary>
/// Reads RFC 4180 CSV: comma-separated, values optionally in double quotes, "" for a quote inside quotes,
/// commas and line breaks allowed inside quotes, Windows or Unix line endings, optional UTF-8 byte order mark.
/// </summary>
public static class CsvReader
{
    public static CsvTable Read(string text, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var records = new List<(int Line, List<string> Values)>();
        var values = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;
        var line = 1;
        var recordStartLine = 1;

        void EndField()
        {
            values.Add(fieldWasQuoted ? field.ToString() : field.ToString().Trim());
            field.Clear();
            fieldWasQuoted = false;
        }

        void EndRecord()
        {
            EndField();
            var blank = values.Count == 1 && values[0].Length == 0;
            if (!blank) records.Add((recordStartLine, values));
            values = [];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else
                {
                    if (c == '\n') line++;
                    field.Append(c);
                }
                continue;
            }

            if (c == '"' && field.ToString().Trim().Length == 0)
            {
                field.Clear();
                inQuotes = true;
                fieldWasQuoted = true;
            }
            else if (c == delimiter)
            {
                EndField();
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRecord();
                line++;
                recordStartLine = line;
            }
            else
            {
                field.Append(c);
            }
        }

        if (inQuotes) throw new CsvFormatException($"A quoted value that starts on line {recordStartLine} is never closed.");
        if (field.Length > 0 || values.Count > 0) EndRecord();

        if (records.Count == 0) return new CsvTable([], []);
        var headers = records[0].Values;
        var rows = records.Skip(1).Select(r => new CsvRow(r.Line, r.Values)).ToList();
        return new CsvTable(headers, rows);
    }
}

public sealed class CsvFormatException(string message) : Exception(message);
