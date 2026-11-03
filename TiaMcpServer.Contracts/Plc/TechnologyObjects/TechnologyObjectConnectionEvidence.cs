using System.Globalization;
using static TiaMcpServer.Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy;

namespace TiaMcpServer.Contracts.Plc.TechnologyObjects;

/// <summary>
/// Siemens-free evidence rules of <c>connect_technology_object_hardware</c>, shared by the worker, the FakeWorker and the
/// host read-back. The Siemens skill: "a successful call is not proof of a real connection"; confirm via
/// <c>IsConnected</c>/<c>InputAddress</c>/<c>OutputAddress</c>. Beyond <c>IsConnected</c>, every connect therefore names the
/// documented property that must read back the requested target (V21 reference property descriptions:
/// <c>InputAddress</c>/<c>OutputAddress</c> "Raw input/output bit address", <c>InputOutputModule</c> "Connected mixed (sub)
/// module", <c>InputModule</c>/<c>OutputModule</c>, <c>ChannelIndex</c>, <c>Channel</c> "Connected Channel", <c>OutputTag</c>
/// "Connected tag", <c>PathToDBMember</c> "Path to connected DB member", <c>ConnectedIdentDevice</c>; and, where the
/// overload takes one, <c>ConnectOption</c> "ConnectOption that has been set when the connection was made"). The worker compares
/// live objects with <see cref="object.Equals(object)"/> and numbers and texts by value; the host and the FakeWorker compare
/// the rendered properties (<see cref="Matches"/>).
/// </summary>
public static class TechnologyObjectConnectionEvidence
{
    /// <summary>How an expected property is compared.</summary>
    public static class TargetKinds
    {
        public const string Number = "number";
        public const string Text = "text";
        public const string DeviceItem = "deviceItem";
        public const string Channel = "channel";
        public const string PlcTag = "plcTag";

        /// <summary>An enum member compared by its exact name (rendered) or by <see cref="object.Equals(object)"/> with the passed value (live).</summary>
        public const string EnumMember = "enumMember";
    }

    /// <summary>Which resolved live argument an object-valued expectation is compared with.</summary>
    public static class Roles
    {
        public const string Module = "module";
        public const string OutputModule = "outputModule";
        public const string Channel = "channel";
        public const string Tag = "tag";
        public const string ConnectOption = "connectOption";
    }

    /// <summary>One documented property and the value it must read back after the connect.</summary>
    public sealed class Expectation
    {
        public Expectation(string property, string kind, string? role, long? number, string? text, IReadOnlyList<string>? modulePath,
            int? channelNumber, string? channelIoType, string? channelType)
        {
            Property = property;
            Kind = kind;
            Role = role;
            Number = number;
            Text = text;
            ModulePath = modulePath;
            ChannelNumber = channelNumber;
            ChannelIoType = channelIoType;
            ChannelType = channelType;
        }

        public string Property { get; }

        /// <summary>One of <see cref="TargetKinds"/>.</summary>
        public string Kind { get; }

        /// <summary>One of <see cref="Roles"/> for an object-valued expectation; null for a number or a text.</summary>
        public string? Role { get; }

        public long? Number { get; }

        public string? Text { get; }

        public IReadOnlyList<string>? ModulePath { get; }

        public int? ChannelNumber { get; }

        public string? ChannelIoType { get; }

        public string? ChannelType { get; }

        /// <summary>The requested value in the words <see cref="Shown"/> uses: <c>Property=value</c>.</summary>
        public string Requested => Property + "=" + Kind switch
        {
            TargetKinds.Number => Number?.ToString(CultureInfo.InvariantCulture),
            TargetKinds.Text or TargetKinds.PlcTag or TargetKinds.EnumMember => Text,
            TargetKinds.DeviceItem => Join(ModulePath),
            TargetKinds.Channel => $"{Join(ModulePath)} channel {ChannelNumber?.ToString(CultureInfo.InvariantCulture)}"
                + (ChannelIoType is null ? string.Empty : $" IoType {ChannelIoType}") + (ChannelType is null ? string.Empty : $" Type {ChannelType}"),
            _ => null,
        };
    }

    /// <summary>One expectation and what the read-back showed: Match null when the property could not be read.</summary>
    public sealed class Observation
    {
        public Observation(Expectation expectation, bool? match, string shown)
        {
            Expectation = expectation;
            Match = match;
            Shown = shown;
        }

        public Expectation Expectation { get; }

        public bool? Match { get; }

        public string Shown { get; }
    }

    /// <summary>
    /// The properties that must read back the target of a connect: the documented property of the overload's argument
    /// on the requested interface. Empty for an invalid interface/kind pair (refused earlier).
    /// </summary>
    public static IReadOnlyList<Expectation> Expectations(TechnologyObjectRequestInfo request)
    {
        var connectionInterface = request.ConnectionInterface ?? string.Empty;
        var kind = request.ConnectionKind ?? string.Empty;
        if (!Allows(connectionInterface, kind)) return Array.Empty<Expectation>();
        Expectation Address(string property, int? value) => new(property, TargetKinds.Number, null, value, null, null, null, null, null);
        Expectation Module(string property, string role, IReadOnlyList<string>? path) => new(property, TargetKinds.DeviceItem, role, null, null, path, null, null, null);
        // The interfaces that document a ConnectOption property (axis/encoder and torque) read back the option the overload passed.
        var option = connectionInterface is Interfaces.Actor or Interfaces.Sensor or Interfaces.Torque
            ? new Expectation("ConnectOption", TargetKinds.EnumMember, Roles.ConnectOption, null, request.ConnectOption ?? "Default", null, null, null, null)
            : null;
        switch (kind)
        {
            case ConnectionKinds.Address when connectionInterface == Interfaces.MeasuringInput:
                return new[] { Address("InputAddress", request.InputAddress) };
            case ConnectionKinds.Address when connectionInterface == Interfaces.OutputCam:
                return new[] { Address("OutputAddress", request.OutputAddress) };
            case ConnectionKinds.Address:
                return new[] { Address("InputAddress", request.InputAddress), Address("OutputAddress", request.OutputAddress), option! };
            case ConnectionKinds.Module when connectionInterface == Interfaces.MeasuringInput:
                return new[] { Module("InputModule", Roles.Module, request.ModulePath), Address("ChannelIndex", request.ChannelIndex) };
            case ConnectionKinds.Module when connectionInterface == Interfaces.Ident:
                return new[] { Module("ConnectedIdentDevice", Roles.Module, request.ModulePath) };
            case ConnectionKinds.Module:
                return new[] { Module("InputOutputModule", Roles.Module, request.ModulePath) };
            case ConnectionKinds.Modules when request.ConnectOption is not null:
                return new[] { Module("InputModule", Roles.Module, request.ModulePath), Module("OutputModule", Roles.OutputModule, request.OutputModulePath), option! };
            case ConnectionKinds.Modules:
                return new[] { Module("InputModule", Roles.Module, request.ModulePath), Module("OutputModule", Roles.OutputModule, request.OutputModulePath) };
            case ConnectionKinds.Channel:
                return new[]
                {
                    new Expectation("Channel", TargetKinds.Channel, Roles.Channel, null, null, request.ModulePath, request.ChannelNumber, request.ChannelIoType, request.ChannelType),
                };
            case ConnectionKinds.Tag:
                return new[] { new Expectation("OutputTag", TargetKinds.PlcTag, Roles.Tag, null, request.TagName, null, null, null, null) };
            case ConnectionKinds.DbMember:
                return new[] { new Expectation("PathToDBMember", TargetKinds.Text, null, null, request.DbMemberPath, null, null, null, null) };
            default:
                return Array.Empty<Expectation>();
        }
    }

    /// <summary>
    /// Whether the interface reads as connected: <c>IsConnected</c>, or for ident (which documents no <c>IsConnected</c>)
    /// whether <c>ConnectedIdentDevice</c> reads a device; null when that cannot be read.
    /// </summary>
    public static bool? Connected(TechnologyObjectConnectionInterfaceInfo endpoint)
    {
        if (endpoint.Interface != Interfaces.Ident) return endpoint.IsConnected;
        var device = endpoint.Properties.FirstOrDefault(p => p.Name == "ConnectedIdentDevice");
        return device is null || device.Kind == TechnologyObjectValueKinds.Unreadable ? null : device.Kind != TechnologyObjectValueKinds.Null;
    }

    /// <summary>A rendered property against an expectation: null when the property is missing or unreadable.</summary>
    public static bool? Matches(Expectation expectation, TechnologyObjectConnectionInterfaceInfo endpoint)
    {
        var property = endpoint.Properties.FirstOrDefault(p => p.Name == expectation.Property);
        if (property is null || property.Kind == TechnologyObjectValueKinds.Unreadable) return null;
        switch (expectation.Kind)
        {
            case TargetKinds.Number:
                return property.Kind == TechnologyObjectValueKinds.Integer && expectation.Number is { } number
                    && long.TryParse(property.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var read) && read == number;
            case TargetKinds.Text:
                return property.Kind == TechnologyObjectValueKinds.String && expectation.Text is not null && string.Equals(property.Value, expectation.Text, StringComparison.Ordinal);
            case TargetKinds.EnumMember:
                return property.Kind == TechnologyObjectValueKinds.Enum && expectation.Text is not null && string.Equals(property.Value, expectation.Text, StringComparison.Ordinal);
            case TargetKinds.PlcTag:
                // PLC tag names are unique per PLC; the rendered tag carries its name only.
                return property.Kind == TechnologyObjectValueKinds.PlcTag && expectation.Text is not null && Names.Equals(property.Value, expectation.Text);
            case TargetKinds.DeviceItem:
                return property.Kind == TechnologyObjectValueKinds.DeviceItem && SamePath(property.ModulePath, expectation.ModulePath);
            case TargetKinds.Channel:
            {
                if (property.Kind != TechnologyObjectValueKinds.Channel || !SamePath(property.ModulePath, expectation.ModulePath)) return false;
                // Rendered as "Type IoType Number" (TechnologyObjectRuntime.Describe).
                var parts = (property.Value ?? string.Empty).Split(' ');
                return parts.Length == 3 && expectation.ChannelNumber is { } channel
                    && parts[2] == channel.ToString(CultureInfo.InvariantCulture)
                    && (expectation.ChannelIoType is null || parts[1] == expectation.ChannelIoType)
                    && (expectation.ChannelType is null || parts[0] == expectation.ChannelType);
            }

            default:
                return false;
        }
    }

    /// <summary>A rendered property in the words of <see cref="Expectation.Requested"/>.</summary>
    public static string Shown(string propertyName, TechnologyObjectConnectionInterfaceInfo endpoint)
    {
        var property = endpoint.Properties.FirstOrDefault(p => p.Name == propertyName);
        return property is null || property.Kind == TechnologyObjectValueKinds.Unreadable
            ? propertyName + " unreadable"
            : propertyName + "=" + ShownValue(property.Kind, property.Value, property.ModulePath);
    }

    /// <summary>A value rendered by kind: a device item by its module path, a channel by its device item path and text.</summary>
    public static string ShownValue(string kind, string? text, IReadOnlyList<string>? modulePath) => kind switch
    {
        TechnologyObjectValueKinds.Null => "null",
        TechnologyObjectValueKinds.DeviceItem => modulePath is null ? text ?? "(no module path)" : Join(modulePath),
        TechnologyObjectValueKinds.Channel => (modulePath is null ? "(no module path)" : Join(modulePath)) + " " + text,
        _ => text ?? "null",
    };

    /// <summary>The observations of a rendered read-back (host and FakeWorker).</summary>
    public static IReadOnlyList<Observation> Observe(IReadOnlyList<Expectation> expectations, TechnologyObjectConnectionInterfaceInfo after)
        => expectations.Select(e => new Observation(e, Matches(e, after), Shown(e.Property, after))).ToList();

    /// <summary>
    /// Null when the connect is evidenced, otherwise the <c>postcondition_failed</c> text. Evidence: the interface reads as
    /// connected afterwards, and every expected property reads back the requested target. A property that cannot be read
    /// is accepted (with a warning) only when the interface was not connected before, because then the change of
    /// <c>IsConnected</c> from false to true is evidence of this connect; a previously connected (or unknown) interface
    /// still reading <c>IsConnected=true</c> shows nothing, and ident has no other evidence.
    /// </summary>
    public static string? ConnectFailure(string connectionInterface, string documented, string objectName, bool? connectedBefore, bool? connectedAfter,
        IReadOnlyList<Observation> observations, List<string> warnings)
    {
        const string Changed = "A returned Connect is not proof of a connection; the project may have been changed. Inspect it with get_technology_object_hardware_connection.";
        if (connectedAfter != true)
            return connectionInterface == Interfaces.Ident
                ? $"{documented} returned, but ConnectedIdentDevice of '{objectName}' reads {(connectedAfter is null ? "unreadable" : "null")}. {Changed}"
                : $"{documented} returned without an exception, but the {connectionInterface} interface of '{objectName}' reads back IsConnected="
                    + $"{(connectedAfter is null ? "(unreadable)" : "false")}. {Changed}";
        var mismatches = observations.Where(o => o.Match == false).ToList();
        if (mismatches.Count > 0)
            return $"{documented} returned, but the {connectionInterface} interface of '{objectName}' reads back "
                + string.Join("; ", mismatches.Select(o => $"{o.Shown} instead of {o.Expectation.Requested}")) + $". {Changed}";
        var unreadable = observations.Where(o => o.Match is null).Select(o => o.Expectation.Property).ToList();
        if (unreadable.Count == 0) return null;
        if (connectionInterface != Interfaces.Ident && connectedBefore == false)
        {
            warnings.Add($"The {connectionInterface} interface of '{objectName}' changed from IsConnected=false to true, but {string.Join(", ", unreadable)} "
                + "could not be read back, so the connected target itself is not verified. Inspect it with get_technology_object_hardware_connection.");
            return null;
        }

        return $"{documented} returned, but {string.Join(", ", unreadable)} of the {connectionInterface} interface of '{objectName}' could not be read back, "
            + (connectionInterface == Interfaces.Ident
                ? "and ident has no other evidence of the connected device."
                : $"and the interface was {(connectedBefore is null ? "of unknown state" : "already connected")} before, so IsConnected=true does not show that the requested connection was made.")
            + " " + Changed;
    }

    private static bool SamePath(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
        => left is not null && right is not null && left.Count == right.Count && left.Zip(right, (a, b) => string.Equals(a, b, StringComparison.Ordinal)).All(same => same);

    private static string Join(IReadOnlyList<string>? path) => path is null ? "(none)" : string.Join("/", path);
}
