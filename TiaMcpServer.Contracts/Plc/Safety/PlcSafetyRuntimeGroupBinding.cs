using System.Reflection;
using System.Runtime.ExceptionServices;

namespace TiaMcpServer.Contracts.Plc.Safety;

/// <summary>
/// How the main safety block (and its instance DB) of an existing F-runtime group is assigned. Evidence: the Siemens skill
/// plc-safety-administration §1 lists <c>RuntimeGroup.MainSafetyBlock</c> and <c>RuntimeGroup.MainSafetyBlockIDB</c> as
/// <c>PlcBlock</c> in the object model and §5 says "Call SetAttribute directly on the RuntimeGroup object". Neither the
/// attribute path nor whether the members are writable properties is documented, so the routes are bound on the live object,
/// in this order, and the route that bound is reported: (i) a public writable instance property of exactly that name whose
/// type is assignable from the block type; (ii) an attribute of exactly that name that <c>GetAttributeInfos()</c> lists as
/// writable, set with <c>SetAttribute(name, block)</c>; (iii) the name attribute (<c>MainSafetyBlockName</c> /
/// <c>MainSafetyBlockIDbName</c>) when <c>GetAttributeInfos()</c> lists it as writable, set with <c>SetAttribute(name, blockName)</c>
/// (a string). None: <see cref="PlcSafetyApiMissingException"/>, nothing called, and the message lists every attribute
/// <c>GetAttributeInfos()</c> reported with its access mode, so a live test shows what TIA Portal offers. Live on TIA Portal V21
/// (2026-11-02, L2) routes (i) and (ii) did not exist: the RuntimeGroup has no public property MainSafetyBlock and
/// GetAttributeInfos() lists no attribute MainSafetyBlock (api_member_missing); route (iii) is not live-tested.
/// Shared by set_safety_runtime_group and the automatic re-link after a block import. Siemens-free.
/// </summary>
public static class PlcSafetyRuntimeGroupBinding
{
    public const string MainSafetyBlock = "MainSafetyBlock";
    public const string MainSafetyBlockIdb = "MainSafetyBlockIDB";

    /// <summary>The attributes the assignment is read back with (documented GetAttribute names, Siemens libraries-and-alarms skill).</summary>
    public const string MainSafetyBlockName = "MainSafetyBlockName";
    public const string MainSafetyBlockIdbName = "MainSafetyBlockIDbName";

    public static class Routes
    {
        public const string Property = "property";
        public const string SetAttribute = "setAttribute";
        public const string SetAttributeName = "setAttributeName";
    }

    /// <summary>The read-back attribute of an assigned member.</summary>
    public static string ReadBackAttribute(string member) => member == MainSafetyBlockIdb ? MainSafetyBlockIdbName : MainSafetyBlockName;

    /// <summary>One attribute <c>GetAttributeInfos()</c> reported: its name and access mode (Read, Write, ReadWrite, ...).</summary>
    public sealed record AttributeAccess(string Name, string AccessMode)
    {
        /// <summary>Write or ReadWrite.</summary>
        public bool Writable => AccessMode.IndexOf("Write", StringComparison.OrdinalIgnoreCase) >= 0;

        public override string ToString() => $"{Name} ({AccessMode})";
    }

    /// <summary>The generic attribute surface of the live object (IEngineeringObject); null members mean the object has none.</summary>
    public sealed class AttributeSurface
    {
        public AttributeSurface(Func<IEnumerable<AttributeAccess>> infos, Action<string, object> set)
        {
            Infos = infos;
            Set = set;
        }

        /// <summary>A surface whose infos carry only writability (tests and stand-ins).</summary>
        public AttributeSurface(Func<IEnumerable<(string Name, bool Writable)>> infos, Action<string, object> set)
            : this(() => infos().Select(i => new AttributeAccess(i.Name, i.Writable ? "ReadWrite" : "Read")), set)
        {
        }

        public Func<IEnumerable<AttributeAccess>> Infos { get; }

        public Action<string, object> Set { get; }
    }

    /// <summary>A bound assignment: the route, a display signature and the call (not yet made).</summary>
    public sealed class Assignment(string member, string route, string signature, Action<object, string?> apply)
    {
        public string Member { get; } = member;

        public string Route { get; } = route;

        public string Signature { get; } = signature;

        /// <summary>
        /// Assigns the block (<paramref name="blockName"/> is its name, used by the name-attribute route; when omitted it is
        /// read from the block's public Name property). A Siemens exception escapes unwrapped for the shared classification.
        /// </summary>
        public void Apply(object block, string? blockName = null) => apply(block, blockName);
    }

    /// <summary>
    /// Binds the assignment of <paramref name="member"/> on <paramref name="group"/> for blocks of <paramref name="blockType"/>
    /// without calling anything.
    /// </summary>
    /// <exception cref="PlcSafetyApiMissingException">Neither route exists on the live object.</exception>
    public static Assignment Bind(object group, string member, Type blockType, AttributeSurface? attributes)
    {
        var properties = group.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => string.Equals(p.Name, member, StringComparison.Ordinal) && p.GetIndexParameters().Length == 0).ToList();
        if (properties.Count == 1 && properties[0] is { CanWrite: true } property && property.GetSetMethod() is not null
            && property.PropertyType.IsAssignableFrom(blockType))
            return new Assignment(member, Routes.Property, $"{property.PropertyType.Name} {member} {{ set; }}",
                (block, _) => Unwrap(() => property.SetValue(group, block, null)));

        string attributeNote;
        if (attributes is null)
        {
            attributeNote = "the object offers no GetAttributeInfos()";
        }
        else
        {
            var infos = attributes.Infos().ToList();
            var listed = infos.Where(info => string.Equals(info.Name, member, StringComparison.Ordinal)).ToList();
            if (listed.Count == 1 && listed[0].Writable)
                return new Assignment(member, Routes.SetAttribute, $"IEngineeringObject.SetAttribute(\"{member}\", {blockType.Name})",
                    (block, _) => attributes.Set(member, block));
            var nameAttribute = ReadBackAttribute(member);
            var named = infos.Where(info => string.Equals(info.Name, nameAttribute, StringComparison.Ordinal)).ToList();
            if (named.Count == 1 && named[0].Writable)
                return new Assignment(member, Routes.SetAttributeName, $"IEngineeringObject.SetAttribute(\"{nameAttribute}\", String)",
                    (block, name) => attributes.Set(nameAttribute, name ?? NameOf(block)));
            attributeNote = (listed.Count == 0 ? $"GetAttributeInfos() lists no attribute '{member}'"
                    : listed.Count > 1 ? $"GetAttributeInfos() lists '{member}' {listed.Count} times"
                    : $"GetAttributeInfos() lists '{member}' as not writable")
                + (named.Count == 0 ? $" and no attribute '{nameAttribute}'"
                    : named.Count > 1 ? $" and '{nameAttribute}' {named.Count} times"
                    : $" and '{nameAttribute}' as not writable ({named[0].AccessMode})")
                + $"; the attributes GetAttributeInfos() reported: {(infos.Count == 0 ? "none" : string.Join(", ", infos))}";
        }

        var propertyNote = properties.Count switch
        {
            0 => $"no public property '{member}'",
            > 1 => $"{properties.Count} public properties named '{member}'",
            _ when !properties[0].CanWrite || properties[0].GetSetMethod() is null => $"the property '{member}' has no public setter",
            _ => $"the property '{member}' is a {properties[0].PropertyType.FullName}, not assignable from {blockType.FullName}",
        };
        throw new PlcSafetyApiMissingException($"{group.GetType().FullName}.{member}",
            $"{propertyNote} and {attributeNote}. The Siemens skill names the member (RuntimeGroup.{member}, a PlcBlock) and says to call SetAttribute on the RuntimeGroup, "
            + "but documents no attribute path, so nothing was assigned (live on TIA Portal V21, 2026-11-02, the RuntimeGroup had neither the property nor the attribute)");
    }

    /// <summary>The public Name property of a block object (the name-attribute route when the caller passed no name).</summary>
    private static string NameOf(object block)
        => block.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(block, null) as string
            ?? throw new InvalidOperationException($"The block object {block.GetType().FullName} has no readable Name.");

    private static void Unwrap(Action call)
    {
        try
        {
            call();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}

/// <summary>One F-runtime group and the block names its main safety block attributes held when read.</summary>
public sealed record PlcMainSafetyLink(string RuntimeGroup, string? MainSafetyBlockName, string? MainSafetyBlockIDbName);

/// <summary>What happened to one reference of one runtime group across a block import.</summary>
public sealed record PlcMainSafetyLinkOutcome(string RuntimeGroup, string Member, string Expected, string? After, string? Route, string? RestoredTo, string? Problem)
{
    public bool Lost => !PlcMainSafetyLinks.Same(After, Expected);

    public bool Restored => Lost && PlcMainSafetyLinks.Same(RestoredTo, Expected);
}

/// <summary>
/// The protection of F-runtime group references across a block import (live 2026-11-02, TIA Portal V21, E7): a SIMATIC SD
/// <c>ImportFromDocuments(..., Override)</c> of the main safety block of an F-runtime group succeeded and compiled, but
/// afterwards the group's <c>MainSafetyBlockName</c> was null (its <c>MainSafetyBlockIDbName</c> stayed) and the PLC compile
/// failed "does not have a valid main safety block"; the SimaticML route (<c>Import(..., Override)</c>, set_network_text)
/// kept the reference (E7b). The worker records the groups that name the block before the import, re-reads them after it and
/// re-assigns a lost reference with <see cref="PlcSafetyRuntimeGroupBinding"/>; this class holds the Siemens-free comparison
/// and the wording, shared by the worker, the FakeWorker and the tests.
/// </summary>
public static class PlcMainSafetyLinks
{
    /// <summary>The verification check of a block import whose runtime-group references were lost and not restored.</summary>
    public const string Check = "safetyRuntimeGroup";

    /// <summary>Block names compare as TIA Portal compares them (case-insensitively).</summary>
    public static bool Same(string? left, string? right)
        => left is not null && right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>The groups whose main safety block or its instance DB names <paramref name="blockName"/>.</summary>
    public static IReadOnlyList<PlcMainSafetyLink> Affected(IEnumerable<PlcMainSafetyLink> groups, string blockName)
        => groups.Where(g => Same(g.MainSafetyBlockName, blockName) || Same(g.MainSafetyBlockIDbName, blockName)).ToList();

    /// <summary>The references of <paramref name="before"/> that name <paramref name="blockName"/>, as (member, expected name).</summary>
    public static IEnumerable<(string Member, string Expected)> References(PlcMainSafetyLink before, string blockName)
    {
        if (Same(before.MainSafetyBlockName, blockName)) yield return (PlcSafetyRuntimeGroupBinding.MainSafetyBlock, before.MainSafetyBlockName!);
        if (Same(before.MainSafetyBlockIDbName, blockName)) yield return (PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdb, before.MainSafetyBlockIDbName!);
    }

    /// <summary>The value of the read-back attribute of <paramref name="member"/> in <paramref name="link"/>.</summary>
    public static string? Value(PlcMainSafetyLink? link, string member)
        => link is null ? null : member == PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdb ? link.MainSafetyBlockIDbName : link.MainSafetyBlockName;

    /// <summary>
    /// Compares <paramref name="before"/> with the re-read groups and re-assigns every lost reference through
    /// <paramref name="restore"/> (group, member, expected block name) → (route, the name read back after it). A restore that
    /// throws a non-fatal exception is recorded as the outcome's problem; <paramref name="isFatal"/> exceptions escape.
    /// </summary>
    public static IReadOnlyList<PlcMainSafetyLinkOutcome> Reconcile(IReadOnlyList<PlcMainSafetyLink> before, string blockName,
        Func<string, PlcMainSafetyLink?> reread, Func<string, string, string, (string Route, string? ReadBack)> restore, Func<Exception, bool> isFatal)
    {
        var outcomes = new List<PlcMainSafetyLinkOutcome>();
        foreach (var group in before)
        {
            PlcMainSafetyLink? after;
            string? readProblem = null;
            try
            {
                after = reread(group.RuntimeGroup);
            }
            catch (Exception exception) when (!isFatal(exception))
            {
                after = null;
                readProblem = $"the group could not be read again: {exception.Message}";
            }

            foreach (var (member, expected) in References(group, blockName))
            {
                var value = Value(after, member);
                if (Same(value, expected))
                {
                    outcomes.Add(new(group.RuntimeGroup, member, expected, value, null, null, null));
                    continue;
                }

                try
                {
                    var (route, readBack) = restore(group.RuntimeGroup, member, expected);
                    outcomes.Add(new(group.RuntimeGroup, member, expected, value, route, readBack,
                        Same(readBack, expected) ? null : $"the assignment returned, but {PlcSafetyRuntimeGroupBinding.ReadBackAttribute(member)} reads '{readBack ?? "null"}' afterwards"));
                }
                catch (Exception exception) when (!isFatal(exception))
                {
                    outcomes.Add(new(group.RuntimeGroup, member, expected, value, null, null, readProblem is null ? exception.Message : readProblem + "; " + exception.Message));
                }
            }
        }

        return outcomes;
    }

    /// <summary>The warning of one lost reference: restored, or what to do.</summary>
    public static string Warning(PlcMainSafetyLinkOutcome outcome, string plcName, string blockPath)
    {
        var attribute = PlcSafetyRuntimeGroupBinding.ReadBackAttribute(outcome.Member);
        var lost = $"F-runtime group '{outcome.RuntimeGroup}' of PLC '{plcName}': {attribute} was '{outcome.Expected}' before the import of '{blockPath}' and '{outcome.After ?? "null"}' after it";
        return outcome.Restored
            ? $"{lost}; the reference was restored with {outcome.Route} ({attribute} reads '{outcome.RestoredTo}' again). "
              + "TIA Portal's SIMATIC SD import (ImportFromDocuments Override) was seen to drop this reference (live, V21, 2026-11-02); confirm with compile_check."
            : $"{lost}, and it could NOT be restored ({outcome.Problem}). The safety program has no valid main safety block until it is repaired: "
              + "TIA Portal Safety Administration > F-runtime groups (select the main safety block again), or plc_write set_safety_runtime_group "
              + $"(runtimeGroupName '{outcome.RuntimeGroup}', mainSafetyBlockPath and, for an F-FB, mainSafetyInstanceDbPath) where Openness offers a member for it "
              + "(not on V21, live 2026-11-02); then compile_check.";
    }

    /// <summary>The planning note of an import of a main safety block.</summary>
    public static string PlanningNote(string operationId, string blockPath, IEnumerable<PlcMainSafetyLink> links, bool simaticSdRoute)
        => $"Operation '{operationId}' imports '{blockPath}', which F-runtime group(s) {string.Join(", ", links.Select(l => $"'{l.RuntimeGroup}'"))} reference as main safety block or its instance DB. "
           + (simaticSdRoute
               ? "A SIMATIC SD import (ImportFromDocuments Override) of a main safety block was seen live (V21, 2026-11-02) to succeed and compile while TIA Portal dropped the runtime group's MainSafetyBlockName, after which the PLC compile failed (\"does not have a valid main safety block\"); the SimaticML route (set_network_text) kept the reference live. "
               : string.Empty)
           + "The worker re-reads the group after the import and tries to re-assign a lost reference (set_safety_runtime_group's mechanism; on V21, live 2026-11-02, the RuntimeGroup offered no member for the main safety block); one it cannot restore fails this item's verification (safetyRuntimeGroup) with the repair.";
}
