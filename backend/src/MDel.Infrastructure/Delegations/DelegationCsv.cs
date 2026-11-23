using System.Globalization;
using System.Text;
using MDel.Infrastructure.Localization;

namespace MDel.Infrastructure.Delegations;

/// <summary>Export CSV al listei de delegări, în limba cererii (ro/en).</summary>
public static class DelegationCsv
{
    public static byte[] Write(IEnumerable<DelegationListItem> rows)
    {
        var en = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', en
            ? new[] { "Id", "Status", "Source", "Source email", "Target", "Target email", "Start (UTC)", "End (UTC)", "Mode", "Approval", "Created by", "Created (UTC)", "Attachments" }
            : new[] { "Id", "Stare", "Sursă", "Email sursă", "Destinatar", "Email destinatar", "Început (UTC)", "Sfârșit (UTC)", "Mod", "Aprobare", "Înregistrat de", "Înregistrat (UTC)", "Atașamente" }));

        foreach (var d in rows)
        {
            sb.AppendLine(string.Join(';',
                d.Id.ToString(CultureInfo.InvariantCulture),
                Escape(Messages.Status(d.Status)),
                Escape(d.From.DisplayName), Escape(d.From.Email),
                Escape(d.To.DisplayName), Escape(d.To.Email),
                Date(d.StartUtc), Date(d.EndUtc),
                Escape(d.KeepCopyInSource ? (en ? "Forward + copy" : "Forward + copie") : (en ? "Forward only" : "Doar forward")),
                Escape(d.RequiresApproval ? (en ? "Yes" : "Da") : (en ? "No" : "Nu")),
                Escape(d.CreatedBy.DisplayName), Date(d.CreatedUtc),
                d.AttachmentCount.ToString(CultureInfo.InvariantCulture)));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Date(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Ghilimele la nevoie; neutralizează formulele (CSV injection: =, +, -, @).</summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if ("=+-@".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
