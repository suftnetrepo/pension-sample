using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Domain.Batches;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

namespace IntellipenImport.Infrastructure.Storage;

/// <summary>
/// The temporary store: <see cref="IDistributedCache"/>, every entry encrypted with ASP.NET Data Protection.
/// In-memory by default; Redis or SQL Server cache when there is more than one server (one line in DependencyInjection).
/// </summary>
public sealed class DistributedCacheHeldBatchStore(IDistributedCache cache, IDataProtectionProvider protection, ImportOptions options, TimeProvider clock)
    : IHeldBatchStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly IDataProtector _protector = protection.CreateProtector("IntellipenImport.HeldBatchStore.v2");

    /// <summary>Readable and unguessable: IMP-20261102-7K3Q9X (no 0/O or 1/I to misread).</summary>
    public string NewId() =>
        $"IMP-{clock.GetUtcNow():yyyyMMdd}-{new string(RandomNumberGenerator.GetItems<char>("ABCDEFGHJKLMNPQRSTUVWXYZ23456789", 6))}";

    public Task SaveBatchAsync(HeldBatch batch, CancellationToken ct) => Put(BatchKey(batch.Id), batch, batch.ExpiresAtUtc, ct);
    public Task<HeldBatch?> GetBatchAsync(string id, CancellationToken ct) => Get<HeldBatch>(BatchKey(id), ct);
    public Task RemoveBatchAsync(string id, CancellationToken ct) => cache.RemoveAsync(BatchKey(id), ct);

    public Task SaveRecordAsync(BatchRecord record, CancellationToken ct) =>
        Put(RecordKey(record.Id), record, clock.GetUtcNow().AddDays(options.MarkerDays), ct);
    public Task<BatchRecord?> GetRecordAsync(string id, CancellationToken ct) => Get<BatchRecord>(RecordKey(id), ct);

    private async Task Put<T>(string key, T value, DateTimeOffset expires, CancellationToken ct)
    {
        var bytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(value, Json));
        await cache.SetAsync(key, bytes, new DistributedCacheEntryOptions { AbsoluteExpiration = expires }, ct);
    }

    private async Task<T?> Get<T>(string key, CancellationToken ct) where T : class
    {
        var bytes = await cache.GetAsync(key, ct);
        if (bytes is null) return null;
        try { return JsonSerializer.Deserialize<T>(_protector.Unprotect(bytes), Json); }
        catch (CryptographicException) { return null; } // keys changed (e.g. restart without persisted keys): treat as gone
    }

    private static string BatchKey(string id) => "imp:batch:" + id.ToUpperInvariant();
    private static string RecordKey(string id) => "imp:record:" + id.ToUpperInvariant();
}
