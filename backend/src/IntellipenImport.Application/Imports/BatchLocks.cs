using System.Collections.Concurrent;

namespace IntellipenImport.Application.Imports;

/// <summary>One lock per batch, shared by commit and acknowledgements, so a check can't slip in while a commit is running.</summary>
internal static class BatchLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static SemaphoreSlim For(string id) => Locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
}
