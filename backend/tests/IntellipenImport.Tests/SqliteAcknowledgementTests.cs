using System.Data.Common;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Processing;
using IntellipenImport.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using static IntellipenImport.Tests.TestFiles;

namespace IntellipenImport.Tests;

/// <summary>The new SQL, against a real SQLite file: acknowledgements and corrections are saved, read back, and added to an older database.</summary>
public sealed class SqliteAcknowledgementTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"intellipen-test-{Guid.NewGuid():N}.db");

    private sealed class Factory(string file) : IDbConnectionFactory
    {
        public DbConnection Create() => new SqliteConnection($"Data Source={file};Foreign Keys=True;Pooling=False");
    }

    public void Dispose()
    {
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_file) + "*")) File.Delete(f);
    }

    [Fact]
    public async Task Checks_and_corrections_are_saved_and_read_back()
    {
        var repo = new SqlBatchRepository(new Factory(_file));
        await repo.InitialiseAsync();
        var batch = new HeldBatch
        {
            Id = "IMP-SQL-1", Result = Run(WithLines(GoodLine.Replace("Deferred", "deferred", StringComparison.Ordinal),
                "DB006,Helen Carter,F,30/04/1966,LM163815B,08/01/1996,30/04/2018,Deferred,48500,50200,52000,0.6,Y")),
        };
        var at = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        var warning = batch.Result.Issues.Single(i => i.Severity == Severity.Warning);
        batch.Acknowledge(new BatchRecord { Id = batch.Id, Status = BatchStatus.ReadyForReview }, [warning.Id], "ann", at, "dates confirmed");
        var record = new BatchRecord { Id = batch.Id, Status = BatchStatus.ReadyForReview, UploadedBy = "ann" };
        record.MarkCommitted("bob", at, new CommitOutcome());

        await repo.SaveCommittedAsync(CommittedSnapshot.From(batch, record), batch, "<html/>", "file", default);
        await repo.SaveCommittedAsync(CommittedSnapshot.From(batch, record), batch, "<html/>", "file", default); // a retried commit replaces the save

        var acks = await repo.AcknowledgementsAsync(batch.Id, default);
        Assert.Equal(new Acknowledgement(warning.Id, "ann", at, "dates confirmed"), Assert.Single(acks));
        var issues = await repo.IssuesAsync(batch.Id, default);
        Assert.Equal(batch.Result.Issues.Select(i => (i.Id, i.Owner, i.Rule)), issues.Select(i => (i.Id, i.Owner, i.Rule)));
        var corrections = await repo.CorrectionsAsync(batch.Id, default);
        Assert.Equal(batch.Result.Corrections, corrections);
        Assert.Equal(1, (await repo.FindAsync(batch.Id, default))!.Figures.WarningsChecked);
    }

    [Fact]
    public async Task A_database_from_an_earlier_build_gets_the_new_columns_and_table()
    {
        await using (var db = new SqliteConnection($"Data Source={_file};Pooling=False"))
        {
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE import_issue (batch_id TEXT NOT NULL, seq INTEGER NOT NULL, severity TEXT NOT NULL, line_number INTEGER NOT NULL,
                    member_id TEXT NOT NULL, field TEXT NOT NULL, value TEXT NOT NULL, rule TEXT NOT NULL, action TEXT NOT NULL, PRIMARY KEY (batch_id, seq));
                CREATE TABLE import_batch (id TEXT NOT NULL PRIMARY KEY, source TEXT NOT NULL, source_name TEXT NOT NULL, schema_version TEXT NOT NULL,
                    mapping_name TEXT NOT NULL, mapping_version TEXT NOT NULL, file_name TEXT NOT NULL, file_sha256 TEXT NOT NULL, uploaded_by TEXT NOT NULL,
                    uploaded_at_utc TEXT NOT NULL, committed_by TEXT NOT NULL, committed_at_utc TEXT NOT NULL, lines_read INTEGER NOT NULL, accepted INTEGER NOT NULL,
                    rejected INTEGER NOT NULL, warnings INTEGER NOT NULL, balanced INTEGER NOT NULL, output_file_name TEXT NOT NULL, output_file_sha256 TEXT NOT NULL,
                    report_html TEXT NOT NULL);
                INSERT INTO import_issue VALUES ('OLD', 0, 'Warning', 2, 'DB001', 'Part-time fraction', '0.6', 'Is not 1', 'Confirm it');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var repo = new SqlBatchRepository(new Factory(_file));
        await repo.InitialiseAsync();
        await repo.InitialiseAsync(); // safe to run on every start

        var old = Assert.Single(await repo.IssuesAsync("OLD", default));
        Assert.Equal((0, "Is not 1"), (old.Id, old.Rule));
        Assert.Empty(await repo.AcknowledgementsAsync("OLD", default));
        Assert.Empty(await repo.CorrectionsAsync("OLD", default));
    }

    [Fact]
    public async Task The_searchable_text_column_holds_no_NI_number_birth_date_or_salary()
    {
        var repo = new SqlBatchRepository(new Factory(_file));
        await repo.InitialiseAsync();
        var batch = new HeldBatch { Id = "IMP-SQL-2", Result = Run(Sample("ashwood-payroll_v1_sample.csv")) };
        var record = new BatchRecord { Id = batch.Id, Status = BatchStatus.ReadyForReview, UploadedBy = "ann" };
        record.MarkCommitted("bob", DateTimeOffset.UtcNow, new CommitOutcome());
        await repo.SaveCommittedAsync(CommittedSnapshot.From(batch, record), batch, "<html/>", "file", default);

        var searchable = new List<string>();
        await using (var db = new SqliteConnection($"Data Source={_file};Pooling=False"))
        {
            await db.OpenAsync();
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT search_text FROM import_member_record WHERE batch_id = 'IMP-SQL-2'";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) searchable.Add(reader.GetString(0));
        }

        Assert.Equal(batch.Result.Members.Where(m => m.Accepted).Sum(m => m.Records.Count), searchable.Count); // every record row has one
        var all = string.Join("\n", searchable);
        SecurityTests.AssertNoSensitiveValues(all, "search_text column");
        Assert.Contains("DB001", all);   // member ID is searchable
        Assert.Contains("Okafor", all);  // so are names
        // and the masking can't be dodged by searching: the real NI number finds nothing, a surname finds the member
        Assert.Equal(0, (await repo.MembersAsync("IMP-SQL-2", "AB123456C", false, 1, 50, default)).Total);
        Assert.Equal(0, (await repo.MembersAsync("IMP-SQL-2", "50000", false, 1, 50, default)).Total);
        Assert.Equal(1, (await repo.MembersAsync("IMP-SQL-2", "Okafor", false, 1, 50, default)).Total);
    }
}
