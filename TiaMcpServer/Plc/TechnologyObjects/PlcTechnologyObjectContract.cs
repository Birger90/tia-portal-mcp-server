using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;

namespace TiaMcpServer.Plc;

/// <summary>Maps a validated technology object item to its worker parameters; every field is forwarded once, unchanged.</summary>
public static class PlcTechnologyObjectRequests
{
    public static TechnologyObjectRequestInfo ToRequest(PlcOperationRequest op) => new()
    {
        PlcName = op.PlcName, Name = op.Name, FolderPath = op.FolderPath, NewName = op.NewName,
        TechnologyType = op.TechnologyType, Version = op.Version, ParameterName = op.ParameterName, Prefix = op.Prefix,
        Offset = op.Offset, Limit = op.Limit, Value = op.Value, TableName = op.TableName, TagName = op.TagName,
        ConnectionInterface = op.ConnectionInterface, InterfaceIndex = op.InterfaceIndex, ConnectionKind = op.ConnectionKind,
        InputAddress = op.InputAddress, OutputAddress = op.OutputAddress, ConnectOption = op.ConnectOption,
        ModulePath = op.ModulePath?.ToList(), OutputModulePath = op.OutputModulePath?.ToList(),
        ChannelNumber = op.ChannelNumber, ChannelIoType = op.ChannelIoType, ChannelType = op.ChannelType, ChannelIndex = op.ChannelIndex,
        DbMemberPath = op.DbMemberPath, FilePath = op.FilePath, Overwrite = op.Overwrite ?? false,
        Content = op.Content, ExpectedContentHash = op.ExpectedContentHash,
    };
}

/// <summary>
/// The declared result types of the technology object operations: one CLR type per operation, decoded through the
/// worker-payload reader (every member required, explicit nulls). A payload that does not decode, or that contradicts
/// the request, is a <c>protocol_error</c> and is never echoed back.
/// </summary>
public static class PlcTechnologyObjectPayloadContract
{
    private static readonly IReadOnlySet<string> ValueKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        TechnologyObjectValueKinds.Null, TechnologyObjectValueKinds.Boolean, TechnologyObjectValueKinds.Integer, TechnologyObjectValueKinds.Real,
        TechnologyObjectValueKinds.String, TechnologyObjectValueKinds.Enum, TechnologyObjectValueKinds.PlcTag, TechnologyObjectValueKinds.DeviceItem,
        TechnologyObjectValueKinds.Channel, TechnologyObjectValueKinds.Object, TechnologyObjectValueKinds.Unreadable,
    };

    /// <summary>The writes whose postcondition failure carries the before/after state as the failed item's result.</summary>
    public static bool HasFailureEvidence(string operation)
        => operation is TechnologyObjectMethods.ConnectHardware or TechnologyObjectMethods.DisconnectHardware or TechnologyObjectMethods.SetParameter;

    /// <exception cref="JsonException">The payload does not decode as the operation's declared type.</exception>
    public static JsonElement DecodeRead(PlcOperationRequest operation, string payload, IReadOnlyList<string> warnings)
        => operation.Operation switch
        {
            TechnologyObjectMethods.List => CanonicalJson.NormalizeWorkerPayload<TechnologyObjectInventoryInfo>(payload, inventory =>
            {
                Require(inventory.Plcs);
                foreach (var plc in inventory.Plcs) CheckPlc(plc);
            }).Element,
            TechnologyObjectMethods.GetParameters => CanonicalJson.NormalizeWorkerPayload<TechnologyObjectParametersInfo>(payload, page =>
            {
                Require(page.Parameters);
                if (!Same(page.Name, operation.Name) || !Path(page.GroupPath) || page.Parameters.Count > page.Limit || page.MatchedCount < page.Parameters.Count)
                    throw new JsonException();
                if (page.Parameters.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != page.Parameters.Count) throw new JsonException();
                // The page answers exactly the request: its paging, filters and counts are consistent.
                if (page.Offset != (operation.Offset ?? 0) || page.Limit != (operation.Limit ?? TechnologyObjectPolicy.DefaultParameterLimit)
                    || page.ParameterName != operation.ParameterName || page.Prefix != operation.Prefix)
                    throw new JsonException();
                if (page.TotalCount is { } total && page.MatchedCount > total) throw new JsonException();
                if (page.NextOffset is { } next && (next != page.Offset + page.Parameters.Count || next >= page.MatchedCount)) throw new JsonException();
                if (operation.ParameterName is not null
                    && (page.Parameters.Count != 1 || page.TotalCount is not null || page.NextOffset is not null || !Same(page.Parameters[0].Name, operation.ParameterName)))
                    throw new JsonException();
                if (operation.Prefix is { } prefix && page.Parameters.Any(p => !p.Name.StartsWith(prefix, StringComparison.Ordinal))) throw new JsonException();
                foreach (var parameter in page.Parameters) CheckParameter(parameter);
            }).Element,
            TechnologyObjectMethods.GetHardwareConnection => CanonicalJson.NormalizeWorkerPayload<TechnologyObjectHardwareConnectionInfo>(payload, connection =>
            {
                Require(connection.Services);
                Require(connection.Interfaces);
                Require(connection.UnavailableServices);
                if (!Same(connection.Name, operation.Name) || !Path(connection.GroupPath) || connection.Services.Count > TechnologyObjectPolicy.MaxServices
                    || connection.UnavailableServices.Count > TechnologyObjectPolicy.MaxServices
                    || connection.Interfaces.Count > TechnologyObjectPolicy.MaxInterfaces)
                    throw new JsonException();
                // Each interface (and sensor position) once.
                if (connection.Interfaces.Select(i => (i.Interface, i.Index)).Distinct().Count() != connection.Interfaces.Count) throw new JsonException();
                foreach (var endpoint in connection.Interfaces) CheckInterface(endpoint);
            }).Element,
            // The SimaticML document itself, hashed like every other content read (xml:sha256 of the delivered text).
            TechnologyObjectMethods.GetContent => CanonicalJson.ToElement(new PlcContentResult(SourceFormatNames.Xml, payload,
                ContentHashes.Compute(SourceFormatNames.Xml, payload), warnings)),
            _ => throw new JsonException($"No declared result contract for technology object operation '{operation.Operation}'."),
        };

    /// <exception cref="JsonException">The payload does not decode as <see cref="TechnologyObjectWriteResultInfo"/> or contradicts the request.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<TechnologyObjectWriteResultInfo>(payload, result =>
        {
            Require(result.ImportedObjects);
            if (result.Operation != operation.Operation || string.IsNullOrEmpty(result.PlcName)) throw new JsonException();
            if (result.GroupPath is not null && !Path(result.GroupPath)) throw new JsonException();
            if (result.TechnologyObject is { } technologyObject) CheckObject(technologyObject);
            if (result.Parameter is { } parameter) CheckParameter(parameter);
            if (result.PreviousParameter is { } previous) CheckParameter(previous);
            if (result.Interface is { } endpoint) CheckInterface(endpoint);
            if (result.PreviousInterface is { } previousEndpoint) CheckInterface(previousEndpoint);
            if (result.ImportedObjects.Any(string.IsNullOrEmpty) || result.ImportedObjects.Count > TechnologyObjectPolicy.MaxObjects
                || result.ImportedObjects.Distinct(TechnologyObjectPolicy.Names).Count() != result.ImportedObjects.Count)
                throw new JsonException();
            var ok = operation.Operation switch
            {
                TechnologyObjectMethods.Create => result.TechnologyObject is not null && Same(result.Name, operation.Name),
                TechnologyObjectMethods.UpdateContent => result.TechnologyObject is not null && Same(result.Name, operation.Name) && result.ImportedObjects.Count == 1,
                TechnologyObjectMethods.Rename => result.TechnologyObject is not null && Same(result.Name, operation.NewName),
                TechnologyObjectMethods.Delete => Same(result.Name, operation.Name),
                TechnologyObjectMethods.CreateGroup or TechnologyObjectMethods.DeleteGroup or TechnologyObjectMethods.RenameGroup => result.GroupPath is not null,
                TechnologyObjectMethods.SetParameter => result.Parameter is not null && result.PreviousParameter is not null
                    && Same(result.Parameter.Name, operation.ParameterName),
                TechnologyObjectMethods.ConnectHardware or TechnologyObjectMethods.DisconnectHardware
                    => result.Interface is not null && result.Interface.Interface == operation.ConnectionInterface
                        && (operation.InterfaceIndex is null || result.Interface.Index == operation.InterfaceIndex)
                        // The same endpoint before and after: interface, provider and position.
                        && (result.PreviousInterface is null || (result.PreviousInterface.Interface == result.Interface.Interface
                            && result.PreviousInterface.Provider == result.Interface.Provider && result.PreviousInterface.Index == result.Interface.Index)),
                // The written file is the requested one (the worker refuses a path its normalization would change).
                TechnologyObjectMethods.Export => result.FilePath is not null && operation.FilePath is not null
                    && string.Equals(result.FilePath, TechnologyObjectPolicy.NormalizeLocalFilePath(operation.FilePath), StringComparison.OrdinalIgnoreCase)
                    && Same(result.Name, operation.Name) && result.FileBytes is >= 0 && LowerHex(result.FileSha256, 64),
                TechnologyObjectMethods.Import => result.GroupPath is not null,
                _ => false,
            };
            if (!ok) throw new JsonException();
        }).Element;

    private static void CheckPlc(TechnologyObjectPlcInfo plc)
    {
        Require(plc.Groups);
        Require(plc.TechnologyObjects);
        // The walk stops at MaxObjects (then complete:false); a complete inventory has no more either.
        if (string.IsNullOrEmpty(plc.PlcName) || plc.Groups.Count + plc.TechnologyObjects.Count > TechnologyObjectPolicy.MaxObjects) throw new JsonException();
        if (plc.Groups.Select(g => g.Path).Distinct(TechnologyObjectPolicy.Names).Count() != plc.Groups.Count) throw new JsonException();
        foreach (var group in plc.Groups)
            if (!Path(group.Path) || group.Path == "/") throw new JsonException();
        foreach (var technologyObject in plc.TechnologyObjects) CheckObject(technologyObject);
    }

    private static void CheckObject(TechnologyObjectInfo technologyObject)
    {
        Require(technologyObject.Unreadable);
        if (string.IsNullOrEmpty(technologyObject.Name) || !Path(technologyObject.GroupPath)) throw new JsonException();
    }

    private static void CheckParameter(TechnologyObjectParameterInfo parameter)
    {
        Require(parameter.EnumMembers);
        if (parameter.Name is null || !ValueKinds.Contains(parameter.ValueKind) || parameter.EnumMembers.Count > TechnologyObjectPolicy.MaxEnumMembers
            || !Bounded(parameter.Value))
            throw new JsonException();
        // Members, the truncation and the flags marker belong to enum values only; a truncated list is full.
        if (parameter.ValueKind != TechnologyObjectValueKinds.Enum && (parameter.EnumMembers.Count > 0 || parameter.EnumMembersTruncated || parameter.EnumFlags))
            throw new JsonException();
        if (parameter.EnumMembersTruncated && parameter.EnumMembers.Count != TechnologyObjectPolicy.MaxEnumMembers) throw new JsonException();
    }

    private static void CheckInterface(TechnologyObjectConnectionInterfaceInfo endpoint)
    {
        Require(endpoint.Properties);
        Require(endpoint.Unreadable);
        if (!TechnologyObjectPolicy.Interfaces.All.Contains(endpoint.Interface, StringComparer.Ordinal) || string.IsNullOrEmpty(endpoint.Provider)) throw new JsonException();
        // Every documented property is reported exactly once: read (properties) or not (unreadable).
        var documented = TechnologyObjectPolicy.PropertiesFor(endpoint.Interface);
        var reported = endpoint.Properties.Select(p => p.Name).Concat(endpoint.Unreadable).ToList();
        if (reported.Count != documented.Count || !reported.OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(documented.OrderBy(n => n, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new JsonException();
        foreach (var property in endpoint.Properties)
        {
            if (string.IsNullOrEmpty(property.Name) || !ValueKinds.Contains(property.Kind) || !Bounded(property.Value)
                || property.ModulePath is { Count: > TechnologyObjectPolicy.MaxModulePathDepth } || property.ModulePath?.Any(name => !Bounded(name)) == true)
                throw new JsonException();
            StandalonePayloadContract.RejectNullElements(property.ModulePath);
        }
    }

    private static bool Path(string? path) => path is not null && path.StartsWith("/", StringComparison.Ordinal);

    /// <summary>A rendered text value: at most <see cref="TechnologyObjectPolicy.MaxTextChars"/> plus the truncation marker.</summary>
    private static bool Bounded(string? text) => text is null || text.Length <= TechnologyObjectPolicy.MaxTextChars + TechnologyObjectPolicy.TruncationMarker.Length;

    private static bool LowerHex(string? text, int length) => text is not null && text.Length == length && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool Same(string? left, string? right) => left is not null && right is not null && TechnologyObjectPolicy.Names.Equals(left, right);

    private static void Require<T>(IEnumerable<T>? values)
    {
        if (values is null) throw new JsonException();
        StandalonePayloadContract.RejectNullElements(values);
    }
}
