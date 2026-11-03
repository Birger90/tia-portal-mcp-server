using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>A user block resolved together with its owner scope and folder names, so its canonical path is known.</summary>
internal sealed class LocatedBlock
{
    public LocatedBlock(ResolvedBlockOwner owner, PlcBlockGroup group, IReadOnlyList<string> folders, PlcBlock block)
    {
        Owner = owner;
        Group = group;
        Folders = folders;
        Block = block;
    }

    public ResolvedBlockOwner Owner { get; }

    public PlcBlockGroup Group { get; }

    public IReadOnlyList<string> Folders { get; }

    public PlcBlock Block { get; }

    public string CanonicalPath => GroupPath(Owner, Folders) + "/" + Block.Name;

    internal static string GroupPath(ResolvedBlockOwner owner, IReadOnlyList<string> folders)
        => folders.Count == 0 ? owner.RootBlocksPath : owner.RootBlocksPath + "/" + string.Join("/", folders);
}

/// <summary>A resolved block group with its owner scope and canonical folder names.</summary>
internal sealed class LocatedBlockGroup
{
    public LocatedBlockGroup(ResolvedBlockOwner owner, PlcBlockGroup group, IReadOnlyList<string> folders)
    {
        Owner = owner;
        Group = group;
        Folders = folders;
    }

    public ResolvedBlockOwner Owner { get; }

    public PlcBlockGroup Group { get; }

    public IReadOnlyList<string> Folders { get; }

    public string Path => LocatedBlock.GroupPath(Owner, Folders);
}

/// <summary>
/// Same lookup rules as <see cref="BlockTargetResolver.ResolveForExport(PlcSoftware, BlockAddress)"/>
/// (deterministic path, or a unique name across the PLC and its software units), but the walk keeps
/// the owner and folder names so the canonical path can be reported and reused.
/// </summary>
internal static class BlockLocator
{
    public static LocatedBlock Locate(PlcSoftware plcSoftware, BlockAddress address)
    {
        if (address.IsDeterministic)
        {
            var owner = BlockTargetResolver.ResolveOwnerForDeterministicPath(plcSoftware, address);
            var resolved = BlockTargetResolver.ResolveBlockGroupPath(owner.RootBlockGroup, address.FolderPath);
            var block = resolved.Group.Blocks.Find(address.BlockName)
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{address.BlockName}' was not found at '{address.ToDisplayPath()}'.");
            return new LocatedBlock(owner, resolved.Group, resolved.ResolvedFolderNames, block);
        }

        var matches = new List<LocatedBlock>();
        foreach (var owner in BlockTargetResolver.EnumerateOwners(plcSoftware))
            Collect(owner, owner.RootBlockGroup, new List<string>(), address.BlockName, matches);
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{address.BlockName}' not found."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                $"Block '{address.BlockName}' is ambiguous. Use the deterministic Path from browse_project_tree, for example '{matches[0].CanonicalPath}'."),
        };
    }

    /// <summary>
    /// A target block group: a deterministic group path ('PLC/Blocks', 'PLC/Blocks/Folder/...',
    /// 'PLC/Units/Unit/Blocks/...').
    /// </summary>
    public static LocatedBlockGroup LocateGroup(ProjectBase project, PlcSoftware plcSoftware, string groupPath)
    {
        BlockAddress address;
        try
        {
            // The group is the parent folder of a placeholder name under it.
            address = BlockAddress.Parse(groupPath.TrimEnd('/') + "/_");
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"targetGroupPath '{groupPath}': {exception.Message}");
        }

        if (!address.IsDeterministic)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"targetGroupPath '{groupPath}' must be a deterministic group path such as 'PLC/Blocks' or 'PLC/Blocks/Folder'.");
        if (!PlcSoftwareLocator.FindUnique(project, address.PlcName).Software.Equals(plcSoftware))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"targetGroupPath '{groupPath}' names another PLC than '{plcSoftware.Name}'; copy and move stay within one PLC.");
        var owner = BlockTargetResolver.ResolveOwnerForDeterministicPath(plcSoftware, address);
        var resolved = BlockTargetResolver.ResolveBlockGroupPath(owner.RootBlockGroup, address.FolderPath);
        return new LocatedBlockGroup(owner, resolved.Group, resolved.ResolvedFolderNames);
    }

    public static PlcBlockIdentityInfo Identity(string plcName, string canonicalPath, PlcBlock block)
        => new()
        {
            PlcName = plcName,
            BlockPath = canonicalPath,
            Name = block.Name,
            BlockType = KindName(block),
            Number = Read(() => (int?)block.Number),
            AutoNumber = Read(() => ((IEngineeringObject)block).GetAttribute("AutoNumber") as bool?),
            ProgrammingLanguage = Read(() => block.ProgrammingLanguage.ToString()),
            IsKnowHowProtected = Read(() => (bool?)block.IsKnowHowProtected),
            InstanceOfName = block is InstanceDB ? Read(() => ((IEngineeringObject)block).GetAttribute("InstanceOfName")?.ToString()) : null,
        };

    public static string KindName(PlcBlock block) => block switch
    {
        GlobalDB => "GlobalDB",
        InstanceDB => "InstanceDB",
        ArrayDB => "ArrayDB",
        OB => "OB",
        FB => "FB",
        FC => "FC",
        _ => block.GetType().Name,
    };

    /// <summary>A diagnostic read: an unreadable value is null, never a failed operation; Portal loss still escapes.</summary>
    internal static T? Read<T>(Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is EngineeringException && OpennessExceptions.IsRecoverable(exception))
        {
            return default;
        }
    }

    private static void Collect(ResolvedBlockOwner owner, PlcBlockGroup group, List<string> folders, string name, List<LocatedBlock> matches)
    {
        var block = group.Blocks.Find(name);
        if (block is not null) matches.Add(new LocatedBlock(owner, group, folders.ToArray(), block));
        foreach (PlcBlockGroup child in group.Groups)
        {
            folders.Add(child.Name);
            Collect(owner, child, folders, name, matches);
            folders.RemoveAt(folders.Count - 1);
        }
    }
}
