using System.Text;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Imports;
using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Infrastructure.Persistence;
using static IntellipenImport.Tests.TestFiles;

namespace IntellipenImport.Tests;

/// <summary>
/// The read-only view of committed batches: it must show exactly what was committed, and export the same files.
/// Uses the in-memory repository; the SQLite repository runs the same read-side contract against real SQL.
/// </summary>
public class CommittedViewTests
{
    private const string Report = "<html><body>Saved reconciliation report</body></html>";

    private sealed class Mappings : ITargetMappingCatalog
    {
        public TargetMapping Current => Mapping;
    }

    private static async Task<(CommittedQueries Queries, HeldBatch Batch, string FileText)> CommitAshwoodAsync(string id = "IMP-20261103-READ01")
    {
        var result = Run(Sample("ashwood-payroll_v1_sample.csv"), Ashwood, new Application.Processing.ImportContext(id, new DateOnly(2026, 11, 3)));
        var at = new DateTimeOffset(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);
        var batch = new HeldBatch
        {
            Id = id, Source = "ashwood-payroll", SourceName = "Ashwood payroll", SchemaVersion = "v1", MappingName = Mapping.Name,
            MappingVersion = Mapping.Version, FileName = "ashwood-payroll_v1_sample.csv", FileSha256 = "abc", UploadedBy = "uploader",
            UploadedAtUtc = at.AddMinutes(-10), ExpiresAtUtc = at.AddHours(24), Result = result,
        };
        var record = new BatchRecord { Id = id, Status = BatchStatus.ReadyForReview, UploadedBy = "uploader" };
        record.MarkCommitted("committer", at, new CommitOutcome { OutputFileSha256 = "def" });
        var fileText = string.Join("\r\n", result.OutputLines) + "\r\n";

        var repository = new InMemoryBatchRepository();
        await repository.SaveCommittedAsync(CommittedSnapshot.From(batch, record), batch, Report, fileText, CancellationToken.None);
        return (new CommittedQueries(repository, new Mappings(), new TestApp.User("committer", committer: true, reveal: false), new TestApp.AuditLog()), batch, fileText);
    }

    [Fact]
    public async Task Summary_shows_what_was_committed_and_can_never_be_committed_again()
    {
        var (queries, batch, _) = await CommitAshwoodAsync();
        var s = await queries.GetAsync(batch.Id, CancellationToken.None);

        Assert.Equal(BatchStatus.Committed, s.Status);
        Assert.False(s.CanCommit);
        Assert.Equal((31, 26, 5, 10), (s.Counts!.LinesRead, s.Counts.Accepted, s.Counts.Rejected, s.Counts.Warnings));
        Assert.True(s.Balanced);
        Assert.Equal(batch.Result.Reconciliation.Select(l => (l.Measure, l.Source, l.Transformed, l.Rejected)),
            s.Reconciliation.Select(l => (l.Measure, l.Source, l.Transformed, l.Rejected)));
        Assert.Equal(("committer", 26, "def"), (s.ClosedBy!, s.Commit!.MembersSaved, s.Commit.OutputFileSha256));
    }

    [Fact]
    public async Task Rows_are_the_saved_members_with_their_records()
    {
        var (queries, batch, _) = await CommitAshwoodAsync();
        var all = await queries.RowsAsync(batch.Id, 1, 200, null, null, CancellationToken.None);
        var warnings = await queries.RowsAsync(batch.Id, 1, 200, null, "warnings", CancellationToken.None);
        var one = await queries.RowsAsync(batch.Id, 1, 200, "DB001", null, CancellationToken.None);

        Assert.Equal(26, all.Total);
        Assert.All(all.Items, m => Assert.True(m.Accepted));
        Assert.Equal(batch.Result.WarningLines, warnings.Total);
        var db001 = Assert.Single(one.Items);
        Assert.Equal(batch.Result.Members.Single(m => m.Key == "DB001").Records.Count, db001.Records.Count);
        Assert.Empty(db001.Source); // the raw source line is not kept after commit
    }

    [Fact]
    public async Task Exports_are_exactly_what_was_saved()
    {
        var (queries, batch, fileText) = await CommitAshwoodAsync();

        var file = await queries.FileDownloadAsync(batch.Id, CancellationToken.None);
        Assert.Equal(fileText, Encoding.UTF8.GetString(file.Content));
        Assert.Equal(batch.Result.OutputFileName, file.FileName);

        var preview = await queries.FilePreviewAsync(batch.Id, 5, CancellationToken.None);
        Assert.Equal(batch.Result.OutputLines.Count, preview.TotalLines);
        Assert.Equal(batch.Result.MaskedOutputLines.Take(5), preview.Lines); // the preview is the masked file, never the real lines
        Assert.NotEqual(batch.Result.OutputLines.Take(5), preview.Lines);

        Assert.Equal(Report, await queries.ReportAsync(batch.Id, CancellationToken.None));
        var reportFile = await queries.ReportDownloadAsync(batch.Id, CancellationToken.None);
        Assert.Equal($"{batch.Id}_reconciliation-report.html", reportFile.FileName);

        var issues = await queries.IssuesAsync(batch.Id, CancellationToken.None);
        Assert.Equal(batch.Result.Issues.Count, issues.Count);
        var csv = Encoding.UTF8.GetString((await queries.IssuesCsvAsync(batch.Id, CancellationToken.None)).Content);
        Assert.StartsWith("﻿Severity,Line,Member", csv);
    }

    [Fact]
    public async Task List_searches_and_pages()
    {
        var (queries, batch, _) = await CommitAshwoodAsync();
        Assert.Equal(1, (await queries.ListAsync("ashwood", 1, 20, CancellationToken.None)).Total);
        Assert.Equal(0, (await queries.ListAsync("nothing-like-this", 1, 20, CancellationToken.None)).Total);
        var page = await queries.ListAsync(null, 1, 20, CancellationToken.None);
        Assert.Equal((batch.Id, "Ashwood payroll", 26), (page.Items[0].Id, page.Items[0].SourceName, page.Items[0].Accepted));
    }

    [Fact]
    public async Task An_unknown_batch_is_not_found()
    {
        var (queries, _, _) = await CommitAshwoodAsync();
        var problem = await Assert.ThrowsAsync<ImportProblem>(() => queries.GetAsync("IMP-NOPE", CancellationToken.None));
        Assert.Equal(ProblemKind.NotFound, problem.Kind);
    }
}
