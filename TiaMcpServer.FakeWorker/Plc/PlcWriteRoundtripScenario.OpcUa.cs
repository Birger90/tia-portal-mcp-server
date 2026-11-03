using TiaMcpServer.Contracts.Plc.OpcUa;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// plc_read get_opcua_configuration and the plc_write OPC UA operations (issue #24) against an in-memory model. PLC_1 offers the
/// OPC UA provider with the server interface <c>Machine</c> (enabled), the SIMATIC interface <c>SIMATIC</c>, the namespace access
/// restriction <c>http://example.org/Machine</c> (session, signing and encryption required) and the role <c>Operator</c> with a
/// read permission on that namespace; PLC_2 lists no provider. Directory <c>C:\OpcUa</c> takes exports and holds <c>Machine.xml</c>
/// for imports; an import re-exports the imported bytes, so it verifies. This models object access only; it is no evidence of
/// Siemens behavior.
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    private sealed class FakeOpcUa
    {
        public List<PlcOpcUaInterfaceInfo> Interfaces { get; } = new()
        {
            new() { Kind = PlcOpcUaNames.Kinds.Server, Name = "Machine", Enabled = true, Author = "Fake", RuntimeType = "Fake.ServerInterface" },
            new() { Kind = PlcOpcUaNames.Kinds.Simatic, Name = "SIMATIC", Enabled = true, UseStringNodeIds = false, RuntimeType = "Fake.SimaticInterface" },
        };

        public List<PlcOpcUaRestrictionInfo> Restrictions { get; } = new()
        {
            new() { NamespaceUri = "http://example.org/Machine", NamespaceIndex = "3", SessionRequired = true, SigningRequired = true, EncryptionRequired = true, ApplyRestrictionsToBrowse = false },
        };

        public List<PlcOpcUaRoleInfo> Roles { get; } = new()
        {
            new()
            {
                Name = "Operator", DefinedInNamespace = "http://example.org/Machine", ProjectRole = null, RoleId = "ns=3;i=1",
                NamespacePermissions = { new() { NamespaceUri = "http://example.org/Machine", NamespaceIndex = "3", Browse = true, Read = true, Write = false, Call = false, ReceiveEvents = false, ReadRolePermissions = false } },
            },
        };
    }

    private const string OpcUaDirectory = @"C:\OpcUa";

    private readonly FakeOpcUa _opcUa = new();
    private readonly HashSet<string> _opcUaInterfaceFiles = new(StringComparer.OrdinalIgnoreCase) { OpcUaDirectory + @"\Machine.xml" };

    private string OpcUa(WorkerRequest request)
    {
        var info = request.PlcOpcUa ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "missing request");
        info.Operation = request.Method;
        if (PlcOpcUaMethods.IsRead(request.Method))
        {
            var result = new PlcOpcUaConfigurationInfo();
            foreach (var plc in _plcs.Where(p => info.PlcName is null || Matches(p, info.PlcName)))
            {
                if (plc.Software != "PLC_1")
                {
                    result.Plcs.Add(new PlcOpcUaPlcInfo { PlcName = plc.Software, DeviceName = plc.Device, Note = $"The PLC software does not list '{PlcOpcUaNames.Provider}' among its GetServiceInfos()." });
                    continue;
                }

                result.Plcs.Add(new PlcOpcUaPlcInfo
                {
                    PlcName = plc.Software, DeviceName = plc.Device, ProviderAvailable = true,
                    ServerInterfaces = _opcUa.Interfaces.Where(i => i.Kind == PlcOpcUaNames.Kinds.Server).ToList(),
                    SimaticInterfaces = _opcUa.Interfaces.Where(i => i.Kind == PlcOpcUaNames.Kinds.Simatic).ToList(),
                    ReferenceNamespaces = _opcUa.Interfaces.Where(i => i.Kind == PlcOpcUaNames.Kinds.ReferenceNamespace).ToList(),
                    NamespaceAccessRestrictions = _opcUa.Restrictions, RoleMappings = _opcUa.Roles,
                });
            }

            if (result.Plcs.Count == 0) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{info.PlcName}' was found in the project.");
            return Ok(result);
        }

        var target = _plcs.Where(p => info.PlcName is null || Matches(p, info.PlcName)).ToList();
        if (target.Count != 1)
            throw new WorkerOperationException(target.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous, $"PLC '{info.PlcName}' matches {target.Count} PLCs.");
        if (target[0].Software != "PLC_1")
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, $"The PLC software does not list '{PlcOpcUaNames.Provider}' among its GetServiceInfos(). Nothing was changed.");
        var write = new PlcOpcUaWriteResultInfo { Operation = info.Operation, PlcName = target[0].Software, Evidence = PlcOpcUaEvidence.Verified };
        PlcOpcUaInterfaceInfo Interface() => _opcUa.Interfaces.FirstOrDefault(i => i.Kind == info.InterfaceKind && i.Name == info.InterfaceName)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No {info.InterfaceKind} interface '{info.InterfaceName}' is listed.");
        PlcOpcUaRoleInfo Role() => _opcUa.Roles.FirstOrDefault(r => r.Name == info.RoleName)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No role named '{info.RoleName}' is listed.");
        switch (info.Operation)
        {
            case PlcOpcUaMethods.CreateInterface:
                if (_opcUa.Interfaces.Any(i => i.Kind == info.InterfaceKind && i.Name == info.InterfaceName))
                    throw new WorkerOperationException(WorkerFailureCategories.EngineeringRejected, $"An interface named '{info.InterfaceName}' already exists.");
                var created = new PlcOpcUaInterfaceInfo { Kind = info.InterfaceKind!, Name = info.InterfaceName, Enabled = false, RuntimeType = "Fake." + info.InterfaceKind };
                _opcUa.Interfaces.Add(created);
                write.Target = $"{info.InterfaceKind} interface '{info.InterfaceName}'";
                write.Calls.Add("Create(String)");
                write.InterfaceAfter = created;
                return Ok(write);
            case PlcOpcUaMethods.DeleteInterface:
                _opcUa.Interfaces.Remove(Interface());
                write.Target = $"{info.InterfaceKind} interface '{info.InterfaceName}'";
                write.Calls.Add("Delete()");
                write.Evidence = PlcOpcUaEvidence.Deleted;
                return Ok(write);
            case PlcOpcUaMethods.ExportInterface:
                _ = Interface();
                if (!info.FilePath!.StartsWith(OpcUaDirectory + @"\", StringComparison.OrdinalIgnoreCase))
                    throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The directory of filePath '{info.FilePath}' does not exist.");
                if (_opcUaInterfaceFiles.Contains(info.FilePath) && !info.Overwrite)
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"filePath '{info.FilePath}' exists; set overwrite:true to replace it.");
                _opcUaInterfaceFiles.Add(info.FilePath);
                write.Target = $"{info.InterfaceKind} interface '{info.InterfaceName}'";
                write.Calls.Add("Export(FileInfo)");
                (write.FilePath, write.Bytes, write.Sha256, write.Evidence) = (info.FilePath, 2048, new string('e', 64), PlcOpcUaEvidence.File);
                return Ok(write);
            case PlcOpcUaMethods.ImportInterface:
                var imported = Interface();
                if (!_opcUaInterfaceFiles.Contains(info.FilePath!)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"filePath '{info.FilePath}' does not exist.");
                write.Target = $"{info.InterfaceKind} interface '{info.InterfaceName}'";
                write.Calls.Add("Import(FileInfo)");
                write.Calls.Add("Export(FileInfo) (verification re-export)");
                (write.FilePath, write.Bytes, write.Sha256, write.ReExportSha256) = (info.FilePath, 2048, new string('f', 64), new string('f', 64));
                write.InterfaceAfter = imported;
                return Ok(write);
            case PlcOpcUaMethods.SetInterface:
                var item = Interface();
                if (info.Enabled is { } enabled) item.Enabled = enabled;
                if (info.UseStringNodeIds is { } strings) item.UseStringNodeIds = strings;
                if (info.GenerateNodes is { } nodes) item.GenerateNodes = nodes;
                if (info.NewName is { } newName) item.Name = newName;
                write.Target = $"{info.InterfaceKind} interface '{info.InterfaceName}'";
                write.Calls.Add("SetAttribute(...)");
                write.InterfaceAfter = item;
                return Ok(write);
            case PlcOpcUaMethods.SetNamespaceRestriction:
                var restriction = _opcUa.Restrictions.FirstOrDefault(r => r.NamespaceUri == info.NamespaceUri)
                    ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No namespace access restriction has the URI '{info.NamespaceUri}'.");
                restriction.SessionRequired = info.SessionRequired ?? restriction.SessionRequired;
                restriction.SigningRequired = info.SigningRequired ?? restriction.SigningRequired;
                restriction.EncryptionRequired = info.EncryptionRequired ?? restriction.EncryptionRequired;
                restriction.ApplyRestrictionsToBrowse = info.ApplyRestrictionsToBrowse ?? restriction.ApplyRestrictionsToBrowse;
                write.Target = $"namespace access restriction '{info.NamespaceUri}'";
                write.Calls.Add("SetAttribute(...)");
                write.RestrictionAfter = restriction;
                return Ok(write);
            case PlcOpcUaMethods.CreateRole:
            case PlcOpcUaMethods.AddStandardRole:
                var role = new PlcOpcUaRoleInfo { Name = info.RoleName, DefinedInNamespace = info.DefiningNamespace ?? "http://opcfoundation.org/UA/", RoleId = "ns=3;i=9" };
                _opcUa.Roles.Add(role);
                write.Target = $"role '{info.RoleName}'";
                write.Calls.Add(info.Operation == PlcOpcUaMethods.CreateRole ? "RoleMappingComposition.Create(String, String)" : "RoleMappingComposition.AddStandardOpcUaRole(String)");
                write.RoleAfter = role;
                return Ok(write);
            case PlcOpcUaMethods.DeleteRole:
                _opcUa.Roles.Remove(Role());
                write.Target = $"role '{info.RoleName}'";
                write.Calls.Add("RoleMapping.Delete()");
                write.Evidence = PlcOpcUaEvidence.Deleted;
                return Ok(write);
            case PlcOpcUaMethods.SetRole:
                var changed = Role();
                changed.DefinedInNamespace = info.DefiningNamespace ?? changed.DefinedInNamespace;
                changed.ProjectRole = info.ProjectRole ?? changed.ProjectRole;
                changed.Name = info.NewRoleName ?? changed.Name;
                write.Target = $"role '{info.RoleName}'";
                write.Calls.Add("RoleMapping.Set...(String)");
                write.RoleAfter = changed;
                return Ok(write);
            case PlcOpcUaMethods.SetNamespacePermission:
                var owner = Role();
                var permission = owner.NamespacePermissions.FirstOrDefault(p => p.NamespaceUri == info.NamespaceUri)
                    ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Role '{info.RoleName}' lists no namespace permission for '{info.NamespaceUri}'.");
                write.Target = $"permission '{info.Permission}' of role '{info.RoleName}' on '{info.NamespaceUri}'";
                write.Calls.Add("NamespacePermission.SetPermission(String, Boolean)");
                switch (info.Permission)
                {
                    case "Write": permission.Write = info.Enabled; break;
                    case "Read": permission.Read = info.Enabled; break;
                    case "Browse": permission.Browse = info.Enabled; break;
                    case "Call": permission.Call = info.Enabled; break;
                    default:
                        write.Evidence = PlcOpcUaEvidence.NotReadBack;
                        write.Note = $"'{info.Permission}' is not one of the documented NamespacePermission properties; the change is not read back.";
                        break;
                }

                write.RoleAfter = owner;
                return Ok(write);
            default:
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"unexpected OPC UA method '{info.Operation}'");
        }
    }
}
