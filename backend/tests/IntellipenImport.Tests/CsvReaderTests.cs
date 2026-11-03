using IntellipenImport.Application.Processing;

namespace IntellipenImport.Tests;

public class CsvReaderTests
{
    [Fact]
    public void Reads_headings_and_lines_with_line_numbers()
    {
        var t = CsvReader.Read("A,B\r\n1,2\r\n3,4\r\n");
        Assert.Equal(["A", "B"], t.Headers);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal(2, t.Rows[0].LineNumber);
        Assert.Equal(["3", "4"], t.Rows[1].Values);
    }

    [Fact]
    public void Reads_other_delimiters()
    {
        var t = CsvReader.Read("A|B\n1|\"x|y\"\n", '|');
        Assert.Equal(["1", "x|y"], t.Rows[0].Values);
    }

    [Fact]
    public void Quoted_values_may_hold_commas_quotes_and_line_breaks()
    {
        var t = CsvReader.Read("A,B,C\n\"£52,000\",\"He said \"\"hi\"\"\",\"two\nlines\"\nx,y,z\n");
        Assert.Equal("£52,000", t.Rows[0].Values[0]);
        Assert.Equal("He said \"hi\"", t.Rows[0].Values[1]);
        Assert.Equal("two\nlines", t.Rows[0].Values[2]);
        Assert.Equal(4, t.Rows[1].LineNumber); // the quoted line break counts as a physical line
    }

    [Fact]
    public void Skips_blank_lines_and_the_byte_order_mark_and_trims_unquoted_values()
    {
        var t = CsvReader.Read("﻿A,B\n\n 1 , 2 \n\n");
        Assert.Equal("A", t.Headers[0]);
        Assert.Single(t.Rows);
        Assert.Equal(["1", "2"], t.Rows[0].Values);
        Assert.Equal(3, t.Rows[0].LineNumber);
    }

    [Fact]
    public void An_unclosed_quote_is_a_file_error()
    {
        var ex = Assert.Throws<CsvFormatException>(() => CsvReader.Read("A,B\n\"open,1\n"));
        Assert.Contains("line 2", ex.Message);
    }

    [Fact]
    public void Writing_quotes_only_when_needed_and_neutralises_spreadsheet_formulas()
    {
        Assert.Equal("plain", CsvText.Escape("plain"));
        Assert.Equal("\"a,b\"", CsvText.Escape("a,b"));
        Assert.Equal("'=SUM(A1)", CsvText.EscapeForSpreadsheet("=SUM(A1)"));
        Assert.Equal("-12.50", CsvText.EscapeForSpreadsheet("-12.50"));
    }
}
