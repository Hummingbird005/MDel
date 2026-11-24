namespace MDel.Application.Abstractions;

/// <summary>Setare așa cum apare în ecranul de administrare (secretele nu se expun niciodată).</summary>
public sealed record SettingView(
    string Key,
    string Category,
    string? Value,
    bool IsSecret,
    bool HasValue,
    DateTime UpdatedUtc,
    string? UpdatedBy);

/// <summary>
/// Acces la setările din AppSettings. Valorile non-secrete sunt cache-uite;
/// cele secrete sunt criptate cu ASP.NET Core Data Protection.
/// </summary>
public interface ISettingsService
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<SettingView>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Actualizează setările. Pentru secrete: null = neschimbat, "" = șters.
    /// Validează și auditează modificările. Aruncă <see cref="SettingsValidationException"/>.
    /// </summary>
    Task UpdateAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);

    /// <summary>
    /// Criptează valorile secrete rămase necriptate în baza de date (ex. utilizatori salvați înainte ca
    /// setarea să devină secretă). Idempotent; returnează numărul de valori criptate.
    /// </summary>
    Task<int> EncryptPlainSecretsAsync(CancellationToken ct = default);
}

public sealed class SettingsValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more settings are invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public static class SettingsServiceExtensions
{
    public static async Task<int> GetIntAsync(this ISettingsService s, string key, int fallback, CancellationToken ct = default) =>
        int.TryParse(await s.GetAsync(key, ct), out var v) ? v : fallback;

    public static async Task<bool> GetBoolAsync(this ISettingsService s, string key, bool fallback, CancellationToken ct = default) =>
        bool.TryParse(await s.GetAsync(key, ct), out var v) ? v : fallback;
}
