using System.Globalization;
using System.Net;
using MDel.Domain.Entities;
using MDel.Domain.Enums;

namespace MDel.Infrastructure.Notifications;

/// <summary>Tipul de mesaj – câte un șablon (subiect + text) pentru fiecare.</summary>
public enum TemplateKind
{
    /// <summary>Cerere de aprobare, către grupul de aprobatori.</summary>
    Request,
    /// <summary>Cerere cu aprobare, pentru cei doar informați (grupul de informare).</summary>
    Pending,
    /// <summary>Delegare înregistrată fără aprobare.</summary>
    Direct,
    Approved,
    Rejected,
    Activated,
    Expired,
    Stopped,
    Failed,
}

/// <summary>
/// Șabloanele emailurilor de notificare. Cele în română se pot modifica din Setări → Șabloane email
/// (AppSettings „Templates.{Tip}.Subject/Body”); cele în engleză sunt fixe.
/// Textul folosește câmpuri {nume}; tabelul cu detaliile delegării și butonul cu link se adaugă automat.
/// </summary>
public static class NotificationTemplates
{
    public sealed record Template(string Subject, string Body);

    /// <summary>Câmpurile disponibile în șabloane.</summary>
    public static readonly IReadOnlyList<string> Placeholders =
        ["sursa", "destinatar", "id", "inceput", "sfarsit", "motiv", "inregistrat_de", "aprobat_de", "observatii"];

    public const int MaxSubjectLength = 300;
    public const int MaxBodyLength = 4000;

    public static string SubjectKey(TemplateKind kind) => $"Templates.{kind}.Subject";
    public static string BodyKey(TemplateKind kind) => $"Templates.{kind}.Body";

    public static readonly IReadOnlyDictionary<TemplateKind, Template> RomanianDefaults = new Dictionary<TemplateKind, Template>
    {
        [TemplateKind.Request] = new("[MDel] Cerere de aprobare: forward {sursa} → {destinatar} (#{id})",
            "A fost înregistrată o cerere de forward care necesită aprobarea dumneavoastră. Este suficientă aprobarea unui singur membru al grupului."),
        [TemplateKind.Pending] = new("[MDel] Cerere de forward înregistrată: {sursa} → {destinatar} (#{id})",
            "A fost înregistrată o cerere de forward, aflată în așteptarea aprobării. Mesajul este doar informativ."),
        [TemplateKind.Direct] = new("[MDel] Forward înregistrat fără aprobare: {sursa} → {destinatar} (#{id})",
            "A fost înregistrat un forward fără aprobare. Mesajul este doar informativ."),
        [TemplateKind.Approved] = new("[MDel] Forward aprobat: {sursa} → {destinatar} (#{id})", "Cererea de forward a fost aprobată de {aprobat_de}."),
        [TemplateKind.Rejected] = new("[MDel] Forward respins: {sursa} → {destinatar} (#{id})", "Cererea de forward a fost respinsă."),
        [TemplateKind.Activated] = new("[MDel] Forward activ: {sursa} → {destinatar} (#{id})",
            "Forwardul a fost aplicat în Exchange și este activ până la {sfarsit}."),
        [TemplateKind.Expired] = new("[MDel] Forward expirat: {sursa} → {destinatar} (#{id})",
            "Perioada s-a încheiat, iar forwardul a fost eliminat automat din Exchange."),
        [TemplateKind.Stopped] = new("[MDel] Forward oprit: {sursa} → {destinatar} (#{id})", "Forwardul a fost oprit de un administrator."),
        [TemplateKind.Failed] = new("[MDel] Eroare forward: {sursa} → {destinatar} (#{id})",
            "Operația în Exchange a eșuat și necesită intervenția unui administrator."),
    };

    private static readonly Dictionary<TemplateKind, Template> EnglishTemplates = new()
    {
        [TemplateKind.Request] = new("[MDel] Approval request: forward {sursa} → {destinatar} (#{id})",
            "A forwarding request needs your approval. One approval from any member of the group is enough."),
        [TemplateKind.Pending] = new("[MDel] Forwarding request registered: {sursa} → {destinatar} (#{id})",
            "A forwarding request was registered and is waiting for approval. This message is for your information only."),
        [TemplateKind.Direct] = new("[MDel] Forwarding registered without approval: {sursa} → {destinatar} (#{id})",
            "A forward was registered without approval. This message is for your information only."),
        [TemplateKind.Approved] = new("[MDel] Forwarding approved: {sursa} → {destinatar} (#{id})", "The forwarding request was approved by {aprobat_de}."),
        [TemplateKind.Rejected] = new("[MDel] Forwarding rejected: {sursa} → {destinatar} (#{id})", "The forwarding request was rejected."),
        [TemplateKind.Activated] = new("[MDel] Forwarding active: {sursa} → {destinatar} (#{id})",
            "The forward was applied in Exchange and is active until {sfarsit}."),
        [TemplateKind.Expired] = new("[MDel] Forwarding expired: {sursa} → {destinatar} (#{id})",
            "The period ended and the forward was removed from Exchange automatically."),
        [TemplateKind.Stopped] = new("[MDel] Forwarding stopped: {sursa} → {destinatar} (#{id})", "The forward was stopped by an administrator."),
        [TemplateKind.Failed] = new("[MDel] Forwarding error: {sursa} → {destinatar} (#{id})",
            "The Exchange operation failed and needs an administrator."),
    };

    public static TemplateKind KindFor(NotificationEvent ev, bool actionRequired, bool requiresApproval) => ev switch
    {
        NotificationEvent.Created => actionRequired ? TemplateKind.Request : requiresApproval ? TemplateKind.Pending : TemplateKind.Direct,
        NotificationEvent.Approved => TemplateKind.Approved,
        NotificationEvent.Rejected => TemplateKind.Rejected,
        NotificationEvent.Activated => TemplateKind.Activated,
        NotificationEvent.Expired => TemplateKind.Expired,
        NotificationEvent.Stopped => TemplateKind.Stopped,
        _ => TemplateKind.Failed,
    };

    /// <summary>Câmpurile {nume} necunoscute dintr-un text (pentru validarea din Setări).</summary>
    public static IReadOnlyList<string> UnknownPlaceholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{([^{}]*)\}")
            .Select(m => m.Groups[1].Value)
            .Where(name => !Placeholders.Contains(name))
            .Distinct()
            .ToList();

    private static readonly Dictionary<string, Dictionary<string, string>> Labels = new()
    {
        ["ro"] = new()
        {
            ["from"] = "De la (căsuța sursă)", ["to"] = "Către", ["start"] = "Început", ["end"] = "Sfârșit",
            ["createdBy"] = "Înregistrat de", ["approvedBy"] = "Aprobat de", ["reason"] = "Motiv", ["notes"] = "Observații",
            ["status"] = "Stare", ["open"] = "Deschide delegarea", ["approve"] = "Deschide pentru aprobare",
            ["footer"] = "Mesaj trimis automat de MDel. Ora este afișată în fusul orar România (Europe/Bucharest).",
        },
        ["en"] = new()
        {
            ["from"] = "From (source mailbox)", ["to"] = "To", ["start"] = "Start", ["end"] = "End",
            ["createdBy"] = "Registered by", ["approvedBy"] = "Approved by", ["reason"] = "Reason", ["notes"] = "Notes",
            ["status"] = "Status", ["open"] = "Open the delegation", ["approve"] = "Open for approval",
            ["footer"] = "Automatic message from MDel. Times are shown in the Romania time zone (Europe/Bucharest).",
        },
    };

    private static readonly TimeZoneInfo Zone = FindZone();

    /// <param name="romanian">Șablonul românesc din Setări (null = implicit). Ignorat pentru limba engleză.</param>
    public static (string Subject, string HtmlBody) Render(
        Delegation d, TemplateKind kind, string lang, string? reason, string? baseUrl, Template? romanian = null)
    {
        lang = lang == "en" ? "en" : "ro";
        var template = lang == "en" ? EnglishTemplates[kind] : romanian ?? RomanianDefaults[kind];
        var l = Labels[lang];
        var culture = CultureInfo.GetCultureInfo(lang == "en" ? "en-GB" : "ro-RO");

        string Date(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone).ToString("dd.MM.yyyy HH:mm", culture);

        var values = new Dictionary<string, string>
        {
            ["sursa"] = d.FromUser.DisplayName,
            ["destinatar"] = d.ToUser.DisplayName,
            ["id"] = d.Id.ToString(CultureInfo.InvariantCulture),
            ["inceput"] = Date(d.StartUtc),
            ["sfarsit"] = Date(d.EndUtc),
            ["motiv"] = reason ?? string.Empty,
            ["inregistrat_de"] = d.CreatedBy?.DisplayName ?? string.Empty,
            ["aprobat_de"] = d.ApprovedBy?.DisplayName ?? string.Empty,
            ["observatii"] = d.Notes ?? string.Empty,
        };

        // Subiectul e text simplu (fără linii noi); corpul se escapează și primește paragrafe.
        var subject = Fill(template.Subject, values).ReplaceLineEndings(" ").Trim();
        if (subject.Length > MaxSubjectLength) subject = subject[..MaxSubjectLength];
        var intro = string.Join(string.Empty, Fill(template.Body, values)
            .ReplaceLineEndings("\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => $"<p style=\"margin:0 0 12px\">{H(p).Replace("\n", "<br>")}</p>"));

        string Row(string label, string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty :
            $"<tr><td style=\"padding:6px 12px 6px 0;color:#64748b;white-space:nowrap;vertical-align:top\">{H(label)}</td>" +
            $"<td style=\"padding:6px 0;color:#0f172a\">{H(value)}</td></tr>";

        var link = string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/delegations/{d.Id}";
        var button = link is null ? string.Empty :
            $"<p style=\"margin:24px 0 0\"><a href=\"{H(link)}\" style=\"display:inline-block;background:#4f46e5;color:#ffffff;" +
            $"text-decoration:none;padding:10px 16px;border-radius:8px;font-weight:600\">{H(kind == TemplateKind.Request ? l["approve"] : l["open"])}</a></p>";

        var body = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;color:#0f172a;max-width:600px">
              {intro}
              <table style="border-collapse:collapse;font-size:14px">
                {Row(l["from"], $"{d.FromUser.DisplayName} <{d.FromUser.Email ?? d.FromUser.SamAccountName}>")}
                {Row(l["to"], $"{d.ToUser.DisplayName} <{d.ToUser.Email ?? d.ToUser.SamAccountName}>")}
                {Row(l["start"], Date(d.StartUtc))}
                {Row(l["end"], Date(d.EndUtc))}
                {Row(l["createdBy"], d.CreatedBy?.DisplayName)}
                {Row(l["approvedBy"], d.ApprovedBy?.DisplayName)}
                {Row(l["reason"], reason)}
                {Row(l["notes"], d.Notes)}
              </table>
              {button}
              <p style="margin:24px 0 0;color:#64748b;font-size:12px">{H(l["footer"])}</p>
            </div>
            """;

        return (subject, body);
    }

    private static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\{([a-z_]+)\}",
            m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    private static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "Europe/Bucharest", "GTB Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }

        return TimeZoneInfo.Utc;
    }
}
