namespace TiaMcpServer.Contracts.Plc.OpcUa;

/// <summary>
/// The OPC UA server interfaces and access rules of a PLC (GitHub issue #24). Evidence: the V21 IntelliSense file
/// Siemens.Engineering.Step7.xml, namespace <c>Siemens.Engineering.SW.OpcUa</c>: <c>OpcUaProvider.CommunicationGroup</c> →
/// <c>OpcUaCommunicationGroup.ServerInterfaceGroup</c> → <c>ServerInterfaceGroup.ServerInterfaces</c> (<c>ServerInterfaceComposition.Create(String)</c>;
/// <c>ServerInterface.Export(FileInfo)</c>, <c>.Import(FileInfo)</c>, <c>.Delete()</c>, <c>.Enabled</c> "Enable server interface and download
/// to PLC", <c>.Name</c>, <c>.Author</c>, <c>.Comment</c>, <c>.CreationTime</c>, <c>.LastModified</c>), <c>.SimaticInterfaces</c>
/// (<c>SimaticInterfaceComposition.Create(String)</c>; <c>SimaticInterface.Export</c>, <c>.Delete</c>, <c>.Enabled</c>, <c>.UseStringNodeIds</c>),
/// <c>.ReferenceNamespaces</c> (<c>ReferenceNamespaceComposition.Create(String, FileInfo)</c>; <c>ReferenceNamespace.Export</c>, <c>.Import</c>,
/// <c>.Delete</c>, <c>.Enabled</c>, <c>.GenerateNodes</c>, <c>.GeneratedInterfaceName</c>, <c>.UseStringNodeIds</c>) and <c>.AccessControl</c>
/// (<c>AccessControl.NamespaceAccessRestrictions</c> of <c>NamespaceAccessRestriction</c> {NamespaceUri, NamespaceIndex, SessionRequired,
/// SigningRequired, EncryptionRequired, ApplyRestrictionsToBrowse}; <c>AccessControl.RoleMappings</c>: <c>RoleMappingComposition.Create(String, String)</c>,
/// <c>.AddStandardOpcUaRole(String)</c>; <c>RoleMapping.Delete()</c>, <c>.SetRoleName(String)</c>, <c>.SetDefiningNamespace(String)</c>,
/// <c>.SetProjectRole(String)</c>, <c>.Name</c>, <c>.DefinedInNamespace</c>, <c>.ProjectRole</c>, <c>.RoleId</c>, <c>.NamespacePermissions</c> of
/// <c>NamespacePermission</c> {NamespaceUri, NamespaceIndex, Browse, Read, Write, Call, ReceiveEvents, ReadRolePermissions,
/// <c>SetPermission(String, Boolean)</c>}). The XML does not say which object offers <c>OpcUaProvider</c>: it is found among the
/// PlcSoftware's <c>GetServiceInfos()</c> by exact full name. Every member is bound at runtime; no stub member was added. The worker
/// methods carry the public operation names. Not live-tested.
/// </summary>
public static class PlcOpcUaMethods
{
    public const string GetConfiguration = "get_opcua_configuration";

    public const string CreateInterface = "create_opcua_interface";
    public const string DeleteInterface = "delete_opcua_interface";
    public const string ExportInterface = "export_opcua_interface";
    public const string ImportInterface = "import_opcua_interface";
    public const string SetInterface = "set_opcua_interface";
    public const string SetNamespaceRestriction = "set_opcua_namespace_restriction";
    public const string CreateRole = "create_opcua_role";
    public const string AddStandardRole = "add_opcua_standard_role";
    public const string DeleteRole = "delete_opcua_role";
    public const string SetRole = "set_opcua_role";
    public const string SetNamespacePermission = "set_opcua_namespace_permission";

    public static IReadOnlyList<string> Read { get; } = new[] { GetConfiguration };

    public static IReadOnlyList<string> Write { get; } = new[]
    {
        CreateInterface, DeleteInterface, ExportInterface, ImportInterface, SetInterface, SetNamespaceRestriction,
        CreateRole, AddStandardRole, DeleteRole, SetRole, SetNamespacePermission,
    };

    public static bool IsRead(string? method) => method is not null && Read.Contains(method, StringComparer.Ordinal);

    public static bool IsWrite(string? method) => method is not null && Write.Contains(method, StringComparer.Ordinal);

    public static bool IsWorkerMethod(string? method) => IsRead(method) || IsWrite(method);
}

public static class PlcOpcUaNames
{
    public const string Provider = "Siemens.Engineering.SW.OpcUa.OpcUaProvider";

    /// <summary>The interface kinds: <c>ServerInterfaces</c>, <c>SimaticInterfaces</c>, <c>ReferenceNamespaces</c>.</summary>
    public static class Kinds
    {
        public const string Server = "server";
        public const string Simatic = "simatic";
        public const string ReferenceNamespace = "referenceNamespace";

        public static IReadOnlyList<string> All { get; } = new[] { Server, Simatic, ReferenceNamespace };

        /// <summary>The documented <c>Import(FileInfo)</c>: server interfaces and reference namespaces (SimaticInterface has none).</summary>
        public static bool Imports(string? kind) => kind is Server or ReferenceNamespace;

        public static string Composition(string kind) => kind switch
        {
            Server => "ServerInterfaces",
            Simatic => "SimaticInterfaces",
            _ => "ReferenceNamespaces",
        };
    }

    /// <summary>The boolean properties of <c>NamespacePermission</c>, read back after <c>SetPermission</c> when the name is one of them.</summary>
    public static IReadOnlyList<string> PermissionProperties { get; } = new[] { "Browse", "Read", "Write", "Call", "ReceiveEvents", "ReadRolePermissions" };

    /// <summary>The boolean properties of <c>NamespaceAccessRestriction</c>.</summary>
    public static IReadOnlyList<string> RestrictionProperties { get; } = new[] { "SessionRequired", "SigningRequired", "EncryptionRequired", "ApplyRestrictionsToBrowse" };

    public const int MaxEntries = 1000;
}

/// <summary>The worker parameters of every OPC UA operation; only the members an operation documents are read.</summary>
public sealed class PlcOpcUaRequestInfo
{
    public string Operation { get; set; } = string.Empty;

    public string? PlcName { get; set; }

    /// <summary>One of <see cref="PlcOpcUaNames.Kinds"/>.</summary>
    public string? InterfaceKind { get; set; }

    public string? InterfaceName { get; set; }

    /// <summary>export: the file written; import, create of a reference namespace or a server interface with content: the XML read.</summary>
    public string? FilePath { get; set; }

    public bool Overwrite { get; set; }

    /// <summary>set_opcua_interface: new name and attribute values (generic attribute API).</summary>
    public string? NewName { get; set; }

    public bool? Enabled { get; set; }

    public bool? UseStringNodeIds { get; set; }

    public bool? GenerateNodes { get; set; }

    /// <summary>set_opcua_namespace_restriction / set_opcua_namespace_permission: the namespace by URI.</summary>
    public string? NamespaceUri { get; set; }

    public bool? SessionRequired { get; set; }

    public bool? SigningRequired { get; set; }

    public bool? EncryptionRequired { get; set; }

    public bool? ApplyRestrictionsToBrowse { get; set; }

    public string? RoleName { get; set; }

    public string? NewRoleName { get; set; }

    public string? DefiningNamespace { get; set; }

    public string? ProjectRole { get; set; }

    /// <summary>set_opcua_namespace_permission: the permission name passed to <c>SetPermission(String, Boolean)</c>.</summary>
    public string? Permission { get; set; }
}

public sealed class PlcOpcUaInterfaceInfo
{
    public string Kind { get; set; } = string.Empty;

    public string? Name { get; set; }

    public bool? Enabled { get; set; }

    public string? Author { get; set; }

    public string? Comment { get; set; }

    public string? CreationTime { get; set; }

    public string? LastModified { get; set; }

    /// <summary>SimaticInterface and ReferenceNamespace only.</summary>
    public bool? UseStringNodeIds { get; set; }

    /// <summary>ReferenceNamespace only.</summary>
    public bool? GenerateNodes { get; set; }

    /// <summary>ReferenceNamespace only.</summary>
    public string? GeneratedInterfaceName { get; set; }

    public string? RuntimeType { get; set; }

    public List<string> Unreadable { get; set; } = new();
}

public sealed class PlcOpcUaRestrictionInfo
{
    public string? NamespaceUri { get; set; }

    public string? NamespaceIndex { get; set; }

    public bool? SessionRequired { get; set; }

    public bool? SigningRequired { get; set; }

    public bool? EncryptionRequired { get; set; }

    public bool? ApplyRestrictionsToBrowse { get; set; }

    public List<string> Unreadable { get; set; } = new();
}

public sealed class PlcOpcUaPermissionInfo
{
    public string? NamespaceUri { get; set; }

    public string? NamespaceIndex { get; set; }

    public bool? Browse { get; set; }

    public bool? Read { get; set; }

    public bool? Write { get; set; }

    public bool? Call { get; set; }

    public bool? ReceiveEvents { get; set; }

    public bool? ReadRolePermissions { get; set; }

    public List<string> Unreadable { get; set; } = new();
}

public sealed class PlcOpcUaRoleInfo
{
    public string? Name { get; set; }

    public string? DefinedInNamespace { get; set; }

    public string? ProjectRole { get; set; }

    public string? RoleId { get; set; }

    public List<PlcOpcUaPermissionInfo> NamespacePermissions { get; set; } = new();

    public List<string> Unreadable { get; set; } = new();
}

/// <summary>One PLC's OPC UA configuration (get_opcua_configuration).</summary>
public sealed class PlcOpcUaPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string? DeviceName { get; set; }

    /// <summary>False when the PLC software does not list <see cref="PlcOpcUaNames.Provider"/> (then everything else is empty).</summary>
    public bool ProviderAvailable { get; set; }

    public string? Note { get; set; }

    public List<PlcOpcUaInterfaceInfo> ServerInterfaces { get; set; } = new();

    public List<PlcOpcUaInterfaceInfo> SimaticInterfaces { get; set; } = new();

    public List<PlcOpcUaInterfaceInfo> ReferenceNamespaces { get; set; } = new();

    public List<PlcOpcUaRestrictionInfo> NamespaceAccessRestrictions { get; set; } = new();

    public List<PlcOpcUaRoleInfo> RoleMappings { get; set; } = new();

    public bool Complete { get; set; } = true;

    public List<string> Unreadable { get; set; } = new();
}

public sealed class PlcOpcUaConfigurationInfo
{
    public List<PlcOpcUaPlcInfo> Plcs { get; set; } = new();
}

/// <summary>The result of every OPC UA write: the target, what was called, the state read back and the files written.</summary>
public sealed class PlcOpcUaWriteResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    /// <summary>The addressed object, e.g. <c>server interface 'Machine'</c> or <c>role 'Operator'</c>.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Every Siemens member called, in order.</summary>
    public List<string> Calls { get; set; } = new();

    /// <summary>The interface after the write (interface operations except delete), read again.</summary>
    public PlcOpcUaInterfaceInfo? InterfaceAfter { get; set; }

    public PlcOpcUaRestrictionInfo? RestrictionAfter { get; set; }

    public PlcOpcUaRoleInfo? RoleAfter { get; set; }

    /// <summary>export: the file written; import: the file read (a private copy) and, when re-exported, the re-export.</summary>
    public string? FilePath { get; set; }

    public long? Bytes { get; set; }

    public string? Sha256 { get; set; }

    /// <summary>import: SHA-256 of a re-export right after the import (null when it could not be made).</summary>
    public string? ReExportSha256 { get; set; }

    /// <summary>
    /// The postcondition: <c>verified</c> (the state read back matches), <c>notReadBack</c> (the call has no documented
    /// read-back, e.g. a permission name that is no property, or an import whose re-export differs from the file) or
    /// <c>deleted</c> (the object is no longer found).
    /// </summary>
    public string Evidence { get; set; } = string.Empty;

    public string? Note { get; set; }
}

public static class PlcOpcUaEvidence
{
    public const string Verified = "verified";
    public const string NotReadBack = "notReadBack";
    public const string Deleted = "deleted";
    public const string File = "file";
}
