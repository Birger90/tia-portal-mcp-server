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

    /// <summary>
    /// plc_read, Observe: whether each PLC looks ready to download, read offline without compiling: <c>PlcBlock.IsConsistent</c>
    /// of every block and the <c>PlcChecksumProvider.Software</c> checksum (idea from 1748477582/tia-portal-openness-mcp
    /// <c>CheckDownloadReadiness</c>, MIT; reimplemented).
    /// </summary>
    public const string CheckDownloadReadiness = "check_download_readiness";

    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(new[] { GetPlcChecksums, GetFingerprints, CheckDownloadReadiness });

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

/// <summary>The result of check_download_readiness: one entry per PLC in project order. Nothing is compiled or downloaded.</summary>
public sealed class PlcDownloadReadinessReadInfo
{
    public string Operation { get; set; } = PlcChangeDetectionMethods.CheckDownloadReadiness;

    public List<PlcDownloadReadinessPlcInfo> Plcs { get; set; } = new();

    public string Note { get; set; } = PlcDownloadReadiness.Note;
}

public sealed class PlcDownloadReadinessPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary><see cref="PlcDownloadReadiness.Ready"/>, <see cref="PlcDownloadReadiness.NotReady"/> or <see cref="PlcDownloadReadiness.Unknown"/>.</summary>
    public string Verdict { get; set; } = PlcDownloadReadiness.Unknown;

    /// <summary>Why the verdict is not ready, one plain sentence each; empty when ready.</summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>Blocks whose <c>IsConsistent</c> was read (program blocks, system groups, software and safety units).</summary>
    public int BlockCount { get; set; }

    public int InconsistentCount { get; set; }

    /// <summary>Blocks whose <c>IsConsistent</c> could not be read.</summary>
    public int UnreadableCount { get; set; }

    /// <summary>Paths of the inconsistent blocks, at most <see cref="PlcDownloadReadiness.MaxListedBlocks"/>.</summary>
    public List<string> InconsistentBlocks { get; set; } = new();

    /// <summary>Inconsistent blocks not listed because of the cap.</summary>
    public int InconsistentBlocksOmitted { get; set; }

    /// <summary>The software and text list checksums, as get_plc_checksums reads them.</summary>
    public PlcChecksumPlcInfo Checksums { get; set; } = new();

    /// <summary>Read problems that did not stop the check (e.g. software units that could not be read).</summary>
    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// The Siemens-free verdict of check_download_readiness, shared by the worker and the FakeWorker. Ready: every block reads
/// consistent and the software checksum has a value. Not ready: a block reads inconsistent, or the checksum reads empty or null
/// (per the Siemens V21 skill change-detection: not compiled or changed since the last compile). Unknown: nothing contradicts a
/// download, but a block or the checksum could not be read, or the PLC offers no checksum provider. The check reads only: it
/// never compiles, and TIA Portal's own download checks still decide (plc_download).
/// </summary>
public static class PlcDownloadReadiness
{
    public const string Ready = "ready";
    public const string NotReady = "notReady";
    public const string Unknown = "unknown";

    public const int MaxListedBlocks = 200;

    public const string Note =
        "Offline check without compiling: IsConsistent of every block and the software checksum. TIA Portal's own checks during plc_download still decide; "
        + "compile with compile_check to make inconsistent blocks consistent.";

    /// <summary>Fills <see cref="PlcDownloadReadinessPlcInfo.Verdict"/> and <see cref="PlcDownloadReadinessPlcInfo.Reasons"/> from the counts and the checksum.</summary>
    public static PlcDownloadReadinessPlcInfo Evaluate(PlcDownloadReadinessPlcInfo entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        entry.Reasons.Clear();
        var notReady = false;
        var unknown = false;
        if (entry.InconsistentCount > 0)
        {
            notReady = true;
            entry.Reasons.Add($"{entry.InconsistentCount} block(s) are not consistent (changed since their last compile); run compile_check for PLC '{entry.PlcName}'.");
        }

        if (entry.UnreadableCount > 0)
        {
            unknown = true;
            entry.Reasons.Add($"The consistency of {entry.UnreadableCount} block(s) could not be read.");
        }

        var software = entry.Checksums.Software;
        if (!entry.Checksums.Available)
        {
            unknown = true;
            entry.Reasons.Add("The PLC offers no software checksum (PlcChecksumProvider); compile state cannot be confirmed from it.");
        }
        else if (software is null || software.State is RuntimeValueStates.Empty or RuntimeValueStates.Null)
        {
            notReady = true;
            entry.Reasons.Add("The software checksum is empty: the software was not compiled or was changed since the last compile; run compile_check.");
        }
        else if (software.State != RuntimeValueStates.Value)
        {
            unknown = true;
            entry.Reasons.Add("The software checksum could not be read.");
        }

        entry.Verdict = notReady ? NotReady : unknown ? Unknown : Ready;
        return entry;
    }

    /// <summary>Counts one block's <c>IsConsistent</c> text (<c>True</c>/<c>False</c>, or null when unreadable) into the entry.</summary>
    public static void Count(PlcDownloadReadinessPlcInfo entry, string path, string? isConsistent)
    {
        entry.BlockCount++;
        if (!bool.TryParse(isConsistent, out var consistent))
        {
            entry.UnreadableCount++;
            return;
        }

        if (consistent) return;
        entry.InconsistentCount++;
        if (entry.InconsistentBlocks.Count < MaxListedBlocks) entry.InconsistentBlocks.Add(path);
        else entry.InconsistentBlocksOmitted++;
    }
}
