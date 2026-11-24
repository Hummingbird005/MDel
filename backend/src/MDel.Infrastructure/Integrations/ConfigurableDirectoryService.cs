using MDel.Application.Abstractions;
using MDel.Domain;

namespace MDel.Infrastructure.Integrations;

/// <summary>
/// Doar în Development cu <c>Integrations:UseFakes</c>: AD-ul e simulat cât timp în Setări → Active Directory nu e
/// completat serverul sau domeniul; imediat ce e completat, se folosește AD-ul real (fără repornire).
/// În producție se folosește direct <see cref="LdapDirectoryService"/>.
/// </summary>
public sealed class ConfigurableDirectoryService(
    ISettingsService settings,
    FakeDirectoryService fake,
    LdapDirectoryService ldap) : IDirectoryService
{
    public async Task<IReadOnlyList<DirectoryUser>> SearchUsersAsync(string term, int maxResults, CancellationToken ct) =>
        await (await CurrentAsync(ct)).SearchUsersAsync(term, maxResults, ct);

    public async Task<DirectoryUser?> FindBySamAccountNameAsync(string samAccountName, CancellationToken ct) =>
        await (await CurrentAsync(ct)).FindBySamAccountNameAsync(samAccountName, ct);

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        if (await IsConfiguredAsync(ct)) return await ldap.TestConnectionAsync(ct);

        var result = await fake.TestConnectionAsync(ct);
        return result with { Message = $"{result.Message} (simulated AD: fill in the server or the domain to use the real one)" };
    }

    private async Task<IDirectoryService> CurrentAsync(CancellationToken ct) =>
        await IsConfiguredAsync(ct) ? ldap : fake;

    private async Task<bool> IsConfiguredAsync(CancellationToken ct) =>
        !string.IsNullOrWhiteSpace(await settings.GetAsync(SettingKeys.AdLdapServer, ct)) ||
        !string.IsNullOrWhiteSpace(await settings.GetAsync(SettingKeys.AdDomain, ct));
}
