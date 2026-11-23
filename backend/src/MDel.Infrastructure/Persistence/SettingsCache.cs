using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Infrastructure.Persistence;

/// <summary>Citire cu cache (60s) a setărilor non-secrete din AppSettings.</summary>
public sealed class SettingsCache(IServiceScopeFactory scopeFactory, IMemoryCache cache)
{
    private const string CacheKey = "mdel.settings";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        return all.GetValueOrDefault(key);
    }

    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<IReadOnlyDictionary<string, string?>> GetAllAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, string?>? cached) && cached is not null)
        {
            return cached;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MDelDbContext>();
        var values = await db.AppSettings.AsNoTracking()
            .Where(s => !s.IsSecret)
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        cache.Set(CacheKey, (IReadOnlyDictionary<string, string?>)values, Ttl);
        return values;
    }
}
