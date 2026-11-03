using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.Contracts.Network;

/// <summary>
/// Names of the IO system and transfer area members (GitHub issue #11). Evidence: the V21 IntelliSense file
/// Siemens.Engineering.Base.xml: <c>M:Siemens.Engineering.HW.IoSystem.Delete</c>, <c>M:...IoConnector.DisconnectFromIoSystem</c>,
/// <c>P:...IoConnector.ConnectedToIoSystem</c>, <c>P:...Features.NetworkInterface.TransferAreas</c> ("Composition of transfer
/// areas"), <c>P:...NetworkInterface.MulticastableTransferAreas</c> ("Composition of multicastable transfer areas"),
/// <c>M:...TransferAreaComposition.Create(System.String,TransferAreaType)</c> and <c>Create(System.String,TransferAreaType,System.Int32)</c>,
/// <c>M:...MulticastableTransferAreaComposition.Create(NetworkInterface,TransferAreaType)</c>, <c>Create(NetworkInterface,TransferAreaType,
/// System.String)</c>, <c>Create(NetworkInterface,TransferAreaType,System.String,System.Int32)</c>, <c>Create(MulticastableTransferArea,
/// TransferAreaType)</c>, <c>M:...TransferArea.Delete</c>, <c>M:...MulticastableTransferArea.Delete</c> and the properties listed in
/// <see cref="TransferAreaProperties"/>. V21 documents no member that creates a DP master system other than the generic
/// <c>IoController.CreateIoSystem(System.String)</c> that create_io_system already calls.
/// </summary>
public static class NetworkSystemNames
{
    public const string TransferAreaType = "Siemens.Engineering.HW.TransferAreaType";

    /// <summary>The documented <c>TransferAreaType</c> members (Siemens.Engineering.Base.xml, V21).</summary>
    public static IReadOnlyList<string> TransferAreaTypes { get; } = new[]
    {
        "CD", "DDX", "F_CD", "F_PS", "F_Proxy_CD", "IN", "IN_OUT", "ISOCHRON_IN", "ISOCHRON_OUT", "ISOC_STATUS_CONTROL",
        "LOCAL_RECORD_PUB", "LOCAL_RECORD_STO", "MS", "MSI", "MSI_MSO", "MSO", "MSO_LOCAL", "None", "OUT",
        "PROFISAFE_IN12_OUT6", "PROFISAFE_IN6_OUT12", "RECORD_READ_PUB", "RECORD_READ_STO", "RECORD_WRITE_PUB", "RECORD_WRITE_STO",
        "SUB_LOCAL_RECORD_PUB_READ", "SUB_LOCAL_RECORD_STO_READ", "SUB_MSI", "SUB_MSO", "TM",
    };

    /// <summary>The two transfer area compositions of a <c>NetworkInterface</c>.</summary>
    public static class TransferAreaCollections
    {
        /// <summary><c>NetworkInterface.TransferAreas</c>: address mapping between a local I-slave / I-device and its partner.</summary>
        public const string TransferAreas = "transferAreas";

        /// <summary><c>NetworkInterface.MulticastableTransferAreas</c>: multicastable areas (e.g. DDX, MSI/MSO).</summary>
        public const string Multicastable = "multicastable";

        public static IReadOnlyList<string> All { get; } = new[] { TransferAreas, Multicastable };

        /// <summary>The <c>NetworkInterface</c> property of a collection.</summary>
        public static string Property(string collection) => collection == Multicastable ? "MulticastableTransferAreas" : "TransferAreas";
    }

    /// <summary>The documented scalar properties read per collection, and the documented collection-valued ones (reported as counts).</summary>
    public static class TransferAreaProperties
    {
        public static IReadOnlyList<string> Scalar(string collection) => collection == TransferAreaCollections.Multicastable
            ? new[] { "Name", "Type", "Direction", "DataLength", "Comment" }
            : new[] { "Name", "Type", "Direction", "PositionNumber", "ExtendedPositionNumber", "LocalToPartnerLength", "PartnerToLocalLength" };

        public static IReadOnlyList<string> Collections(string collection) => collection == TransferAreaCollections.Multicastable
            ? new[] { "Addresses", "PartnerTransferAreas" }
            : new[] { "LocalAddresses", "PartnerAddresses", "TransferAreaMappingRules" };
    }
}

/// <summary>Worker request part of the transfer area operations: which collection, which existing area or what to create.</summary>
public sealed class TransferAreaRequestInfo
{
    /// <summary><see cref="NetworkSystemNames.TransferAreaCollections"/>.</summary>
    public string Collection { get; set; } = string.Empty;

    /// <summary>update/delete: the zero-based index in the collection as list_transfer_areas reports it.</summary>
    public int? Index { get; set; }

    /// <summary>update/delete: the area's current name (evidence); create: the name passed to Create.</summary>
    public string? Name { get; set; }

    /// <summary>create: the <c>TransferAreaType</c> member name.</summary>
    public string? Type { get; set; }

    /// <summary>create in transferAreas: the slot number of <c>TransferAreaComposition.Create(String, TransferAreaType, Int32)</c>.</summary>
    public int? PositionNumber { get; set; }

    /// <summary>create in multicastable: the length of <c>Create(NetworkInterface, TransferAreaType, String, Int32)</c>.</summary>
    public int? DataLength { get; set; }

    /// <summary>create in multicastable: index of an existing area of the partner interface for <c>Create(MulticastableTransferArea, TransferAreaType)</c>.</summary>
    public int? TemplateIndex { get; set; }
}

/// <summary>One documented property of a transfer area, projected as text (the XML gives no property types).</summary>
public sealed class TransferAreaPropertyInfo
{
    public string Name { get; set; } = string.Empty;

    public RuntimeValueInfo Value { get; set; } = new();
}

/// <summary>One attribute <c>GetAttributeInfos()</c> lists for a transfer area, with its access mode (what update_transfer_area may set).</summary>
public sealed class TransferAreaAttributeInfo
{
    public string Name { get; set; } = string.Empty;

    public string AccessMode { get; set; } = string.Empty;
}

/// <summary>One transfer area of a network interface.</summary>
public sealed class HardwareTransferAreaInfo
{
    public string Collection { get; set; } = string.Empty;

    public int Index { get; set; }

    /// <summary><c>Name</c> as text; null when unreadable.</summary>
    public string? Name { get; set; }

    /// <summary>The documented scalar properties, and the collection-valued ones as <c>&lt;Name&gt;Count</c>.</summary>
    public List<TransferAreaPropertyInfo> Properties { get; set; } = new();

    /// <summary>Every attribute GetAttributeInfos() lists; empty when the area is no IEngineeringObject or the list is unreadable.</summary>
    public List<TransferAreaAttributeInfo> Attributes { get; set; } = new();

    public string RuntimeType { get; set; } = string.Empty;
}

/// <summary>Result of list_transfer_areas: both compositions of the interface of the target device item.</summary>
public sealed class HardwareTransferAreaListInfo
{
    public HardwareObjectIdentityInfo Target { get; set; } = new();

    /// <summary>Null when the interface offers no TransferAreas composition on this installation (then a message says so).</summary>
    public List<HardwareTransferAreaInfo>? TransferAreas { get; set; }

    /// <summary>Null when the interface offers no MulticastableTransferAreas composition on this installation.</summary>
    public List<HardwareTransferAreaInfo>? MulticastableTransferAreas { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>Result of create_transfer_area, update_transfer_area and delete_transfer_area.</summary>
public sealed class HardwareTransferAreaWriteResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public HardwareObjectIdentityInfo Target { get; set; } = new();

    public string Collection { get; set; } = string.Empty;

    /// <summary>The partner interface's device item of a multicastable create; null otherwise.</summary>
    public HardwareObjectIdentityInfo? Partner { get; set; }

    /// <summary>The area before the write (update, delete); null for create.</summary>
    public HardwareTransferAreaInfo? Before { get; set; }

    /// <summary>The area read back after the write (create, update); null for delete.</summary>
    public HardwareTransferAreaInfo? After { get; set; }

    /// <summary>The collection's element count before and after the write.</summary>
    public int CountBefore { get; set; }

    public int CountAfter { get; set; }

    /// <summary>update_transfer_area: one outcome per requested attribute; empty otherwise.</summary>
    public List<HardwareAttributeWriteOutcomeInfo> Attributes { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>Result of delete_io_system.</summary>
public sealed class HardwareIoSystemDeleteResultInfo
{
    public string SubnetId { get; set; } = string.Empty;

    public int Number { get; set; }

    /// <summary><c>IoSystem.Name</c> before the delete; null when unreadable.</summary>
    public string? Name { get; set; }

    /// <summary><c>IoSystem.ConnectedIoDevices</c> count before the delete; null when unreadable.</summary>
    public int? ConnectedIoDeviceCount { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>Result of disconnect_from_io_system.</summary>
public sealed class HardwareIoConnectorDisconnectResultInfo
{
    public HardwareObjectIdentityInfo Target { get; set; } = new();

    /// <summary>The zero-based index of the disconnected connector in <c>NetworkInterface.IoConnectors</c>.</summary>
    public int ConnectorIndex { get; set; }

    /// <summary>The IO system the connector was connected to (<c>ConnectedToIoSystem</c> before the call).</summary>
    public string? IoSystemName { get; set; }

    public int? IoSystemNumber { get; set; }

    /// <summary>Read back: <c>ConnectedToIoSystem</c> is null after the call.</summary>
    public bool DisconnectedAfter { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// The Siemens-free part of the transfer area operations, shared by the worker and the FakeWorker: the two compositions are read
/// from the live <c>NetworkInterface</c> by their documented property names, an area is selected by index with its name as
/// evidence, the documented <c>Create</c> overload is bound by exact name and argument types assignable from the runtime values
/// (<see cref="RuntimeServiceValues.Invoke"/>; missing is api_member_missing), and every write is read back from the composition.
/// Siemens exceptions pass through unchanged for the shared classification.
/// </summary>
public static class TransferAreaRun
{
    /// <summary>The composition object of <paramref name="collection"/>; null with a reason when the interface has no such readable property.</summary>
    public static (object? Composition, string? Error) Composition(object networkInterface, string collection)
    {
        var name = NetworkSystemNames.TransferAreaCollections.Property(collection);
        var property = Library.LibraryRuntimeBinding.Property(networkInterface.GetType(), name, out var missing);
        if (property is null) return (null, missing);
        try
        {
            var value = property.GetValue(networkInterface, null);
            return value is null ? (null, $"NetworkInterface.{name} returned null.") : (value, null);
        }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    public static List<object> Elements(object composition, string collection)
        => RuntimeServiceValues.Elements(composition, "NetworkInterface." + NetworkSystemNames.TransferAreaCollections.Property(collection))
            .Where(element => element is not null).Select(element => element!).ToList();

    /// <summary>Projects one area: its documented scalar properties, the collection-valued ones as counts, and the listed attributes.</summary>
    public static HardwareTransferAreaInfo Describe(object area, string collection, int index, Func<Exception, bool> recoverable,
        Func<object, List<TransferAreaAttributeInfo>> attributes)
    {
        var info = new HardwareTransferAreaInfo { Collection = collection, Index = index, RuntimeType = area.GetType().FullName ?? area.GetType().Name };
        foreach (var name in NetworkSystemNames.TransferAreaProperties.Scalar(collection))
        {
            var value = RuntimeServiceValues.ReadProperty(area, name, recoverable);
            info.Properties.Add(new TransferAreaPropertyInfo { Name = name, Value = value });
            if (name == "Name" && value.State is RuntimeValueStates.Value or RuntimeValueStates.Empty) info.Name = value.Value;
        }

        foreach (var name in NetworkSystemNames.TransferAreaProperties.Collections(collection))
            info.Properties.Add(new TransferAreaPropertyInfo { Name = name + "Count", Value = Count(area, name, recoverable) });
        info.Attributes = attributes(area);
        return info;
    }

    /// <summary>The area at <paramref name="index"/>; its name must read as <paramref name="name"/> (evidence), else a refusal.</summary>
    public static object Select(List<object> elements, string collection, int index, string name, Func<object, string?> readName)
    {
        if (index < 0 || index >= elements.Count)
            throw new RuntimeBindingException(Worker.WorkerFailureCategories.TargetNotFound,
                $"The interface has no area at index {index} in {collection} ({elements.Count} listed). Read them with network_read list_transfer_areas.");
        var actual = readName(elements[index]);
        if (!string.Equals(actual, name, StringComparison.Ordinal))
            throw new RuntimeBindingException(Worker.WorkerFailureCategories.TargetEvidenceMismatch,
                $"The area at index {index} in {collection} is named '{actual ?? "(unreadable)"}', not '{name}'. Re-read with network_read list_transfer_areas.");
        return elements[index];
    }

    /// <summary>
    /// Calls the documented Create overload for the request and returns the created area and its index in the re-read composition.
    /// <paramref name="partner"/> is the partner <c>NetworkInterface</c> or (with a template index) the partner's template area.
    /// </summary>
    public static (object Created, int Index, int CountBefore, int CountAfter) Create(object composition, TransferAreaRequestInfo request, object type,
        object? partner)
    {
        var before = Elements(composition, request.Collection).Count;
        Type[] types;
        object?[] arguments;
        string documented;
        if (request.Collection == NetworkSystemNames.TransferAreaCollections.TransferAreas)
        {
            var name = request.Name ?? throw new RuntimeBindingException(Worker.WorkerFailureCategories.ValidationError, "A transfer area needs a name.");
            (types, arguments, documented) = request.PositionNumber is { } slot
                ? (new[] { typeof(string), type.GetType(), typeof(int) }, new object?[] { name, type, slot }, "TransferAreaComposition.Create(String, TransferAreaType, Int32)")
                : (new[] { typeof(string), type.GetType() }, new object?[] { name, type }, "TransferAreaComposition.Create(String, TransferAreaType)");
        }
        else
        {
            var source = partner ?? throw new RuntimeBindingException(Worker.WorkerFailureCategories.ValidationError, "A multicastable transfer area needs a partner.");
            if (request.TemplateIndex is not null)
                (types, arguments, documented) = (new[] { source.GetType(), type.GetType() }, new object?[] { source, type },
                    "MulticastableTransferAreaComposition.Create(MulticastableTransferArea, TransferAreaType)");
            else if (request.Name is null)
                (types, arguments, documented) = (new[] { source.GetType(), type.GetType() }, new object?[] { source, type },
                    "MulticastableTransferAreaComposition.Create(NetworkInterface, TransferAreaType)");
            else if (request.DataLength is { } length)
                (types, arguments, documented) = (new[] { source.GetType(), type.GetType(), typeof(string), typeof(int) }, new object?[] { source, type, request.Name, length },
                    "MulticastableTransferAreaComposition.Create(NetworkInterface, TransferAreaType, String, Int32)");
            else
                (types, arguments, documented) = (new[] { source.GetType(), type.GetType(), typeof(string) }, new object?[] { source, type, request.Name },
                    "MulticastableTransferAreaComposition.Create(NetworkInterface, TransferAreaType, String)");
        }

        var created = RuntimeServiceValues.Invoke(composition, "Create", types, arguments, documented)
            ?? throw new RuntimeBindingException(Worker.WorkerFailureCategories.PostconditionFailed,
                $"{documented} returned null; the area may exist. Inspect it with network_read list_transfer_areas before retrying.");
        var after = Elements(composition, request.Collection);
        var index = after.FindIndex(element => Equals(element, created));
        if (index < 0)
            throw new RuntimeBindingException(Worker.WorkerFailureCategories.PostconditionFailed,
                $"{documented} returned an area that the re-read composition does not list. Inspect it with network_read list_transfer_areas before retrying.");
        return (created, index, before, after.Count);
    }

    /// <summary>Calls <c>Delete()</c> on the area and returns the composition count before and after; one fewer is required.</summary>
    public static (int CountBefore, int CountAfter) Delete(object composition, string collection, object area)
    {
        var before = Elements(composition, collection).Count;
        RuntimeServiceValues.Invoke(area, "Delete", Type.EmptyTypes, Array.Empty<object?>(), collection == NetworkSystemNames.TransferAreaCollections.Multicastable
            ? "MulticastableTransferArea.Delete()" : "TransferArea.Delete()");
        var after = Elements(composition, collection).Count;
        if (after != before - 1)
            throw new RuntimeBindingException(Worker.WorkerFailureCategories.PostconditionFailed,
                $"Delete() returned, but the composition lists {after} areas (before: {before}). Inspect it with network_read list_transfer_areas before retrying.");
        return (before, after);
    }

    private static RuntimeValueInfo Count(object area, string name, Func<Exception, bool> recoverable)
    {
        var property = Library.LibraryRuntimeBinding.Property(area.GetType(), name, out var missing);
        if (property is null) return new RuntimeValueInfo { State = RuntimeValueStates.Missing, Error = missing };
        object? value;
        try
        {
            value = property.GetValue(area, null);
        }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
        {
            if (!recoverable(exception.InnerException))
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            return new RuntimeValueInfo { State = RuntimeValueStates.Unreadable, Error = RuntimeServiceValues.Bound($"{exception.InnerException.GetType().Name}: {exception.InnerException.Message}") };
        }

        if (value is null) return new RuntimeValueInfo { State = RuntimeValueStates.Null };
        try
        {
            var count = RuntimeServiceValues.Elements(value, name).Count;
            return new RuntimeValueInfo { State = RuntimeValueStates.Value, Value = count.ToString(System.Globalization.CultureInfo.InvariantCulture), RuntimeType = value.GetType().FullName };
        }
        catch (RuntimeBindingException exception)
        {
            return new RuntimeValueInfo { State = RuntimeValueStates.Unreadable, Error = exception.Message, RuntimeType = value.GetType().FullName };
        }
    }
}
