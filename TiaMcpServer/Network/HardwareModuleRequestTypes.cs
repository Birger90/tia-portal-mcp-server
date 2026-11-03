using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Network;

/// <summary>One typed attribute assignment of <c>set_hardware_attributes</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HardwareAttributeAssignment
{
    [Description("Exact, case-sensitive attribute name as inspect_network_object reports it.")]
    public string? Name { get; set; }

    [Description("Value kind: string, boolean, integer, number or enum.")]
    public string? Kind { get; set; }

    [Description("The value: a JSON string (string, or the exact enum member name for enum), true/false (boolean), an integer (integer) or a finite number (number).")]
    public JsonElement? Value { get; set; }
}

/// <summary>Selects one existing Address of a device item by the evidence list_io_addresses reports.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HardwareAddressSelector
{
    [Description("AddressIoType member name: Input, Output, Diagnosis, Substitute or None.")]
    public string? IoType { get; set; }

    [Description("Current start address in bytes; required and nonnegative.")]
    public int? StartAddress { get; set; }

    [Description("Optional current length in bytes (1 or greater) as an extra constraint.")]
    public int? Length { get; set; }
}

/// <summary>The transfer area of create/update/delete_transfer_area: which composition, which existing area or what to create.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class NetworkTransferAreaRequest
{
    [Description("Composition: transferAreas (NetworkInterface.TransferAreas, I-slave/I-device address mapping) or multicastable (NetworkInterface.MulticastableTransferAreas, e.g. DDX). Required.")]
    public string? Collection { get; set; }

    [Description("update/delete: zero-based index of the area in the composition, as list_transfer_areas reports it. Required there; not valid for create.")]
    public int? Index { get; set; }

    [Description("update/delete: the area's current name as evidence (required). create: the new area's name; required for transferAreas, optional for multicastable (not with templateIndex).")]
    public string? Name { get; set; }

    [Description("create: the TransferAreaType member name (e.g. DDX, CD, F_CD, MSI, MSO; the documented V21 members). Required for create; not valid for update/delete.")]
    public string? Type { get; set; }

    [Description("create in transferAreas only: slot number passed to TransferAreaComposition.Create(String, TransferAreaType, Int32); nonnegative.")]
    public int? PositionNumber { get; set; }

    [Description("create in multicastable only: data length passed to Create(NetworkInterface, TransferAreaType, String, Int32); requires name; 1 or greater.")]
    public int? DataLength { get; set; }

    [Description("create in multicastable only: index of an existing multicastable transfer area on the partner interface, passed to Create(MulticastableTransferArea, TransferAreaType); not with name or dataLength.")]
    public int? TemplateIndex { get; set; }
}

/// <summary>What set_io_address changes; every member is optional, at least one is required.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HardwareAddressChanges
{
    [Description("New start address in bytes (Address.StartAddress); nonnegative. TIA Portal may change the coupled output range and never rewires tags.")]
    public int? StartAddress { get; set; }

    [Description("New process image partition number (dynamic attribute ProcessImage); nonnegative.")]
    public int? ProcessImage { get; set; }

    [Description("Isochronous mode of this address (dynamic attribute IsochronousMode).")]
    public bool? IsochronousMode { get; set; }

    [Description("Interrupt OB number (dynamic attribute InterruptObNumber; documented for S7-300/400 only); nonnegative.")]
    public long? InterruptObNumber { get; set; }
}
