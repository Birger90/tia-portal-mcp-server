using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using static TiaMcpServer.Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy;

namespace TiaMcpServer.Plc;

/// <summary>
/// The technology object operations of plc_read/plc_write: their field sets, merged into the catalog's specs, and
/// their shape validation. Methods, not fields, so the merge does not depend on static initialization order across
/// the partial files.
/// </summary>
public static partial class PlcOperationCatalog
{
    private static Dictionary<string, Spec> WithTechnologyObjectSpecs(Dictionary<string, Spec> specs)
    {
        var plc = new[] { "plcName" };
        specs[TechnologyObjectMethods.List] = Read(Array.Empty<string>(), new[] { "plcName", "folderPath" });
        specs[TechnologyObjectMethods.GetParameters] = Read(new[] { "name" }, new[] { "plcName", "parameterName", "prefix", "offset", "limit" });
        specs[TechnologyObjectMethods.GetHardwareConnection] = Read(new[] { "name" }, plc);
        specs[TechnologyObjectMethods.GetContent] = Read(new[] { "name" }, plc);

        specs[TechnologyObjectMethods.Create] = Write(new[] { "name", "technologyType", "version" }, new[] { "plcName", "folderPath" });
        specs[TechnologyObjectMethods.Delete] = Write(new[] { "name" }, plc);
        specs[TechnologyObjectMethods.Rename] = Write(new[] { "name", "newName" }, plc);
        specs[TechnologyObjectMethods.CreateGroup] = Write(new[] { "folderPath" }, plc);
        specs[TechnologyObjectMethods.DeleteGroup] = Write(new[] { "folderPath" }, plc);
        specs[TechnologyObjectMethods.RenameGroup] = Write(new[] { "folderPath", "newName" }, plc);
        specs[TechnologyObjectMethods.SetParameter] = Write(new[] { "name", "parameterName" }, new[] { "plcName", "value", "tableName", "tagName" });
        specs[TechnologyObjectMethods.ConnectHardware] = Write(new[] { "name", "connectionInterface", "connectionKind" }, new[]
        {
            "plcName", "interfaceIndex", "inputAddress", "outputAddress", "connectOption", "modulePath", "outputModulePath",
            "channelNumber", "channelIoType", "channelType", "channelIndex", "tableName", "tagName", "dbMemberPath",
        });
        specs[TechnologyObjectMethods.DisconnectHardware] = Write(new[] { "name", "connectionInterface" }, new[] { "plcName", "interfaceIndex" });
        specs[TechnologyObjectMethods.Export] = Write(new[] { "name", "filePath" }, new[] { "plcName", "overwrite" });
        specs[TechnologyObjectMethods.Import] = Write(new[] { "filePath" }, new[] { "plcName", "folderPath", "overwrite" });
        specs[TechnologyObjectMethods.UpdateContent] = Write(new[] { "name", "content", "expectedContentHash" }, plc);
        return specs;
    }

    private static (string Name, Func<PlcOperationRequest, string?> Get)[] WithTechnologyObjectStringFields(
        (string Name, Func<PlcOperationRequest, string?> Get)[] fields)
        => fields.Concat(new (string Name, Func<PlcOperationRequest, string?> Get)[]
        {
            ("technologyType", op => op.TechnologyType),
            ("version", op => op.Version),
            ("parameterName", op => op.ParameterName),
            ("prefix", op => op.Prefix),
            ("tagName", op => op.TagName),
            ("connectionInterface", op => op.ConnectionInterface),
            ("connectionKind", op => op.ConnectionKind),
            ("connectOption", op => op.ConnectOption),
            ("channelIoType", op => op.ChannelIoType),
            ("channelType", op => op.ChannelType),
            ("dbMemberPath", op => op.DbMemberPath),
            // filePath is shared with the alarm file operations and declared in the common list.
        }).ToArray();

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> TechnologyObjectOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("offset", op => op.Offset is not null),
        ("limit", op => op.Limit is not null),
        ("interfaceIndex", op => op.InterfaceIndex is not null),
        ("inputAddress", op => op.InputAddress is not null),
        ("outputAddress", op => op.OutputAddress is not null),
        ("modulePath", op => op.ModulePath is not null),
        ("outputModulePath", op => op.OutputModulePath is not null),
        ("channelNumber", op => op.ChannelNumber is not null),
        ("channelIndex", op => op.ChannelIndex is not null),
        // overwrite is shared with the alarm exports and declared in the common list.
    };

    /// <summary>The shape rules of one technology object item; nothing here reads the project.</summary>
    private static void ValidateTechnologyObjectItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (!TechnologyObjectMethods.IsWorkerMethod(op.Operation)) return;
        void Error(string message) => errors.Add($"{prefix}: {message}");
        foreach (var (field, value) in new[]
                 {
                     ("name", op.Name), ("newName", op.NewName), ("technologyType", op.TechnologyType), ("parameterName", op.ParameterName),
                     ("tagName", op.TagName), ("connectOption", op.ConnectOption), ("channelIoType", op.ChannelIoType),
                     ("channelType", op.ChannelType), ("dbMemberPath", op.DbMemberPath),
                 })
        {
            if (value is not null && string.IsNullOrWhiteSpace(value)) Error($"'{field}' must be nonblank when supplied.");
        }

        if (op.NewName is not null && op.NewName.Contains('/')) Error("'newName' must not contain '/'.");
        if (op.Prefix is not null && op.Prefix.Length == 0) Error("'prefix' must not be empty when supplied.");
        switch (op.Operation)
        {
            case TechnologyObjectMethods.GetParameters:
                if (op.ParameterName is not null && (op.Prefix is not null || op.Offset is not null || op.Limit is not null))
                    Error("'parameterName' reads one parameter exactly and cannot be combined with prefix, offset or limit.");
                if (op.Offset is < 0) Error("'offset' must be 0 or more.");
                if (op.Limit is < 1 or > MaxParameterLimit) Error($"'limit' must be between 1 and {MaxParameterLimit}.");
                break;
            case TechnologyObjectMethods.Create:
                if (op.Version is not null && ParseVersion(op.Version) is null)
                    Error($"'version' '{op.Version}' must be a version of two to four numeric components, e.g. 6.0.");
                if (op.Name?.Contains('/') == true) Error("'name' must not contain '/'; place the object with folderPath.");
                break;
            case TechnologyObjectMethods.CreateGroup:
            case TechnologyObjectMethods.DeleteGroup:
            case TechnologyObjectMethods.RenameGroup:
                if (op.FolderPath is not null && Segments(op.FolderPath).Count == 0)
                    Error("'folderPath' must name a user group, for example /Axes/Line1; the system group '/' cannot be created, deleted or renamed.");
                break;
            case TechnologyObjectMethods.SetParameter:
            {
                var tag = op.TableName is not null || op.TagName is not null;
                if (tag && (op.TableName is null || op.TagName is null)) Error("a PlcTag value needs both tableName and tagName.");
                if (tag == (op.Value is not null)) Error("set exactly one of value (a text value) or tableName with tagName (a PlcTag value).");
                // Before any worker call; the worker refuses it again with the same text (ValueLengthRefusal).
                if (ValueLengthRefusal(op.Value) is { } tooLong) Error(tooLong + ".");
                break;
            }

            case TechnologyObjectMethods.ConnectHardware:
                ValidateConnect(op, Error);
                break;
            case TechnologyObjectMethods.DisconnectHardware:
                if (op.ConnectionInterface is { } disconnecting)
                {
                    if (!Interfaces.All.Contains(disconnecting, StringComparer.Ordinal))
                        Error($"'connectionInterface' must be one of {string.Join(", ", Interfaces.All)}.");
                    else if (!HasDisconnect(disconnecting))
                        Error($"the {disconnecting} interface has no documented Disconnect.");
                    if (op.InterfaceIndex is not null && disconnecting != Interfaces.Sensor) Error("'interfaceIndex' applies only to connectionInterface sensor.");
                }

                if (op.InterfaceIndex is < 0) Error("'interfaceIndex' must be 0 or more.");
                break;
            case TechnologyObjectMethods.Export:
            case TechnologyObjectMethods.Import:
                if (op.FilePath is not null && ValidateLocalFilePath(op.FilePath) is { } refusal) Error("'filePath' " + refusal);
                break;
        }
    }

    private static void ValidateConnect(PlcOperationRequest op, Action<string> error)
    {
        if (op.ConnectionInterface is not { } connectionInterface || op.ConnectionKind is not { } kind) return;
        if (!Interfaces.All.Contains(connectionInterface, StringComparer.Ordinal))
        {
            error($"'connectionInterface' must be one of {string.Join(", ", Interfaces.All)}.");
            return;
        }

        if (!ConnectionKinds.All.Contains(kind, StringComparer.Ordinal))
        {
            error($"'connectionKind' must be one of {string.Join(", ", ConnectionKinds.All)}.");
            return;
        }

        if (!Allows(connectionInterface, kind))
        {
            error($"connectionKind '{kind}' has no documented Connect overload on the {connectionInterface} interface (documented: {string.Join(", ", KindsFor(connectionInterface))}).");
            return;
        }

        var needed = new List<string>();
        var allowed = new List<string> { "interfaceIndex" };
        switch (kind)
        {
            case ConnectionKinds.Address when connectionInterface == Interfaces.MeasuringInput:
                needed.Add("inputAddress");
                break;
            case ConnectionKinds.Address when connectionInterface == Interfaces.OutputCam:
                needed.Add("outputAddress");
                break;
            case ConnectionKinds.Address:
                needed.AddRange(new[] { "inputAddress", "outputAddress" });
                allowed.Add("connectOption");
                break;
            case ConnectionKinds.Module:
                needed.Add("modulePath");
                if (connectionInterface == Interfaces.MeasuringInput) needed.Add("channelIndex");
                break;
            case ConnectionKinds.Modules:
                needed.AddRange(new[] { "modulePath", "outputModulePath" });
                allowed.Add("connectOption");
                break;
            case ConnectionKinds.Channel:
                needed.AddRange(new[] { "modulePath", "channelNumber" });
                allowed.AddRange(new[] { "channelIoType", "channelType" });
                break;
            case ConnectionKinds.Tag:
                needed.AddRange(new[] { "tableName", "tagName" });
                break;
            case ConnectionKinds.DbMember:
                needed.Add("dbMemberPath");
                break;
        }

        var supplied = new (string Name, bool Set)[]
        {
            ("inputAddress", op.InputAddress is not null), ("outputAddress", op.OutputAddress is not null), ("connectOption", op.ConnectOption is not null),
            ("modulePath", op.ModulePath is not null), ("outputModulePath", op.OutputModulePath is not null), ("channelNumber", op.ChannelNumber is not null),
            ("channelIoType", op.ChannelIoType is not null), ("channelType", op.ChannelType is not null), ("channelIndex", op.ChannelIndex is not null),
            ("tableName", op.TableName is not null), ("tagName", op.TagName is not null), ("dbMemberPath", op.DbMemberPath is not null),
            ("interfaceIndex", op.InterfaceIndex is not null),
        };
        foreach (var (name, set) in supplied)
        {
            if (needed.Contains(name) && !set) error($"connectionKind '{kind}' on the {connectionInterface} interface requires '{name}'.");
            if (set && !needed.Contains(name) && !allowed.Contains(name))
                error($"'{name}' is not used by connectionKind '{kind}' on the {connectionInterface} interface.");
        }

        if (op.InterfaceIndex is not null && connectionInterface != Interfaces.Sensor) error("'interfaceIndex' applies only to connectionInterface sensor.");
        foreach (var (name, value) in new[] { ("interfaceIndex", op.InterfaceIndex), ("inputAddress", op.InputAddress), ("outputAddress", op.OutputAddress),
                     ("channelNumber", op.ChannelNumber), ("channelIndex", op.ChannelIndex) })
            if (value is < 0) error($"'{name}' must be 0 or more.");
        foreach (var (name, path) in new[] { ("modulePath", op.ModulePath), ("outputModulePath", op.OutputModulePath) })
            if (path is not null && (path.Count < 2 || path.Any(string.IsNullOrEmpty)))
                error($"'{name}' must list the device name and at least one device item name (each nonempty).");
    }
}
