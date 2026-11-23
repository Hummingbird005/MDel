using System.Globalization;
using System.Net;
using MDel.Domain.Entities;
using MDel.Domain.Enums;

namespace MDel.Infrastructure.Notifications;

/// <summary>Șabloanele emailurilor de notificare, în română și engleză.</summary>
public static class NotificationTemplates
{
    private sealed record Texts(string Subject, string Intro);

    // Variante pentru Created: "request" (aprobatori), "pending" (informați despre o cerere cu aprobare), "direct" (fără aprobare).
    private static readonly Dictionary<(NotificationEvent, string Variant, string Lang), Texts> Map = new()
    {
        [(NotificationEvent.Created, "request", "ro")] = new("Cerere de aprobare: forward {0} → {1} (#{2})",
            "A fost înregistrată o cerere de forward care necesită aprobarea dumneavoastră. Este suficientă aprobarea unui singur membru al grupului."),
        [(NotificationEvent.Created, "request", "en")] = new("Approval request: forward {0} → {1} (#{2})",
            "A forwarding request needs your approval. One approval from any member of the group is enough."),
        [(NotificationEvent.Created, "pending", "ro")] = new("Cerere de forward înregistrată: {0} → {1} (#{2})",
            "A fost înregistrată o cerere de forward, aflată în așteptarea aprobării. Mesajul este doar informativ."),
        [(NotificationEvent.Created, "pending", "en")] = new("Forwarding request registered: {0} → {1} (#{2})",
            "A forwarding request was registered and is waiting for approval. This message is for your information only."),
        [(NotificationEvent.Created, "direct", "ro")] = new("Forward înregistrat fără aprobare: {0} → {1} (#{2})",
            "A fost înregistrat un forward fără aprobare. Mesajul este doar informativ."),
        [(NotificationEvent.Created, "direct", "en")] = new("Forwarding registered without approval: {0} → {1} (#{2})",
            "A forward was registered without approval. This message is for your information only."),
        [(NotificationEvent.Approved, "", "ro")] = new("Forward aprobat: {0} → {1} (#{2})", "Cererea de forward a fost aprobată."),
        [(NotificationEvent.Approved, "", "en")] = new("Forwarding approved: {0} → {1} (#{2})", "The forwarding request was approved."),
        [(NotificationEvent.Rejected, "", "ro")] = new("Forward respins: {0} → {1} (#{2})", "Cererea de forward a fost respinsă."),
        [(NotificationEvent.Rejected, "", "en")] = new("Forwarding rejected: {0} → {1} (#{2})", "The forwarding request was rejected."),
        [(NotificationEvent.Activated, "", "ro")] = new("Forward activ: {0} → {1} (#{2})", "Forwardul a fost aplicat în Exchange și este activ."),
        [(NotificationEvent.Activated, "", "en")] = new("Forwarding active: {0} → {1} (#{2})", "The forward was applied in Exchange and is now active."),
        [(NotificationEvent.Expired, "", "ro")] = new("Forward expirat: {0} → {1} (#{2})", "Perioada s-a încheiat, iar forwardul a fost eliminat automat din Exchange."),
        [(NotificationEvent.Expired, "", "en")] = new("Forwarding expired: {0} → {1} (#{2})", "The period ended and the forward was removed from Exchange automatically."),
        [(NotificationEvent.Stopped, "", "ro")] = new("Forward oprit: {0} → {1} (#{2})", "Forwardul a fost oprit de un administrator."),
        [(NotificationEvent.Stopped, "", "en")] = new("Forwarding stopped: {0} → {1} (#{2})", "The forward was stopped by an administrator."),
        [(NotificationEvent.Failed, "", "ro")] = new("Eroare forward: {0} → {1} (#{2})", "Operația în Exchange a eșuat și necesită intervenția unui administrator."),
        [(NotificationEvent.Failed, "", "en")] = new("Forwarding error: {0} → {1} (#{2})", "The Exchange operation failed and needs an administrator."),
    };

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

    public static (string Subject, string HtmlBody) Render(
        Delegation d, NotificationEvent ev, bool actionRequired, string lang, string? reason, string? baseUrl)
    {
        lang = lang == "en" ? "en" : "ro";
        var variant = ev != NotificationEvent.Created ? "" : actionRequired ? "request" : d.RequiresApproval ? "pending" : "direct";
        var texts = Map[(ev, variant, lang)];
        var l = Labels[lang];
        var culture = CultureInfo.GetCultureInfo(lang == "en" ? "en-GB" : "ro-RO");

        var subject = "[MDel] " + string.Format(culture, texts.Subject, d.FromUser.DisplayName, d.ToUser.DisplayName, d.Id);

        string Row(string label, string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty :
            $"<tr><td style=\"padding:6px 12px 6px 0;color:#64748b;white-space:nowrap;vertical-align:top\">{H(label)}</td>" +
            $"<td style=\"padding:6px 0;color:#0f172a\">{H(value)}</td></tr>";

        string Date(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone).ToString("dd.MM.yyyy HH:mm", culture);

        var link = string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/delegations/{d.Id}";
        var button = link is null ? string.Empty :
            $"<p style=\"margin:24px 0 0\"><a href=\"{H(link)}\" style=\"display:inline-block;background:#4f46e5;color:#ffffff;" +
            $"text-decoration:none;padding:10px 16px;border-radius:8px;font-weight:600\">{H(actionRequired ? l["approve"] : l["open"])}</a></p>";

        var body = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;font-size:14px;line-height:1.5;color:#0f172a;max-width:600px">
              <p style="margin:0 0 16px">{H(texts.Intro)}</p>
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
