using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using MDel.Application.Abstractions;
using MDel.Domain;
using Microsoft.Extensions.Logging;

namespace MDel.Infrastructure.Integrations;

/// <summary>
/// Acces la Active Directory prin LDAP (System.DirectoryServices.Protocols).
/// Conexiunea se configurează din administrare: server, Base DN, cont de serviciu.
/// Dacă nu e setat un cont de serviciu, se folosește identitatea procesului (app pool) prin Negotiate.
/// </summary>
public sealed class LdapDirectoryService(ISettingsService settings, ILogger<LdapDirectoryService> logger) : IDirectoryService
{
    private static readonly string[] UserAttributes =
        ["objectGUID", "sAMAccountName", "displayName", "mail", "msExchRecipientTypeDetails", "homeMDB"];

    // Utilizatori activi (exclude conturile dezactivate: bitul 2 din userAccountControl).
    private const string ActiveUserFilter =
        "(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<IReadOnlyList<DirectoryUser>> SearchUsersAsync(string term, int maxResults, CancellationToken ct)
    {
        term = term.Trim();
        if (term.Length < 2) return [];

        var t = EscapeFilterValue(term);
        var filter = $"(&{ActiveUserFilter}(|(sAMAccountName={t}*)(displayName=*{t}*)(mail={t}*)(givenName={t}*)(sn={t}*)))";
        return await SearchAsync(filter, Math.Clamp(maxResults, 1, 100), ct);
    }

    public async Task<DirectoryUser?> FindBySamAccountNameAsync(string samAccountName, CancellationToken ct)
    {
        var sam = samAccountName.Contains('\\') ? samAccountName[(samAccountName.IndexOf('\\') + 1)..] : samAccountName;
        var filter = $"(&(objectCategory=person)(objectClass=user)(sAMAccountName={EscapeFilterValue(sam)}))";
        var result = await SearchAsync(filter, 1, ct);
        return result.FirstOrDefault();
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var (connection, baseDn) = await ConnectAsync(ct);
            using (connection)
            {
                var request = new SearchRequest(baseDn, "(objectClass=*)", SearchScope.Base, "distinguishedName");
                var response = (SearchResponse)await SendAsync(connection, request);
                return new ConnectionTestResult(response.Entries.Count == 1,
                    $"Connected to {connection.Directory} – base DN '{baseDn}' is reachable.");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AD connection test failed");
            return new ConnectionTestResult(false, $"AD connection failed: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<DirectoryUser>> SearchAsync(string filter, int sizeLimit, CancellationToken ct)
    {
        var (connection, baseDn) = await ConnectAsync(ct);
        using (connection)
        {
            var request = new SearchRequest(baseDn, filter, SearchScope.Subtree, UserAttributes) { SizeLimit = sizeLimit };
            SearchResponse response;
            try
            {
                response = (SearchResponse)await SendAsync(connection, request);
            }
            catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.SizeLimitExceeded)
            {
                response = (SearchResponse)ex.Response;
            }

            return response.Entries.Cast<SearchResultEntry>().Select(Map).OrderBy(u => u.DisplayName).ToList();
        }
    }

    private async Task<(LdapConnection Connection, string BaseDn)> ConnectAsync(CancellationToken ct)
    {
        var server = await settings.GetAsync(SettingKeys.AdLdapServer, ct);
        var domain = await settings.GetAsync(SettingKeys.AdDomain, ct);
        var baseDn = await settings.GetAsync(SettingKeys.AdBaseDn, ct);
        var user = await settings.GetAsync(SettingKeys.AdServiceUser, ct);

        // Fără server explicit, se folosește domeniul (localizare DC prin DNS).
        var host = string.IsNullOrWhiteSpace(server) ? domain : server;
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("AD is not configured: set the LDAP server or the domain in Administration.");
        if (string.IsNullOrWhiteSpace(baseDn))
            baseDn = DomainToBaseDn(domain ?? host);

        // "ldaps://dc01:636" sau "dc01" / "dc01:389"
        var useSsl = host.StartsWith("ldaps://", StringComparison.OrdinalIgnoreCase);
        var hostPart = host.Replace("ldaps://", "", StringComparison.OrdinalIgnoreCase)
                           .Replace("ldap://", "", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
        var port = useSsl ? 636 : 389;
        if (hostPart.LastIndexOf(':') is var idx and > 0 && int.TryParse(hostPart[(idx + 1)..], out var p))
        {
            port = p;
            hostPart = hostPart[..idx];
        }

        var connection = new LdapConnection(new LdapDirectoryIdentifier(hostPart, port))
        {
            AuthType = AuthType.Negotiate,
            Timeout = Timeout,
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        if (useSsl) connection.SessionOptions.SecureSocketLayer = true;
        else
        {
            // Kerberos sign & seal: credențialele și datele nu circulă în clar nici pe portul 389.
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
        }

        if (!string.IsNullOrWhiteSpace(user))
        {
            var password = await settings.GetSecretAsync(SettingKeys.AdServicePassword, ct);
            var (u, d) = SplitUser(user, domain);
            connection.Credential = new NetworkCredential(u, password, d);
        }

        try
        {
            connection.Bind();
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return (connection, baseDn);
    }

    private static Task<DirectoryResponse> SendAsync(LdapConnection connection, DirectoryRequest request) =>
        Task.Factory.FromAsync(
            (callback, state) => connection.BeginSendRequest(request, PartialResultProcessing.NoPartialResultSupport, callback, state),
            connection.EndSendRequest,
            null);

    private static DirectoryUser Map(SearchResultEntry e)
    {
        string? Str(string name) => e.Attributes[name] is { Count: > 0 } a ? a[0]?.ToString() : null;

        var guidBytes = e.Attributes["objectGUID"] is { Count: > 0 } g ? g[0] as byte[] : null;
        var sam = Str("sAMAccountName") ?? string.Empty;

        // msExchRecipientTypeDetails: 1 = UserMailbox, 4 = SharedMailbox; homeMDB e setat pentru căsuțe on-prem.
        var typeDetails = long.TryParse(Str("msExchRecipientTypeDetails"), out var td) ? td : 0;
        var hasMailbox = Str("homeMDB") is not null || typeDetails is 1 or 4;

        return new DirectoryUser(
            guidBytes is { Length: 16 } ? new Guid(guidBytes) : Guid.Empty,
            sam.ToLowerInvariant(),
            Str("displayName") ?? sam,
            Str("mail"),
            hasMailbox);
    }

    /// <summary>Escape RFC 4515 pentru valori din filtre LDAP (previne LDAP injection).</summary>
    public static string EscapeFilterValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\5c"); break;
                case '*': sb.Append(@"\2a"); break;
                case '(': sb.Append(@"\28"); break;
                case ')': sb.Append(@"\29"); break;
                case '\0': sb.Append(@"\00"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    public static string DomainToBaseDn(string domain) =>
        string.Join(",", domain.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(p => $"DC={p}"));

    private static (string User, string? Domain) SplitUser(string user, string? defaultDomain)
    {
        var slash = user.IndexOf('\\');
        if (slash > 0) return (user[(slash + 1)..], user[..slash]);
        return user.Contains('@') ? (user, null) : (user, defaultDomain);
    }
}
