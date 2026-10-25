using System.Text.Json;
using System.Text.RegularExpressions;

namespace ThunderbirdAccounts.Core;

public sealed record MailAccount(string Id, string Name, string Email, string Type, string Incoming, string Outgoing);

public static partial class AccountSettings
{
    // Deliberate allowlists: extensions, scripts, caches and file references never enter the archive.
    private static readonly HashSet<string> ServerFields = Fields("type hostname port userName name authMethod socketType isSecure login_at_startup check_new_mail check_time download_on_biff max_cached_connections offline_download use_idle using_subscription dual_use_folders delete_model cleanup_inbox_on_exit empty_trash_on_exit leave_on_server delete_mail_left_on_server delete_by_age num_days_to_leave_on_server deferred_to_account defer_get_new_mail namespace.personal namespace.public namespace.other_users override_namespaces server_sub_directory capabilities socketType timeout is_gmail storeContractID redirector_type realhostname realuserName oauth2.scope oauth2.issuer trash_folder_name autosync_offline_stores autosync_max_age_days");
    private static readonly HashSet<string> IdentityFields = Fields("useremail fullName organization reply_to valid smtpServer doBcc doBccList doCc doCcList compose_html reply_on_top sig_bottom auto_quote fcc_folder fcc_folder_picker_mode fcc_reply_follows_parent fcc drafts_folder drafts_folder_picker_mode stationery_folder tmpl_folder_picker_mode archive_folder archives_folder_picker_mode archive_enabled archive_granularity archive_keep_folder_structure attach_vcard escapedVCard htmlSigText htmlSigFormat suppress_signature_separator showSaveMsgDlg sig_on_reply sig_on_fwd request_return_receipt receipt_header_type receipt_policy dsn_use_custom_prefs dsn_always_request");
    private static readonly HashSet<string> SmtpFields = Fields("hostname port username authMethod try_ssl description hello_argument oauth2.scope oauth2.issuer");
    private static HashSet<string> Fields(string fields) => new(fields.Split(' '), StringComparer.Ordinal);
    [GeneratedRegex("^(account|server|id|smtp)[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
    public static bool ValidId(string id) => IdPattern().IsMatch(id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }

    public static IReadOnlyList<MailAccount> Read(IReadOnlyDictionary<string, JsonElement> prefs)
    {
        var accounts = new List<MailAccount>();
        foreach (string id in Preferences.List(prefs.Text("mail.accountmanager.accounts")))
        {
            Require(id.StartsWith("account", StringComparison.Ordinal) && ValidId(id), "Ogiltigt konto-ID.");
            string server = prefs.Text($"mail.account.{id}.server");
            Require(server.StartsWith("server", StringComparison.Ordinal) && ValidId(server), $"Kontot {id} saknar server.");
            string prefix = $"mail.server.{server}.";
            string type = prefs.Text(prefix + "type");
            Require(type is "imap" or "pop3" or "none", $"Kontotypen '{type}' stöds inte i version 0.1 (endast IMAP/POP3/lokala mappar).");
            var identities = Preferences.List(prefs.Text($"mail.account.{id}.identities"));
            foreach (var identity in identities) Require(identity.StartsWith("id", StringComparison.Ordinal) && ValidId(identity), "Ogiltig identitet.");
            string identityId = identities.FirstOrDefault() ?? "";
            string smtpId = prefs.Text($"mail.identity.{identityId}.smtpServer");
            if (smtpId.Length == 0) smtpId = prefs.Text("mail.smtp.defaultserver");
            if (type != "none") Require(prefs.Text(prefix + "hostname").Length > 0, "E-postserverns adress saknas.");
            accounts.Add(new(id, prefs.Text(prefix + "name"), string.Join(", ", identities.Select(i => prefs.Text($"mail.identity.{i}.useremail"))), type,
                Endpoint(prefs, prefix), Endpoint(prefs, $"mail.smtpserver.{smtpId}.")));
        }
        Require(accounts.Count > 0, "Profilen innehåller inga konfigurerade konton.");
        Require(accounts.Select(a => a.Id).Distinct().Count() == accounts.Count, "Dubbla konto-ID:n.");
        return accounts;
    }
    private static string Endpoint(IReadOnlyDictionary<string, JsonElement> prefs, string prefix) =>
        prefs.Text(prefix + "hostname") + (prefs.TryGetValue(prefix + "port", out var port) ? ":" + port.ToString() : "");

    public static Dictionary<string, JsonElement> Export(IReadOnlyDictionary<string, JsonElement> source)
    {
        var accounts = Read(source);
        var servers = accounts.Select(a => source.Text($"mail.account.{a.Id}.server")).ToHashSet(StringComparer.Ordinal);
        string local = source.Text("mail.accountmanager.localfoldersserver");
        if (local.Length > 0) { Require(local.StartsWith("server", StringComparison.Ordinal) && ValidId(local), "Ogiltig lokal server."); servers.Add(local); }
        var identities = accounts.SelectMany(a => Preferences.List(source.Text($"mail.account.{a.Id}.identities"))).ToHashSet(StringComparer.Ordinal);
        var smtp = Preferences.List(source.Text("mail.smtpservers")).ToHashSet(StringComparer.Ordinal);
        foreach (string i in identities)
        {
            string s = source.Text($"mail.identity.{i}.smtpServer"); if (s.Length > 0) smtp.Add(s);
            // Key export needs a separate OpenPGP/S/MIME workflow, never silently lose signing keys.
            foreach (string field in new[] { "openpgp_key_id", "encryption_cert_name", "signing_cert_name", "last_entered_external_gnupg_key_id" })
                Require(source.Text($"mail.identity.{i}.{field}").Length == 0, "OpenPGP/S/MIME-identitet upptäckt. Exportera nycklar separat; denna version avbryter för att undvika ofullständig återställning.");
        }
        string defaultSmtp = source.Text("mail.smtp.defaultserver"); if (defaultSmtp.Length > 0) smtp.Add(defaultSmtp);
        foreach (string s in smtp) Require(s.StartsWith("smtp", StringComparison.Ordinal) && ValidId(s), "Ogiltigt SMTP-ID.");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        CopyExact(source, result, "mail.accountmanager.accounts", "mail.accountmanager.defaultaccount", "mail.accountmanager.localfoldersserver", "mail.smtpservers", "mail.smtp.defaultserver");
        foreach (var a in accounts) CopyExact(source, result, $"mail.account.{a.Id}.server", $"mail.account.{a.Id}.identities");
        CopyBranch(source, result, "mail.server.", servers, ServerFields);
        CopyBranch(source, result, "mail.identity.", identities, IdentityFields);
        CopyBranch(source, result, "mail.smtpserver.", smtp, SmtpFields);
        foreach (string server in servers)
        {
            string prefix = $"mail.server.{server}.";
            string type = source.Text(prefix + "type");
            Require(type is "imap" or "pop3" or "none", "Ogiltig servertyp.");
            string deferred = source.Text(prefix + "deferred_to_account");
            Require(deferred.Length == 0 || accounts.Any(a => a.Id == deferred), "POP3 hänvisar till ett saknat konto.");
            string dir = (type == "imap" ? "ImapMail/" : "Mail/") + server;
            result[prefix + "directory-rel"] = Preferences.Json("[ProfD]" + dir);
            // First launch must not immediately perform POP3 deletion or download.
            result[prefix + "login_at_startup"] = Preferences.Json(false);
            if (type == "pop3")
            {
                result[prefix + "check_new_mail"] = Preferences.Json(false);
                result[prefix + "download_on_biff"] = Preferences.Json(false);
                result[prefix + "leave_on_server"] = Preferences.Json(true);
                result[prefix + "delete_mail_left_on_server"] = Preferences.Json(false);
                result[prefix + "delete_by_age"] = Preferences.Json(false);
            }
        }
        // External signature files aren't mail-account data. Don't preserve broken file references.
        foreach (string identity in identities) result[$"mail.identity.{identity}.attach_signature"] = Preferences.Json(false);
        Require(result.Text("mail.accountmanager.defaultaccount").Length == 0 || accounts.Any(a => a.Id == result.Text("mail.accountmanager.defaultaccount")), "Standardkontot saknas.");
        Read(result); return result;
    }
    private static void CopyExact(IReadOnlyDictionary<string, JsonElement> source, Dictionary<string, JsonElement> dest, params string[] keys)
    { foreach (string key in keys) if (source.TryGetValue(key, out var v)) dest[key] = v.Clone(); }
    private static void CopyBranch(IReadOnlyDictionary<string, JsonElement> source, Dictionary<string, JsonElement> dest, string prefix, HashSet<string> ids, HashSet<string> fields)
    {
        foreach (string id in ids)
            foreach (string field in fields)
            {
                string key = prefix + id + "." + field;
                if (!source.TryGetValue(key, out var value)) continue;
                if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Ogiltigt inställningsvärde.");
                // Local folder URIs contain old absolute profile paths. Thunderbird recreates its defaults.
                if (value.ValueKind == JsonValueKind.String && value.GetString()!.StartsWith("mailbox:", StringComparison.OrdinalIgnoreCase)) continue;
                dest[key] = value.Clone();
            }
    }
}
