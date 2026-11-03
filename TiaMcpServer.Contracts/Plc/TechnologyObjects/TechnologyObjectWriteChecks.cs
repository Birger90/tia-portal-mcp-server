using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.TechnologyObjects;

/// <summary>
/// The Siemens-free pre-checks and postconditions of the structural technology object writes, shared by the worker and
/// the FakeWorker (so the fixture runs the worker's rules, not a copy): each returns null when the check passes,
/// otherwise the failure (category and message). The worker supplies what it read from TIA Portal; the rules decide.
/// </summary>
public static class TechnologyObjectWriteChecks
{
    /// <summary>A failed check: a worker failure category and its message.</summary>
    public sealed class Failure
    {
        public Failure(string category, string message)
        {
            Category = category;
            Message = message;
        }

        public string Category { get; }

        public string Message { get; }
    }

    private static Failure Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);

    private static Failure Postcondition(string message) => new(WorkerFailureCategories.PostconditionFailed, message);

    /// <summary>Before Create: names are unique per PLC (a technology object is a DB), so an existing one is refused and the object found afterwards can only be the created one.</summary>
    public static Failure? ExistingObject(string name, string? existingGroupPath, string plcName)
        => existingGroupPath is null ? null
            : Invalid($"A technology object named '{name}' already exists in group '{existingGroupPath}' of PLC '{plcName}'. Nothing was changed.");

    /// <summary>After Create: exactly one object of the name is in the requested group.</summary>
    public static Failure? Created(string name, string type, string version, string? returnedType, string groupPath, int foundInGroup)
        => foundInGroup == 1 ? null
            : Postcondition($"Create('{name}', '{type}', {version}) returned {(returnedType is null ? "null" : "'" + returnedType + "'")}, but group '{groupPath}' lists "
                + $"{foundInGroup} technology objects named '{name}' afterwards instead of one. The Siemens skill notes that accepted type/version pairs are installation-specific "
                + "and depend on the CPU firmware (live V21 2026-11-02, CPU 1516-3 PN/DP FW V2.9: TO_PositioningAxis 6.0 accepted, 10.0 rejected); inspect the group with list_technology_objects.");

    /// <summary>After Delete: the group no longer lists the object.</summary>
    public static Failure? Deleted(string name, string groupPath, bool stillListed)
        => stillListed ? Postcondition($"Technology object '{name}' is still in group '{groupPath}' after Delete() returned.") : null;

    /// <summary>Before a group Create: the parent has no group of that name.</summary>
    public static Failure? ExistingGroup(string path, bool exists, string plcName)
        => exists ? Invalid($"Technology object group '{path}' already exists in PLC '{plcName}'. Nothing was changed.") : null;

    /// <summary>After a group Create: exactly one group of the name and one group more than before in the parent.</summary>
    public static Failure? CreatedGroup(string parentPath, string leaf, int named, int before, int after)
        => named == 1 && after == before + 1 ? null
            : Postcondition($"Group '{parentPath}' lists {named} groups named '{leaf}' and {after} groups in all (before: {before}) after Create('{leaf}') returned; "
                + "inspect it with list_technology_objects.");

    /// <summary>After a group Delete: the parent (resolved by path before the delete) no longer lists it.</summary>
    public static Failure? DeletedGroup(string path, string parentPath, bool stillListed)
        => stillListed ? Postcondition($"Group '{path}' is still listed in '{parentPath}' after Delete() returned.") : null;

    /// <summary>
    /// After Import: the imported names and the failure. When <c>Import</c> returned a list of technology objects
    /// (<paramref name="reported"/>), it must name at least one and every one must be listed in the group. Otherwise (the
    /// return type is undocumented) the objects new in the group are the only evidence: none new without overwrite is a
    /// failure, and a warning says that a replaced object is not named.
    /// </summary>
    public static (List<string> Imported, Failure? Failure) Imported(IReadOnlyList<string>? reported, IEnumerable<string> before, IEnumerable<string> after,
        Func<string, bool> listed, bool overwrite, string source, string groupPath, string? returnedType, List<string> warnings)
    {
        if (reported is not null)
        {
            if (reported.Count == 0)
                return (new List<string>(), Postcondition($"Import of '{source}' into group '{groupPath}' returned no technology object; inspect the group with list_technology_objects."));
            var missing = reported.Where(name => !listed(name)).ToList();
            return missing.Count == 0 ? (reported.ToList(), null)
                : (reported.ToList(), Postcondition($"Import reported [{string.Join(", ", missing)}], which group '{groupPath}' does not list afterwards."));
        }

        var returned = returnedType is null ? "null" : "'" + returnedType + "'";
        var imported = after.Except(before, TechnologyObjectPolicy.Names).ToList();
        if (imported.Count == 0 && !overwrite)
            return (imported, Postcondition($"Import of '{source}' into group '{groupPath}' returned {returned} and no new technology object is in the group afterwards, "
                + "so nothing shows that it imported anything; inspect the group with list_technology_objects."));
        warnings.Add($"Import returned {returned}, not a list of technology objects; importedObjects lists the objects that are new in group '{groupPath}', "
            + "so an object replaced with overwrite:true is not named.");
        return (imported, null);
    }
}
