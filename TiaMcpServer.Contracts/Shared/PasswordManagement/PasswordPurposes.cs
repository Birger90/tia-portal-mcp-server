using TiaMcpServer.Contracts.Plc.Security;

namespace TiaMcpServer.Contracts.Shared.PasswordManagement;

/// <summary>One purpose of a password map entry with the German label the password manager shows.</summary>
public sealed class PasswordPurpose
{
    public PasswordPurpose(string purpose, string label, string usage)
    {
        Purpose = purpose;
        Label = label;
        Usage = usage;
    }

    /// <summary>The map's <c>purpose</c> value.</summary>
    public string Purpose { get; }

    /// <summary>The German label.</summary>
    public string Label { get; }

    /// <summary>Which tools use it (German).</summary>
    public string Usage { get; }

    public override string ToString() => Label;
}

/// <summary>The purposes the server's password map knows, as the password manager offers them.</summary>
public static class PasswordPurposes
{
    public static IReadOnlyList<PasswordPurpose> Known { get; } = Array.AsReadOnly(new[]
    {
        new PasswordPurpose("FullAccess", "Zugriffsstufe: Vollzugriff", "plc_write set_plc_access_level"),
        new PasswordPurpose("FullAccessIncludingFailsafe", "Zugriffsstufe: Vollzugriff einschließlich fehlersicher", "plc_write set_plc_access_level (F-CPU)"),
        new PasswordPurpose("ReadAccess", "Zugriffsstufe: Lesezugriff", "plc_write set_plc_access_level"),
        new PasswordPurpose("HMIAccess", "Zugriffsstufe: HMI-Zugriff", "plc_write set_plc_access_level"),
        new PasswordPurpose(PlcSecurityPolicy.MasterSecretPurpose, "Vertrauliche PLC-Konfigurationsdaten", "plc_write protect/change/disable_plc_confidential_data_protection, plc_download"),
        new PasswordPurpose(PasswordMapFile.SafetyPurpose, "Safety (Passwort des F-Programms)", "plc_write safety_login, set_safety_password, revoke_safety_password"),
        new PasswordPurpose(Plc.BlockProtection.PlcBlockProtectionNames.KnowHowPurpose, "Know-how-Schutz (Bausteine)", "plc_write set/remove_block_know_how_protection"),
        new PasswordPurpose(Plc.BlockProtection.PlcBlockProtectionNames.WriteProtectionPurpose, "Schreibschutz (Bausteine)", "plc_write define/set/remove_block_write_protection, change_block_write_protection_password (aktuelles Passwort)"),
        new PasswordPurpose("ModuleWriteAccessPassword", "Modul-/Online-Passwort: Schreibzugriff", "plc_download, plc_station_upload"),
        new PasswordPurpose("ModuleReadAccessPassword", "Modul-/Online-Passwort: Lesezugriff", "plc_download, plc_station_upload"),
        new PasswordPurpose("OnlineReadAccessPassword", "Online-Passwort: Lesezugriff", "plc_tia_online, plc_read compare_to_online"),
        new PasswordPurpose("PasswordReadAccess", "Hochladen: Lesezugriff", "plc_station_upload"),
    });

    /// <summary>The known purpose, or null for another password configuration type short name.</summary>
    public static PasswordPurpose? Find(string? purpose) => Known.FirstOrDefault(p => string.Equals(p.Purpose, purpose, StringComparison.Ordinal));

    /// <summary>The German label of <paramref name="purpose"/>; another type short name is shown as itself.</summary>
    public static string LabelOf(string purpose) => Find(purpose)?.Label ?? "Passwortkonfiguration " + purpose;

    /// <summary>Why <paramref name="purpose"/> is not a map purpose (German); null when it is.</summary>
    public static string? Error(string? purpose)
        => PasswordMapFile.IsPurpose(purpose)
            ? null
            : "Der Zweck muss einer der angebotenen sein oder der Kurzname einer Passwortkonfiguration (Buchstaben, Ziffern, Unterstrich; nicht TlsVerificationConfiguration).";
}
