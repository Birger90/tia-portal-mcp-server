using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The technology object fields of a PLC operation; <see cref="PlcTechnologyObjectCatalog"/> restricts them per operation.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("create_technology_object: the technology object type passed to TechnologicalInstanceDBComposition.Create(name, type, version), e.g. TO_PositioningAxis, TO_SpeedAxis. Required.")]
    public string? TechnologyType { get; set; }

    [Description("create_technology_object: the type version, two to four numeric components, e.g. 6.0. The accepted version depends on the CPU firmware and the installation: live on TIA Portal V21 (2026-11-02, CPU 1516-3 PN/DP FW V2.9) TO_PositioningAxis 10.0 was rejected (\"The type or version of the library element 'TO_PositioningAxis.10.0' does not exist\") and 6.0 was created; the Siemens skill reports 10.0 on its V21 setup. No version is guessed or probed. Required.")]
    public string? Version { get; set; }

    [Description("Full dot-notation technology object parameter name as TechnologicalParameterComposition.Find takes it, e.g. Actor.InverseDirection or PositionLimits_HW.Active. Required by set_technology_object_parameter; optional for get_technology_object_parameters (an exact read, exclusive with prefix, offset and limit).")]
    public string? ParameterName { get; set; }

    [Description("get_technology_object_parameters: ordinal (case-sensitive) parameter name prefix, e.g. Dynamics. or PositionLimits_SW.; omitted lists every parameter.")]
    public string? Prefix { get; set; }

    // Offset and Limit are shared with find_objects and declared once, with both descriptions, in
    // PlcOperationRequest.SourcesFolders.cs; FilePath and Overwrite are shared with the alarm and the external-source and
    // file-export operations and declared once, with every description, in PlcOperationRequest.cs.

    [Description("With tableName, a PLC tag of the same PLC: the PlcTag value of set_technology_object_parameter (for a parameter that holds a PlcTag or no value) or the target of connect_technology_object_hardware connectionKind tag (Connect(PlcTag)).")]
    public string? TagName { get; set; }

    [Description("connect/disconnect_technology_object_hardware (required) and the interface named in get_technology_object_hardware_connection: actor, sensor or torque (AxisHardwareConnectionProvider; sensor is also EncoderHardwareConnectionProvider.SensorInterface), measuringInput (MeasuringInputHardwareConnectionProvider), outputCam (OutputCamHardwareConnectionProvider) or ident (IdentTechnologicalObjectProvider; connect only, no Disconnect is documented). Only the providers the interface needs are resolved (sensor: the axis provider first, the encoder provider only when the axis offers no sensor); a listed provider GetService does not return fails the item only when the interface needs it.")]
    public string? ConnectionInterface { get; set; }

    [Description("connect/disconnect_technology_object_hardware with connectionInterface sensor: the zero-based position in the SensorInterface composition, as get_technology_object_hardware_connection reports it in index; required when the object has several sensor interfaces.")]
    public int? InterfaceIndex { get; set; }

    [Description("connect_technology_object_hardware (required): the documented Connect overload. address: Connect(Int32 inputAddress, Int32 outputAddress, ConnectOption) for actor, sensor, torque; Connect(Int32 inputAddress) for measuringInput; Connect(Int32 outputAddress) for outputCam. module: Connect(DeviceItem modulePath) for actor, sensor, torque, ident; Connect(DeviceItem modulePath, Int32 channelIndex) for measuringInput. modules: Connect(DeviceItem modulePath, DeviceItem outputModulePath[, ConnectOption]) for actor, sensor, torque. channel: Connect(Channel) for actor, sensor, measuringInput, outputCam (modulePath plus channelNumber). tag: Connect(PlcTag) for actor, sensor (analog), outputCam (tableName plus tagName). dbMember: Connect(String dbMemberPath) for actor, sensor, torque.")]
    public string? ConnectionKind { get; set; }

    [Description("connect_technology_object_hardware connectionKind address: the raw input bit address (byte address x 8, as the Siemens example derives it from a telegram's StartAddress), 0 or more.")]
    public int? InputAddress { get; set; }

    [Description("connect_technology_object_hardware connectionKind address: the raw output bit address (byte address x 8), 0 or more.")]
    public int? OutputAddress { get; set; }

    [Description("connect_technology_object_hardware connectionKind address (default Default) or modules (optional; selects the three-argument overload): the exact ConnectOption member name, parsed on the installed enum (the sources document only Default).")]
    public string? ConnectOption { get; set; }

    [Description("connect_technology_object_hardware connectionKind module, modules (the input module) and channel: the device item as its exact names from the device down, e.g. [\"Drive_1\", \"Drive_1\", \"Module_1\"]; each level must match exactly one item. get_technology_object_hardware_connection reports connected modules in this form (modulePath).")]
    public IReadOnlyList<string>? ModulePath { get; set; }

    [Description("connect_technology_object_hardware connectionKind modules: the output module, written like modulePath.")]
    public IReadOnlyList<string>? OutputModulePath { get; set; }

    [Description("connect_technology_object_hardware connectionKind channel: Channel.Number within the modulePath item's Channels, 0 or more.")]
    public int? ChannelNumber { get; set; }

    [Description("connect_technology_object_hardware connectionKind channel: optional exact Channel.IoType member name to disambiguate channels with the same number.")]
    public string? ChannelIoType { get; set; }

    [Description("connect_technology_object_hardware connectionKind channel: optional exact Channel.Type member name to disambiguate channels with the same number.")]
    public string? ChannelType { get; set; }

    [Description("connect_technology_object_hardware connectionInterface measuringInput with connectionKind module: the channel index of Connect(DeviceItem, Int32), 0 or more.")]
    public int? ChannelIndex { get; set; }

    [Description("connect_technology_object_hardware connectionKind dbMember: the DB member path passed to Connect(String).")]
    public string? DbMemberPath { get; set; }
}
