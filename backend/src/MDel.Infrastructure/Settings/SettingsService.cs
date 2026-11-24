using MDel.Application.Abstractions;
using MDel.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace MDel.Infrastructure.Settings;

/// <summary>
/// Implementare singleton: citește prin scope-uri proprii, cache 60s pentru valorile non-secrete.
/// Secretele sunt criptate cu Data Protection (cheile trebuie persistate – vezi DataProtection:KeysPath).
/// </summary>
public sealed class SettingsService(
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache,
    IDataProtectionProvider dataProtection,
    SettingsValidator validator) : ISettingsService
{
    private const string CacheKey = "mdel.settings";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private readonly IDataProtector _protector = dataProtection.CreateProtector("MDel.Settings.v1");

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var all = await GetPlainValuesAsync(ct);
        return all.GetValueOrDefault(key);
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MDelDbContext>();
        var setting = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null || string.IsNullOrEmpty(setting.Value)) return null;
        if (!setting.IsSecret) return setting.Value;

        try
        {
            return _protector.Unprotect(setting.Value);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"Secret '{key}' cannot be decrypted (Data Protection keys changed). Re-enter it in Administration.", ex);
        }
    }

    public async Task<IReadOnlyList<SettingView>> GetAllAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MDelDbContext>();
        return await db.AppSettings.AsNoTracking()
            .OrderBy(s => s.Category).ThenBy(s => s.Key)
            .Select(s => new SettingView(
                s.Key,
                s.Category,
                s.IsSecret ? null : s.Value,
                s.IsSecret,
                s.Value != null && s.Value != "",
                s.UpdatedUtc,
                s.UpdatedBy != null ? s.UpdatedBy.DisplayName : null))
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MDelDbContext>();
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var keys = values.Keys.ToList();
        var settings = await db.AppSettings.Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);

        var errors = new Dictionary<string, string[]>();
        foreach (var (key, value) in values)
        {
            if (!settings.ContainsKey(key)) errors[key] = ["Unknown setting."];
            else if (validator.Validate(key, value?.Trim()) is { } error) errors[key] = [error];
        }

        if (errors.Count > 0) throw new SettingsValidationException(errors);

        var oldValues = new Dictionary<string, string?>();
        var newValues = new Dictionary<string, string?>();
        var now = DateTime.UtcNow;

        foreach (var (key, raw) in values)
        {
            var setting = settings[key];
            var value = raw?.Trim();

            if (setting.IsSecret)
            {
                if (value is null) continue; // neschimbat
                setting.Value = value.Length == 0 ? null : _protector.Protect(value);
                oldValues[key] = "***";
                newValues[key] = value.Length == 0 ? "(cleared)" : "***";
            }
            else
            {
                var normalized = string.IsNullOrEmpty(value) ? "" : value;
                if (normalized == (setting.Value ?? "")) continue;
                oldValues[key] = setting.Value;
                newValues[key] = normalized;
                setting.Value = normalized;
            }

            setting.UpdatedUtc = now;
            setting.UpdatedById = currentUser.UserId;
        }

        if (newValues.Count == 0) return;

        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);

        await audit.WriteAsync(new AuditEntry("Settings.Updated", "AppSetting", string.Join(",", newValues.Keys),
            OldValues: oldValues, NewValues: newValues), ct);
    }

    /// <summary>Valorile criptate cu Data Protection încep mereu cu acest prefix (versiunea 0 a formatului).</summary>
    private const string ProtectedPrefix = "CfDJ8";

    public async Task<int> EncryptPlainSecretsAsync(CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MDelDbContext>();
        var plain = await db.AppSettings
            .Where(s => s.IsSecret && s.Value != null && s.Value != "" && !s.Value.StartsWith(ProtectedPrefix))
            .ToListAsync(ct);
        foreach (var setting in plain) setting.Value = _protector.Protect(setting.Value!);

        if (plain.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            cache.Remove(CacheKey);
        }

        return plain.Count;
    }

    private async Task<IReadOnlyDictionary<string, string?>> GetPlainValuesAsync(CancellationToken ct)
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
