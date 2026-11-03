using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Infrastructure.Persistence;

/// <summary>Opens database connections. The only part that knows which database product is used.</summary>
public interface IDbConnectionFactory
{
    DbConnection Create();
}

/// <summary>
/// Saves committed batches (batch, member records, issues, reconciliation, report, IntelliPen file) in one transaction,
/// and reads them back for the read-only view. Written against ADO.NET's DbConnection, so it works with SQLite here and a
/// server database later with the same SQL shape.
/// </summary>
public sealed class SqlBatchRepository(IDbConnectionFactory connections) : IBatchRepository, ICommittedBatchReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Creates the tables if they don't exist, and adds columns a database from an earlier build lacks. Called once at start-up.</summary>
    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        await using var db = connections.Create();
        await db.OpenAsync(ct);
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = SqliteSql.CreateTables;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = SqliteSql.BatchColumns;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(0));
        }
        foreach (var (column, sql) in SqliteSql.AddedColumns.Where(c => !columns.Contains(c.Column)))
        {
            await using var cmd = db.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task SaveCommittedAsync(CommittedSnapshot snapshot, HeldBatch batch, string reportHtml, string outputFile, CancellationToken ct)
    {
        var r = batch.Result;
        var s = snapshot;
        await using var db = connections.Create();
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        foreach (var sql in SqliteSql.DeleteBatch)
            await Execute(db, tx, sql, ct, ("id", batch.Id));

        await Execute(db, tx, SqliteSql.InsertBatch, ct,
            ("id", s.Id), ("source", s.Source), ("source_name", s.SourceName), ("schema_version", s.SchemaVersion),
            ("mapping_name", s.MappingName), ("mapping_version", s.MappingVersion), ("file_name", s.FileName), ("file_sha256", s.FileSha256),
            ("uploaded_by", s.UploadedBy), ("uploaded_at_utc", Iso(s.UploadedAtUtc)),
            ("committed_by", s.CommittedBy), ("committed_at_utc", Iso(s.CommittedAtUtc)),
            ("lines_read", r.LinesRead), ("accepted", r.Accepted), ("rejected", r.Rejected), ("warnings", r.WarningCount), ("balanced", r.Balanced ? 1 : 0),
            ("output_file_name", s.OutputFileName), ("output_file_sha256", s.OutputFileSha256), ("report_html", reportHtml),
            ("output_file", outputFile), ("summary_json", JsonSerializer.Serialize(s.Figures, Json)));

        // One prepared command per table, re-used for every row: fast for tens of thousands of records.
        await using (var insert = Prepare(db, tx, SqliteSql.InsertRecord, "id", "line_number", "record_index", "member_id", "record_type", "fields_json"))
        {
            foreach (var m in r.Members.Where(m => m.Accepted))
                for (var i = 0; i < m.Records.Count; i++)
                    await Run(insert, ct, batch.Id, m.LineNumber, i, m.Key, m.Records[i].Type, JsonSerializer.Serialize(m.Records[i].Fields, Json));
        }
        await using (var insert = Prepare(db, tx, SqliteSql.InsertIssue, "id", "seq", "severity", "line_number", "member_id", "field", "value", "rule", "action"))
        {
            for (var i = 0; i < r.Issues.Count; i++)
            {
                var x = r.Issues[i];
                await Run(insert, ct, batch.Id, i, x.Severity.ToString(), x.LineNumber, x.Key, x.Field, x.Value, x.Rule, x.Action);
            }
        }
        await using (var insert = Prepare(db, tx, SqliteSql.InsertReconciliation, "id", "measure", "source", "transformed", "rejected"))
        {
            foreach (var l in r.Reconciliation)
                await Run(insert, ct, batch.Id, l.Measure, Num(l.Source), Num(l.Transformed), Num(l.Rejected));
        }

        await tx.CommitAsync(ct);
    }

    // ---- Read side ----

    public async Task<Page<CommittedBatch>> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        var filter = Search(search);
        var total = Convert.ToInt32(await Scalar(db, SqliteSql.ListCount, ct, filter), CultureInfo.InvariantCulture);
        var list = await Query(db, SqliteSql.ListPage, ct, r => new CommittedBatch(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), Date(r.GetString(6)),
                r.GetInt32(7), r.GetInt32(8), r.GetString(9)),
            [.. filter, ("take", pageSize), ("skip", (page - 1) * pageSize)]);
        return new Page<CommittedBatch>(list, total);
    }

    public async Task<CommittedSnapshot?> FindAsync(string id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        var found = await Query(db, SqliteSql.Find, ct, r =>
        {
            var json = r.GetString(14);
            var figures = string.IsNullOrEmpty(json)
                // A batch committed by an earlier build has no stored figures: show the counts it does have.
                ? new CommittedFigures { LinesRead = r.GetInt32(15), Accepted = r.GetInt32(16), Rejected = r.GetInt32(17), Warnings = r.GetInt32(18), Balanced = r.GetInt32(19) == 1 }
                : JsonSerializer.Deserialize<CommittedFigures>(json, Json) ?? new CommittedFigures();
            return new CommittedSnapshot
            {
                Id = r.GetString(0), Source = r.GetString(1), SourceName = r.GetString(2), SchemaVersion = r.GetString(3),
                MappingName = r.GetString(4), MappingVersion = r.GetString(5), FileName = r.GetString(6), FileSha256 = r.GetString(7),
                UploadedBy = r.GetString(8), UploadedAtUtc = Date(r.GetString(9)), CommittedBy = r.GetString(10), CommittedAtUtc = Date(r.GetString(11)),
                OutputFileName = r.GetString(12), OutputFileSha256 = r.GetString(13), Figures = figures,
            };
        }, ("id", id));
        return found.FirstOrDefault();
    }

    public async Task<Page<CommittedMember>> MembersAsync(string id, string? search, bool warningsOnly, int page, int pageSize, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        (string, object?)[] filter = [("id", id), ("warnings_only", warningsOnly ? 1 : 0), .. Search(search)];
        var total = Convert.ToInt32(await Scalar(db, SqliteSql.MemberCount, ct, filter), CultureInfo.InvariantCulture);
        var heads = await Query(db, SqliteSql.MemberPage, ct, r => (Line: r.GetInt32(0), Key: r.GetString(1), Warn: r.GetInt32(2) == 1),
            [.. filter, ("take", pageSize), ("skip", (page - 1) * pageSize)]);
        if (heads.Count == 0) return new Page<CommittedMember>([], total);

        // Every record for the members on this page, in one query (page lines are in order, so a range covers them).
        var records = await Query(db, SqliteSql.MemberRecords, ct, r => (Line: r.GetInt32(0), Record: new TargetRecord
            {
                Type = r.GetString(1),
                Fields = JsonSerializer.Deserialize<List<TargetField>>(r.GetString(2), Json) ?? [],
            }),
            ("id", id), ("first", heads[0].Line), ("last", heads[^1].Line));
        var byLine = records.ToLookup(x => x.Line, x => x.Record);
        return new Page<CommittedMember>(heads.Select(h => new CommittedMember(h.Line, h.Key, h.Warn, byLine[h.Line].ToList())).ToList(), total);
    }

    public async Task<IReadOnlyList<ImportIssue>> IssuesAsync(string id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return await Query(db, SqliteSql.Issues, ct, r => new ImportIssue
        {
            Severity = Enum.Parse<Severity>(r.GetString(0)), LineNumber = r.GetInt32(1), Key = r.GetString(2), Field = r.GetString(3),
            Value = r.GetString(4), Rule = r.GetString(5), Action = r.GetString(6),
        }, ("id", id));
    }

    public async Task<string?> ReportAsync(string id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return await Scalar(db, SqliteSql.Report, ct, ("id", id)) as string;
    }

    public async Task<string?> OutputFileAsync(string id, CancellationToken ct)
    {
        await using var db = await OpenAsync(ct);
        return await Scalar(db, SqliteSql.OutputFile, ct, ("id", id)) as string;
    }

    private async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var db = connections.Create();
        await db.OpenAsync(ct);
        return db;
    }

    /// <summary>The $search / $like pair: '' matches everything; otherwise a contains-match with LIKE wildcards escaped.</summary>
    private static (string, object?)[] Search(string? text)
    {
        var s = text?.Trim() ?? string.Empty;
        var like = "%" + s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        return [("search", s), ("like", like)];
    }

    private static DateTimeOffset Date(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static async Task<object?> Scalar(DbConnection db, string sql, CancellationToken ct, params (string Name, object? Value)[] values)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in values) AddParameter(cmd, name, value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is DBNull ? null : result;
    }

    private static async Task<List<T>> Query<T>(DbConnection db, string sql, CancellationToken ct, Func<DbDataReader, T> map, params (string Name, object? Value)[] values)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in values) AddParameter(cmd, name, value);
        var list = new List<T>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) list.Add(map(reader));
        return list;
    }

    private static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    private static string Num(decimal d) => d.ToString(CultureInfo.InvariantCulture);

    private static async Task Execute(DbConnection db, DbTransaction tx, string sql, CancellationToken ct, params (string Name, object? Value)[] values)
    {
        await using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in values) AddParameter(cmd, name, value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DbCommand Prepare(DbConnection db, DbTransaction tx, string sql, params string[] names)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var name in names) AddParameter(cmd, name, null);
        return cmd;
    }

    private static async Task Run(DbCommand cmd, CancellationToken ct, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++) cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParameter(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "$" + name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
