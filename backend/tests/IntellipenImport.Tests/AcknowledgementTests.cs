using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Imports;
using IntellipenImport.Application.Processing;
using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Domain.Processing;
using IntellipenImport.Domain.Schemas;
using IntellipenImport.Infrastructure.Persistence;
using static IntellipenImport.Tests.TestFiles;

namespace IntellipenImport.Tests;

/// <summary>Checking warnings: the domain rules, the commit gate, and what survives into the committed view.</summary>
public class AcknowledgementTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private const string PartTimeLine = "DB006,Helen Carter,F,30/04/1966,LM163815B,08/01/1996,30/04/2018,Deferred,48500,50200,52000,0.6,Y"; // one warning
    private const string NoNiLine = "DB022,Matthew Roberts,F,19/04/1961,,01/09/1997,13/01/2012,Deferred,34100,35300,36500,1,N";              // one error

    private static HeldBatch Held() => new() { Id = "IMP-1", Result = Run(WithLines(GoodLine, PartTimeLine, NoNiLine)) };
    private static BatchRecord Open() => new() { Id = "IMP-1", Status = BatchStatus.ReadyForReview };
    private static int WarningId(HeldBatch b) => b.Result.Issues.Single(i => i.Severity == Severity.Warning).Id;
    private static int ErrorId(HeldBatch b) => b.Result.Issues.First(i => i.Severity == Severity.Error).Id;

    // ---- Domain ----

    [Fact]
    public void A_warning_can_be_checked()
    {
        var b = Held();
        Assert.Equal(1, b.WarningsPending);
        b.Acknowledge(Open(), [WarningId(b)], "ann", At, " looks right ");
        var a = Assert.Single(b.Acknowledgements);
        Assert.Equal(("ann", At, "looks right"), (a.By, a.AtUtc, a.Note));
        Assert.Equal(0, b.WarningsPending);
    }

    [Fact]
    public void An_error_cannot_be_checked_and_nothing_changes()
    {
        var b = Held();
        Assert.Throws<BatchRuleException>(() => b.Acknowledge(Open(), [WarningId(b), ErrorId(b)], "ann", At, null));
        Assert.Empty(b.Acknowledgements);
        Assert.Throws<BatchRuleException>(() => b.Acknowledge(Open(), [999], "ann", At, null));
    }

    [Theory]
    [InlineData(BatchStatus.Committed)]
    [InlineData(BatchStatus.Discarded)]
    [InlineData(BatchStatus.Expired)]
    public void Only_a_batch_ready_for_review_can_be_checked_or_unchecked(BatchStatus status)
    {
        var b = Held();
        var closed = new BatchRecord { Id = "IMP-1", Status = status };
        Assert.Throws<BatchRuleException>(() => b.Acknowledge(closed, [WarningId(b)], "ann", At, null));
        Assert.Throws<BatchRuleException>(() => b.Unacknowledge(closed, WarningId(b)));
    }

    [Fact]
    public void Checking_twice_keeps_the_first_check()
    {
        var b = Held();
        b.Acknowledge(Open(), [WarningId(b)], "ann", At, "first");
        b.Acknowledge(Open(), [WarningId(b), WarningId(b)], "bob", At.AddHours(1), "second");
        var a = Assert.Single(b.Acknowledgements);
        Assert.Equal(("ann", "first"), (a.By, a.Note));
    }

    [Fact]
    public void A_check_can_be_undone_before_commit_and_undoing_nothing_is_fine()
    {
        var b = Held();
        b.Acknowledge(Open(), [WarningId(b)], "ann", At, null);
        b.Unacknowledge(Open(), WarningId(b));
        Assert.Empty(b.Acknowledgements);
        Assert.Equal(1, b.WarningsPending);
        b.Unacknowledge(Open(), WarningId(b));
        b.Unacknowledge(Open(), 12345);
    }

    [Fact]
    public void Commit_is_blocked_until_every_warning_is_checked_when_the_rule_is_on()
    {
        var b = Held();
        var ex = Assert.Throws<BatchRuleException>(() => b.EnsureCommittable(requireWarningsAcknowledged: true));
        Assert.Equal("1 warning is still to be checked. Check every warning before committing.", ex.Message);
        b.EnsureCommittable(requireWarningsAcknowledged: false); // the rule is switched off in configuration
        b.Acknowledge(Open(), [WarningId(b)], "ann", At, null);
        b.EnsureCommittable(requireWarningsAcknowledged: true);
    }

    // ---- Application ----

    private sealed class Schemas : ISourceSchemaCatalog
    {
        public IReadOnlyList<SourceSchema> All => AllSchemas;
        public SourceSchema? Find(string source, string version) => AllSchemas.FirstOrDefault(s => s.Source == source && s.Version == version);
    }

    private sealed class Mappings : ITargetMappingCatalog { public TargetMapping Current => Mapping; }

    private sealed class Store : IHeldBatchStore
    {
        private readonly Dictionary<string, HeldBatch> _batches = [];
        private readonly Dictionary<string, BatchRecord> _records = [];
        public string NewId() => "IMP-20261103-TEST01";
        public Task SaveBatchAsync(HeldBatch batch, CancellationToken ct) { _batches[batch.Id] = batch; return Task.CompletedTask; }
        public Task<HeldBatch?> GetBatchAsync(string id, CancellationToken ct) => Task.FromResult(_batches.GetValueOrDefault(id));
        public Task RemoveBatchAsync(string id, CancellationToken ct) { _batches.Remove(id); return Task.CompletedTask; }
        public Task SaveRecordAsync(BatchRecord record, CancellationToken ct) { _records[record.Id] = record; return Task.CompletedTask; }
        public Task<BatchRecord?> GetRecordAsync(string id, CancellationToken ct) => Task.FromResult(_records.GetValueOrDefault(id));
    }

    private sealed class User(string id) : ICurrentUser { public string UserId => id; public bool IsCommitter => true; }
    private sealed class Files : IIntellipenFileTarget { public Task<string> WriteAsync(string n, byte[] c, CancellationToken ct) => Task.FromResult("test"); }
    private sealed class Reports : IReportSender { public Task<string> SendAsync(string b, string s, string h, CancellationToken ct) => Task.FromResult("test"); }
    private sealed class Audit : IAuditLog
    {
        public List<string> Lines { get; } = [];
        public void Record(string action, string batchId, string user, string detail = "") => Lines.Add($"{action}|{batchId}|{user}|{detail}");
        public void Failure(string action, string batchId, Exception error) { }
    }

    private sealed class App
    {
        public required ImportCommands Commands { get; init; }
        public required ImportQueries Queries { get; init; }
        public required CommittedQueries Committed { get; init; }
        public required Audit Audit { get; init; }
        public string Id { get; set; } = string.Empty;
    }

    private static App Build(bool requireAcknowledged = true, string user = "ann")
    {
        var store = new Store();
        var options = new ImportOptions { RequireWarningsAcknowledged = requireAcknowledged };
        var clock = new FixedClock(At);
        var audit = new Audit();
        var currentUser = new User(user);
        var access = new BatchAccess(store, new Mappings(), currentUser, options, clock, audit);
        var repository = new InMemoryBatchRepository();
        return new App
        {
            Commands = new ImportCommands(new Schemas(), new Mappings(), store, repository, new Files(), new Reports(), access, currentUser, options, clock, audit),
            Queries = new ImportQueries(new Schemas(), access),
            Committed = new CommittedQueries(repository, new Mappings()),
            Audit = audit,
        };
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private static async Task<App> UploadAsync(App app)
    {
        var csv = WithLines(GoodLine, PartTimeLine.Replace("52000", "\"£52,000\"", StringComparison.Ordinal), "DB023,Laura Jones,F,22/12/1961,gh 60 60 73 a,01/04/2006,,active,67200,69600,72000,1,Y");
        await using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var summary = await app.Commands.UploadAsync(new UploadCommand("members.csv", content.Length, content, "ashwood-payroll", "v1"), default);
        app.Id = summary.Id;
        return app;
    }

    [Fact]
    public async Task Acknowledge_then_commit_succeeds_and_the_committed_view_shows_who_checked()
    {
        var app = await UploadAsync(Build());
        var before = await app.Queries.GetAsync(app.Id, default);
        Assert.Equal((1, false, 1, 3), (before.WarningsPending, before.CanCommit, before.Counts!.Warnings, before.Counts.Corrections));

        var issues = await app.Queries.IssuesAsync(app.Id, default);
        var warning = Assert.Single(issues, i => i.Severity == "Warning");
        Assert.Equal(("Employer", null), (warning.Owner, warning.Checked));

        var after = await app.Commands.AcknowledgeAsync(app.Id, [warning.Id], "part-time dates confirmed", default);
        Assert.Equal((0, true, 1), (after.WarningsPending, after.CanCommit, after.Counts!.WarningsChecked));
        await app.Commands.AcknowledgeAsync(app.Id, [warning.Id], null, default); // repeated: no change

        var committed = await app.Commands.CommitAsync(app.Id, reviewed: true, default);
        Assert.Equal(BatchStatus.Committed, committed.Status);

        var saved = await app.Committed.IssuesAsync(app.Id, default);
        var savedWarning = Assert.Single(saved, i => i.Severity == "Warning");
        Assert.Equal(("ann", "part-time dates confirmed", At), (savedWarning.Checked!.By, savedWarning.Checked.Note, savedWarning.Checked.AtUtc));
        Assert.Equal(warning.Id, savedWarning.Id);

        var corrections = await app.Committed.CorrectionsAsync(app.Id, default);
        Assert.Equal(3, corrections.Count);
        Assert.Contains(corrections, c => c.Column == "NI number" && c.Before == "gh60****a" && c.After == "GH60****A");
        Assert.DoesNotContain(corrections, c => (c.Before + c.After).Contains("606073", StringComparison.Ordinal));
        var summary = await app.Committed.GetAsync(app.Id, default);
        Assert.Equal((1, 3), (summary.Counts!.WarningsChecked, summary.Counts.Corrections));

        var csv = System.Text.Encoding.UTF8.GetString((await app.Committed.IssuesCsvAsync(app.Id, default)).Content);
        Assert.Contains("ann", csv);
        Assert.Contains("Who acts", csv);
        Assert.StartsWith("﻿Line,Member,Column,Before,After", System.Text.Encoding.UTF8.GetString((await app.Committed.CorrectionsCsvAsync(app.Id, default)).Content));
        Assert.Contains(app.Audit.Lines, l => l.StartsWith("warnings-checked|" + app.Id + "|ann|issues ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Commit_with_a_warning_still_to_check_is_refused_with_a_clear_message()
    {
        var app = await UploadAsync(Build());
        var problem = await Assert.ThrowsAsync<ImportProblem>(() => app.Commands.CommitAsync(app.Id, reviewed: true, default));
        Assert.Equal(ProblemKind.Conflict, problem.Kind);
        Assert.Contains("1 warning is still to be checked", problem.Message);
    }

    [Fact]
    public async Task With_the_rule_switched_off_commit_needs_no_checks()
    {
        var app = await UploadAsync(Build(requireAcknowledged: false));
        Assert.True((await app.Queries.GetAsync(app.Id, default)).CanCommit);
        Assert.Equal(BatchStatus.Committed, (await app.Commands.CommitAsync(app.Id, reviewed: true, default)).Status);
    }

    [Fact]
    public async Task An_error_cannot_be_checked_and_a_committed_batch_cannot_be_changed()
    {
        var app = await UploadAsync(Build());
        var warning = (await app.Queries.IssuesAsync(app.Id, default)).Single(i => i.Severity == "Warning");
        await app.Commands.AcknowledgeAsync(app.Id, [warning.Id], null, default);
        await app.Commands.CommitAsync(app.Id, reviewed: true, default);

        var gone = await Assert.ThrowsAsync<ImportProblem>(() => app.Commands.UnacknowledgeAsync(app.Id, warning.Id, default));
        Assert.Equal(ProblemKind.Gone, gone.Kind);
        gone = await Assert.ThrowsAsync<ImportProblem>(() => app.Commands.AcknowledgeAsync(app.Id, [warning.Id], null, default));
        Assert.Equal(ProblemKind.Gone, gone.Kind);
    }

    [Fact]
    public async Task Undo_puts_the_warning_back_to_pending()
    {
        var app = await UploadAsync(Build());
        var warning = (await app.Queries.IssuesAsync(app.Id, default)).Single(i => i.Severity == "Warning");
        await app.Commands.AcknowledgeAsync(app.Id, [warning.Id], null, default);
        var summary = await app.Commands.UnacknowledgeAsync(app.Id, warning.Id, default);
        Assert.Equal((1, false), (summary.WarningsPending, summary.CanCommit));
        Assert.Null((await app.Queries.IssuesAsync(app.Id, default)).Single(i => i.Severity == "Warning").Checked);
    }

    [Fact]
    public async Task The_report_says_who_checked_each_warning_and_lists_the_corrections()
    {
        var app = await UploadAsync(Build());
        var warning = (await app.Queries.IssuesAsync(app.Id, default)).Single(i => i.Severity == "Warning");
        var notChecked = await app.Queries.ReportAsync(app.Id, default);
        Assert.Contains("Not checked", notChecked);
        Assert.Contains("Warnings checked</th><td>0 of 1", notChecked);

        await app.Commands.AcknowledgeAsync(app.Id, [warning.Id], "ok", default);
        var report = await app.Queries.ReportAsync(app.Id, default);
        Assert.Contains("Checked by ann at 03/11/2026 10:00 UTC: ok", report);
        Assert.Contains("Warnings checked</th><td>1 of 1", report);
        Assert.Contains("Automatic corrections (3)", report);
        Assert.DoesNotContain("606073", report);
    }
}
