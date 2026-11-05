/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
namespace Bookmarkarr.Application.Downloads.Common;

/// <summary>Coalesces short-lived identical reads across independently scoped gateways.</summary>
public sealed class ClientQueueRequestCache(IMemoryCache cache)
{
    internal bool Enabled { get; set; } = true;
    // A fixed stripe set bounds memory even if clients or tracked hashes change over time.
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1)).ToArray();
    public async Task<List<QueueItem>> GetAsync(DownloadClientConfiguration client, IEnumerable<string>? ids,
        Func<Task<List<QueueItem>>> fetch, CancellationToken ct)
    {
        if (!Enabled) return await fetch();
        var identity = $"{client.Id}|{client.Type}|{client.Host}|{client.Port}|{client.UseSSL}|{client.Username}|{client.Password}|{client.SettingsJson}|{string.Join(',', (ids ?? []).Order(StringComparer.OrdinalIgnoreCase))}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var key = "client-queue-request:" + Volatile.Read(ref generation) + ":" + Convert.ToHexString(digest);
        var gate = gates[digest[0] % gates.Length];
        await gate.WaitAsync(ct);
        try
        {
            if (!cache.TryGetValue(key, out List<QueueItem>? items))
            {
                items = await fetch();
                cache.Set(key, DownloadQueueSnapshotMapper.CloneQueueItems(items), TimeSpan.FromSeconds(10));
            }
            return DownloadQueueSnapshotMapper.CloneQueueItems(items);
        }
        finally { gate.Release(); }
    }
    public void Invalidate() => Interlocked.Increment(ref generation);
    private int generation;
}
