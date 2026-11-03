using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.PlcDownload;
using M = TiaMcpServer.Contracts.Plc.Settings.PlcCpuSettingsMethods;

namespace TiaMcpServer.Plc;

/// <summary>The settings family (issues #15, #13, #12, #14, #21, #25): field sets and shape rules; nothing here reads the project.</summary>
public static partial class PlcOperationCatalog
{
    private static IReadOnlyDictionary<string, Spec> WithSettingsSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        foreach (var entry in existing) specs[entry.Key] = entry.Value;
        specs[M.GetCpuSettings] = Read(Array.Empty<string>(), new[] { "plcName" });
        specs[M.SetAccountLocking] = Write(Array.Empty<string>(), new[] { "plcName", "settings" });
        specs[M.SetPlcPasswordPolicy] = Write(Array.Empty<string>(), new[] { "settings" });
        specs[M.SetLegacyPlcPasswordPolicy] = Write(Array.Empty<string>(), new[] { "settings" });
        specs[M.SetSyslog] = Write(Array.Empty<string>(), new[] { "plcName", "settings" });
        specs[M.AddSyslogServer] = Write(new[] { "address" }, new[] { "plcName", "port" });
        specs[M.RemoveSyslogServer] = Write(new[] { "address" }, new[] { "plcName" });
        specs[M.CreateProjectSyslogServer] = Write(new[] { "name" }, new[] { "settings" });
        specs[M.UpdateProjectSyslogServer] = Write(new[] { "name" }, new[] { "settings" });
        specs[M.DeleteProjectSyslogServer] = Write(new[] { "name" }, Array.Empty<string>());
        specs[M.SetSystemWebPages] = Write(Array.Empty<string>(), new[] { "plcName", "settings" });
        specs[M.SetDefaultWebApplication] = Write(new[] { "name" }, new[] { "plcName" });
        specs[M.SetWatchTableAccess] = Write(new[] { "tableName", "access" }, new[] { "plcName", "folderPath", "display" });
        specs[M.RemoveWatchTableAccess] = Write(new[] { "tableName" }, new[] { "plcName", "folderPath", "display" });
        specs[M.SetForceTableAccess] = Write(new[] { "tableName", "access" }, new[] { "plcName", "folderPath", "display" });
        specs[M.RemoveForceTableAccess] = Write(new[] { "tableName" }, new[] { "plcName", "folderPath", "display" });
        specs[M.SetDisplayPassword] = Write(new[] { "passwordVariable" }, new[] { "plcName" });
        specs[M.SetFrontPanelDisplay] = Write(Array.Empty<string>(), new[] { "plcName", "settings", "filePath" });
        specs[M.SetPlcProjectLanguage] = Write(new[] { "culture" }, new[] { "plcName", "position" });
        specs[M.SetIpAccessibility] = Write(new[] { "moduleName" }, new[] { "plcName" });
        specs[M.GenerateWebBlocks] = Write(Array.Empty<string>(), new[] { "plcName", "directoryPath", "filePath", "name", "overwrite" });
        MoreSettingsSpecs(specs);
        return specs;
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, string?> Get)> SettingsStringFields() => new (string, Func<PlcOperationRequest, string?>)[]
    {
        ("access", op => op.Access),
        ("address", op => op.Address),
        ("moduleName", op => op.ModuleName),
    }.Concat(MoreSettingsStringFields());

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> SettingsOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("settings", op => op.Settings is not null),
        ("display", op => op.Display is not null),
        ("port", op => op.Port is not null),
    }.Concat(MoreSettingsOtherFields());

    /// <summary>The settings shape rules. Messages never repeat a password source value.</summary>
    private static void ValidateSettingsItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        var definition = PlcSettingsCatalog.Find(op.Operation);
        if (definition is null) return;
        void Error(string message) => errors.Add($"{prefix}: {message}");
        var settings = op.Settings?.Entries ?? Array.Empty<KeyValuePair<string, string>>();
        if (op.Settings is not null)
        {
            if (settings.Count > PlcSettingsNames.MaxSettings) Error($"settings takes at most {PlcSettingsNames.MaxSettings} entries.");
            foreach (var entry in settings.Where(e => !definition.Settings.Contains(e.Key, StringComparer.Ordinal)))
                Error($"settings: '{entry.Key}' is not a setting of {op.Operation} (allowed: {string.Join(", ", definition.Settings)}).");
        }

        if (definition.RequiresSettings && settings.Count == 0) Error($"{op.Operation} needs settings with at least one of: {string.Join(", ", definition.Settings)}.");
        if (op.PasswordVariable is PlcDownloadPasswordMap.RedactedVariable)
            Error("'passwordVariable' must name where the password is (an environment variable NAME or a credential target), never the password itself; the value was redacted.");
        else if (op.PasswordVariable is PlcDownloadPasswordMap.UnsetVariable)
            Error("'passwordVariable' names an environment variable that is not set (or empty) for this server; set it in the MCP client configuration (env) and restart the server. The name was redacted.");
        if (op.Port is { } port && (port < 0 || port > ushort.MaxValue)) Error("port must be 0 to 65535.");
        if (op.Position is { } position && position < 1) Error("position must be 1 or more.");
        switch (op.Operation)
        {
            case M.AddSyslogServer when op.Port is null:
                Error("add_syslog_server needs port.");
                break;
            case M.SetWatchTableAccess or M.SetForceTableAccess when op.Access is not null && !Contracts.Plc.Settings.PlcCpuSettingsNames.Accesses.Contains(op.Access, StringComparer.Ordinal):
                Error($"access must be one of {string.Join(", ", Contracts.Plc.Settings.PlcCpuSettingsNames.Accesses)} (TIA Portal refuses WatchAndForceTableAccess.None; remove the rule instead).");
                break;
            case M.SetPlcProjectLanguage when op.Position is null:
                Error("set_plc_project_language needs position (the 1-based row of the CPU's MultilingualSupport table, as get_cpu_settings lists it).");
                break;
            case M.SetFrontPanelDisplay when settings.Count == 0 && op.FilePath is null:
                Error("set_front_panel_display needs filePath (the logo) and/or settings.");
                break;
            case M.GenerateWebBlocks when op.DirectoryPath is not null && (op.FilePath is null || op.Name is null):
                Error("generate_web_blocks with directoryPath (the HTML directory) needs filePath (the default HTML page) and name (the application name).");
                break;
            case M.GenerateWebBlocks when op.DirectoryPath is null && (op.FilePath is not null || op.Name is not null):
                Error("generate_web_blocks takes filePath and name only with directoryPath.");
                break;
        }

        ValidateMoreSettings(definition, op, Error);
    }
}
