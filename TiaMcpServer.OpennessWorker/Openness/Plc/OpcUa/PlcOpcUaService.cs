using System.Security.Cryptography;
using Siemens.Engineering;
using TiaMcpServer.Contracts.Plc.OpcUa;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc.OpcUa;

/// <summary>
/// plc_read get_opcua_configuration and the plc_write OPC UA operations (issue #24, not live-tested). The provider is the
/// PlcSoftware's <c>Siemens.Engineering.SW.OpcUa.OpcUaProvider</c> service, found among its <c>GetServiceInfos()</c> by exact full
/// name; every member below it is read or called at runtime by its documented name (<see cref="PlcOpcUa"/>), a missing one fails
/// closed (api_member_missing / unsupported_capability) before anything changes. Attribute writes (<c>Enabled</c>,
/// <c>UseStringNodeIds</c>, <c>GenerateNodes</c>, <c>Name</c>, the restriction flags) use the documented generic attribute API after a
/// <c>GetAttributeInfos</c> writability check and are read back. No stub member was added.
/// </summary>
internal static class PlcOpcUaService
{
    // ================================================================ read

    public static PlcOpcUaConfigurationInfo Read(ProjectBase project, PlcOpcUaRequestInfo request)
    {
        var result = new PlcOpcUaConfigurationInfo();
        var plcs = request.PlcName is null ? PlcSoftwareLocator.FindAll(project) : new[] { PlcSoftwareLocator.FindUnique(project, request.PlcName) };
        foreach (var plc in plcs)
        {
            var info = new PlcOpcUaPlcInfo { PlcName = plc.Software.Name, DeviceName = plc.DeviceName };
            result.Plcs.Add(info);
            var group = Group(plc.Software, out var note);
            if (group is null)
            {
                info.Note = note;
                continue;
            }

            info.ProviderAvailable = true;
            foreach (var kind in PlcOpcUaNames.Kinds.All)
            {
                var list = kind == PlcOpcUaNames.Kinds.Server ? info.ServerInterfaces : kind == PlcOpcUaNames.Kinds.Simatic ? info.SimaticInterfaces : info.ReferenceNamespaces;
                foreach (var item in Items(group, PlcOpcUaNames.Kinds.Composition(kind), info.Unreadable))
                {
                    if (list.Count >= PlcOpcUaNames.MaxEntries) { info.Complete = false; break; }
                    list.Add(Interface(kind, item));
                }
            }

            var access = Value(group, "AccessControl", info.Unreadable, "ServerInterfaceGroup.AccessControl");
            if (access is null) continue;
            foreach (var restriction in Items(access, "NamespaceAccessRestrictions", info.Unreadable))
                info.NamespaceAccessRestrictions.Add(Restriction(restriction));
            foreach (var role in Items(access, "RoleMappings", info.Unreadable))
                info.RoleMappings.Add(Role(role));
            info.Unreadable = info.Unreadable.Distinct(StringComparer.Ordinal).ToList();
        }

        return result;
    }

    // ================================================================ write

    public static PlcOpcUaWriteResultInfo Write(ProjectBase project, PlcOpcUaRequestInfo request)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var group = Group(plc.Software, out var note) ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, note + " Nothing was changed.");
        var result = new PlcOpcUaWriteResultInfo { Operation = request.Operation, PlcName = plc.Software.Name };
        switch (request.Operation)
        {
            case PlcOpcUaMethods.CreateInterface:
                return CreateInterface(group, request, result);
            case PlcOpcUaMethods.DeleteInterface:
            {
                var (composition, item) = FindInterface(group, request);
                result.Target = Describe(request);
                Call(item, "Delete", Type.EmptyTypes, Array.Empty<object>(), Documented(request, "Delete()"), result);
                if (FindByName(composition, request.InterfaceName!) is not null)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Delete() returned, but the {Describe(request)} is still listed.");
                result.Evidence = PlcOpcUaEvidence.Deleted;
                return result;
            }
            case PlcOpcUaMethods.ExportInterface:
            {
                var (_, item) = FindInterface(group, request);
                result.Target = Describe(request);
                Export(item, request.FilePath!, request.Overwrite, Documented(request, "Export(FileInfo)"), result);
                result.Evidence = PlcOpcUaEvidence.File;
                return result;
            }
            case PlcOpcUaMethods.ImportInterface:
            {
                if (!PlcOpcUaNames.Kinds.Imports(request.InterfaceKind))
                    throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "Only server interfaces and reference namespaces document Import(FileInfo).");
                var (_, item) = FindInterface(group, request);
                result.Target = Describe(request);
                Import(item, request, result);
                result.InterfaceAfter = Interface(request.InterfaceKind!, item);
                return result;
            }
            case PlcOpcUaMethods.SetInterface:
            {
                var (composition, item) = FindInterface(group, request);
                result.Target = Describe(request);
                var expected = new List<(string Name, object Value)>();
                if (request.Enabled is { } enabled) expected.Add(("Enabled", enabled));
                if (request.UseStringNodeIds is { } useStrings) expected.Add(("UseStringNodeIds", useStrings));
                if (request.GenerateNodes is { } generate) expected.Add(("GenerateNodes", generate));
                if (request.NewName is { } newName) expected.Add(("Name", newName));
                SetAttributes(item, expected, Describe(request), result);
                result.InterfaceAfter = Interface(request.InterfaceKind!, request.NewName is null ? item : FindByName(composition, request.NewName) ?? item);
                result.Evidence = PlcOpcUaEvidence.Verified;
                return result;
            }
            case PlcOpcUaMethods.SetNamespaceRestriction:
            {
                var access = Access(group);
                var restriction = FindRestriction(access, request.NamespaceUri!);
                result.Target = $"namespace access restriction '{request.NamespaceUri}'";
                var expected = new List<(string Name, object Value)>();
                if (request.SessionRequired is { } session) expected.Add(("SessionRequired", session));
                if (request.SigningRequired is { } signing) expected.Add(("SigningRequired", signing));
                if (request.EncryptionRequired is { } encryption) expected.Add(("EncryptionRequired", encryption));
                if (request.ApplyRestrictionsToBrowse is { } browse) expected.Add(("ApplyRestrictionsToBrowse", browse));
                SetAttributes(restriction, expected, result.Target, result);
                result.RestrictionAfter = Restriction(restriction);
                result.Evidence = PlcOpcUaEvidence.Verified;
                return result;
            }
            case PlcOpcUaMethods.CreateRole:
            case PlcOpcUaMethods.AddStandardRole:
            {
                var roles = Required(Access(group), "RoleMappings", "AccessControl.RoleMappings");
                result.Target = $"role '{request.RoleName}'";
                if (request.Operation == PlcOpcUaMethods.CreateRole)
                    Call(roles, "Create", new[] { typeof(string), typeof(string) }, new object[] { request.RoleName!, request.DefiningNamespace! },
                        "RoleMappingComposition.Create(String, String)", result);
                else
                    Call(roles, "AddStandardOpcUaRole", new[] { typeof(string) }, new object[] { request.RoleName! }, "RoleMappingComposition.AddStandardOpcUaRole(String)", result);
                var created = FindRole(roles, request.RoleName!) ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"The call returned, but no role named '{request.RoleName}' is listed among the role mappings.");
                result.RoleAfter = Role(created);
                result.Evidence = PlcOpcUaEvidence.Verified;
                return result;
            }
            case PlcOpcUaMethods.DeleteRole:
            {
                var roles = Required(Access(group), "RoleMappings", "AccessControl.RoleMappings");
                var role = FindRole(roles, request.RoleName!) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No role named '{request.RoleName}' is listed.");
                result.Target = $"role '{request.RoleName}'";
                Call(role, "Delete", Type.EmptyTypes, Array.Empty<object>(), "RoleMapping.Delete()", result);
                if (FindRole(roles, request.RoleName!) is not null)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Delete() returned, but role '{request.RoleName}' is still listed.");
                result.Evidence = PlcOpcUaEvidence.Deleted;
                return result;
            }
            case PlcOpcUaMethods.SetRole:
            {
                var roles = Required(Access(group), "RoleMappings", "AccessControl.RoleMappings");
                var role = FindRole(roles, request.RoleName!) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No role named '{request.RoleName}' is listed.");
                result.Target = $"role '{request.RoleName}'";
                if (request.DefiningNamespace is not null)
                    Call(role, "SetDefiningNamespace", new[] { typeof(string) }, new object[] { request.DefiningNamespace }, "RoleMapping.SetDefiningNamespace(String)", result);
                if (request.ProjectRole is not null)
                    Call(role, "SetProjectRole", new[] { typeof(string) }, new object[] { request.ProjectRole }, "RoleMapping.SetProjectRole(String)", result);
                if (request.NewRoleName is not null)
                    Call(role, "SetRoleName", new[] { typeof(string) }, new object[] { request.NewRoleName }, "RoleMapping.SetRoleName(String)", result);
                var after = Role(role);
                result.RoleAfter = after;
                Expect("RoleMapping.DefinedInNamespace", request.DefiningNamespace, after.DefinedInNamespace);
                Expect("RoleMapping.ProjectRole", request.ProjectRole, after.ProjectRole);
                Expect("RoleMapping.Name", request.NewRoleName, after.Name);
                result.Evidence = PlcOpcUaEvidence.Verified;
                return result;
            }
            case PlcOpcUaMethods.SetNamespacePermission:
            {
                var roles = Required(Access(group), "RoleMappings", "AccessControl.RoleMappings");
                var role = FindRole(roles, request.RoleName!) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No role named '{request.RoleName}' is listed.");
                var permission = TechnologyObjectRuntime.Items(Required(role, "NamespacePermissions", "RoleMapping.NamespacePermissions"), "RoleMapping.NamespacePermissions")
                    .FirstOrDefault(candidate => string.Equals(Text(candidate, "NamespaceUri"), request.NamespaceUri, StringComparison.Ordinal))
                    ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Role '{request.RoleName}' lists no namespace permission for '{request.NamespaceUri}'.");
                result.Target = $"permission '{request.Permission}' of role '{request.RoleName}' on '{request.NamespaceUri}'";
                Call(permission, "SetPermission", new[] { typeof(string), typeof(bool) }, new object[] { request.Permission!, request.Enabled!.Value },
                    "NamespacePermission.SetPermission(String, Boolean)", result);
                result.RoleAfter = Role(role);
                // The permission names SetPermission takes are not documented; when the name is one of the documented boolean
                // properties, that property is the read-back.
                var property = PlcOpcUaNames.PermissionProperties.FirstOrDefault(name => string.Equals(name, request.Permission, StringComparison.OrdinalIgnoreCase));
                if (property is null)
                {
                    result.Evidence = PlcOpcUaEvidence.NotReadBack;
                    result.Note = $"'{request.Permission}' is not one of the documented NamespacePermission properties ({string.Join(", ", PlcOpcUaNames.PermissionProperties)}); the change is not read back.";
                    return result;
                }

                var read = TechnologyObjectRuntime.Read(permission, property);
                if (read.Found && read.Value is bool value && value != request.Enabled.Value)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"SetPermission returned, but NamespacePermission.{property} reads {value}.");
                result.Evidence = read.Found && read.Value is bool ? PlcOpcUaEvidence.Verified : PlcOpcUaEvidence.NotReadBack;
                return result;
            }
            default:
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported OPC UA operation '{request.Operation}'.");
        }
    }

    private static PlcOpcUaWriteResultInfo CreateInterface(object group, PlcOpcUaRequestInfo request, PlcOpcUaWriteResultInfo result)
    {
        var kind = request.InterfaceKind!;
        var composition = Required(group, PlcOpcUaNames.Kinds.Composition(kind), "ServerInterfaceGroup." + PlcOpcUaNames.Kinds.Composition(kind));
        result.Target = Describe(request);
        object? created;
        if (kind == PlcOpcUaNames.Kinds.ReferenceNamespace)
        {
            var copy = PrivateCopy(request.FilePath!);
            try
            {
                created = Call(composition, "Create", new[] { typeof(string), typeof(FileInfo) }, new object[] { request.InterfaceName!, new FileInfo(copy) },
                    "ReferenceNamespaceComposition.Create(String, FileInfo)", result);
            }
            finally
            {
                TryDelete(copy);
            }
        }
        else
        {
            created = Call(composition, "Create", new[] { typeof(string) }, new object[] { request.InterfaceName! },
                kind == PlcOpcUaNames.Kinds.Server ? "ServerInterfaceComposition.Create(String)" : "SimaticInterfaceComposition.Create(String)", result);
        }

        var found = FindByName(composition, request.InterfaceName!) ?? created
            ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Create returned, but no {kind} interface named '{request.InterfaceName}' is listed.");
        if (kind == PlcOpcUaNames.Kinds.Server && request.FilePath is not null) Import(found, request, result);
        else result.Evidence = PlcOpcUaEvidence.Verified;
        result.InterfaceAfter = Interface(kind, found);
        return result;
    }

    /// <summary>Imports a private copy of the caller file, then re-exports it: equal bytes are the verification, otherwise notReadBack.</summary>
    private static void Import(object item, PlcOpcUaRequestInfo request, PlcOpcUaWriteResultInfo result)
    {
        var copy = PrivateCopy(request.FilePath!);
        try
        {
            (result.Bytes, result.Sha256) = Hash(copy);
            result.FilePath = request.FilePath;
            Call(item, "Import", new[] { typeof(FileInfo) }, new object[] { new FileInfo(copy) }, Documented(request, "Import(FileInfo)"), result);
        }
        finally
        {
            TryDelete(copy);
        }

        var reExport = Path.Combine(Path.GetTempPath(), $"TiaMcp-opcua-{Guid.NewGuid():N}.xml");
        try
        {
            RuntimeServiceValues.Invoke(item, "Export", new[] { typeof(FileInfo) }, new object[] { new FileInfo(reExport) }, Documented(request, "Export(FileInfo)"));
            result.Calls.Add(Documented(request, "Export(FileInfo)") + " (verification re-export)");
            result.ReExportSha256 = File.Exists(reExport) ? Hash(reExport).Sha256 : null;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            result.Note = $"The verification re-export failed: {exception.Message}";
        }
        finally
        {
            TryDelete(reExport);
        }

        result.Evidence = result.ReExportSha256 == result.Sha256 ? PlcOpcUaEvidence.Verified : PlcOpcUaEvidence.NotReadBack;
        if (result.Evidence == PlcOpcUaEvidence.NotReadBack)
            result.Note = (result.Note is null ? string.Empty : result.Note + " ")
                + "The re-export after the import differs from the imported file (TIA Portal may normalize the XML), so the import is not verified byte for byte; inspect it.";
    }

    private static void Export(object item, string filePath, bool overwrite, string documented, PlcOpcUaWriteResultInfo result)
    {
        var target = Path.GetFullPath(filePath);
        if (!Directory.Exists(Path.GetDirectoryName(target)))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The directory of filePath '{target}' does not exist.");
        if (File.Exists(target) && !overwrite) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"filePath '{target}' exists; set overwrite:true to replace it.");
        var staged = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileNameWithoutExtension(target)}.{Guid.NewGuid():N}.tmp{Path.GetExtension(target)}");
        try
        {
            Call(item, "Export", new[] { typeof(FileInfo) }, new object[] { new FileInfo(staged) }, documented, result);
            if (!File.Exists(staged)) throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "Export(FileInfo) returned, but wrote no file.");
            if (File.Exists(target)) File.Replace(staged, target, null);
            else File.Move(staged, target);
        }
        finally
        {
            TryDelete(staged);
        }

        result.FilePath = target;
        (result.Bytes, result.Sha256) = Hash(target);
    }

    // ================================================================ navigation

    /// <summary>The <c>ServerInterfaceGroup</c> of the PLC's OpcUaProvider; null with the reason when the provider is not offered.</summary>
    private static object? Group(object software, out string? note)
    {
        note = null;
        var offered = TechnologyObjectRuntime.ServiceTypes(software);
        object? provider;
        string? unavailable;
        try
        {
            (provider, unavailable) = RuntimeServiceValues.Find(offered, type => TechnologyObjectRuntime.TryService(software, offered, type.FullName!, out _), PlcOpcUaNames.Provider);
        }
        catch (RuntimeBindingException exception)
        {
            note = exception.Message;
            return null;
        }

        if (provider is null)
        {
            note = unavailable ?? RuntimeServiceValues.NotListed("The PLC software", PlcOpcUaNames.Provider);
            return null;
        }

        var communication = Required(provider, "CommunicationGroup", "OpcUaProvider.CommunicationGroup");
        return Required(communication, "ServerInterfaceGroup", "OpcUaCommunicationGroup.ServerInterfaceGroup");
    }

    private static object Access(object group) => Required(group, "AccessControl", "ServerInterfaceGroup.AccessControl");

    private static (object Composition, object Item) FindInterface(object group, PlcOpcUaRequestInfo request)
    {
        var composition = Required(group, PlcOpcUaNames.Kinds.Composition(request.InterfaceKind!), "ServerInterfaceGroup." + PlcOpcUaNames.Kinds.Composition(request.InterfaceKind!));
        return (composition, FindByName(composition, request.InterfaceName!)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No {Describe(request)} is listed."));
    }

    private static object? FindByName(object composition, string name)
    {
        var matches = TechnologyObjectRuntime.Items(composition, "OPC UA interfaces").Where(item => string.Equals(Text(item, "Name"), name, StringComparison.Ordinal)).ToList();
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"{matches.Count} OPC UA objects are named '{name}'."),
        };
    }

    private static object? FindRole(object roles, string name)
        => TechnologyObjectRuntime.Items(roles, "AccessControl.RoleMappings").FirstOrDefault(role => string.Equals(Text(role, "Name"), name, StringComparison.Ordinal));

    private static object FindRestriction(object access, string uri)
        => TechnologyObjectRuntime.Items(Required(access, "NamespaceAccessRestrictions", "AccessControl.NamespaceAccessRestrictions"), "AccessControl.NamespaceAccessRestrictions")
            .FirstOrDefault(item => string.Equals(Text(item, "NamespaceUri"), uri, StringComparison.Ordinal))
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No namespace access restriction has the URI '{uri}'.");

    // ================================================================ projection

    private static PlcOpcUaInterfaceInfo Interface(string kind, object item)
    {
        var info = new PlcOpcUaInterfaceInfo { Kind = kind, RuntimeType = item.GetType().FullName };
        info.Name = Text(item, "Name", info.Unreadable);
        info.Enabled = Bool(item, "Enabled", info.Unreadable);
        info.Author = Text(item, "Author", info.Unreadable);
        info.Comment = Text(item, "Comment", info.Unreadable);
        info.CreationTime = Text(item, "CreationTime", info.Unreadable);
        info.LastModified = Text(item, "LastModified", info.Unreadable);
        if (kind != PlcOpcUaNames.Kinds.Server) info.UseStringNodeIds = Bool(item, "UseStringNodeIds", info.Unreadable);
        if (kind == PlcOpcUaNames.Kinds.ReferenceNamespace)
        {
            info.GenerateNodes = Bool(item, "GenerateNodes", info.Unreadable);
            info.GeneratedInterfaceName = Text(item, "GeneratedInterfaceName", info.Unreadable);
        }

        return info;
    }

    private static PlcOpcUaRestrictionInfo Restriction(object item)
    {
        var info = new PlcOpcUaRestrictionInfo();
        info.NamespaceUri = Text(item, "NamespaceUri", info.Unreadable);
        info.NamespaceIndex = Text(item, "NamespaceIndex", info.Unreadable);
        info.SessionRequired = Bool(item, "SessionRequired", info.Unreadable);
        info.SigningRequired = Bool(item, "SigningRequired", info.Unreadable);
        info.EncryptionRequired = Bool(item, "EncryptionRequired", info.Unreadable);
        info.ApplyRestrictionsToBrowse = Bool(item, "ApplyRestrictionsToBrowse", info.Unreadable);
        return info;
    }

    private static PlcOpcUaRoleInfo Role(object item)
    {
        var info = new PlcOpcUaRoleInfo();
        info.Name = Text(item, "Name", info.Unreadable);
        info.DefinedInNamespace = Text(item, "DefinedInNamespace", info.Unreadable);
        info.ProjectRole = Text(item, "ProjectRole", info.Unreadable);
        info.RoleId = Text(item, "RoleId", info.Unreadable);
        foreach (var permission in Items(item, "NamespacePermissions", info.Unreadable))
        {
            var entry = new PlcOpcUaPermissionInfo();
            entry.NamespaceUri = Text(permission, "NamespaceUri", entry.Unreadable);
            entry.NamespaceIndex = Text(permission, "NamespaceIndex", entry.Unreadable);
            entry.Browse = Bool(permission, "Browse", entry.Unreadable);
            entry.Read = Bool(permission, "Read", entry.Unreadable);
            entry.Write = Bool(permission, "Write", entry.Unreadable);
            entry.Call = Bool(permission, "Call", entry.Unreadable);
            entry.ReceiveEvents = Bool(permission, "ReceiveEvents", entry.Unreadable);
            entry.ReadRolePermissions = Bool(permission, "ReadRolePermissions", entry.Unreadable);
            info.NamespacePermissions.Add(entry);
        }

        return info;
    }

    // ================================================================ helpers

    private static string Describe(PlcOpcUaRequestInfo request)
        => $"{(request.InterfaceKind == PlcOpcUaNames.Kinds.ReferenceNamespace ? "reference namespace" : request.InterfaceKind + " interface")} '{request.InterfaceName}'";

    private static string Documented(PlcOpcUaRequestInfo request, string member)
        => request.InterfaceKind switch
        {
            PlcOpcUaNames.Kinds.Server => "ServerInterface." + member,
            PlcOpcUaNames.Kinds.Simatic => "SimaticInterface." + member,
            _ => "ReferenceNamespace." + member,
        };

    private static object? Call(object target, string name, Type[] argumentTypes, object[] arguments, string documented, PlcOpcUaWriteResultInfo result)
    {
        var returned = RuntimeServiceValues.Invoke(target, name, argumentTypes, arguments, documented);
        result.Calls.Add(documented);
        return returned;
    }

    /// <summary>Sets each attribute through the generic attribute API (writability checked first for all), then reads every one back.</summary>
    private static void SetAttributes(object target, IReadOnlyList<(string Name, object Value)> values, string description, PlcOpcUaWriteResultInfo result)
    {
        if (target is not IEngineeringObject engineering)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"The {description} is not an engineering object; its attributes cannot be set.");
        foreach (var (name, _) in values) PlcAlarms.PlcTextListResolver.RequireWritable(engineering, name, $"The {description}");
        foreach (var (name, value) in values)
        {
            engineering.SetAttribute(name, value);
            result.Calls.Add($"SetAttribute(\"{name}\", {value})");
        }

        foreach (var (name, value) in values)
        {
            var after = engineering.GetAttribute(name);
            if (!Equals(after, value) && !string.Equals(after?.ToString(), value.ToString(), StringComparison.Ordinal))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"The {description} reads {name} = {after ?? "null"} after it was set to {value}.");
        }
    }

    private static void Expect(string member, string? requested, string? actual)
    {
        if (requested is not null && !string.Equals(requested, actual, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"{member} reads '{actual}' after it was set to '{requested}'.");
    }

    private static object Required(object target, string name, string documented)
    {
        var (found, value) = TechnologyObjectRuntime.Read(target, name);
        if (!found) throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
            $"This TIA Portal installation exposes no readable {documented} on '{target.GetType().FullName}'. Nothing was changed.");
        return value ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, $"{documented} returned nothing. Nothing was changed.");
    }

    private static object? Value(object target, string name, List<string> unreadable, string documented)
    {
        try
        {
            var (found, value) = TechnologyObjectRuntime.Read(target, name);
            if (!found || value is null) unreadable.Add(documented);
            return value;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            unreadable.Add(documented);
            return null;
        }
    }

    private static List<object> Items(object target, string name, List<string> unreadable)
    {
        var collection = Value(target, name, unreadable, name);
        return collection is null ? new List<object>() : TechnologyObjectRuntime.Items(collection, name);
    }

    private static string? Text(object target, string name, List<string>? unreadable = null)
    {
        var value = Value(target, name, unreadable ?? new List<string>(), name);
        return value is null ? null : PlcAlarms.PlcTextListResolver.Text(value);
    }

    private static bool? Bool(object target, string name, List<string> unreadable) => Value(target, name, unreadable, name) as bool?;

    private static string PrivateCopy(string path)
    {
        if (!File.Exists(path)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"filePath '{path}' does not exist.");
        var copy = Path.Combine(Path.GetTempPath(), $"TiaMcp-opcua-import-{Guid.NewGuid():N}{Path.GetExtension(path)}");
        File.Copy(path, copy);
        return copy;
    }

    private static (long Bytes, string Sha256) Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return (stream.Length, string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2"))));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
