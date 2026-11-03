using TiaMcpServer.Contracts.Plc.OpcUa;

namespace TiaMcpServer.Plc;

/// <summary>plc_read get_opcua_configuration and the plc_write OPC UA operations (issue #24): field sets and shape rules.</summary>
public static partial class PlcOperationCatalog
{
    private static IReadOnlyDictionary<string, Spec> WithOpcUaSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        foreach (var entry in existing) specs[entry.Key] = entry.Value;
        specs[PlcOpcUaMethods.GetConfiguration] = Read(Array.Empty<string>(), new[] { "plcName" });
        specs[PlcOpcUaMethods.CreateInterface] = Write(new[] { "interfaceKind", "interfaceName" }, new[] { "plcName", "filePath" });
        specs[PlcOpcUaMethods.DeleteInterface] = Write(new[] { "interfaceKind", "interfaceName" }, new[] { "plcName" });
        specs[PlcOpcUaMethods.ExportInterface] = Write(new[] { "interfaceKind", "interfaceName", "filePath" }, new[] { "plcName", "overwrite" });
        specs[PlcOpcUaMethods.ImportInterface] = Write(new[] { "interfaceKind", "interfaceName", "filePath" }, new[] { "plcName" });
        specs[PlcOpcUaMethods.SetInterface] = Write(new[] { "interfaceKind", "interfaceName" }, new[] { "plcName", "enabled", "useStringNodeIds", "generateNodes", "newName" });
        specs[PlcOpcUaMethods.SetNamespaceRestriction] = Write(new[] { "namespaceUri" },
            new[] { "plcName", "sessionRequired", "signingRequired", "encryptionRequired", "applyRestrictionsToBrowse" });
        specs[PlcOpcUaMethods.CreateRole] = Write(new[] { "roleName", "definingNamespace" }, new[] { "plcName" });
        specs[PlcOpcUaMethods.AddStandardRole] = Write(new[] { "roleName" }, new[] { "plcName" });
        specs[PlcOpcUaMethods.DeleteRole] = Write(new[] { "roleName" }, new[] { "plcName" });
        specs[PlcOpcUaMethods.SetRole] = Write(new[] { "roleName" }, new[] { "plcName", "newRoleName", "definingNamespace", "projectRole" });
        specs[PlcOpcUaMethods.SetNamespacePermission] = Write(new[] { "roleName", "namespaceUri", "permission" }, new[] { "plcName", "enabled" });
        return specs;
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, string?> Get)> OpcUaStringFields() => new (string, Func<PlcOperationRequest, string?>)[]
    {
        ("interfaceKind", op => op.InterfaceKind),
        ("interfaceName", op => op.InterfaceName),
        ("namespaceUri", op => op.NamespaceUri),
        ("roleName", op => op.RoleName),
        ("newRoleName", op => op.NewRoleName),
        ("definingNamespace", op => op.DefiningNamespace),
        ("projectRole", op => op.ProjectRole),
        ("permission", op => op.Permission),
        // compare_software secondary_plc (issue #20) shares this partial's field registration.
        ("targetProject", op => op.TargetProject),
    };

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> OpcUaOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("enabled", op => op.Enabled is not null),
        ("useStringNodeIds", op => op.UseStringNodeIds is not null),
        ("generateNodes", op => op.GenerateNodes is not null),
        ("sessionRequired", op => op.SessionRequired is not null),
        ("signingRequired", op => op.SigningRequired is not null),
        ("encryptionRequired", op => op.EncryptionRequired is not null),
        ("applyRestrictionsToBrowse", op => op.ApplyRestrictionsToBrowse is not null),
    };

    private static void ValidateOpcUaItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (op.Operation == Contracts.PlcOnline.PlcOnlineMethods.CompareSoftware
            && (op.TargetProject is null) != (op.CompareTarget != Contracts.PlcOnline.PlcOnlinePolicy.CompareTargets.SecondaryPlc))
            errors.Add($"{prefix}: targetProject (the path of an open secondary project) is required for compareTarget secondary_plc and valid only there.");
        if (!PlcOpcUaMethods.IsWrite(op.Operation)) return;
        void Error(string message) => errors.Add($"{prefix}: {message}");
        if (op.InterfaceKind is not null && !PlcOpcUaNames.Kinds.All.Contains(op.InterfaceKind, StringComparer.Ordinal))
            Error($"interfaceKind must be one of {string.Join(", ", PlcOpcUaNames.Kinds.All)}.");
        switch (op.Operation)
        {
            case PlcOpcUaMethods.CreateInterface when op.InterfaceKind == PlcOpcUaNames.Kinds.ReferenceNamespace && op.FilePath is null:
                Error("create_opcua_interface of a referenceNamespace needs filePath (ReferenceNamespaceComposition.Create(String, FileInfo)).");
                break;
            case PlcOpcUaMethods.CreateInterface when op.InterfaceKind == PlcOpcUaNames.Kinds.Simatic && op.FilePath is not null:
                Error("a simatic interface is created empty (SimaticInterfaceComposition.Create(String)); SimaticInterface documents no Import.");
                break;
            case PlcOpcUaMethods.ImportInterface when op.InterfaceKind == PlcOpcUaNames.Kinds.Simatic:
                Error("SimaticInterface documents no Import(FileInfo); import server interfaces and reference namespaces.");
                break;
            case PlcOpcUaMethods.SetInterface:
                if (op.Enabled is null && op.UseStringNodeIds is null && op.GenerateNodes is null && op.NewName is null)
                    Error("set_opcua_interface needs at least one of enabled, useStringNodeIds, generateNodes or newName.");
                if (op.UseStringNodeIds is not null && op.InterfaceKind == PlcOpcUaNames.Kinds.Server)
                    Error("useStringNodeIds is documented on simatic interfaces and reference namespaces only.");
                if (op.GenerateNodes is not null && op.InterfaceKind != PlcOpcUaNames.Kinds.ReferenceNamespace)
                    Error("generateNodes is documented on reference namespaces only.");
                break;
            case PlcOpcUaMethods.SetNamespaceRestriction when op.SessionRequired is null && op.SigningRequired is null && op.EncryptionRequired is null && op.ApplyRestrictionsToBrowse is null:
                Error("set_opcua_namespace_restriction needs at least one of sessionRequired, signingRequired, encryptionRequired or applyRestrictionsToBrowse.");
                break;
            case PlcOpcUaMethods.SetNamespacePermission when op.Enabled is null:
                Error("set_opcua_namespace_permission needs enabled (the value SetPermission(String, Boolean) sets).");
                break;
            case PlcOpcUaMethods.SetRole when op.NewRoleName is null && op.DefiningNamespace is null && op.ProjectRole is null:
                Error("set_opcua_role needs at least one of newRoleName, definingNamespace or projectRole.");
                break;
        }

        if (op.FilePath is not null && Contracts.Library.LibraryPolicy.FilePathError(op.FilePath) is { } pathError) Error($"filePath {pathError}");
    }
}
