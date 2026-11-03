using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// get_block_interface, get_type_interface and get_db_values: the exported SimaticML interface
/// trees, and DB values read natively with <c>Interface.Members.Find</c> /
/// <c>Member.GetAttribute</c>.
/// </summary>
internal static partial class PlcBlockEditService
{
    private static readonly string[] InstanceSections = { "Input", "Output", "InOut", "Static" };

    public static PlcBlockInterfaceInfo GetBlockInterface(ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var blockPath = Require(request.BlockPath, "blockPath");
        var (plc, located) = Locate(project, blockPath);
        var bundle = BlockExporter.Export(project, blockPath, SourceFormatNames.Xml);
        var info = new PlcBlockInterfaceInfo
        {
            Block = BlockLocator.Identity(plc.Name, located.CanonicalPath, located.Block),
            ContentHash = ContentHashRules.Compute(SourceFormatNames.Xml, bundle),
        };
        if (!PlcBlockBundleText.TryGetSimaticMl(bundle, out _, out var xml))
        {
            info.Messages.Add("The export has no SimaticML document (the block may be inconsistent; compile it and read again).");
            return info;
        }

        Describe(xml, info.Messages, (element, safety, sections) =>
        {
            info.DocumentElement = element;
            info.IsSafety = safety;
            info.Sections = sections;
        });
        return info;
    }

    public static PlcTypeInterfaceInfo GetTypeInterface(ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var typePath = Require(request.TypePath, "typePath");
        var address = ParseType(typePath);
        var plc = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
        var xml = PlcTypeExporter.Export(project, typePath, SourceFormatNames.Xml);
        var info = new PlcTypeInterfaceInfo
        {
            PlcName = plc.Name,
            TypePath = typePath,
            Name = address.TypeName,
            ContentHash = ContentHashRules.Compute(SourceFormatNames.Xml, xml),
        };
        Describe(xml, info.Messages, (element, _, sections) =>
        {
            info.DocumentElement = element;
            info.Sections = sections;
        });
        return info;
    }

    public static PlcDbValuesInfo GetDbValues(ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var blockPath = Require(request.BlockPath, "blockPath");
        var (plc, located) = Locate(project, blockPath);
        var db = RequireDataBlock(located);
        var identity = BlockLocator.Identity(plc.Name, located.CanonicalPath, located.Block);
        var subtree = request.MemberPath is null ? null : NativePath(request.MemberPath);
        var result = new PlcDbValuesInfo { Block = identity, MemberPath = subtree, IsComplete = true };

        var cache = new Dictionary<string, IReadOnlyList<PlcInterfaceMemberInfo>?>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<PlcInterfaceMemberInfo>? Resolve(string typeName)
        {
            if (!cache.TryGetValue(typeName, out var members))
                cache[typeName] = members = TypeMembers(project, plc, typeName, result.Messages);
            return members;
        }

        IReadOnlyList<PlcInterfaceMemberInfo> roots;
        if (located.Block is InstanceDB)
        {
            var owner = identity.InstanceOfName
                ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"The FB of instance DB '{located.CanonicalPath}' (InstanceOfName) could not be read.");
            roots = Resolve(owner) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"The interface of '{owner}', which instance DB '{located.CanonicalPath}' belongs to, could not be exported, so its values cannot be enumerated. Read one value with memberPath.");
        }
        else
        {
            var document = SimaticMlInterfaceDocument.Parse(BlockExporter.ExportSimaticMl(located));
            roots = document.ReadSections().SelectMany(section => section.Members).ToList();
        }

        var tree = PlcDbValueTree.Build(roots, Resolve, subtree);
        result.Messages.AddRange(tree.Messages);
        result.IsComplete = tree.IsComplete;
        var entries = tree.Entries.ToList();
        if (subtree is not null && !tree.SubtreeFound)
        {
            // Not enumerable from the documents (for example inside a system type): ask Openness directly.
            entries.Add(new PlcDbValueEntry(subtree, null, PlcDbValueKinds.Value, null));
            result.Messages.Add($"'{subtree}' is not part of the enumerated interface; it was looked up directly with Interface.Members.Find.");
        }

        foreach (var entry in entries)
        {
            var value = new PlcDbValueInfo { Path = entry.Path, DataType = entry.DataType, Kind = entry.Kind, Message = entry.Message };
            if (entry.Kind == PlcDbValueKinds.Value)
            {
                var member = db.Interface.Members.Find(entry.Path);
                if (member is null)
                {
                    value.Kind = PlcDbValueKinds.Missing;
                    value.Message = "Interface.Members.Find returned no member for this path.";
                    result.IsComplete = false;
                }
                else
                {
                    var engineering = (IEngineeringObject)member;
                    try
                    {
                        value.StartValue = Text(engineering.GetAttribute("StartValue"));
                    }
                    catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
                    {
                        value.Message = "StartValue could not be read: " + exception.Message;
                        result.IsComplete = false;
                    }

                    if (request.WithAttributes == true) value.Attributes = Attributes(engineering);
                }
            }

            result.Values.Add(value);
        }

        return result;
    }

    private static IReadOnlyList<PlcInterfaceMemberInfo>? TypeMembers(ProjectBase project, PlcSoftware plc, string typeName, List<string> messages)
    {
        try
        {
            var type = PlcTypeTargetResolver.ResolveForExport(project, PlcTypeAddress.Parse($"{plc.Name}/{typeName}")).Type;
            if (type is not null)
                return SimaticMlInterfaceDocument.Parse(BlockExporter.ExportSimaticMl(type, typeName)).ReadSections().SelectMany(s => s.Members).ToList();
        }
        catch (Exception exception) when (exception is WorkerOperationException { FailureCategory: WorkerFailureCategories.TargetNotFound } || exception is ArgumentException)
        {
            // Not a PLC data type: try an FB (multi-instance or instance DB owner) below.
        }
        catch (Exception exception) when (exception is WorkerOperationException or PlcInterfaceEditException
            || exception is EngineeringException && OpennessExceptions.IsRecoverable(exception))
        {
            messages.Add($"Type '{typeName}' could not be read: {exception.Message}");
            return null;
        }

        try
        {
            var located = BlockLocator.Locate(plc, BlockAddress.Parse($"{plc.Name}/{typeName}"));
            if (located.Block is not FB) return null;
            var sections = SimaticMlInterfaceDocument.Parse(BlockExporter.ExportSimaticMl(located)).ReadSections();
            return InstanceSections.SelectMany(name => sections.Where(s => s.Name == name).SelectMany(s => s.Members)).ToList();
        }
        catch (Exception exception) when (exception is WorkerOperationException or PlcInterfaceEditException or ArgumentException
            || exception is EngineeringException && OpennessExceptions.IsRecoverable(exception))
        {
            if (exception is not WorkerOperationException { FailureCategory: WorkerFailureCategories.TargetNotFound })
                messages.Add($"Type '{typeName}' could not be read: {exception.Message}");
            return null;
        }
    }

    private static PlcDbMemberAttributesInfo Attributes(IEngineeringObject member)
        => new()
        {
            ExternalAccessible = Flag(member, "ExternalAccessible"),
            ExternalVisible = Flag(member, "ExternalVisible"),
            ExternalWritable = Flag(member, "ExternalWritable"),
            Retain = Flag(member, "Retain"),
            SetPoint = Flag(member, "SetPoint"),
        };

    private static bool? Flag(IEngineeringObject member, string attribute) => BlockLocator.Read(() => member.GetAttribute(attribute) as bool?);

    private static void Describe(string xml, List<string> messages, Action<string, bool, List<PlcInterfaceSectionInfo>> accept)
    {
        try
        {
            var document = SimaticMlInterfaceDocument.Parse(xml);
            accept(document.DocumentElement, document.IsSafety, document.ReadSections());
            if (!document.HasInterface) messages.Add("The SimaticML document has no AttributeList/Interface/Sections element.");
        }
        catch (PlcInterfaceEditException exception)
        {
            messages.Add(exception.Message);
        }
    }

    private static PlcTypeAddress ParseType(string typePath)
    {
        try
        {
            return PlcTypeAddress.Parse(typePath);
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, exception.Message);
        }
    }
}
