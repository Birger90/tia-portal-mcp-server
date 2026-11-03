using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Hmi.HmiGuardDefinitions;

namespace TiaMcpServer.Hmi;

/// <summary>
/// The Classic WinCC writes (issue #28) in the HMI working state: no planning read covers classic objects, so the effect
/// lists the request; an item depends on the latest earlier item of the same classic kind (conservative).
/// </summary>
internal sealed partial class HmiWorkingState
{
    private HmiWriteEffect ResolveClassic(HmiWriteOperationRequest r, List<FiredGuard> guards)
    {
        var kind = r.ClassicKind!;
        var scope = r.TableName is not null ? $"table {r.TableName}" : r.GroupPath ?? "(system folder)";
        var changes = new List<HmiFieldChange>();
        void Change(string field, string? current, string? requested) => changes.Add(new(field, current, requested, false));
        HmiTargetIdentity target;
        switch (r.Operation)
        {
            case "export_classic_objects":
                target = Identity("ClassicObjects", $"{kind}:{scope}", table: r.TableName, group: r.GroupPath);
                var directory = r.DirectoryPath!.Replace('/', '\\').TrimEnd('\\');
                foreach (var name in r.ObjectNames ?? new List<string> { HmiClassicKinds.Find(kind)?.IsSingleton == true ? kind : "*" })
                    Change($"file {name}", null, $"{directory}\\{(name == "*" ? "<name>" : name)}.xml");
                if (r.Overwrite == true) guards.Add(Fire(ClassicImportOverrides, r, "overwrite:true replaces existing export files."));
                break;
            case "import_classic_objects":
                target = Identity("ClassicObjects", $"{kind}:{scope}", table: r.TableName, group: r.GroupPath);
                Change("import", null, r.FilePath);
                if (r.Overwrite == true)
                    guards.Add(Fire(ClassicImportOverrides, r, $"import_classic_objects with overwrite uses ImportOptions.Override: {kind} objects of the same name are replaced."));
                break;
            case "delete_classic_object":
                target = Identity("ClassicObject", $"{kind}:{r.ObjectName}", table: r.TableName, group: r.GroupPath);
                Change("delete", r.ObjectName, null);
                guards.Add(Fire(ClassicDeletes, r, $"Deleting classic {kind} '{r.ObjectName}' removes it; objects that name it are not changed."));
                break;
            default:
                target = Identity("ClassicFolder", $"{kind}:{r.GroupPath}", group: r.GroupPath);
                if (r.Operation == "create_classic_folder") Change("folder", null, r.GroupPath);
                else
                {
                    Change("folder", r.GroupPath, null);
                    guards.Add(Fire(ClassicDeletes, r, $"Deleting the classic {kind} folder '{r.GroupPath}' removes it with its contents."));
                }

                break;
        }

        return new(r.Operation, target, changes, null, Dependency(new[] { Identity("ClassicKind", kind), target }));
    }

    private void ApplyClassic(HmiWriteOperationRequest r, HmiWriteEffect effect)
    {
        Touch(effect.Target, r.OperationId);
        Touch(Identity("ClassicKind", r.ClassicKind!), r.OperationId);
    }
}
