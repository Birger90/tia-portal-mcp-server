using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Supervision;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcAlarms;

/// <summary>
/// The ProDiag members blocks.md documents (section 12): <c>FB.Supervisions</c> (a <c>SupervisionComposition</c> whose
/// <c>Supervision</c> items have no documented member, so only its <c>Count</c> is read, as presence evidence),
/// <c>PlcBlock.ProgrammingLanguage</c> compared with the member name <c>ProDiag</c>, the ProDiag-FB attributes
/// <c>ProDiagVersion</c>, <c>InitialValueAcquisition</c> and <c>UseCentralTimeStamp</c> (GetAttribute), the
/// <c>AssignedProDiagFB</c> attribute of instance DBs, UDT-instance members of global DBs and tags (GetAttribute and
/// SetAttribute with the FB's name, read back) and <c>CodeBlock.ExportProDIAGInfo(DirectoryInfo)</c> (return type
/// undocumented: bound at runtime by name and argument type, its result ignored like the sample does).
/// </summary>
internal static class PlcProDiagService
{
    private const string ExportInfo = "CodeBlock.ExportProDIAGInfo(DirectoryInfo)";

    // ================================================================ reads

    public static PlcProDiagFbInfo GetFb(ProjectBase project, PlcAlarmRequestInfo request)
    {
        var (plc, block) = ResolveBlock(project, request.BlockPath);
        var info = new PlcProDiagFbInfo { BlockPath = request.BlockPath!, PlcName = plc, BlockType = BlockLocator.KindName(block) };
        info.ProgrammingLanguage = Read(() => block.ProgrammingLanguage.ToString(), "ProgrammingLanguage", info.Unreadable);
        info.IsProDiag = info.ProgrammingLanguage == PlcAlarmPolicy.ProDiagLanguage;
        info.IsConsistent = Consistent(block, info.Unreadable);
        if (block is FB fb)
        {
            info.SupervisionCount = Read(() => fb.Supervisions?.Count, "Supervisions.Count", info.Unreadable);
            info.HasSupervisionProvider = Read(() => (bool?)(((IEngineeringServiceProvider)fb).GetService<SupervisionProvider>() is not null),
                "GetService<SupervisionProvider>", info.Unreadable) == true;
        }

        foreach (var name in PlcAlarmPolicy.ProDiagFbAttributes)
        {
            var value = Read(() => Text(((IEngineeringObject)block).GetAttribute(name)), name, info.Unreadable);
            if (value is not null || !info.Unreadable.Contains(name)) info.Attributes.Add(new PlcProDiagAttributeInfo { Name = name, Value = value });
        }

        return info;
    }

    public static PlcProDiagAssignmentInfo GetAssignment(ProjectBase project, PlcAlarmRequestInfo request)
    {
        var target = ResolveAssignmentTarget(project, request);
        var info = new PlcProDiagAssignmentInfo { TargetKind = target.Kind, Target = target.Display, PlcName = target.PlcName };
        var read = ReadAssignment(target);
        info.Readable = read.Readable;
        info.AssignedProDiagFb = read.Name;
        info.Message = read.Message;
        return info;
    }

    internal readonly record struct AssignmentRead(bool Readable, string? Name, string? Message);

    /// <summary>
    /// AssignedProDiagFB of <paramref name="target"/> by FB name: blocks.md writes the FB's name, and GetAttribute may return
    /// that name (a string) or the FB itself (a block, whose Name is used); null or "" is unset. A recoverable Siemens
    /// failure or any other value type is unreadable, with the reason.
    /// </summary>
    internal static AssignmentRead ReadAssignment(AssignmentTarget target)
    {
        object? value;
        try
        {
            value = target.Object.GetAttribute(PlcAlarmPolicy.AssignedProDiagFbAttribute);
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return new AssignmentRead(false, null, $"GetAttribute(\"{PlcAlarmPolicy.AssignedProDiagFbAttribute}\") failed: {exception.Message} "
                + "(blocks.md: the attribute exists on instance DBs, on UDT instances in global DBs and on tags in tag tables).");
        }

        return value switch
        {
            null => new AssignmentRead(true, null, null),
            string name => new AssignmentRead(true, Blank(name), null),
            PlcBlock block => new AssignmentRead(true, Blank(block.Name), null),
            _ => new AssignmentRead(false, null, $"GetAttribute(\"{PlcAlarmPolicy.AssignedProDiagFbAttribute}\") returned a {value.GetType().FullName}, "
                + "neither the FB name nor a block; its FB name is not read."),
        };
    }

    // ================================================================ writes

    /// <summary>
    /// Sets <c>AssignedProDiagFB</c> to the FB's name (blocks.md: <c>SetAttribute("AssignedProDiagFB", prodiag.Name)</c>) and
    /// reads it back by FB name (<see cref="ReadAssignment"/>). The named block must be an FB of the same PLC; one whose language
    /// is not ProDiag is passed on with a warning. An unreadable value before the write counts as unset (with a warning); an
    /// unreadable read-back leaves <c>assignedProDiagFbAfter</c> null with a warning and the verification decides
    /// (notVerified); only a readable different name fails postcondition_failed.
    /// </summary>
    public static PlcAlarmWriteResultInfo SetAssignment(ProjectBase project, PlcAlarmRequestInfo request, PlcAlarmWriteResultInfo result)
    {
        var target = ResolveAssignmentTarget(project, request);
        var fbName = ProDiagFbName(project, target.PlcName, request.ProDiagFb);
        result.PlcName = target.PlcName;
        result.TargetKind = target.Kind;
        result.Target = target.Display;
        var before = ReadAssignment(target);
        result.AssignedProDiagFbBefore = before.Name;
        if (!before.Readable) Console.Error.WriteLine($"{target.Display}: the assignment before the write is treated as unset; {before.Message}");
        target.Object.SetAttribute(PlcAlarmPolicy.AssignedProDiagFbAttribute, fbName);
        result.AppliedMembers = new List<string> { PlcAlarmPolicy.AssignedProDiagFbAttribute };
        var after = ReadAssignment(target);
        result.AssignedProDiagFbAfter = after.Name;
        if (!after.Readable)
        {
            Console.Error.WriteLine($"{target.Display}: SetAttribute(\"{PlcAlarmPolicy.AssignedProDiagFbAttribute}\", '{fbName}') returned, but the value does not read back: {after.Message}");
            return result;
        }

        if (!string.Equals(result.AssignedProDiagFbAfter, fbName, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"{target.Display} reads back {PlcAlarmPolicy.AssignedProDiagFbAttribute} '{result.AssignedProDiagFbAfter}' after SetAttribute with '{fbName}'; TIA Portal did not apply it.");
        return result;
    }

    /// <summary>The FB, its IsConsistent evidence and the bound ExportProDIAGInfo(DirectoryInfo), resolved before anything is written.</summary>
    public static (FB Fb, Action<string> Export) PrepareExport(ProjectBase project, PlcAlarmRequestInfo request, PlcAlarmWriteResultInfo? result, List<string> warnings)
    {
        var fb = ResolveFb(project, request.BlockPath, "ProDiag alarm information is exported from ProDiag FBs");
        var method = PlcAlarmTransfers.Method(fb, "ExportProDIAGInfo", new[] { typeof(DirectoryInfo) }, ExportInfo);
        var consistent = Consistent(fb, new List<string>());
        if (result is not null)
        {
            result.BlockPath = request.BlockPath;
            result.IsConsistent = consistent;
        }

        var (language, _) = Evidence(fb);
        if (language != PlcAlarmPolicy.ProDiagLanguage)
            warnings.Add($"FB '{request.BlockPath}' has ProgrammingLanguage {language ?? "(unreadable)"}, not ProDiag; the documented sample exports ProDiag FBs only.");
        if (consistent != true)
            warnings.Add($"FB '{request.BlockPath}' is {(consistent is null ? "of unreadable consistency" : "not consistent")}; the documented sample exports only consistent FBs (compile it first).");
        return (fb, directory => PlcAlarmTransfers.Call(method, fb, new object?[] { new DirectoryInfo(directory) }));
    }

    public static string Bound => $"{ExportInfo} (result ignored)";

    // ================================================================ resolution

    /// <summary>The FB at <paramref name="blockPath"/>; another block kind is target_kind_unsupported.</summary>
    public static FB ResolveFb(ProjectBase project, string? blockPath, string why)
    {
        var (_, block) = ResolveBlock(project, blockPath);
        return block as FB ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"Block '{blockPath}' is a {BlockLocator.KindName(block)}, not an FB; {why}.");
    }

    /// <summary>ProgrammingLanguage by member name and FB.Supervisions.Count, each null when unreadable.</summary>
    public static (string? Language, int? Count) Evidence(FB fb)
        => (Read(() => fb.ProgrammingLanguage.ToString(), "ProgrammingLanguage", null), Read(() => fb.Supervisions?.Count, "Supervisions.Count", null));

    internal sealed record AssignmentTarget(string Kind, string Display, string PlcName, IEngineeringObject Object);

    /// <summary>
    /// The object carrying AssignedProDiagFB: a tag (tableName + name, optional folderPath), a DB member (blockPath +
    /// memberPath) or an instance DB (blockPath). blocks.md names the kinds; what TIA Portal accepts on a given object it decides.
    /// </summary>
    public static AssignmentTarget ResolveAssignmentTarget(ProjectBase project, PlcAlarmRequestInfo request)
    {
        if (request.TableName is not null)
        {
            var tag = TagTargetResolver.Resolve(project, request.PlcName, request.TableName, request.FolderPath, request.Name ?? string.Empty);
            return new AssignmentTarget(PlcAlarmPolicy.AssignmentTargets.Tag, $"Tag '{tag.Tag.Name}' of tag table '{tag.FolderPath.TrimEnd('/')}/{tag.Table.Name}'",
                tag.PlcName, (IEngineeringObject)tag.Tag);
        }

        var (plc, block) = ResolveBlock(project, request.BlockPath);
        if (request.MemberPath is not null)
        {
            var db = block as DataBlock ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"Block '{request.BlockPath}' is a {BlockLocator.KindName(block)}; a DB member's AssignedProDiagFB is set on members of global DBs.");
            var path = PlcBlockEditService.NativePath(request.MemberPath);
            var member = db.Interface.Members.Find(path)
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Member '{path}' was not found in '{request.BlockPath}' (Interface.Members.Find returned none).");
            return new AssignmentTarget(PlcAlarmPolicy.AssignmentTargets.DbMember, $"Member '{path}' of '{request.BlockPath}'", plc, member);
        }

        return new AssignmentTarget(block is InstanceDB ? PlcAlarmPolicy.AssignmentTargets.InstanceDb : BlockLocator.KindName(block),
            $"Block '{request.BlockPath}'", plc, block);
    }

    /// <summary>The name of the FB to assign: it must exist as an FB of the PLC (an FB whose language is not ProDiag is passed on).</summary>
    private static string ProDiagFbName(ProjectBase project, string plcName, string? name)
    {
        if (PlcAlarmPolicy.CheckName(name, "proDiagFb") is { } invalid) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, invalid);
        var plc = PlcSoftwareLocator.FindUnique(project, plcName).Software;
        BlockAddress address;
        try
        {
            address = BlockAddress.Parse($"{plc.Name}/{name}");
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, exception.Message);
        }

        var block = BlockTargetResolver.ResolveForExport(plc, address).Block
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{name}' was not found in PLC '{plc.Name}'.");
        if (block is not FB fb)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{name}' is a {BlockLocator.KindName(block)}, not an FB; AssignedProDiagFB names a ProDiag FB.");
        var (language, _) = Evidence(fb);
        if (language != PlcAlarmPolicy.ProDiagLanguage)
            Console.Error.WriteLine($"FB '{fb.Name}' has ProgrammingLanguage {language ?? "(unreadable)"}, not ProDiag; TIA Portal decides whether it can be assigned.");
        return fb.Name;
    }

    private static (string PlcName, PlcBlock Block) ResolveBlock(ProjectBase project, string? blockPath)
    {
        if (string.IsNullOrWhiteSpace(blockPath)) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "blockPath is required.");
        BlockAddress address;
        try
        {
            address = BlockAddress.Parse(blockPath!);
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, exception.Message);
        }

        var plc = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
        var block = BlockTargetResolver.ResolveForExport(plc, address).Block
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{blockPath}' was not found.");
        return (plc.Name, block);
    }

    /// <summary>PlcBlock.IsConsistent (documented, bool), read as the property or attribute of that name; null when unreadable.</summary>
    private static bool? Consistent(PlcBlock block, List<string> unreadable)
        => Read(() => bool.TryParse(OpennessReflection.ReadPropertyOrAttribute(block, "IsConsistent"), out var consistent) ? consistent : (bool?)null,
            "IsConsistent", unreadable);

    private static T? Read<T>(Func<T?> read, string name, List<string>? unreadable)
    {
        try
        {
            return read();
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            unreadable?.Add(name);
            return default;
        }
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        bool flag => flag ? "True" : "False",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string? Blank(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
