using System.Collections.Concurrent;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Domain.Batches;
using IntellipenImport.Domain.Processing;

namespace IntellipenImport.Infrastructure.Persistence;

/// <summary>For demos and tests (Infrastructure:Persistence = "InMemory"): committed batches are lost when the API stops.</summary>
public sealed class InMemoryBatchRepository : IBatchRepository, ICommittedBatchReader
{
    private sealed record Saved(CommittedSnapshot Snapshot, List<CommittedMember> Members, List<ImportIssue> Issues, string Report, string OutputFile);

    private readonly ConcurrentDictionary<string, Saved> _batches = new(StringComparer.OrdinalIgnoreCase);

    public Task SaveCommittedAsync(CommittedSnapshot snapshot, HeldBatch batch, string reportHtml, string outputFile, CancellationToken ct)
    {
        _batches[snapshot.Id] = new Saved(snapshot, CommittedSnapshot.Members(batch).ToList(), [.. batch.Result.Issues], reportHtml, outputFile);
        return Task.CompletedTask;
    }

    public Task<Page<CommittedBatch>> ListAsync(string? search, int page, int pageSize, CancellationToken ct)
    {
        var s = search?.Trim() ?? string.Empty;
        var matches = _batches.Values.Select(b => b.Snapshot)
            .Where(b => s.Length == 0 || new[] { b.Id, b.FileName, b.SourceName, b.CommittedBy }.Any(v => v.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(b => b.CommittedAtUtc).ThenBy(b => b.Id)
            .ToList();
        var items = matches.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new CommittedBatch(b.Id, b.Source, b.SourceName, b.SchemaVersion, b.FileName, b.CommittedBy, b.CommittedAtUtc,
                b.Figures.Accepted, b.Figures.Rejected, b.OutputFileName))
            .ToList();
        return Task.FromResult(new Page<CommittedBatch>(items, matches.Count));
    }

    public Task<CommittedSnapshot?> FindAsync(string id, CancellationToken ct) => Task.FromResult(_batches.GetValueOrDefault(id)?.Snapshot);

    public Task<Page<CommittedMember>> MembersAsync(string id, string? search, bool warningsOnly, int page, int pageSize, CancellationToken ct)
    {
        if (!_batches.TryGetValue(id, out var saved)) return Task.FromResult(new Page<CommittedMember>([], 0));
        var s = search?.Trim() ?? string.Empty;
        var matches = saved.Members
            .Where(m => !warningsOnly || m.HasWarnings)
            .Where(m => s.Length == 0 || m.Key.Contains(s, StringComparison.OrdinalIgnoreCase)
                        || m.Records.Any(r => r.Fields.Any(f => f.Value.Contains(s, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        return Task.FromResult(new Page<CommittedMember>(matches.Skip((page - 1) * pageSize).Take(pageSize).ToList(), matches.Count));
    }

    public Task<IReadOnlyList<ImportIssue>> IssuesAsync(string id, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ImportIssue>>(_batches.GetValueOrDefault(id)?.Issues ?? []);

    public Task<string?> ReportAsync(string id, CancellationToken ct) => Task.FromResult(_batches.GetValueOrDefault(id)?.Report);

    public Task<string?> OutputFileAsync(string id, CancellationToken ct) => Task.FromResult(_batches.GetValueOrDefault(id)?.OutputFile);
}
