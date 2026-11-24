namespace MDel.Infrastructure.Integrations;

/// <summary>
/// Conexiunea la Active Directory din <c>appsettings.json</c>, secțiunea <c>ActiveDirectory</c>.
/// Cu <see cref="Enabled"/> = true are prioritate față de ecranul Setări (tab-ul Active Directory devine doar
/// informativ); cu false se folosesc setările din aplicație.
/// </summary>
public sealed class ActiveDirectoryOptions
{
    public const string SectionName = "ActiveDirectory";

    public bool Enabled { get; set; }

    /// <summary>Controlerul de domeniu (ex. <c>dc01.corp.local</c>) sau numele domeniului (DC găsit prin DNS).</summary>
    public string? Server { get; set; }

    /// <summary>Gol = 636 cu SSL, 389 fără.</summary>
    public int? Port { get; set; }

    /// <summary>LDAPS. Certificatul DC-ului trebuie să fie de încredere pe serverul aplicației.</summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// Doar pentru teste / certificat self-signed: acceptă orice certificat al DC-ului (doar pe Windows).
    /// </summary>
    public bool ValidateCertificate { get; set; } = true;

    /// <summary>
    /// Contul de citire: <c>CORP\svc-mdel</c>, <c>svc-mdel@corp.local</c> sau un DN (<c>CN=svc-mdel,OU=…</c>).
    /// Gol = identitatea app pool-ului (Kerberos/NTLM).
    /// </summary>
    public string? BindUser { get; set; }

    public string? BindPassword { get; set; }

    /// <summary>
    /// <c>Negotiate</c> (Kerberos/NTLM, implicit pentru CORP\user și user@domeniu) sau <c>Basic</c> (simple bind,
    /// implicit pentru un DN; doar cu SSL). Gol = ales automat.
    /// </summary>
    public string? AuthType { get; set; }

    /// <summary>OU-urile în care se caută utilizatorii. Gol = tot domeniul (defaultNamingContext).</summary>
    public string[] SearchBases { get; set; } = [];

    /// <summary>Numele DNS al domeniului (ex. <c>corp.local</c>), pentru conturile fără domeniu. Opțional.</summary>
    public string? Domain { get; set; }

    public int TimeoutSeconds { get; set; } = 15;
}
