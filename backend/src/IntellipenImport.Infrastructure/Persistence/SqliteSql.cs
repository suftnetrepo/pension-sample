namespace IntellipenImport.Infrastructure.Persistence;

/// <summary>
/// Every SQL statement the repository runs, in one place.
/// Dates are ISO-8601 text; money is text, so no rounding ever happens in the database.
/// </summary>
internal static class SqliteSql
{
    public const string CreateTables = """
        CREATE TABLE IF NOT EXISTS import_batch (
            id                  TEXT    NOT NULL PRIMARY KEY,
            source              TEXT    NOT NULL,
            source_name         TEXT    NOT NULL,
            schema_version      TEXT    NOT NULL,
            mapping_name        TEXT    NOT NULL,
            mapping_version     TEXT    NOT NULL,
            file_name           TEXT    NOT NULL,
            file_sha256         TEXT    NOT NULL,
            uploaded_by         TEXT    NOT NULL,
            uploaded_at_utc     TEXT    NOT NULL,
            committed_by        TEXT    NOT NULL,
            committed_at_utc    TEXT    NOT NULL,
            lines_read          INTEGER NOT NULL,
            accepted            INTEGER NOT NULL,
            rejected            INTEGER NOT NULL,
            warnings            INTEGER NOT NULL,
            balanced            INTEGER NOT NULL,
            output_file_name    TEXT    NOT NULL,
            output_file_sha256  TEXT    NOT NULL,
            report_html         TEXT    NOT NULL,
            output_file         TEXT    NOT NULL DEFAULT '',
            summary_json        TEXT    NOT NULL DEFAULT ''
        );
        CREATE INDEX IF NOT EXISTS ix_import_batch_committed ON import_batch (committed_at_utc DESC);

        CREATE TABLE IF NOT EXISTS import_member_record (
            batch_id      TEXT    NOT NULL REFERENCES import_batch (id),
            line_number   INTEGER NOT NULL,
            record_index  INTEGER NOT NULL,
            member_id     TEXT    NOT NULL,
            record_type   TEXT    NOT NULL,
            fields_json   TEXT    NOT NULL,
            PRIMARY KEY (batch_id, line_number, record_index)
        );

        CREATE TABLE IF NOT EXISTS import_issue (
            batch_id     TEXT    NOT NULL REFERENCES import_batch (id),
            seq          INTEGER NOT NULL,
            severity     TEXT    NOT NULL,
            line_number  INTEGER NOT NULL,
            member_id    TEXT    NOT NULL,
            field        TEXT    NOT NULL,
            value        TEXT    NOT NULL,
            rule         TEXT    NOT NULL,
            action       TEXT    NOT NULL,
            owner        TEXT    NOT NULL DEFAULT '',
            acknowledged_by         TEXT NULL,
            acknowledged_at_utc     TEXT NULL,
            acknowledgement_note    TEXT NULL,
            PRIMARY KEY (batch_id, seq)
        );

        CREATE TABLE IF NOT EXISTS import_correction (
            batch_id     TEXT    NOT NULL REFERENCES import_batch (id),
            seq          INTEGER NOT NULL,
            line_number  INTEGER NOT NULL,
            member_id    TEXT    NOT NULL,
            "column"     TEXT    NOT NULL,
            before_value TEXT    NOT NULL,
            after_value  TEXT    NOT NULL,
            description  TEXT    NOT NULL,
            PRIMARY KEY (batch_id, seq)
        );

        CREATE TABLE IF NOT EXISTS import_reconciliation (
            batch_id     TEXT NOT NULL REFERENCES import_batch (id),
            measure      TEXT NOT NULL,
            source       TEXT NOT NULL,
            transformed  TEXT NOT NULL,
            rejected     TEXT NOT NULL,
            PRIMARY KEY (batch_id, measure)
        );
        """;

    /// <summary>Columns added after the first release, for a database created by an earlier build (SQLite has no ADD COLUMN IF NOT EXISTS).</summary>
    public const string TableColumns = "SELECT name FROM pragma_table_info($table);";

    public static readonly (string Table, string Column, string Sql)[] AddedColumns =
    [
        ("import_batch", "output_file", "ALTER TABLE import_batch ADD COLUMN output_file TEXT NOT NULL DEFAULT '';"),
        ("import_batch", "summary_json", "ALTER TABLE import_batch ADD COLUMN summary_json TEXT NOT NULL DEFAULT '';"),
        ("import_issue", "owner", "ALTER TABLE import_issue ADD COLUMN owner TEXT NOT NULL DEFAULT '';"),
        ("import_issue", "acknowledged_by", "ALTER TABLE import_issue ADD COLUMN acknowledged_by TEXT NULL;"),
        ("import_issue", "acknowledged_at_utc", "ALTER TABLE import_issue ADD COLUMN acknowledged_at_utc TEXT NULL;"),
        ("import_issue", "acknowledgement_note", "ALTER TABLE import_issue ADD COLUMN acknowledgement_note TEXT NULL;"),
    ];

    /// <summary>Run first in the commit transaction, so a retried commit replaces the earlier save.</summary>
    public static readonly string[] DeleteBatch =
    [
        "DELETE FROM import_reconciliation WHERE batch_id = $id;",
        "DELETE FROM import_issue WHERE batch_id = $id;",
        "DELETE FROM import_correction WHERE batch_id = $id;",
        "DELETE FROM import_member_record WHERE batch_id = $id;",
        "DELETE FROM import_batch WHERE id = $id;",
    ];

    public const string InsertBatch = """
        INSERT INTO import_batch (id, source, source_name, schema_version, mapping_name, mapping_version, file_name, file_sha256,
            uploaded_by, uploaded_at_utc, committed_by, committed_at_utc, lines_read, accepted, rejected, warnings, balanced,
            output_file_name, output_file_sha256, report_html, output_file, summary_json)
        VALUES ($id, $source, $source_name, $schema_version, $mapping_name, $mapping_version, $file_name, $file_sha256,
            $uploaded_by, $uploaded_at_utc, $committed_by, $committed_at_utc, $lines_read, $accepted, $rejected, $warnings, $balanced,
            $output_file_name, $output_file_sha256, $report_html, $output_file, $summary_json);
        """;

    public const string InsertRecord = """
        INSERT INTO import_member_record (batch_id, line_number, record_index, member_id, record_type, fields_json)
        VALUES ($id, $line_number, $record_index, $member_id, $record_type, $fields_json);
        """;

    public const string InsertIssue = """
        INSERT INTO import_issue (batch_id, seq, severity, line_number, member_id, field, value, rule, action, owner,
            acknowledged_by, acknowledged_at_utc, acknowledgement_note)
        VALUES ($id, $seq, $severity, $line_number, $member_id, $field, $value, $rule, $action, $owner,
            $acknowledged_by, $acknowledged_at_utc, $acknowledgement_note);
        """;

    public const string InsertCorrection = """
        INSERT INTO import_correction (batch_id, seq, line_number, member_id, "column", before_value, after_value, description)
        VALUES ($id, $seq, $line_number, $member_id, $column, $before_value, $after_value, $description);
        """;

    public const string InsertReconciliation = """
        INSERT INTO import_reconciliation (batch_id, measure, source, transformed, rejected)
        VALUES ($id, $measure, $source, $transformed, $rejected);
        """;

    // ---- Read side (committed batches are only ever read after commit) ----

    /// <summary>Search matches batch ID, file, source or who committed. $like is '%text%' with \ escaping; LIKE ignores case for A-Z.</summary>
    private const string ListWhere = """
        WHERE $search = ''
           OR id LIKE $like ESCAPE '\' OR file_name LIKE $like ESCAPE '\'
           OR source_name LIKE $like ESCAPE '\' OR committed_by LIKE $like ESCAPE '\'
        """;

    public const string ListCount = "SELECT COUNT(*) FROM import_batch\n" + ListWhere + ";";

    public const string ListPage = """
        SELECT id, source, source_name, schema_version, file_name, committed_by, committed_at_utc, accepted, rejected, output_file_name
        FROM import_batch
        """ + "\n" + ListWhere + "\n" + """
        ORDER BY committed_at_utc DESC, id
        LIMIT $take OFFSET $skip;
        """;

    public const string Find = """
        SELECT id, source, source_name, schema_version, mapping_name, mapping_version, file_name, file_sha256,
               uploaded_by, uploaded_at_utc, committed_by, committed_at_utc, output_file_name, output_file_sha256, summary_json,
               lines_read, accepted, rejected, warnings, balanced
        FROM import_batch WHERE id = $id;
        """;

    /// <summary>One row per member (its first record); filters on warnings and on member ID or any field value.</summary>
    private const string MemberWhere = """
        WHERE r.batch_id = $id AND r.record_index = 0
          AND ($warnings_only = 0 OR EXISTS (SELECT 1 FROM import_issue i WHERE i.batch_id = r.batch_id AND i.line_number = r.line_number AND i.severity = 'Warning'))
          AND ($search = '' OR r.member_id LIKE $like ESCAPE '\'
               OR EXISTS (SELECT 1 FROM import_member_record x WHERE x.batch_id = r.batch_id AND x.line_number = r.line_number AND x.fields_json LIKE $like ESCAPE '\'))
        """;

    public const string MemberCount = "SELECT COUNT(*) FROM import_member_record r\n" + MemberWhere + ";";

    public const string MemberPage = """
        SELECT r.line_number, r.member_id,
               EXISTS (SELECT 1 FROM import_issue i WHERE i.batch_id = r.batch_id AND i.line_number = r.line_number AND i.severity = 'Warning')
        FROM import_member_record r
        """ + "\n" + MemberWhere + "\n" + """
        ORDER BY r.line_number
        LIMIT $take OFFSET $skip;
        """;

    public const string MemberRecords = """
        SELECT line_number, record_type, fields_json
        FROM import_member_record
        WHERE batch_id = $id AND line_number BETWEEN $first AND $last
        ORDER BY line_number, record_index;
        """;

    public const string Issues = """
        SELECT seq, severity, line_number, member_id, field, value, rule, action, owner
        FROM import_issue WHERE batch_id = $id ORDER BY seq;
        """;

    public const string Acknowledgements = """
        SELECT seq, acknowledged_by, acknowledged_at_utc, acknowledgement_note
        FROM import_issue WHERE batch_id = $id AND acknowledged_by IS NOT NULL ORDER BY seq;
        """;

    public const string Corrections = """
        SELECT line_number, member_id, "column", before_value, after_value, description
        FROM import_correction WHERE batch_id = $id ORDER BY seq;
        """;

    public const string Report = "SELECT report_html FROM import_batch WHERE id = $id;";

    public const string OutputFile = "SELECT output_file FROM import_batch WHERE id = $id;";
}
