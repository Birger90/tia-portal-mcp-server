using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// The external-source operations: <c>list_external_sources</c>, <c>import_external_source</c>
/// (<c>PlcExternalSourceComposition.CreateFromFile(name, path)</c>), <c>generate_blocks_from_source</c>
/// (<c>PlcExternalSource.GenerateBlocksFromSource</c> with a GenerateBlockOption, into the scope root or a block
/// or PLC data type user group), <c>delete_external_source</c> (<c>PlcExternalSource.Delete()</c>) and
/// <c>export_source_file</c> (<c>PlcExternalSourceSystemGroup.GenerateSource(objects, file, options)</c> into a
/// temporary file next to the target, then moved into place). Every scope (the PLC and each software unit)
/// owns its own <c>ExternalSourceGroup</c>; only its root sources are addressed, because the blocks reference
/// states that groups inside the "External source files" folder are not supported. Imports, deletes and
/// generated sources are read back with <c>Find(name)</c>. Nothing runs inside a transaction.
/// </summary>
internal static class PlcExternalSourceService
{
    public static PlcExternalSourceInventoryInfo List(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var failures = new List<string>();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, request.PlcName, failures).ToList();
        if (request.PlcName is not null && plcs.Count == 0 && failures.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{request.PlcName}' was found in the project.");

        var inventory = new PlcExternalSourceInventoryInfo { Complete = failures.Count == 0, Messages = failures };
        foreach (var plc in plcs)
        {
            var info = new PlcExternalSourcePlcInfo { PlcName = plc.Software.Name ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty };
            AddSources(info, inventory, () => plc.Software.ExternalSourceGroup, unitName: null);
            PlcUnitProvider? provider = null;
            try
            {
                provider = plc.Software.GetService<PlcUnitProvider>();
            }
            catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
            {
                Incomplete(inventory, $"The software units of PLC '{info.PlcName}' could not be read: {exception.Message}");
            }

            if (provider is not null)
            {
                try
                {
                    foreach (var unit in PlcUnits.All(provider.UnitGroup)) // Audit T4: safety units too.
                        AddSources(info, inventory, () => unit.ExternalSourceGroup, unit.Name);
                }
                catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
                {
                    // A unit that cannot be enumerated makes the inventory incomplete; it never fails the read.
                    Incomplete(inventory, $"The software units of PLC '{info.PlcName}' could not be enumerated: {exception.Message}");
                }
            }
            inventory.Plcs.Add(info);
        }

        return inventory;
    }

    public static PlcExternalSourceMutationInfo Import(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var path = RequirePath(request.FilePath, "filePath");
        if (!File.Exists(path))
            throw Invalid(Directory.Exists(path) ? $"'{path}' is a directory, not a source file. Nothing was imported." : $"The source file '{path}' does not exist. Nothing was imported.");
        var scope = Scope(project, request);
        RequireExtension(path, scope.UnitName, "filePath");
        var name = string.IsNullOrWhiteSpace(request.SourceName) ? PlcFileTargets.DefaultSourceName(path) : request.SourceName!.Trim();
        scope.Group.ExternalSources.CreateFromFile(name, path);
        if (scope.Group.ExternalSources.Find(name) is null)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"CreateFromFile returned, but external source '{name}' is not found in {Describe(scope)} afterwards. Inspect it with plc_read list_external_sources.");
        return Mutation(PlcSourceFolderMethods.ImportExternalSource, scope, name, path);
    }

    public static PlcSourceGenerationInfo Generate(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var scope = Scope(project, request);
        var name = RequireText(request.SourceName, "sourceName");
        var source = scope.Group.ExternalSources.Find(name) ?? throw new WorkerOperationException(
            WorkerFailureCategories.TargetNotFound, $"External source '{name}' was not found in {Describe(scope)}. Nothing was generated.");
        var optionName = request.GenerateOption ?? "None";
        if (!PlcSourceFolderMethods.GenerateBlockOptions.Contains(optionName, StringComparer.Ordinal))
            throw Invalid($"generateOption must be one of {string.Join(", ", PlcSourceFolderMethods.GenerateBlockOptions)} (received '{optionName}').");
        // None is the compiled member the existing source imports already pass; KeepOnError has no documented
        // value and is taken from the installed assembly by its exact name (fails closed when absent).
        var option = optionName == "None"
            ? GenerateBlockOption.None
            : (GenerateBlockOption)LibraryReflection.EnumValue(typeof(GenerateBlockOption), optionName, "GenerateBlockOption");

        IList<IEngineeringObject> generated;
        string? usedGroup = null;
        var target = request.TargetGroupPath is null ? null : RequireGroupPath(request.TargetGroupPath, "targetGroupPath");
        if (target is not null && !SameScope(target, scope))
            throw Invalid($"targetGroupPath '{request.TargetGroupPath}' is not in the scope of the source ({Describe(scope)}); generate into a group of the same PLC and software unit.");
        if (target is null || target.IsRoot)
        {
            generated = source.GenerateBlocksFromSource(option);
        }
        else if (target.Kind == PlcGroupPath.BlocksKind)
        {
            var group = BlockTargetResolver.ResolveBlockGroupPath(BlockRoot(scope), target.Folders).Group as PlcBlockUserGroup
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"'{request.TargetGroupPath}' is not a block user group.");
            generated = source.GenerateBlocksFromSource(group, option);
            usedGroup = target.ToDisplayPath();
        }
        else
        {
            var group = PlcSourceFolderTree.TypeGroup(TypeRoot(scope), target.Folders) as PlcTypeUserGroup
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"'{request.TargetGroupPath}' is not a PLC data type user group.");
            generated = source.GenerateBlocksFromSource(group, option);
            usedGroup = target.ToDisplayPath();
        }

        return new PlcSourceGenerationInfo
        {
            PlcName = scope.Plc.Software.Name ?? string.Empty,
            DeviceName = scope.Plc.DeviceName ?? string.Empty,
            UnitName = scope.UnitName,
            SourceName = name,
            GenerateOption = optionName,
            TargetGroupPath = usedGroup,
            Generated = (generated ?? new List<IEngineeringObject>()).Select(Describe).ToList(),
        };
    }

    public static PlcExternalSourceMutationInfo Delete(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var scope = Scope(project, request);
        var name = RequireText(request.SourceName, "sourceName");
        var source = scope.Group.ExternalSources.Find(name) ?? throw new WorkerOperationException(
            WorkerFailureCategories.TargetNotFound, $"External source '{name}' was not found in {Describe(scope)}. Nothing was deleted.");
        source.Delete();
        if (scope.Group.ExternalSources.Find(name) is not null)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"Delete returned, but external source '{name}' is still found in {Describe(scope)}.");
        return Mutation(PlcSourceFolderMethods.DeleteExternalSource, scope, name, null);
    }

    public static PlcSourceFileExportInfo ExportSourceFile(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var destination = RequirePath(request.FilePath, "filePath");
        var blockPaths = request.BlockPaths ?? new List<string>();
        var typePaths = request.TypePaths ?? new List<string>();
        if (blockPaths.Count + typePaths.Count == 0) throw Invalid("export_source_file needs at least one blockPaths or typePaths entry. No file was written.");

        var objects = new List<IGenerateSource>();
        var groups = new List<PlcExternalSourceSystemGroup>();
        var plcNames = new List<PlcSoftware>();
        foreach (var blockPath in blockPaths)
        {
            var address = BlockAddress.Parse(blockPath);
            var plc = PlcSoftwareLocator.FindUnique(project, address.PlcName);
            var target = BlockTargetResolver.ResolveForExport(plc.Software, address);
            objects.Add(target.Block!);
            groups.Add(target.ExternalSourceGroup);
            plcNames.Add(plc.Software);
        }

        foreach (var typePath in typePaths)
        {
            var address = PlcTypeAddress.Parse(typePath);
            var plc = PlcSoftwareLocator.FindUnique(project, address.PlcName);
            var target = PlcTypeTargetResolver.ResolveForExport(project, address);
            objects.Add(target.Type!);
            groups.Add(target.ExternalSourceGroup);
            plcNames.Add(plc.Software);
        }

        // One GenerateSource call belongs to one software scope's external source group (the PLC's or a unit's).
        var group = groups[0];
        if (groups.Any(candidate => !Equals(candidate, group)) || plcNames.Any(candidate => !Equals(candidate, plcNames[0])))
            throw Invalid("The blocks and PLC data types of one export_source_file item must belong to one PLC and one software scope (the PLC itself or one software unit). No file was written.");
        var software = plcNames[0];
        var unitName = UnitOwning(software, group);
        RequireExtension(destination, unitName, "filePath");
        if (PlcFileTargets.WriteTargetError(destination, request.Overwrite) is { } refusal) throw Invalid(refusal);
        if (PlcFileTargets.InspectDirectory(Path.GetDirectoryName(destination)!).Refusal is { } location) throw Invalid(location);

        var directory = Path.GetDirectoryName(destination)!;
        var staging = Path.Combine(directory, ".tiamcp-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        // A failed swap over an existing file keeps the new content in the staging directory (and names it).
        var keepStaging = false;
        try
        {
            var staged = Path.Combine(staging, Path.GetFileName(destination));
            group.GenerateSource(objects, new FileInfo(staged), request.WithDependencies ? GenerateOptions.WithDependencies : GenerateOptions.None);
            if (!File.Exists(staged))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    "TIA Portal reported no error but produced no source file. No file was written.");
            var bytes = File.ReadAllBytes(staged);
            PlcCommit commit;
            try
            {
                commit = PlcFileTargets.Commit(staged, destination, request.Overwrite);
            }
            catch (PlcFileCommitException exception)
            {
                keepStaging = true;
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"The generated source could not replace '{destination}': {exception.Message}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var exists = !request.Overwrite && File.Exists(destination);
                throw new WorkerOperationException(exists ? WorkerFailureCategories.ValidationError : WorkerFailureCategories.WorkerOperationFailed,
                    $"The generated source could not be moved to '{destination}': {exception.Message}"
                    + (exists ? " The file exists now; pass overwrite:true to replace it." : string.Empty));
            }

            string actual;
            try
            {
                actual = PlcFileTargets.VerifyCommitted(destination, PlcFileTargets.Sha256Hex(bytes), commit);
            }
            catch (PlcFileNotVerifiedException exception)
            {
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, exception.Message);
            }
            return new PlcSourceFileExportInfo
            {
                FilePath = destination,
                Overwritten = commit.Replaced,
                Bytes = bytes.LongLength,
                Sha256 = actual,
                WithDependencies = request.WithDependencies,
                PlcName = software.Name ?? string.Empty,
                UnitName = unitName,
                Objects = blockPaths.Concat(typePaths).ToList(),
            };
        }
        finally
        {
            try
            {
                if (!keepStaging && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"export_source_file: the staging directory '{staging}' could not be removed: {exception.Message}");
            }
        }
    }

    internal sealed class SourceScope
    {
        public SourceScope(PlcSoftwareLocator.DiscoveredPlcSoftware plc, PlcUnit? unit, PlcExternalSourceSystemGroup group)
        {
            Plc = plc;
            Unit = unit;
            Group = group;
        }

        public PlcSoftwareLocator.DiscoveredPlcSoftware Plc { get; }

        public PlcUnit? Unit { get; }

        public string? UnitName => Unit?.Name;

        public PlcExternalSourceSystemGroup Group { get; }
    }

    internal static SourceScope Scope(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        if (request.UnitName is null) return new SourceScope(plc, null, plc.Software.ExternalSourceGroup);
        var unit = PlcSourceFolderTree.Unit(plc.Software, request.UnitName);
        return new SourceScope(plc, unit, unit.ExternalSourceGroup);
    }

    private static PlcBlockGroup BlockRoot(SourceScope scope) => scope.Unit?.BlockGroup ?? (PlcBlockGroup)scope.Plc.Software.BlockGroup;

    private static PlcTypeGroup TypeRoot(SourceScope scope) => scope.Unit?.TypeGroup ?? (PlcTypeGroup)scope.Plc.Software.TypeGroup;

    private static bool SameScope(PlcGroupPath target, SourceScope scope)
        => (string.Equals(target.PlcName, scope.Plc.Software.Name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(target.PlcName, scope.Plc.DeviceName, StringComparison.OrdinalIgnoreCase))
            && string.Equals(target.UnitName ?? string.Empty, scope.UnitName ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    /// <summary>The software unit whose external source group is <paramref name="group"/>; null for the PLC's own.</summary>
    private static string? UnitOwning(PlcSoftware software, PlcExternalSourceSystemGroup group)
    {
        if (Equals(software.ExternalSourceGroup, group)) return null;
        var provider = software.GetService<PlcUnitProvider>();
        if (provider is not null)
            foreach (var unit in PlcUnits.All(provider.UnitGroup))
                if (Equals(unit.ExternalSourceGroup, group)) return unit.Name;
        throw new WorkerOperationException(WorkerFailureCategories.StateChanged,
            "The external source group of the resolved objects belongs neither to the PLC nor to one of its software units. No file was written.");
    }

    private static void AddSources(PlcExternalSourcePlcInfo info, PlcExternalSourceInventoryInfo inventory, Func<PlcExternalSourceSystemGroup> group, string? unitName)
    {
        try
        {
            foreach (PlcExternalSource source in group().ExternalSources)
                info.Sources.Add(new PlcExternalSourceInfo { Name = source.Name ?? string.Empty, UnitName = unitName });
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            Incomplete(inventory, $"The external sources of {(unitName is null ? $"PLC '{info.PlcName}'" : $"software unit '{unitName}' of PLC '{info.PlcName}'")} could not be read: {exception.Message}");
        }
    }

    private static void Incomplete(PlcExternalSourceInventoryInfo inventory, string message)
    {
        inventory.Complete = false;
        inventory.Messages.Add(message);
    }

    private static PlcGeneratedObjectInfo Describe(IEngineeringObject generated)
        => generated switch
        {
            OB block => new PlcGeneratedObjectInfo { Kind = "OB", Name = block.Name },
            FB block => new PlcGeneratedObjectInfo { Kind = "FB", Name = block.Name },
            FC block => new PlcGeneratedObjectInfo { Kind = "FC", Name = block.Name },
            GlobalDB block => new PlcGeneratedObjectInfo { Kind = "GlobalDB", Name = block.Name },
            InstanceDB block => new PlcGeneratedObjectInfo { Kind = "InstanceDB", Name = block.Name },
            ArrayDB block => new PlcGeneratedObjectInfo { Kind = "ArrayDB", Name = block.Name },
            PlcBlock block => new PlcGeneratedObjectInfo { Kind = "Block", Name = block.Name },
            PlcType type => new PlcGeneratedObjectInfo { Kind = "Type", Name = type.Name },
            null => new PlcGeneratedObjectInfo { Kind = "null", Name = null },
            _ => new PlcGeneratedObjectInfo { Kind = generated.GetType().FullName ?? generated.GetType().Name, Name = OpennessReflection.ReadPropertyOrAttribute(generated, "Name") },
        };

    private static PlcExternalSourceMutationInfo Mutation(string operation, SourceScope scope, string name, string? path) => new()
    {
        Operation = operation,
        PlcName = scope.Plc.Software.Name ?? string.Empty,
        DeviceName = scope.Plc.DeviceName ?? string.Empty,
        UnitName = scope.UnitName,
        SourceName = name,
        FilePath = path,
        ReadBackConfirmed = true,
    };

    private static string Describe(SourceScope scope)
        => scope.UnitName is null ? $"the external sources of PLC '{scope.Plc.Software.Name}'" : $"the external sources of software unit '{scope.UnitName}' of PLC '{scope.Plc.Software.Name}'";

    private static void RequireExtension(string path, string? unitName, string member)
    {
        var allowed = unitName is null ? PlcFileTargets.SourceExtensions : PlcFileTargets.UnitSourceExtensions;
        var extension = PlcFileTargets.Extension(path);
        if (!allowed.Contains(extension, StringComparer.Ordinal))
            throw Invalid($"'{member}' must end in {string.Join(", ", allowed)}{(unitName is null ? string.Empty : " for a software unit")} (received '{extension}').");
    }

    private static string RequirePath(string? path, string member)
        => PlcFileTargets.PathShapeError(member, path) is { } error ? throw Invalid(error) : path!;

    private static PlcGroupPath RequireGroupPath(string path, string member)
        => PlcGroupPath.TryParse(path, out var group, out var error) ? group! : throw Invalid($"'{member}': {error}");

    private static string RequireText(string? value, string member)
        => string.IsNullOrWhiteSpace(value) ? throw Invalid($"'{member}' is required.") : value!.Trim();

    private static WorkerOperationException Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);
}

/// <summary>Group and software-unit lookup shared by the external-source and group-rename services.</summary>
internal static class PlcSourceFolderTree
{
    public static PlcUnit Unit(PlcSoftware software, string unitName)
    {
        var provider = software.GetService<PlcUnitProvider>() ?? throw new WorkerOperationException(
            WorkerFailureCategories.TargetNotFound, $"PLC software '{software.Name}' does not expose software units.");
        var matches = provider.UnitGroup.Units.Cast<PlcUnit>()
            .Where(unit => string.Equals(unit.Name, unitName, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Software unit '{unitName}' was not found in PLC software '{software.Name}'."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Software unit '{unitName}' is ambiguous in PLC software '{software.Name}'."),
        };
    }

    /// <summary>Walks PLC data type groups by name (case-insensitive, exactly one match per level).</summary>
    public static PlcTypeGroup TypeGroup(PlcTypeGroup root, IReadOnlyList<string> folders)
    {
        var current = root;
        foreach (var folder in folders)
            current = Single(current.Groups.Cast<PlcTypeUserGroup>().Where(group => string.Equals(group.Name, folder, StringComparison.OrdinalIgnoreCase)),
                $"PLC data type folder '{folder}'");
        return current;
    }

    /// <summary>Walks tag table groups by name (case-insensitive, exactly one match per level).</summary>
    public static Siemens.Engineering.SW.Tags.PlcTagTableGroup TagTableGroup(Siemens.Engineering.SW.Tags.PlcTagTableGroup root, IReadOnlyList<string> folders)
    {
        var current = root;
        foreach (var folder in folders)
            current = Single(current.Groups.Cast<Siemens.Engineering.SW.Tags.PlcTagTableUserGroup>()
                .Where(group => string.Equals(group.Name, folder, StringComparison.OrdinalIgnoreCase)), $"tag table folder '{folder}'");
        return current;
    }

    private static T Single<T>(IEnumerable<T> matches, string what)
    {
        var found = matches.Take(2).ToList();
        return found.Count switch
        {
            1 => found[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The {what} was not found."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"The {what} is ambiguous."),
        };
    }
}
