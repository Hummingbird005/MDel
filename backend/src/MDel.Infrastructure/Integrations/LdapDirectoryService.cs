using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MDel.Application.Abstractions;
using MDel.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MDel.Infrastructure.Integrations;

/// <summary>
/// Acces la Active Directory prin LDAP (System.DirectoryServices.Protocols).
/// Conexiunea vine din <c>appsettings.json</c> (secțiunea <c>ActiveDirectory</c>, când <c>Enabled = true</c>)
/// sau din ecranul Setări. Fără cont de citire se folosește identitatea procesului (app pool) prin Negotiate.
/// </summary>
public sealed partial class LdapDirectoryService(
    ISettingsService settings,
    IOptionsMonitor<ActiveDirectoryOptions> options,
    ILogger<LdapDirectoryService> logger) : IDirectoryService
{
    private static readonly string[] UserAttributes =
        ["objectGUID", "sAMAccountName", "displayName", "mail", "msExchRecipientTypeDetails", "homeMDB"];

    // Utilizatori activi (exclude conturile dezactivate: bitul 2 din userAccountControl).
    private const string ActiveUserFilter =
        "(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))";

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
        LdapConnectionInfo? info = null;
        try
        {
            info = await ResolveAsync(ct);
            using var connection = Connect(info);
            var bases = await GetSearchBasesAsync(connection, info);

            var lines = new List<string> { $"Connected: {info.Describe()}." };
            var allOk = true;
            foreach (var searchBase in bases)
            {
                try
                {
                    var request = new SearchRequest(searchBase, "(objectClass=*)", SearchScope.Base, "distinguishedName");
                    await SendAsync(connection, request);
                    lines.Add($"Search base '{searchBase}': OK.");
                }
                catch (DirectoryOperationException ex)
                {
                    allOk = false;
                    lines.Add($"Search base '{searchBase}': {ex.Response?.ResultCode} – check the DN (it must exist and be readable).");
                }
            }

            return new ConnectionTestResult(allOk, string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AD connection test failed");
            var where = info is null ? "" : $" ({info.Describe()})";
            return new ConnectionTestResult(false, $"AD connection failed{where}: {Explain(ex, info)}");
        }
    }

    // ------------------------------------------------------------------ Configurare

    /// <summary>Parametrii conexiunii: din appsettings.json (dacă e activat) sau din ecranul Setări.</summary>
    internal async Task<LdapConnectionInfo> ResolveAsync(CancellationToken ct)
    {
        var o = options.CurrentValue;
        if (o.Enabled) return LdapConnectionInfo.FromOptions(o);

        var server = await settings.GetAsync(SettingKeys.AdLdapServer, ct);
        var domain = await settings.GetAsync(SettingKeys.AdDomain, ct);
        var baseDn = await settings.GetAsync(SettingKeys.AdBaseDn, ct);
        var user = await settings.GetAsync(SettingKeys.AdServiceUser, ct);
        var password = string.IsNullOrWhiteSpace(user) ? null : await settings.GetSecretAsync(SettingKeys.AdServicePassword, ct);
        return LdapConnectionInfo.FromSettings(server, domain, baseDn, user, password);
    }

    private LdapConnection Connect(LdapConnectionInfo info)
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(info.Host, info.Port))
        {
            AuthType = info.AuthType,
            Timeout = info.Timeout,
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        if (info.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
            if (!info.ValidateCertificate && OperatingSystem.IsWindows())
            {
                logger.LogWarning("ActiveDirectory:ValidateCertificate is false – the domain controller certificate is not checked.");
                connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
            }
        }
        else if (info.AuthType == AuthType.Negotiate)
        {
            // Kerberos sign & seal: credențialele și datele nu circulă în clar nici pe portul 389.
            connection.SessionOptions.Signing = true;
            connection.SessionOptions.Sealing = true;
        }

        if (!string.IsNullOrWhiteSpace(info.BindUser))
        {
            if (info.AuthType == AuthType.Basic)
            {
                connection.Credential = new NetworkCredential(info.BindUser, info.BindPassword);
            }
            else
            {
                var (user, domain) = SplitUser(info.BindUser, info.Domain);
                connection.Credential = new NetworkCredential(user, info.BindPassword, domain);
            }
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

        return connection;
    }

    /// <summary>OU-urile configurate sau, dacă lipsesc, tot domeniul (defaultNamingContext din RootDSE).</summary>
    private static async Task<IReadOnlyList<string>> GetSearchBasesAsync(LdapConnection connection, LdapConnectionInfo info)
    {
        if (info.SearchBases.Count > 0) return info.SearchBases;

        var rootDse = (SearchResponse)await SendAsync(connection,
            new SearchRequest(null, "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
        var naming = rootDse.Entries.Count > 0 && rootDse.Entries[0].Attributes["defaultNamingContext"] is { Count: > 0 } a
            ? a[0]?.ToString()
            : null;
        return string.IsNullOrWhiteSpace(naming)
            ? throw new InvalidOperationException("Could not read defaultNamingContext from the domain controller; set the search bases explicitly.")
            : [naming];
    }

    // ------------------------------------------------------------------ Căutare

    private async Task<IReadOnlyList<DirectoryUser>> SearchAsync(string filter, int sizeLimit, CancellationToken ct)
    {
        var info = await ResolveAsync(ct);
        using var connection = Connect(info);
        var bases = await GetSearchBasesAsync(connection, info);

        var found = new Dictionary<string, DirectoryUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var searchBase in bases)
        {
            if (found.Count >= sizeLimit) break;
            ct.ThrowIfCancellationRequested();

            var request = new SearchRequest(searchBase, filter, SearchScope.Subtree, UserAttributes) { SizeLimit = sizeLimit - found.Count };
            SearchResponse response;
            try
            {
                response = (SearchResponse)await SendAsync(connection, request);
            }
            catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.SizeLimitExceeded)
            {
                response = (SearchResponse)ex.Response;
            }
            catch (DirectoryOperationException ex) when (ex.Response?.ResultCode == ResultCode.NoSuchObject && bases.Count > 1)
            {
                // Un OU greșit nu blochează căutarea în celelalte; testul de conexiune îl semnalează.
                logger.LogWarning("AD search base '{Base}' does not exist.", searchBase);
                continue;
            }

            foreach (var user in response.Entries.Cast<SearchResultEntry>().Select(Map))
            {
                found.TryAdd(user.SamAccountName, user);
            }
        }

        return found.Values.OrderBy(u => u.DisplayName).Take(sizeLimit).ToList();
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

    // ------------------------------------------------------------------ Mesaje de eroare

    /// <summary>Eroarea LDAP tradusă în cauza probabilă (codurile „data xxx” din mesajul AD).</summary>
    public static string Explain(Exception ex, LdapConnectionInfo? info)
    {
        if (ex is not LdapException ldap) return ex.Message;

        var server = ldap.ServerErrorMessage ?? "";
        var data = AdDataCode().Match(server) is { Success: true } m ? m.Groups[1].Value.ToLowerInvariant() : null;
        var hint = data switch
        {
            "52e" => "wrong user name or password (BindUser / BindPassword)",
            "525" => "the bind user does not exist",
            "530" or "531" => "the bind user may not log on now / from this computer",
            "532" or "773" => "the bind user's password expired or must be changed",
            "533" => "the bind user is disabled",
            "701" => "the bind user's account expired",
            "775" => "the bind user is locked out",
            _ => ldap.ErrorCode switch
            {
                49 => "invalid credentials (BindUser / BindPassword)",
                81 when info?.UseSsl == true =>
                    $"cannot reach {info.Host}:{info.Port} over SSL – check the port (636) and the firewall, and that the " +
                    "domain controller certificate is trusted on this server and its name matches Server",
                81 => $"cannot reach {info?.Host}:{info?.Port} – check the server name, the port and the firewall",
                8 => "the domain controller requires a signed or SSL connection: set UseSsl = true (port 636)",
                13 => "the domain controller requires SSL for this kind of bind: set UseSsl = true (port 636)",
                _ => null,
            },
        };

        var detail = string.IsNullOrWhiteSpace(server) ? ldap.Message : $"{ldap.Message} {server.Trim()}";
        return hint is null ? $"{detail} (LDAP {ldap.ErrorCode})" : $"{hint}. {detail} (LDAP {ldap.ErrorCode})";
    }

    [GeneratedRegex(@"\bdata ([0-9a-fA-F]{3,4})\b")]
    private static partial Regex AdDataCode();

    // ------------------------------------------------------------------ Utilitare

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

/// <summary>Parametrii unei conexiuni LDAP, din oricare sursă (appsettings.json sau Setări).</summary>
public sealed record LdapConnectionInfo(
    string Source,
    string Host,
    int Port,
    bool UseSsl,
    bool ValidateCertificate,
    AuthType AuthType,
    string? BindUser,
    string? BindPassword,
    string? Domain,
    IReadOnlyList<string> SearchBases,
    TimeSpan Timeout)
{
    public string Describe() =>
        $"{Source}, {Host}:{Port}{(UseSsl ? " SSL" : "")}, {AuthType} as " +
        (string.IsNullOrWhiteSpace(BindUser) ? "the application pool identity" : BindUser);

    public static LdapConnectionInfo FromOptions(ActiveDirectoryOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.Server))
            throw new InvalidOperationException("ActiveDirectory:Server is empty in appsettings.json.");

        var (host, port, ssl) = ParseServer(o.Server.Trim(), o.UseSsl, o.Port);
        var bases = (o.SearchBases ?? []).Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b.Trim()).ToArray();
        return Create("appsettings.json", host, port, ssl, o.ValidateCertificate, o.AuthType, o.BindUser, o.BindPassword,
            o.Domain, bases, TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSeconds, 3, 120)));
    }

    public static LdapConnectionInfo FromSettings(string? server, string? domain, string? baseDn, string? user, string? password)
    {
        // Fără server explicit, se folosește domeniul (localizare DC prin DNS).
        var target = string.IsNullOrWhiteSpace(server) ? domain : server;
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("AD is not configured: set the LDAP server or the domain in Administration.");

        // "ldaps://dc01:636" sau "dc01" / "dc01:389"
        var (host, port, ssl) = ParseServer(target.Trim(), useSsl: false, port: null);
        string[] bases = !string.IsNullOrWhiteSpace(baseDn) ? [baseDn.Trim()]
            : !string.IsNullOrWhiteSpace(domain) ? [LdapDirectoryService.DomainToBaseDn(domain)]
            : [];
        return Create("Settings", host, port, ssl, true, null, user, password, domain, bases, TimeSpan.FromSeconds(15));
    }

    private static LdapConnectionInfo Create(string source, string host, int port, bool ssl, bool validateCertificate,
        string? authType, string? user, string? password, string? domain, string[] bases, TimeSpan timeout)
    {
        user = string.IsNullOrWhiteSpace(user) ? null : user.Trim();
        var auth = ResolveAuthType(authType, user);
        if (auth == AuthType.Basic)
        {
            if (user is null)
                throw new InvalidOperationException("Basic (simple bind) needs a BindUser and BindPassword.");
            if (!ssl)
                throw new InvalidOperationException(
                    "Basic (simple bind) sends the password in clear text: use UseSsl = true (port 636), " +
                    "or a CORP\\user / user@domain account with Negotiate.");
        }

        return new LdapConnectionInfo(source, host, port, ssl, validateCertificate, auth, user, password,
            string.IsNullOrWhiteSpace(domain) ? null : domain.Trim(), bases, timeout);
    }

    /// <summary>Explicit (Negotiate / Basic) sau automat: un DN (CN=…) se poate folosi doar cu simple bind.</summary>
    public static AuthType ResolveAuthType(string? configured, string? user) => configured?.Trim().ToLowerInvariant() switch
    {
        "basic" or "simple" => AuthType.Basic,
        "negotiate" or "kerberos" or "ntlm" => AuthType.Negotiate,
        null or "" => user is not null && user.Contains('=') ? AuthType.Basic : AuthType.Negotiate,
        var other => throw new InvalidOperationException($"ActiveDirectory:AuthType '{other}' is not supported (Negotiate or Basic)."),
    };

    /// <summary>„dc01”, „dc01:636”, „ldaps://dc01”, „ldap://dc01:389” → host, port, SSL.</summary>
    public static (string Host, int Port, bool Ssl) ParseServer(string server, bool useSsl, int? port)
    {
        var ssl = useSsl;
        var host = server;
        if (host.StartsWith("ldaps://", StringComparison.OrdinalIgnoreCase)) { ssl = true; host = host[8..]; }
        else if (host.StartsWith("ldap://", StringComparison.OrdinalIgnoreCase)) { host = host[7..]; }
        host = host.TrimEnd('/');

        int? explicitPort = port is > 0 ? port : null;
        if (host.LastIndexOf(':') is var idx and > 0 && int.TryParse(host[(idx + 1)..], out var p))
        {
            explicitPort ??= p;
            host = host[..idx];
        }

        return (host, explicitPort ?? (ssl ? 636 : 389), ssl);
    }
}
