using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.ChangeDetection;

/// <summary>
/// Worker methods of the offline change-detection reads (GitHub issue #6). Each name is also the public <c>plc_read</c>
/// operation (Observe). Parameters travel in <c>WorkerRequest.PlcChangeDetection</c>. Evidence: the V21 IntelliSense file
/// Siemens.Engineering.Step7.xml, members <c>P:Siemens.Engineering.SW.PlcChecksumProvider.Software</c> ("Software checksum"),
/// <c>.TextLists</c> ("Text lists checksum"), <c>M:Siemens.Engineering.SW.FingerprintProvider.GetFingerprints</c> ("Read
/// Fingerprint", returns <c>IList&lt;Siemens.Engineering.SW.Fingerprint&gt;</c>) and <c>P:Siemens.Engineering.SW.Fingerprint.Id</c> /
/// <c>.Value</c>. The XML gives no type for the two checksums, Id or Value, and does not say which object offers the providers;
/// both are found at runtime among <c>GetServiceInfos()</c>.
/// </summary>
public static class PlcChangeDetectionMethods
{
    /// <summary>plc_read, Observe: <c>PlcChecksumProvider.Software</c> and <c>.TextLists</c> of one PLC or of every PLC.</summary>
    public const string GetPlcChecksums = "get_plc_checksums";

    /// <summary>plc_read, Observe: <c>FingerprintProvider.GetFingerprints()</c> of one block or PLC data type.</summary>
    public const string GetFingerprints = "get_fingerprints";

    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(new[] { GetPlcChecksums, GetFingerprints });

    public static bool IsRead(string? method) => method is not null && Read.Contains(method, StringComparer.Ordinal);

    public static bool IsWorkerMethod(string? method) => IsRead(method);
}

/// <summary>The service type names and members the change-detection reads bind at runtime (exact full names).</summary>
public static class PlcChangeDetectionNames
{
    public const string ChecksumProvider = "Siemens.Engineering.SW.PlcChecksumProvider";
    public const string SoftwareProperty = "Software";
    public const string TextListsProperty = "TextLists";

    public const string FingerprintProvider = "Siemens.Engineering.SW.FingerprintProvider";
    public const string GetFingerprintsMethod = "GetFingerprints";
    public const string IdProperty = "Id";
    public const string ValueProperty = "Value";

    public const string ObjectKindBlock = "block";
    public const string ObjectKindType = "type";

    /// <summary>What an empty or missing checksum means, per the Siemens TIA V21 skill change-detection (not the XML).</summary>
    public const string EmptyChecksumNote =
        "An empty or null checksum means, per the Siemens TIA Portal V21 skill change-detection, that the software was not compiled or was changed since the last compile; compile it (compile_check) and read again.";
}

/// <summary>The worker parameters of both reads: get_plc_checksums takes <c>PlcName</c> (null = every PLC); get_fingerprints takes exactly one of <c>BlockPath</c> or <c>TypePath</c>.</summary>
public sealed class PlcChangeDetectionRequestInfo
{
    public string? PlcName { get; set; }

    public string? BlockPath { get; set; }

    public string? TypePath { get; set; }
}

/// <summary>The result of get_plc_checksums: one entry per PLC in project order.</summary>
public sealed class PlcChecksumReadInfo
{
    public string Operation { get; set; } = PlcChangeDetectionMethods.GetPlcChecksums;

    public List<PlcChecksumPlcInfo> Plcs { get; set; } = new();

    public string Note { get; set; } = PlcChangeDetectionNames.EmptyChecksumNote;
}

public sealed class PlcChecksumPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>True when the PLC software listed and returned <c>PlcChecksumProvider</c>.</summary>
    public bool Available { get; set; }

    /// <summary>Why the provider is unavailable (not listed, or listed but not returned); null when available.</summary>
    public string? Unavailable { get; set; }

    /// <summary><c>PlcChecksumProvider.Software</c> as text; null when the provider is unavailable.</summary>
    public RuntimeValueInfo? Software { get; set; }

    /// <summary><c>PlcChecksumProvider.TextLists</c> as text; null when the provider is unavailable.</summary>
    public RuntimeValueInfo? TextLists { get; set; }
}

/// <summary>The result of get_fingerprints for one block or PLC data type.</summary>
public sealed class PlcFingerprintReadInfo
{
    public string Operation { get; set; } = PlcChangeDetectionMethods.GetFingerprints;

    /// <summary><see cref="PlcChangeDetectionNames.ObjectKindBlock"/> or <see cref="PlcChangeDetectionNames.ObjectKindType"/>.</summary>
    public string ObjectKind { get; set; } = string.Empty;

    /// <summary>The requested blockPath or typePath, unchanged.</summary>
    public string Path { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    /// <summary>The runtime type of the returned collection.</summary>
    public string? CollectionType { get; set; }

    public List<PlcFingerprintInfo> Fingerprints { get; set; } = new();
}

/// <summary>One <c>Siemens.Engineering.SW.Fingerprint</c>: its <c>Id</c> (documented values of <c>FingerprintId</c> are not assumed) and <c>Value</c>, as text.</summary>
public sealed class PlcFingerprintInfo
{
    public RuntimeValueInfo Id { get; set; } = new();

    public RuntimeValueInfo Value { get; set; } = new();
}

/// <summary>
/// The Siemens-free part of both reads, shared by the worker and the FakeWorker: the provider is taken from the object's
/// listed services by exact full name, every member is bound by exact name on the live object, and every value is projected
/// as text without interpretation. Failures are <see cref="RuntimeBindingException"/> with their category.
/// </summary>
public static class PlcChangeDetectionRun
{
    public static PlcChecksumPlcInfo ReadChecksums(string plcName, string deviceName, IReadOnlyList<Type> offered, Func<Type, object?> retrieve,
        Func<Exception, bool> recoverable)
    {
        var entry = new PlcChecksumPlcInfo { PlcName = plcName, DeviceName = deviceName };
        var (provider, unavailable) = RuntimeServiceValues.Find(offered, retrieve, PlcChangeDetectionNames.ChecksumProvider);
        if (provider is null)
        {
            entry.Unavailable = unavailable ?? RuntimeServiceValues.NotListed($"The PLC software '{plcName}'", PlcChangeDetectionNames.ChecksumProvider);
            return entry;
        }

        entry.Available = true;
        entry.Software = RuntimeServiceValues.ReadProperty(provider, PlcChangeDetectionNames.SoftwareProperty, recoverable);
        entry.TextLists = RuntimeServiceValues.ReadProperty(provider, PlcChangeDetectionNames.TextListsProperty, recoverable);
        return entry;
    }

    /// <summary>
    /// <c>FingerprintProvider.GetFingerprints()</c> of the object: not listed or not returned is unsupported_capability, a missing
    /// <c>GetFingerprints()</c> is api_member_missing, a null or non-collection return is worker_operation_failed.
    /// </summary>
    public static PlcFingerprintReadInfo ReadFingerprints(string objectKind, string path, string plcName, IReadOnlyList<Type> offered,
        Func<Type, object?> retrieve, Func<Exception, bool> recoverable)
    {
        var (provider, unavailable) = RuntimeServiceValues.Find(offered, retrieve, PlcChangeDetectionNames.FingerprintProvider);
        if (provider is null)
            throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
                (unavailable ?? RuntimeServiceValues.NotListed($"The {objectKind} '{path}'", PlcChangeDetectionNames.FingerprintProvider)) + " No fingerprint was read.");
        var returned = RuntimeServiceValues.Invoke(provider, PlcChangeDetectionNames.GetFingerprintsMethod, Type.EmptyTypes, Array.Empty<object?>(),
            "FingerprintProvider.GetFingerprints()");
        var result = new PlcFingerprintReadInfo { ObjectKind = objectKind, Path = path, PlcName = plcName, CollectionType = returned?.GetType().FullName };
        foreach (var element in RuntimeServiceValues.Elements(returned, "FingerprintProvider.GetFingerprints()"))
        {
            if (element is null)
            {
                result.Fingerprints.Add(new PlcFingerprintInfo
                {
                    Id = new RuntimeValueInfo { State = RuntimeValueStates.Null },
                    Value = new RuntimeValueInfo { State = RuntimeValueStates.Null },
                });
                continue;
            }

            result.Fingerprints.Add(new PlcFingerprintInfo
            {
                Id = RuntimeServiceValues.ReadProperty(element, PlcChangeDetectionNames.IdProperty, recoverable),
                Value = RuntimeServiceValues.ReadProperty(element, PlcChangeDetectionNames.ValueProperty, recoverable),
            });
        }

        return result;
    }
}
