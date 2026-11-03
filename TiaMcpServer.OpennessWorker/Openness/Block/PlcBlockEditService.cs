using System.Globalization;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;
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
/// Granular block writes (the reads are in PlcBlockEditService.Reads.cs). Native Openness members are
/// used where the V21 references document them (Member.Get/SetAttribute for DB values,
/// CreateInstanceDB, SetAttribute Name / AutoNumber / Number); copy and move use the documented
/// SimaticML export/import round trip inside one ExclusiveAccess transaction (<see cref="PlcRoundTrip"/>)
/// and refuse know-how-protected blocks; fail-safe blocks follow TIA Portal's safety login rule (<see cref="Safety.PlcSafetyGate"/>).
/// </summary>
internal static partial class PlcBlockEditService
{
    // ---- writes -----------------------------------------------------------------------------

    public static PlcDbMemberUpdateResultInfo UpdateDbMember(ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var blockPath = Require(request.BlockPath, "blockPath");
        var path = NativePath(Require(request.MemberPath, "memberPath"));
        var (plc, located) = Locate(project, blockPath);
        Safety.PlcSafetyGate.RequireChangePermitted(project, plc.Name, located.Block, PlcBlockEditMethods.UpdateDbMember);
        var db = RequireDataBlock(located);
        var member = db.Interface.Members.Find(path)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Member '{path}' was not found in '{located.CanonicalPath}' (Interface.Members.Find returned none).");
        var engineering = (IEngineeringObject)member;
        var requested = new List<(string Attribute, object Value)>();
        if (request.StartValue is not null) requested.Add(("StartValue", request.StartValue));
        if (request.ExternalAccessible is { } accessible) requested.Add(("ExternalAccessible", accessible));
        if (request.ExternalVisible is { } visible) requested.Add(("ExternalVisible", visible));
        if (request.ExternalWritable is { } writable) requested.Add(("ExternalWritable", writable));
        if (request.Retain is { } retain) requested.Add(("Retain", retain));
        if (request.SetPoint is { } setPoint) requested.Add(("SetPoint", setPoint));
        if (requested.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "update_db_member needs startValue or at least one attribute.");

        var result = new PlcDbMemberUpdateResultInfo
        {
            Block = BlockLocator.Identity(plc.Name, located.CanonicalPath, located.Block),
            MemberPath = path,
        };
        foreach (var (attribute, value) in requested)
        {
            var change = new PlcDbAttributeChangeInfo { Attribute = attribute, Requested = Text(value)!, Previous = BlockLocator.Read(() => Text(engineering.GetAttribute(attribute))) };
            result.Changes.Add(change);
            engineering.SetAttribute(attribute, value);
            change.Observed = BlockLocator.Read(() => Text(engineering.GetAttribute(attribute)));
        }

        // Kept the previous value: the read-back is the old text and not the requested one (an equal number in another spelling counts as requested).
        var ignored = result.Changes.Where(c => !Same(c.Observed, c.Requested) && !PlcStartValueLiterals.SameNumber(c.Observed, c.Requested)
            && Same(c.Observed, c.Previous)).ToList();
        if (ignored.Count > 0)
        {
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"TIA Portal kept the previous value of {string.Join(", ", ignored.Select(c => $"{c.Attribute} ('{c.Observed}', requested '{c.Requested}')"))} on '{located.CanonicalPath}.{path}'. "
                + $"Attributes set before it in this item stay applied: {string.Join(", ", result.Changes.TakeWhile(c => !ignored.Contains(c)).Select(c => c.Attribute))}.");
        }

        return result;
    }

    public static PlcBlockEditResultInfo CreateInstanceDb(ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var blockPath = Require(request.BlockPath, "blockPath");
        var owner = Require(request.InstanceOfName, "instanceOfName");
        var address = ParseBlock(blockPath);
        if (address.PlcName is null)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "create_instance_db needs 'PLC/Name' or 'PLC/Blocks/.../Name'.");
        var plc = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
        var group = BlockMutationService.ResolveGroupFromAddress(plc, address);
        var created = group.Blocks.CreateInstanceDB(address.BlockName, request.Number is null, request.Number ?? 1, owner)
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "CreateInstanceDB returned no block.");
        var located = BlockLocator.Locate(plc, address.IsDeterministic ? address : ParseBlock($"{plc.Name}/{created.Name}"));
        var identity = BlockLocator.Identity(plc.Name, located.CanonicalPath, located.Block);
        var problems = new List<string>();
        if (!string.Equals(identity.Name, address.BlockName, StringComparison.Ordinal)) problems.Add($"name '{identity.Name}'");
        if (!string.Equals(identity.InstanceOfName, owner, StringComparison.OrdinalIgnoreCase)) problems.Add($"InstanceOfName '{identity.InstanceOfName ?? "(unreadable)"}'");
        if (request.Number is { } number && identity.Number != number) problems.Add($"number {identity.Number?.ToString() ?? "(unreadable)"}");
        if (problems.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"The instance DB was created but reads back with {string.Join(", ", problems)} instead of the request; inspect '{located.CanonicalPath}'.");
        return new PlcBlockEditResultInfo { Operation = PlcBlockEditMethods.CreateInstanceDb, Block = identity };
    }

    public static PlcBlockEditResultInfo RenameBlock(ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var blockPath = Require(request.BlockPath, "blockPath");
        var newName = Require(request.NewName, "newName");
        var (plc, located) = Locate(project, blockPath);
        Safety.PlcSafetyGate.RequireChangePermitted(project, plc.Name, located.Block, PlcBlockEditMethods.RenameBlock);
        var source = located.CanonicalPath;
        ((IEngineeringObject)located.Block).SetAttribute("Name", newName);
        var observed = located.Block.Name;
        if (!string.Equals(observed, newName, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"TIA Portal kept the name '{observed}' for '{source}' (requested '{newName}'). Nothing else was changed.");
        return new PlcBlockEditResultInfo
        {
            Operation = PlcBlockEditMethods.RenameBlock,
            SourceBlockPath = source,
            Block = BlockLocator.Identity(plc.Name, LocatedBlock.GroupPath(located.Owner, located.Folders) + "/" + observed, located.Block),
        };
    }

    public static PlcBlockEditResultInfo RenumberBlock(ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var blockPath = Require(request.BlockPath, "blockPath");
        var (plc, located) = Locate(project, blockPath);
        Safety.PlcSafetyGate.RequireChangePermitted(project, plc.Name, located.Block, PlcBlockEditMethods.RenumberBlock);
        var block = (IEngineeringObject)located.Block;
        if (request.AutoNumber == true)
        {
            if (request.Number is not null)
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "renumber_block takes either number or autoNumber:true.");
            block.SetAttribute("AutoNumber", true);
        }
        else
        {
            var number = request.Number ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "renumber_block needs number or autoNumber:true.");
            block.SetAttribute("AutoNumber", false);
            block.SetAttribute("Number", number);
        }

        var identity = BlockLocator.Identity(plc.Name, located.CanonicalPath, located.Block);
        var failed = request.AutoNumber == true
            ? identity.AutoNumber != true ? $"AutoNumber reads back as {identity.AutoNumber?.ToString() ?? "(unreadable)"}" : null
            : identity.Number != request.Number ? $"Number reads back as {identity.Number?.ToString() ?? "(unreadable)"} (AutoNumber {identity.AutoNumber?.ToString() ?? "(unreadable)"})" : null;
        if (failed is not null)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"Renumbering '{located.CanonicalPath}' did not take effect: {failed}. AutoNumber may already have been switched off.");
        return new PlcBlockEditResultInfo { Operation = PlcBlockEditMethods.RenumberBlock, SourceBlockPath = located.CanonicalPath, Block = identity };
    }

    /// <summary>
    /// Imports the source's SimaticML export under the new name inside one ExclusiveAccess transaction,
    /// then sets the requested number (<c>SetAttribute("AutoNumber"/"Number")</c>) and reads the copy
    /// back; any failure rolls the transaction back, so no partial copy is left, and the failure says
    /// whether a copy is present.
    /// </summary>
    public static PlcBlockEditResultInfo CopyBlock(TiaPortal portal, ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var blockPath = Require(request.BlockPath, "blockPath");
        var newName = Require(request.NewName, "newName");
        var (plc, located) = Locate(project, blockPath);
        var document = ExportForRoundTrip(project, plc, located, PlcBlockEditMethods.CopyBlock);
        var target = ResolveTarget(project, plc, located, request.TargetGroupPath);
        document.PrepareCopy(newName);
        var xml = document.Serialize();
        var outcome = PlcRoundTrip.Run(portal, project, $"copy_block '{located.CanonicalPath}' as '{newName}'", () =>
        {
            var created = ImportBlock(target.Group, xml, newName);
            if (request.Number is { } number)
            {
                var engineering = (IEngineeringObject)created;
                engineering.SetAttribute("AutoNumber", false);
                engineering.SetAttribute("Number", number);
            }

            var identity = BlockLocator.Identity(plc.Name, target.Path + "/" + created.Name, created);
            if (!string.Equals(identity.Name, newName, StringComparison.Ordinal) || request.Number is { } requested && identity.Number != requested)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"The copy '{identity.BlockPath}' read back as name '{identity.Name}', number {identity.Number?.ToString(CultureInfo.InvariantCulture) ?? "(unreadable)"} instead of the request.");
            return identity;
        });
        if (outcome.State == PlcRoundTrip.TransactionState.Committed)
        {
            return new PlcBlockEditResultInfo
            {
                Operation = PlcBlockEditMethods.CopyBlock, SourceBlockPath = located.CanonicalPath, Block = outcome.Result!,
                Messages = new List<string> { "The copy was imported from the source's SimaticML export: a library-type link is not carried over (Siemens: instances become detached on re-import)." },
            };
        }

        throw PlcRoundTrip.CopyFailure(outcome, new PlcRoundTrip.Subject
        {
            Operation = PlcBlockEditMethods.CopyBlock, ObjectKind = "block", ObjectName = newName, PlcName = plc.Name,
            SourcePath = located.CanonicalPath, TargetPath = target.Path,
        }, () => target.Group.Blocks.Find(newName) is not null);
    }

    /// <summary>
    /// Exports the block, then deletes it and imports the export into the target group inside one
    /// ExclusiveAccess transaction and reads it back there; any failure rolls the transaction back.
    /// Only when the rollback did not restore the source is it re-imported, and only when that fails is
    /// its export kept as a recovery file (<see cref="PlcRoundTrip.MoveFailure{T}"/>).
    /// </summary>
    public static PlcBlockEditResultInfo MoveBlock(TiaPortal portal, ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var blockPath = Require(request.BlockPath, "blockPath");
        var targetPath = Require(request.TargetGroupPath, "targetGroupPath");
        var (plc, located) = Locate(project, blockPath);
        var target = ResolveTarget(project, plc, located, targetPath);
        if (target.Group.Equals(located.Group))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{located.CanonicalPath}' is already in '{target.Path}'.");
        var document = ExportForRoundTrip(project, plc, located, PlcBlockEditMethods.MoveBlock);
        var xml = document.Serialize();
        var name = located.Block.Name;
        var number = BlockLocator.Read(() => (int?)located.Block.Number);
        var source = located.CanonicalPath;

        var outcome = PlcRoundTrip.Run(portal, project, $"move_block '{source}' to '{target.Path}'", () =>
        {
            located.Block.Delete();
            var moved = ImportBlock(target.Group, xml, name);
            var identity = BlockLocator.Identity(plc.Name, target.Path + "/" + moved.Name, moved);
            if (!string.Equals(identity.Name, name, StringComparison.Ordinal) || number is not null && identity.Number != number)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"The moved block '{identity.BlockPath}' read back as name '{identity.Name}', number {identity.Number?.ToString(CultureInfo.InvariantCulture) ?? "(unreadable)"} (was '{name}', {number?.ToString(CultureInfo.InvariantCulture) ?? "unreadable"}).");
            return identity;
        });
        if (outcome.State == PlcRoundTrip.TransactionState.Committed)
        {
            return new PlcBlockEditResultInfo
            {
                Operation = PlcBlockEditMethods.MoveBlock, SourceBlockPath = source, Block = outcome.Result!,
                Messages = new List<string>
                {
                    "The block was moved by SimaticML export, delete and import in one transaction: it has a new object identity, a library-type link is not carried over, and instance DBs and callers resolve it again by name at the next compile.",
                },
            };
        }

        throw PlcRoundTrip.MoveFailure(outcome, new PlcRoundTrip.Subject
        {
            Operation = PlcBlockEditMethods.MoveBlock, ObjectKind = "block", ObjectName = name, PlcName = plc.Name,
            SourcePath = source, TargetPath = target.Path, Document = xml,
        },
            sourcePresent: () => located.Group.Blocks.Find(name) is not null,
            targetPresent: () => target.Group.Blocks.Find(name) is not null,
            restore: () => ImportBlock(located.Group, xml, name),
            existingDocumentPath: null, out _);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static (PlcSoftware Plc, LocatedBlock Located) Locate(ProjectBase project, string blockPath)
    {
        var address = ParseBlock(blockPath);
        var plc = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
        return (plc, BlockLocator.Locate(plc, address));
    }

    private static DataBlock RequireDataBlock(LocatedBlock located)
        => located.Block is DataBlock db and not ArrayDB
            ? db
            : throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"'{located.CanonicalPath}' is a {BlockLocator.KindName(located.Block)}; DB values are read and written on global and instance DBs.");

    /// <summary>
    /// Refuses know-how-protected (or unverifiable) blocks before any round trip: their SimaticML export carries only the
    /// interface and Openness offers no protected import (IMPORT_EXPORT_OPTIONS_SUMMARY), so this route cannot carry the
    /// code. A fail-safe block is exported and imported like any other block (the generic XML route TIA Portal offers for
    /// consistent F-blocks) under the safety login rule; TIA Portal's rejection, if any, rolls the transaction back.
    /// </summary>
    private static SimaticMlInterfaceDocument ExportForRoundTrip(ProjectBase project, PlcSoftware plc, LocatedBlock located, string operation)
    {
        var protectedState = BlockLocator.Read(() => (bool?)located.Block.IsKnowHowProtected);
        if (protectedState != false)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, protectedState == true
                ? $"'{located.CanonicalPath}' is know-how protected; its export carries only the interface and Openness has no protected import, so it is not copied or moved (copy it in TIA Portal)."
                : $"The know-how protection of '{located.CanonicalPath}' could not be read, so it is not copied or moved: a protected block would lose its code on this route.");
        Safety.PlcSafetyGate.RequireChangePermitted(project, plc.Name, located.Block, operation);
        return SimaticMlInterfaceDocument.Parse(BlockExporter.ExportSimaticMl(located));
    }

    private static LocatedBlockGroup ResolveTarget(ProjectBase project, PlcSoftware plc, LocatedBlock located, string? targetGroupPath)
    {
        if (targetGroupPath is null) return new LocatedBlockGroup(located.Owner, located.Group, located.Folders);
        var target = BlockLocator.LocateGroup(project, plc, targetGroupPath);
        if (!string.Equals(target.Owner.RootBlocksPath, located.Owner.RootBlocksPath, StringComparison.OrdinalIgnoreCase))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"'{target.Path}' belongs to another software scope than '{located.CanonicalPath}'; copy and move stay within one PLC or software unit.");
        return target;
    }

    private static PlcBlock ImportBlock(PlcBlockGroup group, string xml, string expectedName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-block-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "block.xml");
            File.WriteAllText(path, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            foreach (PlcBlock block in group.Blocks.Import(new FileInfo(path), ImportOptions.None))
                return block;
            return group.Blocks.Find(expectedName)
                ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"Importing '{expectedName}' returned no block.");
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(directory);
        }
    }

    /// <summary>The path as Interface.Members.Find takes it: names joined with '.', indices in brackets.</summary>
    internal static string NativePath(string memberPath)
    {
        try
        {
            return PlcMemberPath.Parse(memberPath, allowIndices: true).ToFindPath();
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, exception.Message);
        }
    }

    /// <summary>Attribute values as invariant text, so a worker on any Windows culture reports the same literal.</summary>
    internal static string? Text(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "True" : "False",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static BlockAddress ParseBlock(string blockPath)
    {
        try
        {
            return BlockAddress.Parse(blockPath);
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, exception.Message);
        }
    }

    internal static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{field} is required.")
            : value!;
}
