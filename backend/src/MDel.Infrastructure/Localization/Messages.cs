using System.Globalization;

namespace MDel.Infrastructure.Localization;

/// <summary>Mesaje de eroare ale API-ului în română și engleză (după Accept-Language).</summary>
public static class Messages
{
    private static readonly Dictionary<string, (string Ro, string En)> Texts = new()
    {
        ["UserRequired"] = ("Selectați utilizatorul.", "Select a user."),
        ["UserNotFound"] = ("Utilizatorul nu există în Active Directory.", "The user does not exist in Active Directory."),
        ["UserNoMailbox"] = ("Utilizatorul nu are căsuță de email.", "The user has no mailbox."),
        ["SameUser"] = ("Destinatarul trebuie să fie diferit de sursă.", "The target must differ from the source."),
        ["NotInternal"] = ("Forwardul este permis doar către căsuțe interne.", "Forwarding is allowed only to internal mailboxes."),
        ["CannotVerifyRecipient"] = ("Destinatarul nu a putut fi verificat în Exchange: {0}", "The recipient could not be verified in Exchange: {0}"),
        ["EndAfterStart"] = ("Data de sfârșit trebuie să fie după data de început.", "The end date must be after the start date."),
        ["EndInPast"] = ("Data de sfârșit trebuie să fie în viitor.", "The end date must be in the future."),
        ["StartInPast"] = ("Data de început nu poate fi în trecut.", "The start date cannot be in the past."),
        ["Overlap"] = ("Căsuța sursă are deja o delegare (#{0}) în acest interval.", "The source mailbox already has a delegation (#{0}) in this interval."),
        ["Loop"] = ("Există deja un forward invers (#{0}) în acest interval – s-ar crea o buclă.", "A reverse forward (#{0}) already exists in this interval – it would create a loop."),
        ["NotEditable"] = ("Delegarea nu mai poate fi modificată în starea {0}.", "The delegation can no longer be edited in status {0}."),
        ["ActiveOnlyEnd"] = ("La o delegare activă se pot modifica doar data de sfârșit și observațiile.", "Only the end date and notes can be changed on an active delegation."),
        ["NotStoppable"] = ("Delegarea nu poate fi oprită în starea {0}.", "The delegation cannot be stopped in status {0}."),
        ["ReasonRequired"] = ("Motivul este obligatoriu.", "A reason is required."),
        ["Concurrency"] = ("Delegarea a fost modificată între timp de altcineva. Reîncărcați pagina.", "The delegation was changed by someone else. Reload the page."),
        ["StorageNotConfigured"] = ("Calea de stocare a fișierelor nu este configurată (Administrare → Fișiere).", "The file storage path is not configured (Administration → Files)."),
        ["FileTooLarge"] = ("Fișierul „{0}” depășește limita de {1} MB.", "File \"{0}\" exceeds the {1} MB limit."),
        ["FileExtension"] = ("Tipul fișierului „{0}” nu este permis. Extensii permise: {1}", "File type of \"{0}\" is not allowed. Allowed extensions: {1}"),
        ["FileEmpty"] = ("Fișierul „{0}” este gol.", "File \"{0}\" is empty."),
        ["FilePurged"] = ("Fișierul a fost șters conform politicii de retenție.", "The file was deleted by the retention policy."),
        ["CreatedWithoutApproval"] = ("Creată fără aprobare", "Created without approval"),
        ["EditedReapproval"] = ("Modificată – necesită reaprobare", "Edited – re-approval required"),
        ["ExchangeFailed"] = ("Operația în Exchange a eșuat: {0}", "The Exchange operation failed: {0}"),
    };

    public static string Get(string key, params object[] args)
    {
        var (ro, en) = Texts[key];
        var text = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? en : ro;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }
}
