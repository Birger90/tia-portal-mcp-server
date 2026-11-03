using System.Globalization;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.TechnologyObjects;

/// <summary>
/// Siemens-free rules of the technology object operations, shared by the host catalog, the worker and the
/// FakeWorker: names of the documented V21 types and members bound at runtime, the documented
/// <c>Connect</c> overload matrix, group paths, local file paths, versions and the import document check.
/// </summary>
public static class TechnologyObjectPolicy
{
    public const int DefaultParameterLimit = 200;
    public const int MaxParameterLimit = 2000;

    /// <summary>Objects plus groups listed per PLC before the walk stops with <c>complete:false</c>.</summary>
    public const int MaxObjects = 5000;

    public const int MaxEnumMembers = 200;
    public const int MaxServices = 100;

    /// <summary>Connection interfaces reported per object (more fails the read instead of being cut).</summary>
    public const int MaxInterfaces = 64;

    /// <summary>Names in a reported module path (device down to the item; the worker walks at most this many parents).</summary>
    public const int MaxModulePathDepth = 32;
    public const int MaxPathChars = 1024;

    /// <summary>Text values are cut to this length (with a marker), never silently.</summary>
    public const int MaxTextChars = 4000;

    /// <summary>The selectors of the documented connection interfaces.</summary>
    public static class Interfaces
    {
        /// <summary><c>AxisHardwareConnectionProvider.ActorInterface</c>.</summary>
        public const string Actor = "actor";

        /// <summary><c>AxisHardwareConnectionProvider.SensorInterface</c> (axis) or <c>EncoderHardwareConnectionProvider.SensorInterface</c> (encoder).</summary>
        public const string Sensor = "sensor";

        /// <summary><c>AxisHardwareConnectionProvider.TorqueInterface</c>.</summary>
        public const string Torque = "torque";

        /// <summary>The <c>MeasuringInputHardwareConnectionProvider</c> itself.</summary>
        public const string MeasuringInput = "measuringInput";

        /// <summary>The <c>OutputCamHardwareConnectionProvider</c> itself.</summary>
        public const string OutputCam = "outputCam";

        /// <summary>The <c>IdentTechnologicalObjectProvider</c> (<c>ConnectedIdentDevice</c>, <c>Connect(DeviceItem)</c>).</summary>
        public const string Ident = "ident";

        public static IReadOnlyList<string> All { get; } = new[] { Actor, Sensor, Torque, MeasuringInput, OutputCam, Ident };
    }

    /// <summary>Which documented <c>Connect</c> overload a connect item calls.</summary>
    public static class ConnectionKinds
    {
        /// <summary><c>Connect(Int32, Int32, ConnectOption)</c> (actor, sensor, torque) or <c>Connect(Int32)</c> (measuring input: input bit, output cam: output bit).</summary>
        public const string Address = "address";

        /// <summary><c>Connect(DeviceItem)</c> (actor, sensor, torque, ident) or <c>Connect(DeviceItem, Int32)</c> (measuring input, with channelIndex).</summary>
        public const string Module = "module";

        /// <summary><c>Connect(DeviceItem, DeviceItem)</c>, or <c>Connect(DeviceItem, DeviceItem, ConnectOption)</c> with connectOption (actor, sensor, torque).</summary>
        public const string Modules = "modules";

        /// <summary><c>Connect(Channel)</c> (actor, sensor, measuring input, output cam).</summary>
        public const string Channel = "channel";

        /// <summary><c>Connect(PlcTag)</c> (actor, sensor: analog connection; output cam).</summary>
        public const string Tag = "tag";

        /// <summary><c>Connect(String)</c>: connect to a DB member (actor, sensor, torque).</summary>
        public const string DbMember = "dbMember";

        public static IReadOnlyList<string> All { get; } = new[] { Address, Module, Modules, Channel, Tag, DbMember };
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DocumentedOverloads = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        [Interfaces.Actor] = new[] { ConnectionKinds.Address, ConnectionKinds.Module, ConnectionKinds.Modules, ConnectionKinds.Channel, ConnectionKinds.Tag, ConnectionKinds.DbMember },
        [Interfaces.Sensor] = new[] { ConnectionKinds.Address, ConnectionKinds.Module, ConnectionKinds.Modules, ConnectionKinds.Channel, ConnectionKinds.Tag, ConnectionKinds.DbMember },
        [Interfaces.Torque] = new[] { ConnectionKinds.Address, ConnectionKinds.Module, ConnectionKinds.Modules, ConnectionKinds.DbMember },
        [Interfaces.MeasuringInput] = new[] { ConnectionKinds.Address, ConnectionKinds.Module, ConnectionKinds.Channel },
        [Interfaces.OutputCam] = new[] { ConnectionKinds.Address, ConnectionKinds.Channel, ConnectionKinds.Tag },
        [Interfaces.Ident] = new[] { ConnectionKinds.Module },
    };

    /// <summary>The connection kinds whose <c>Connect</c> overload the V21 reference documents for <paramref name="connectionInterface"/>.</summary>
    public static IReadOnlyList<string> KindsFor(string connectionInterface)
        => DocumentedOverloads.TryGetValue(connectionInterface, out var kinds) ? kinds : Array.Empty<string>();

    public static bool Allows(string connectionInterface, string connectionKind) => KindsFor(connectionInterface).Contains(connectionKind, StringComparer.Ordinal);

    /// <summary>Only the ident interface has no documented <c>Disconnect</c>.</summary>
    public static bool HasDisconnect(string connectionInterface) => connectionInterface != Interfaces.Ident && Interfaces.All.Contains(connectionInterface, StringComparer.Ordinal);

    /// <summary>Exact V21 type and member names (Step7 assembly) bound at runtime; never matched loosely.</summary>
    public static class SiemensNames
    {
        public const string MotionNamespace = "Siemens.Engineering.SW.TechnologicalObjects.Motion";
        public const string AxisProvider = MotionNamespace + ".AxisHardwareConnectionProvider";
        public const string EncoderProvider = MotionNamespace + ".EncoderHardwareConnectionProvider";
        public const string MeasuringInputProvider = MotionNamespace + ".MeasuringInputHardwareConnectionProvider";
        public const string OutputCamProvider = MotionNamespace + ".OutputCamHardwareConnectionProvider";
        public const string IdentProvider = "Siemens.Engineering.SW.TechnologicalObjects.Ident.IdentTechnologicalObjectProvider";
        public const string ConnectOption = MotionNamespace + ".ConnectOption";

        public static IReadOnlyList<string> Providers { get; } = new[] { AxisProvider, EncoderProvider, MeasuringInputProvider, OutputCamProvider, IdentProvider };
    }

    /// <summary>
    /// The documented providers that serve <paramref name="connectionInterface"/>, in the order a connect or disconnect
    /// consults them: actor and torque only <c>AxisHardwareConnectionProvider</c>; sensor <c>AxisHardwareConnectionProvider.SensorInterface</c>
    /// first and <c>EncoderHardwareConnectionProvider.SensorInterface</c> only as the documented alternative (an object
    /// whose axis provider offers no sensor); measuring input, output cam and ident their own provider. A write resolves
    /// only these, so a listed service that <c>GetService</c> does not return and that the interface does not need never
    /// fails it (K18, live V21 2026-11-02: TO_PositioningAxis 6.0 listed <c>EncoderHardwareConnectionProvider</c> but
    /// GetService returned null, while <c>AxisHardwareConnectionProvider</c> served the actor interface).
    /// </summary>
    public static IReadOnlyList<string> ProvidersFor(string connectionInterface) => connectionInterface switch
    {
        Interfaces.Actor or Interfaces.Torque => new[] { SiemensNames.AxisProvider },
        Interfaces.Sensor => new[] { SiemensNames.AxisProvider, SiemensNames.EncoderProvider },
        Interfaces.MeasuringInput => new[] { SiemensNames.MeasuringInputProvider },
        Interfaces.OutputCam => new[] { SiemensNames.OutputCamProvider },
        Interfaces.Ident => new[] { SiemensNames.IdentProvider },
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// The endpoints of <paramref name="connectionInterface"/> a write chooses among: those of the first provider in
    /// <see cref="ProvidersFor"/> that offers at least one (so an axis's own sensors win over an encoder provider's);
    /// empty when none does. Shared by the host planner, the worker and the FakeWorker.
    /// </summary>
    public static List<(string Interface, string Provider, int? Index, T Target)> PreferredEndpoints<T>(
        IEnumerable<(string Interface, string Provider, int? Index, T Target)> offered, string connectionInterface)
    {
        var matching = offered.Where(e => e.Interface == connectionInterface).ToList();
        foreach (var provider in ProvidersFor(connectionInterface))
        {
            var served = matching.Where(e => string.Equals(e.Provider, provider, StringComparison.Ordinal)).ToList();
            if (served.Count > 0) return served;
        }

        return new List<(string Interface, string Provider, int? Index, T Target)>();
    }

    /// <summary>
    /// The <c>unsupported_capability</c> message of a write whose interface no provider serves while a provider it needs is
    /// listed but not returned by <c>GetService</c>: the reasons of exactly those providers (<paramref name="unavailable"/>
    /// holds <see cref="TiaMcpServer.Contracts.Shared.ListedServices.UnavailableReason"/> texts), ending in one
    /// "Nothing was changed."; null when no needed provider is among them.
    /// </summary>
    public static string? UnavailableProviderMessage(string connectionInterface, IEnumerable<string> unavailable)
    {
        var reasons = ProvidersFor(connectionInterface).Select(TiaMcpServer.Contracts.Shared.ListedServices.UnavailableReason)
            .Where(unavailable.Contains).ToList();
        return reasons.Count == 0 ? null
            : $"The {connectionInterface} interface cannot be reached: {string.Join(" ", reasons)} Nothing was changed.";
    }

    /// <summary>The documented properties of <c>AxisEncoderHardwareConnectionInterface</c> (actor and sensor).</summary>
    public static IReadOnlyList<string> AxisEncoderInterfaceProperties { get; } = new[]
    {
        "Channel", "ConnectOption", "InputAddress", "InputModule", "InputOutputModule", "IsConnected", "OutputAddress", "OutputModule",
        "OutputTag", "PathToDBMember", "SensorIndexInActorTelegram",
    };

    /// <summary>The documented properties of <c>TorqueHardwareConnectionInterface</c>.</summary>
    public static IReadOnlyList<string> TorqueInterfaceProperties { get; } = new[]
    {
        "ConnectOption", "InputAddress", "InputModule", "InputOutputModule", "IsConnected", "OutputAddress", "OutputModule", "PathToDBMember",
    };

    /// <summary>The documented properties of <c>MeasuringInputHardwareConnectionProvider</c>.</summary>
    public static IReadOnlyList<string> MeasuringInputProperties { get; } = new[] { "Channel", "ChannelIndex", "InputAddress", "InputModule", "IsConnected" };

    /// <summary>The documented properties of <c>OutputCamHardwareConnectionProvider</c>.</summary>
    public static IReadOnlyList<string> OutputCamProperties { get; } = new[] { "Channel", "IsConnected", "OutputAddress", "OutputTag" };

    /// <summary>The documented property of <c>IdentTechnologicalObjectProvider</c>.</summary>
    public static IReadOnlyList<string> IdentProperties { get; } = new[] { "ConnectedIdentDevice" };

    public static IReadOnlyList<string> PropertiesFor(string connectionInterface) => connectionInterface switch
    {
        Interfaces.Actor or Interfaces.Sensor => AxisEncoderInterfaceProperties,
        Interfaces.Torque => TorqueInterfaceProperties,
        Interfaces.MeasuringInput => MeasuringInputProperties,
        Interfaces.OutputCam => OutputCamProperties,
        Interfaces.Ident => IdentProperties,
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// The one documented hint that a parameter write is not a hardware connection: the Siemens skill states that
    /// writing <c>Actor.*</c> parameters succeeds without connecting the drive.
    /// </summary>
    public const string ActorParameterPrefix = "Actor.";

    public static string NormalizeGroupPath(string? path)
    {
        var segments = Segments(path);
        return segments.Count == 0 ? "/" : "/" + string.Join("/", segments);
    }

    public static IReadOnlyList<string> Segments(string? path)
        => (path ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())
            .Where(s => s.Length > 0).ToArray();

    public static string ParentPath(string groupPath)
    {
        var normalized = NormalizeGroupPath(groupPath);
        var slash = normalized.LastIndexOf('/');
        return slash > 0 ? normalized.Substring(0, slash) : "/";
    }

    public static string Combine(string groupPath, string name) => groupPath == "/" ? "/" + name : groupPath + "/" + name;

    public static StringComparer Names => StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// The version text of a create: <see cref="System.Version"/> syntax with two to four numeric components and no
    /// surrounding blanks; null when malformed. The accepted versions are installation-specific (Siemens skill).
    /// </summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrEmpty(text) || text!.Trim() != text || text.Any(c => !(char.IsDigit(c) || c == '.'))) return null;
        return System.Version.TryParse(text, out var version) ? version : null;
    }

    /// <summary>
    /// Null when <paramref name="path"/> is an absolute local file path a Windows TIA Portal host can write, by its text
    /// alone (the check is lexical; the worker reports what only the file system can tell, see <see cref="LocalFileWarnings"/>):
    /// drive-rooted (<c>X:\</c> or <c>X:/</c>), naming a file (no trailing separator), at most <see cref="MaxPathChars"/>
    /// characters, without control or Unicode format characters, wildcard or reserved characters, a second colon
    /// (alternate data streams), empty, <c>.</c> or <c>..</c> segments, segments ending in a dot or a space (Windows drops
    /// them silently) and reserved device names (<see cref="IsReservedDeviceName"/>) in any segment. UNC shares (<c>\\server\share\...</c>)
    /// are accepted since 2026-11-01 (owner decision); device paths (<c>\\?\</c>, <c>\\.\</c>, <c>\??\</c>) are refused
    /// (<see cref="TiaMcpServer.Contracts.Files.LocalFilePaths.DevicePathReason"/>). Otherwise the refusal text. A valid path names exactly one file, so
    /// <see cref="NormalizeLocalFilePath"/> is the only normalization left.
    /// </summary>
    public static string? ValidateLocalFilePath(string? path)
    {
        const string Shape = "must be an absolute local file path on a drive or a UNC share path (for example C:\\Exports\\Axis_1.xml or \\\\server\\share\\Axis_1.xml)";
        if (string.IsNullOrWhiteSpace(path)) return "is required and " + Shape + ".";
        if (path!.Length > MaxPathChars) return $"is longer than {MaxPathChars} characters.";
        static bool Separator(char c) => c is '\\' or '/';
        if (TiaMcpServer.Contracts.Files.LocalFilePaths.IsDevicePath(path))
            return TiaMcpServer.Contracts.Files.LocalFilePaths.DevicePathReason + $" (received '{Bound(path)}').";
        var unc = TiaMcpServer.Contracts.Files.LocalFilePaths.IsUncShare(path);
        if (!unc && path.Length >= 2 && Separator(path[0]) && Separator(path[1])) return Shape + $" (received '{Bound(path)}').";
        if (path.Any(c => char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format))
            return "contains control or Unicode format characters.";
        var driveRooted = path.Length >= 3 && (path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') && path[1] == ':' && Separator(path[2]);
        if (!driveRooted && !unc) return Shape + $" (received '{Bound(path)}').";
        if (Separator(path[path.Length - 1])) return "must name a file, not a directory.";
        if (path.Skip(2).Any(c => c is ':' or '*' or '?' or '"' or '<' or '>' or '|'))
            return "contains a character Windows does not allow in a path (: * ? \" < > |) after the drive.";
        var segments = path.Substring(unc ? 2 : 3).Split('\\', '/');
        if (segments.Any(s => s.Length == 0)) return "contains an empty path segment.";
        foreach (var segment in segments)
        {
            if (segment is "." or "..") return "must not contain '.' or '..' segments; give the path without relative navigation.";
            if (segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
                return $"has the segment '{Bound(segment)}', which ends with a dot or a space (Windows removes them silently).";
            if (IsReservedDeviceName(segment))
                return $"has the segment '{Bound(segment)}', a reserved Windows device name (CON, PRN, AUX, NUL, COM0-9, LPT0-9, CONIN$, CONOUT$, CLOCK$, also with an extension).";
        }

        return null;
    }

    /// <summary>The path of a valid <see cref="ValidateLocalFilePath"/> path with backslash separators, as the effect and the worker show it.</summary>
    public static string NormalizeLocalFilePath(string path) => path.Replace('/', '\\');

    /// <summary>
    /// A Windows reserved device name, with or without an extension (the name before the first dot, trailing blanks
    /// ignored, case-insensitive): CON, PRN, AUX, NUL, COM0-COM9, LPT0-LPT9, the superscript COM¹²³/LPT¹²³, CONIN$, CONOUT$
    /// and CLOCK$. The single rule for every caller-file check of the technology object operations.
    /// </summary>
    public static bool IsReservedDeviceName(string segment)
    {
        var dot = segment.IndexOf('.');
        var stem = (dot < 0 ? segment : segment.Substring(0, dot)).TrimEnd(' ');
        if (new[] { "CONIN$", "CONOUT$", "CLOCK$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)) return true;
        if (stem.Length == 3)
            return new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase);
        return stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && "0123456789\u00B9\u00B2\u00B3".IndexOf(stem[3]) >= 0;
    }

    /// <summary>
    /// Warnings about a validated full local path that only the file system can give, never refusals (owner decision: a
    /// mapped network drive is used as requested): the drive is a network drive, the file itself or an existing ancestor
    /// directory is a reparse point (symbolic link, junction, mount point), so the file is read or written where it
    /// points. <paramref name="isReparsePoint"/> answers for an existing file or directory; the probes return null when
    /// they cannot tell (or the path does not exist).
    /// </summary>
    public static List<string> LocalFileWarnings(string fullPath, Func<string, bool?> isNetworkDrive, Func<string, bool?> isReparsePoint)
    {
        var warnings = new List<string>();
        if (fullPath.Length < 3) return warnings;
        var unc = TiaMcpServer.Contracts.Files.LocalFilePaths.IsUncShare(fullPath);
        var root = fullPath.Substring(0, 3);
        if (unc)
            warnings.Add($"'{fullPath}' is on a network share; the file is used there as requested.");
        else if (isNetworkDrive(root) == true)
            warnings.Add($"'{fullPath}' is on a network drive ({root}); the file is used there as requested, on the share the drive is mapped to.");
        if (isReparsePoint(fullPath) == true)
            warnings.Add($"'{fullPath}' is itself a reparse point (symbolic link or junction), so the file it points to is used.");
        var separators = new[] { '\\', '/' };
        // A UNC share's server and share names are no directories to probe.
        var floor = unc ? fullPath.IndexOfAny(separators, fullPath.IndexOfAny(separators, 2) + 1) : 2;
        for (var end = fullPath.LastIndexOfAny(separators); end > floor; end = fullPath.LastIndexOfAny(separators, end - 1))
        {
            var directory = fullPath.Substring(0, end);
            if (isReparsePoint(directory) != true) continue;
            warnings.Add($"'{directory}' is a reparse point (junction, symbolic link or mount point), so '{fullPath}' is used where it points.");
            break;
        }

        return warnings;
    }

    /// <summary>
    /// The full path of a caller file: the lexical check, then <paramref name="fullPath"/> (the worker passes
    /// <c>Path.GetFullPath</c>) checked again and required to equal <see cref="NormalizeLocalFilePath"/> of the request
    /// (case-insensitively); (null, refusal) otherwise or when the normalization throws.
    /// </summary>
    public static (string? Full, string? Refusal) ResolveLocalFile(string? path, Func<string, string> fullPath)
    {
        if (ValidateLocalFilePath(path) is { } refusal) return (null, "filePath " + refusal);
        string full;
        try
        {
            full = fullPath(path!);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, $"filePath '{Bound(path)}' is not a valid path: {exception.Message}");
        }

        if (ValidateLocalFilePath(full) is { } normalized) return (null, "filePath " + normalized);
        // A valid path names one file lexically; a normalization that changes more than separators (for example a short
        // 8.3 name) would make the reported path differ from the requested one, so it is refused before anything runs.
        return string.Equals(full, NormalizeLocalFilePath(path!), StringComparison.OrdinalIgnoreCase)
            ? (full, null)
            : (null, $"filePath '{Bound(path)}' resolves to '{Bound(full)}'; give that path.");
    }

    /// <summary>
    /// Exactly one endpoint for the requested interface (and sensor position) among the offered ones: (endpoint, null, null),
    /// or (null, failure category, message) when the interface is missing, the position does not exist or several match.
    /// </summary>
    public static (T? Endpoint, string? Category, string? Message) SelectEndpoint<T>(IReadOnlyList<(string Interface, string Provider, int? Index, T Target)> offered,
        string connectionInterface, int? index) where T : class
    {
        if (!Interfaces.All.Contains(connectionInterface, StringComparer.Ordinal))
            return (null, WorkerFailureCategories.ValidationError, $"connectionInterface must be one of {string.Join(", ", Interfaces.All)}.");
        var candidates = offered.Where(e => e.Interface == connectionInterface).ToList();
        if (candidates.Count == 0)
            return (null, WorkerFailureCategories.TargetKindUnsupported,
                $"The technology object offers no {connectionInterface} interface (no documented provider for it is among its services). Nothing was changed.");
        if (index is not null)
        {
            var indexed = candidates.Where(e => e.Index == index).ToList();
            return indexed.Count == 1 ? (indexed[0].Target, null, null)
                : (null, WorkerFailureCategories.TargetNotFound,
                    $"The {connectionInterface} interface has no position {index} (positions: {string.Join(", ", candidates.Select(e => e.Index?.ToString(CultureInfo.InvariantCulture) ?? "(single)"))}). Nothing was changed.");
        }

        return candidates.Count == 1 ? (candidates[0].Target, null, null)
            : (null, WorkerFailureCategories.TargetAmbiguous,
                $"The technology object has {candidates.Count} {connectionInterface} interfaces ({string.Join(", ", candidates.Select(e => e.Provider + (e.Index is null ? string.Empty : "[" + e.Index.Value.ToString(CultureInfo.InvariantCulture) + "]")))}); "
                + "set interfaceIndex. Nothing was changed.");
    }

    /// <summary>The arguments of a documented <c>Connect</c> overload, in order.</summary>
    public static class ConnectArguments
    {
        public const string InputAddress = "inputAddress";
        public const string OutputAddress = "outputAddress";
        public const string ConnectOption = "connectOption";
        public const string Module = "modulePath";
        public const string OutputModule = "outputModulePath";
        public const string ChannelIndex = "channelIndex";
        public const string Channel = "channel";
        public const string Tag = "tag";
        public const string DbMember = "dbMemberPath";
    }

    /// <summary>
    /// The documented <c>Connect</c> overload a connect item calls: its arguments in order and its signature text; null for a
    /// kind the interface does not document. <paramref name="connectOption"/> selects the three-argument modules overload.
    /// </summary>
    public static (IReadOnlyList<string> Arguments, string Signature)? Overload(string connectionInterface, string kind, bool connectOption)
    {
        if (!Allows(connectionInterface, kind)) return null;
        switch (kind)
        {
            case ConnectionKinds.Address when connectionInterface == Interfaces.MeasuringInput:
                return (new[] { ConnectArguments.InputAddress }, "Int32");
            case ConnectionKinds.Address when connectionInterface == Interfaces.OutputCam:
                return (new[] { ConnectArguments.OutputAddress }, "Int32");
            case ConnectionKinds.Address:
                return (new[] { ConnectArguments.InputAddress, ConnectArguments.OutputAddress, ConnectArguments.ConnectOption }, "Int32, Int32, ConnectOption");
            case ConnectionKinds.Module when connectionInterface == Interfaces.MeasuringInput:
                return (new[] { ConnectArguments.Module, ConnectArguments.ChannelIndex }, "DeviceItem, Int32");
            case ConnectionKinds.Module:
                return (new[] { ConnectArguments.Module }, "DeviceItem");
            case ConnectionKinds.Modules when connectOption:
                return (new[] { ConnectArguments.Module, ConnectArguments.OutputModule, ConnectArguments.ConnectOption }, "DeviceItem, DeviceItem, ConnectOption");
            case ConnectionKinds.Modules:
                return (new[] { ConnectArguments.Module, ConnectArguments.OutputModule }, "DeviceItem, DeviceItem");
            case ConnectionKinds.Channel:
                return (new[] { ConnectArguments.Channel }, "Channel");
            case ConnectionKinds.Tag:
                return (new[] { ConnectArguments.Tag }, "PlcTag");
            case ConnectionKinds.DbMember:
                return (new[] { ConnectArguments.DbMember }, "String");
            default:
                return null;
        }
    }

    /// <summary>
    /// The names of an <c>Import</c> return value (undocumented type) that is an enumerable whose every element
    /// <paramref name="nameOf"/> recognises as a technology object; null for anything else.
    /// </summary>
    public static List<string>? ReturnedNames(object? returned, Func<object, string?> nameOf)
    {
        if (returned is null or string || returned is not System.Collections.IEnumerable enumerable) return null;
        var names = new List<string>();
        foreach (var item in enumerable)
        {
            if (item is null || nameOf(item) is not { } name) return null;
            names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Null when <paramref name="import"/> describes exactly the addressed object the same way the current export does:
    /// the same root element with the same child elements (by name and count; <c>DocumentInfo</c>, which content reads
    /// remove, is ignored on both sides), exactly one child element of the root with <c>AttributeList/Name</c>, of the
    /// same element name and naming <paramref name="name"/> exactly (no trimming). The current export is the template, so
    /// nothing about the TO SimaticML schema is assumed; an export that does not have that shape makes the check (and the
    /// update) fail closed.
    /// </summary>
    public static string? CheckImportDocument(string? exported, string? import, string name)
    {
        if (!TryParse(exported, out var export) || export!.Root is null)
            return $"the current export of '{name}' could not be parsed, so the import document cannot be checked";
        var exportObjects = ObjectElements(export.Root).ToList();
        if (exportObjects.Count != 1 || ObjectName(exportObjects[0]) is not { } exportName || !string.Equals(exportName, name, StringComparison.Ordinal))
            return $"the current export of '{name}' has no single recognisable object element (one AttributeList with one Name '{name}'), so the import document cannot be checked";
        if (!TryParse(import, out var document) || document!.Root is null)
            return "content is not a well-formed XML document (DTDs are not accepted)";
        if (document.Root.Name != export.Root.Name)
            return $"content's root element is '{document.Root.Name}', but the SimaticML export of this object has '{export.Root.Name}'";
        var expectedChildren = RootChildren(export.Root);
        var children = RootChildren(document.Root);
        if (!expectedChildren.SequenceEqual(children))
            return $"content's root has the child elements [{string.Join(", ", children)}], but the export of '{name}' has [{string.Join(", ", expectedChildren)}]";
        var objects = ObjectElements(document.Root).ToList();
        if (objects.Count != 1)
            return $"content must describe exactly one object, but it has {objects.Count} object element(s)";
        if (objects[0].Name != exportObjects[0].Name)
            return $"content describes a '{objects[0].Name}', but '{name}' is a '{exportObjects[0].Name}'";
        if (ObjectName(objects[0]) is not { } named)
            return "content's object element must have exactly one AttributeList with exactly one Name";
        return string.Equals(named, name, StringComparison.Ordinal) ? null : $"content names '{Bound(named)}', not the addressed object '{name}' (compared exactly)";
    }

    /// <summary>The marker <see cref="Bound"/> appends to a cut text.</summary>
    public const string TruncationMarker = " [TRUNCATED]";

    /// <summary>Text cut to <see cref="MaxTextChars"/> with a visible marker (so at most <see cref="MaxTextChars"/> plus the marker).</summary>
    public static string? Bound(string? text)
        => text is null || text.Length <= MaxTextChars ? text : text.Substring(0, MaxTextChars) + TruncationMarker;

    /// <summary>
    /// The refusal of a parameter <c>value</c> longer than <see cref="MaxTextChars"/> (a longer text could not be read back
    /// uncut, so its write could not be verified), or null. The host catalog refuses it before any worker call and the
    /// worker again before any Siemens call, with this one text.
    /// </summary>
    public static string? ValueLengthRefusal(string? value)
        => value is { Length: > MaxTextChars }
            ? $"'value' has {value.Length.ToString(CultureInfo.InvariantCulture)} characters; at most {MaxTextChars.ToString(CultureInfo.InvariantCulture)} are accepted"
            : null;

    private static bool TryParse(string? text, out System.Xml.Linq.XDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = System.Xml.XmlReader.Create(new StringReader(text!), settings);
            document = System.Xml.Linq.XDocument.Load(reader);
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static IEnumerable<System.Xml.Linq.XElement> ObjectElements(System.Xml.Linq.XElement root)
        => root.Elements().Where(element => element.Elements().Any(child => child.Name.LocalName == "AttributeList"
            && child.Elements().Any(name => name.Name.LocalName == "Name")));

    /// <summary>The name of an object element with exactly one AttributeList holding exactly one Name; null otherwise (ambiguous).</summary>
    private static string? ObjectName(System.Xml.Linq.XElement element)
    {
        var lists = element.Elements().Where(child => child.Name.LocalName == "AttributeList").ToList();
        if (lists.Count != 1) return null;
        var names = lists[0].Elements().Where(child => child.Name.LocalName == "Name").ToList();
        return names.Count == 1 ? names[0].Value : null;
    }

    /// <summary>The root's child element names, sorted, without <c>DocumentInfo</c>.</summary>
    private static List<string> RootChildren(System.Xml.Linq.XElement root)
        => root.Elements().Select(child => child.Name.ToString()).Where(child => child != "DocumentInfo").OrderBy(child => child, StringComparer.Ordinal).ToList();
}
