using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Imports;
using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Domain.Schemas;
using IntellipenImport.Infrastructure.Persistence;
using static IntellipenImport.Tests.TestFiles;

namespace IntellipenImport.Tests;

/// <summary>The use cases wired to in-memory fakes, for tests of upload, review, commit and the committed view as one user.</summary>
internal sealed class TestApp
{
    public static readonly DateTimeOffset At = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    public required ImportCommands Commands { get; init; }
    public required ImportQueries Queries { get; init; }
    public required CommittedQueries Committed { get; init; }
    public required AuditLog Audit { get; init; }
    public string Id { get; set; } = string.Empty;

    public sealed class AuditLog : IAuditLog
    {
        public List<string> Lines { get; } = [];
        public void Record(string action, string batchId, string user, string detail = "") => Lines.Add($"{action}|{batchId}|{user}|{detail}");
        public void Failure(string action, string batchId, Exception error) { }
    }

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

    internal sealed class User(string id, bool committer, bool reveal) : ICurrentUser
    {
        public string UserId => id;
        public bool IsCommitter => committer;
        public bool CanReveal => committer || reveal;
    }

    private sealed class Files : IIntellipenFileTarget { public Task<string> WriteAsync(string n, byte[] c, CancellationToken ct) => Task.FromResult("test"); }
    private sealed class Reports : IReportSender { public Task<string> SendAsync(string b, string s, string h, CancellationToken ct) => Task.FromResult("test"); }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    /// <summary>A committer by default (as in Development); pass committer: false, reveal: true for an uploader who may reveal.</summary>
    public static TestApp Build(bool requireAcknowledged = true, string user = "ann", bool committer = true, bool reveal = false)
    {
        var store = new Store();
        var options = new ImportOptions { RequireWarningsAcknowledged = requireAcknowledged };
        var clock = new FixedClock(At);
        var audit = new AuditLog();
        var currentUser = new User(user, committer, reveal);
        var access = new BatchAccess(store, new Mappings(), currentUser, options, clock, audit);
        var repository = new InMemoryBatchRepository();
        return new TestApp
        {
            Commands = new ImportCommands(new Schemas(), new Mappings(), store, repository, new Files(), new Reports(), access, currentUser, options, clock, audit),
            Queries = new ImportQueries(new Schemas(), access, currentUser, audit),
            Committed = new CommittedQueries(repository, new Mappings(), currentUser, audit),
            Audit = audit,
        };
    }
}
