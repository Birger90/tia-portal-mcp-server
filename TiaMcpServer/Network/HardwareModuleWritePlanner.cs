using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Network;

/// <summary>
/// Hardware-module evidence carried on a <see cref="NetworkWriteEffect"/>: the resolved target and
/// container, what a delete removes, tags a start-address change leaves unrewired, and whether each
/// requested attribute value converts. Collections are always present.
/// </summary>
public sealed record HardwareModuleEffect(
    HardwareObjectIdentityInfo? Target,
    HardwareObjectIdentityInfo? Container,
    IReadOnlyList<HardwareObjectIdentityInfo> RemovedItems,
    IReadOnlyList<IoTagMatchInfo> AffectedTags,
    IReadOnlyList<HardwareAttributeConversionInfo> Conversions,
    bool? CanPlug);

/// <summary>
/// Plans hardware-module items for network_write from the call's one ordinary hardware snapshot plus
/// per-item planning reads: CanPlugNew/CanPlugMove/CanPlugCopy, the current attribute values with
/// conversion verdicts, and the current addresses with their tag matches. A refused feasibility check,
/// an unknown or non-writable attribute or a value that does not convert fails the plan as
/// <c>validation_error</c> before any mutation, in every mode and in dry runs.
/// </summary>
public static class HardwareModuleWritePlanner
{
    public static bool Handles(string? operation) => NetworkOperationCatalog.IsHardwareModuleOperation(operation);

    public static async Task<ItemReplan<NetworkWriteEffect>> ResolveAsync(OpennessWorkerClient client, NetworkOperationRequest item,
        HardwareConfigInfo state)
    {
        if (item.Operation == HardwareModuleMethods.ExportIoMap) return ResolveExport(item);
        if (!NetworkWritePlanner.DiscoveryComplete(state))
            return Fail(WorkerFailureCategories.WorkerOperationFailed,
                "Network target discovery is incomplete. Inspect the hardware configuration before retrying.");
        if (item.Operation == HardwareModuleMethods.DeleteIoSystem) return ResolveIoSystemDelete(item, state);
        if (item.Operation is HardwareModuleMethods.ExportCax or HardwareModuleMethods.ImportCax) return ResolveCax(item, state);
        var target = SnapshotResolve(state, item.Target!, "target");
        if (target.Error is { } targetError) return ItemReplan<NetworkWriteEffect>.Fail(targetError.Category, $"Operation '{item.OperationId}': {targetError.Message}");
        var requested = new Dictionary<string, string>(StringComparer.Ordinal);
        var current = new Dictionary<string, string?>(StringComparer.Ordinal);
        HardwareObjectIdentityInfo? container = null;
        var removed = new List<HardwareObjectIdentityInfo>();
        var affectedNodes = new List<NetworkNodeIdentityInfo>();
        var tags = new List<IoTagMatchInfo>();
        var conversions = new List<HardwareAttributeConversionInfo>();
        bool? canPlug = null;
        bool? connectionsComplete = null;

        switch (item.Operation)
        {
            case HardwareModuleMethods.PlugDeviceItem:
            case HardwareModuleMethods.MoveDeviceItem:
            case HardwareModuleMethods.CopyDeviceItem:
            {
                if (item.Operation == HardwareModuleMethods.PlugDeviceItem)
                {
                    container = target.Identity;
                    requested["typeIdentifier"] = item.TypeIdentifier!;
                    if (item.DeviceItemName is not null) requested["deviceItemName"] = item.DeviceItemName;
                }
                else
                {
                    var destination = item.Destination is { } explicitDestination
                        ? SnapshotResolve(state, explicitDestination, "destination")
                        : SnapshotResolve(state, ParentTarget(item.Target!), "destination");
                    if (destination.Error is { } destinationError)
                        return ItemReplan<NetworkWriteEffect>.Fail(destinationError.Category, $"Operation '{item.OperationId}': {destinationError.Message}");
                    container = destination.Identity;
                    current["positionNumber"] = Number(target.Identity!.PositionNumber);
                    requested["destination"] = Describe(container!);
                }

                requested["positionNumber"] = item.PositionNumber!.Value.ToString(CultureInfo.InvariantCulture);
                var probe = await DecodeAsync<HardwarePlugProbeInfo>(HardwareModuleInvoker.ProbePlugAsync(client, item), item, "the plug feasibility check")
                    .ConfigureAwait(false);
                if (probe.Error is { } probeError) return ItemReplan<NetworkWriteEffect>.Fail(probeError.Category, probeError.Message);
                canPlug = probe.Value!.CanPlug;
                current["canPlug"] = canPlug.Value ? "true" : "false";
                if (!canPlug.Value)
                    return Fail(WorkerFailureCategories.ValidationError,
                        $"Operation '{item.OperationId}': TIA Portal reports that this {item.Operation.Split('_')[0]} to position {item.PositionNumber} of "
                        + $"{Describe(container!)} is not possible ({FeasibilityMember(item.Operation)} returned false). Nothing was changed. "
                        + (item.Operation == HardwareModuleMethods.PlugDeviceItem && target.Item is null
                            ? RackHint(target.Device!)
                            : "List free positions with network_read list_plug_locations and check the type identifier."));
                break;
            }
            case HardwareModuleMethods.DeleteDeviceItem:
            {
                connectionsComplete = true;
                foreach (var (identity, node) in Subtree(target))
                {
                    if (identity is not null) removed.Add(identity);
                    if (node is null) continue;
                    if (node.ConnectionEvidence is not { Complete: true }) connectionsComplete = false;
                    if (node.ConnectionEvidence?.SubnetId is not null)
                        affectedNodes.Add(new NetworkNodeIdentityInfo { DeviceName = target.Device!.Name ?? string.Empty, NodeId = node.NodeId });
                }

                current["removedItemCount"] = removed.Count.ToString(CultureInfo.InvariantCulture);
                break;
            }
            case HardwareModuleMethods.ChangeDeviceItemType:
                requested["typeIdentifier"] = item.TypeIdentifier!;
                current["typeIdentifier"] = target.Identity!.TypeIdentifier;
                break;
            case HardwareModuleMethods.SetHardwareAttributes:
            {
                var read = await DecodeAsync<HardwareAttributeReadInfo>(HardwareModuleInvoker.ReadAttributesAsync(client, item), item, "the current attribute values")
                    .ConfigureAwait(false);
                if (read.Error is { } readError) return ItemReplan<NetworkWriteEffect>.Fail(readError.Category, readError.Message);
                conversions.AddRange(read.Value!.Conversions);
                var refused = conversions.Where(verdict => !verdict.Success).ToArray();
                if (refused.Length > 0)
                    return Fail(WorkerFailureCategories.ValidationError,
                        $"Operation '{item.OperationId}': {string.Join(" ", refused.Select(verdict => verdict.Error))} Nothing was changed.");
                foreach (var attribute in item.Attributes!)
                {
                    requested[attribute.Name!] = ValueText(attribute.Value!.Value);
                    var observed = read.Value.Attributes.FirstOrDefault(entry => entry.Name == attribute.Name);
                    current[attribute.Name!] = observed?.Value is { } value ? ValueText(value) : null;
                }

                if (item.AttributeErrorPolicy is { } policy) requested["attributeErrorPolicy"] = policy;
                if (item.AttributeWriteMode is { } mode) requested["attributeWriteMode"] = mode;
                break;
            }
            case HardwareModuleMethods.SetCustomIdentity:
            case HardwareModuleMethods.RemoveCustomIdentity:
            {
                // The provider must be listed on the target, else the planning read fails unsupported_capability before any change.
                var read = await DecodeAsync<HardwareCustomIdentityReadInfo>(HardwareModuleInvoker.ReadCustomIdentityAsync(client, item), item, "the current custom identity")
                    .ConfigureAwait(false);
                if (read.Error is { } readError) return ItemReplan<NetworkWriteEffect>.Fail(readError.Category, readError.Message);
                requested["key"] = item.Key!;
                current["state"] = read.Value!.Observed.State;
                current["value"] = read.Value.Observed.Value;
                if (item.Operation == HardwareModuleMethods.SetCustomIdentity) requested["value"] = item.Value!;
                else requested["removed"] = "true";
                break;
            }
            case HardwareModuleMethods.ConnectPorts:
            case HardwareModuleMethods.DisconnectPorts:
            {
                var partner = SnapshotResolve(state, item.Partner!, "partner");
                if (partner.Error is { } partnerError)
                    return ItemReplan<NetworkWriteEffect>.Fail(partnerError.Category, $"Operation '{item.OperationId}': {partnerError.Message}");
                container = partner.Identity;
                requested["partner"] = Describe(partner.Identity!);
                requested["connected"] = item.Operation == HardwareModuleMethods.ConnectPorts ? "true" : "false";
                // The target must offer NetworkPort, else the planning read finds no port and the item fails before any change.
                var list = await DecodeAsync<HardwarePortListInfo>(HardwareModuleInvoker.ListPortsAsync(client, item), item, "the current port links")
                    .ConfigureAwait(false);
                if (list.Error is { } listError) return ItemReplan<NetworkWriteEffect>.Fail(listError.Category, listError.Message);
                var own = list.Value!.Ports.FirstOrDefault(port => SameItem(port.Port, target.Identity!));
                if (own is null)
                    return Fail(WorkerFailureCategories.UnsupportedCapability,
                        $"Operation '{item.OperationId}': {Describe(target.Identity!)} lists no {NetworkPortNames.Service} among its services; select a port device item "
                        + "(network_read list_port_connections). Nothing was changed.");
                current["connected"] = own.ConnectedPortsState != NetworkPortNames.ReadStates.Read ? null
                    : own.ConnectedPorts.Any(link => link.Port is { } linked && SameItem(linked, partner.Identity!)) ? "true" : "false";
                current["linkedPorts"] = own.ConnectedPortsState != NetworkPortNames.ReadStates.Read ? null
                    : string.Join("; ", own.ConnectedPorts.Select(link => link.Port is { } linked ? Describe(linked) : $"(unresolved {link.RuntimeType})"));
                break;
            }
            case HardwareModuleMethods.CreateTransferArea:
            case HardwareModuleMethods.UpdateTransferArea:
            case HardwareModuleMethods.DeleteTransferArea:
            {
                var area = item.TransferArea!;
                var read = await DecodeAsync<HardwareTransferAreaListInfo>(HardwareModuleInvoker.ListTransferAreasAsync(client, item.Target!), item, "the current transfer areas")
                    .ConfigureAwait(false);
                if (read.Error is { } readError) return ItemReplan<NetworkWriteEffect>.Fail(readError.Category, readError.Message);
                var areas = area.Collection == NetworkSystemNames.TransferAreaCollections.Multicastable ? read.Value!.MulticastableTransferAreas : read.Value!.TransferAreas;
                if (areas is null)
                    return Fail(WorkerFailureCategories.UnsupportedCapability,
                        $"Operation '{item.OperationId}': the interface of {Describe(target.Identity!)} offers no {area.Collection} composition on this installation"
                        + (read.Value.Messages.Count > 0 ? $" ({string.Join("; ", read.Value.Messages)})" : string.Empty) + ". Nothing was changed.");
                requested["collection"] = area.Collection!;
                current["count"] = areas.Count.ToString(CultureInfo.InvariantCulture);
                if (item.Operation == HardwareModuleMethods.CreateTransferArea)
                {
                    requested["type"] = area.Type!;
                    if (area.Name is not null) requested["name"] = area.Name;
                    if (area.PositionNumber is { } slot) requested["positionNumber"] = slot.ToString(CultureInfo.InvariantCulture);
                    if (area.DataLength is { } length) requested["dataLength"] = length.ToString(CultureInfo.InvariantCulture);
                    if (item.Partner is { } partnerTarget)
                    {
                        var partner = SnapshotResolve(state, partnerTarget, "partner");
                        if (partner.Error is { } partnerError)
                            return ItemReplan<NetworkWriteEffect>.Fail(partnerError.Category, $"Operation '{item.OperationId}': {partnerError.Message}");
                        container = partner.Identity;
                        requested["partner"] = Describe(partner.Identity!);
                        if (area.TemplateIndex is { } templateIndex)
                        {
                            var partnerRead = await DecodeAsync<HardwareTransferAreaListInfo>(HardwareModuleInvoker.ListTransferAreasAsync(client, partnerTarget), item,
                                "the partner's transfer areas").ConfigureAwait(false);
                            if (partnerRead.Error is { } partnerReadError) return ItemReplan<NetworkWriteEffect>.Fail(partnerReadError.Category, partnerReadError.Message);
                            var templates = partnerRead.Value!.MulticastableTransferAreas ?? new List<HardwareTransferAreaInfo>();
                            if (templateIndex >= templates.Count)
                                return Fail(WorkerFailureCategories.TargetNotFound,
                                    $"Operation '{item.OperationId}': the partner interface has no multicastable transfer area at index {templateIndex} ({templates.Count} listed). Nothing was changed.");
                            requested["templateIndex"] = templateIndex.ToString(CultureInfo.InvariantCulture);
                            current["templateName"] = templates[templateIndex].Name;
                        }
                    }
                }
                else
                {
                    var index = area.Index!.Value;
                    if (index >= areas.Count)
                        return Fail(WorkerFailureCategories.TargetNotFound,
                            $"Operation '{item.OperationId}': the interface has no area at index {index} in {area.Collection} ({areas.Count} listed). Read them with network_read list_transfer_areas.");
                    var selected = areas[index];
                    if (!string.Equals(selected.Name, area.Name, StringComparison.Ordinal))
                        return Fail(WorkerFailureCategories.TargetEvidenceMismatch,
                            $"Operation '{item.OperationId}': the area at index {index} in {area.Collection} is named '{selected.Name ?? "(unreadable)"}', not '{area.Name}'. Re-read with network_read list_transfer_areas.");
                    requested["index"] = index.ToString(CultureInfo.InvariantCulture);
                    requested["name"] = area.Name!;
                    foreach (var property in selected.Properties) current[property.Name] = property.Value.Value;
                    if (item.Operation == HardwareModuleMethods.UpdateTransferArea)
                        foreach (var attribute in item.Attributes!)
                        {
                            var listed = selected.Attributes.FirstOrDefault(entry => entry.Name == attribute.Name);
                            if (listed is null)
                                return Fail(WorkerFailureCategories.ValidationError,
                                    $"Operation '{item.OperationId}': the area lists no attribute '{attribute.Name}' (GetAttributeInfos, case-sensitive); listed: "
                                    + (selected.Attributes.Count == 0 ? "(none)" : string.Join(", ", selected.Attributes.Select(entry => entry.Name))) + ". Nothing was changed.");
                            requested["attribute:" + attribute.Name] = ValueText(attribute.Value!.Value);
                        }
                    else requested["deleted"] = "true";
                }

                break;
            }
            case HardwareModuleMethods.DisconnectFromIoSystem:
            {
                var systems = state.Subnets.SelectMany(subnet => subnet.IoSystems.Select(system => (Subnet: subnet, System: system)))
                    .Where(entry => entry.System.ConnectedDeviceNames.Contains(target.Identity!.DeviceName, StringComparer.OrdinalIgnoreCase)).ToArray();
                current["ioSystems"] = string.Join("; ", systems.Select(entry => $"{entry.System.Name} ({entry.Subnet.SubnetId}/{Number(entry.System.Number) ?? "?"})"));
                requested["disconnected"] = "true";
                break;
            }
            case HardwareModuleMethods.SetIoAddress:
            {
                var list = await DecodeAsync<HardwareIoAddressListInfo>(HardwareModuleInvoker.ListAddressesAsync(client, item), item, "the current addresses")
                    .ConfigureAwait(false);
                if (list.Error is { } listError) return ItemReplan<NetworkWriteEffect>.Fail(listError.Category, listError.Message);
                var selector = item.Address!;
                var matches = list.Value!.Addresses.Where(address => address.IoType == selector.IoType && address.StartAddress == selector.StartAddress
                    && (selector.Length is null || address.Length == selector.Length)).ToArray();
                // The listing holds the item's own addresses or, when it has none, its sub-items' addresses (K06).
                if (matches.Length == 0)
                    return Fail(WorkerFailureCategories.TargetNotFound,
                        $"Operation '{item.OperationId}': no {selector.IoType} address starts at {selector.StartAddress} on {Describe(target.Identity!)}"
                        + (selector.Length is { } wanted ? $" with length {wanted}" : string.Empty)
                        + ". Read the current addresses with network_read list_io_addresses.");
                if (matches.Length > 1)
                    return Fail(WorkerFailureCategories.ValidationError,
                        $"Operation '{item.OperationId}': {matches.Length} {selector.IoType} addresses start at {selector.StartAddress} on {Describe(target.Identity!)}: "
                        + string.Join("; ", matches.Select(Candidate)) + ". Add address.length to select one. Nothing was changed.");
                var address = matches[0];
                if (address.SubItemPath.Count > 0) current["subItemPath"] = SubItemText(address.SubItemPath);
                var changes = item.AddressChanges!;
                Pair("StartAddress", Number(address.StartAddress), changes.StartAddress?.ToString(CultureInfo.InvariantCulture));
                Pair("ProcessImage", Number(address.ProcessImage), changes.ProcessImage?.ToString(CultureInfo.InvariantCulture));
                Pair("IsochronousMode", Flag(address.IsochronousMode), Flag(changes.IsochronousMode));
                Pair("InterruptObNumber", address.InterruptObNumber?.ToString(CultureInfo.InvariantCulture), changes.InterruptObNumber?.ToString(CultureInfo.InvariantCulture));
                current["Length"] = Number(address.Length);
                if (changes.StartAddress is not null && address.StartAddress is { } start && address.Length is { } length)
                {
                    var mapped = await TagsAtAsync(client, target, address.SubItemPath, address.IoType, start, length).ConfigureAwait(false);
                    if (mapped is null) current["tagMatches"] = null;
                    else
                    {
                        tags.AddRange(mapped);
                        current["tagMatches"] = mapped.Count.ToString(CultureInfo.InvariantCulture);
                    }
                }

                break;
            }
        }

        var effect = new NetworkWriteEffect(item.Operation,
            new NetworkWriteTargetEvidence(item.OperationId, item.Operation, target.Identity!.DeviceName, target.Identity.TypeIdentifier,
                target.Identity.ItemPath.Select(segment => segment.Name).ToArray(), null, null, null, null, null, null, null),
            requested, current, affectedNodes, connectionsComplete,
            new HardwareModuleEffect(target.Identity, container, removed, tags, conversions, canPlug));
        return ItemReplan<NetworkWriteEffect>.Ok(ItemPlan<NetworkWriteEffect>.Resolved(effect, new[]
        {
            new CheckedPrecondition("networkDiscoveryComplete", "true", "true", true),
        }));

        void Pair(string key, string? before, string? after)
        {
            if (after is null) return;
            requested[key] = after;
            current[key] = before;
        }
    }

    /// <summary>The info guards a planned hardware item fires; block guards are not used (no hard blocks beyond invalid input).</summary>
    public static IEnumerable<FiredGuard> Guards(NetworkOperationRequest item, NetworkWriteEffect? effect)
    {
        var hardware = effect?.Hardware;
        switch (item.Operation)
        {
            case HardwareModuleMethods.DeleteDeviceItem when hardware is not null:
                yield return new FiredGuard(NetworkGuardDefinitions.DeletesDeviceItem, item.OperationId,
                    $"Deleting {Describe(hardware.Target!)} removes it and {Math.Max(0, hardware.RemovedItems.Count - 1)} sub-item(s); "
                    + $"{effect!.AffectedNodes.Count} connected node(s) lose their subnet connection"
                    + (effect.ConnectionsComplete == true ? "." : "; the connection evidence of some nodes is incomplete.")
                    + " Tags and program references to its addresses are not changed.");
                break;
            case HardwareModuleMethods.ChangeDeviceItemType:
                yield return new FiredGuard(NetworkGuardDefinitions.ChangesDeviceItemType, item.OperationId,
                    "ChangeType reports no details of what it changed; parameters, addresses and sub-items of the item may change. "
                    + "Re-read the item with network_read afterwards.");
                break;
            case HardwareModuleMethods.SetIoAddress when item.AddressChanges?.StartAddress is not null:
                yield return new FiredGuard(NetworkGuardDefinitions.AddressTagsNotRewired, item.OperationId,
                    "Changing StartAddress does not rewire assigned PLC tags and may also change the coupled input/output range of the module."
                    + (hardware?.AffectedTags.Count > 0
                        ? $" {hardware.AffectedTags.Count} tag(s) use the current range: {string.Join(", ", hardware.AffectedTags.Take(20).Select(tag => $"'{tag.Name}' ({tag.LogicalAddress})"))}."
                        : effect?.CurrentSettings.TryGetValue("tagMatches", out var known) == true && known is null
                            ? " The tags at the current range could not be determined."
                            : " No matched tag uses the current range."));
                break;
            case HardwareModuleMethods.SetHardwareAttributes when item.AttributeErrorPolicy == HardwareModuleMethods.AttributeErrorPolicies.Ignore:
                yield return new FiredGuard(NetworkGuardDefinitions.AttributeErrorsIgnored, item.OperationId,
                    "attributeErrorPolicy 'ignore' lets TIA Portal skip a rejected attribute and apply the others (partial update); the result reports each attribute's status.");
                break;
            case HardwareModuleMethods.ConnectPorts when effect?.CurrentSettings.GetValueOrDefault("connected") == "true":
                yield return new FiredGuard(NetworkGuardDefinitions.PortLinkExists, item.OperationId,
                    $"{Describe(hardware!.Target!)} is already linked to {Describe(hardware.Container!)}; the Siemens skill reports that ConnectToPort throws for an existing link. "
                    + "The item runs and TIA Portal decides.");
                break;
            case HardwareModuleMethods.DisconnectPorts when effect?.CurrentSettings.GetValueOrDefault("connected") != "true":
                yield return new FiredGuard(NetworkGuardDefinitions.PortLinkMissing, item.OperationId,
                    effect?.CurrentSettings.GetValueOrDefault("connected") == "false"
                        ? $"{Describe(hardware!.Target!)} is not linked to {Describe(hardware.Container!)} in the planning read; the item runs and TIA Portal decides."
                        : "The port links could not be read during planning (NetworkPort.ConnectedPorts); the item runs and TIA Portal decides.");
                break;
            case HardwareModuleMethods.DeleteTransferArea when hardware is not null:
                yield return new FiredGuard(NetworkGuardDefinitions.DeletesTransferArea, item.OperationId,
                    $"Deleting transfer area '{item.TransferArea!.Name}' ({item.TransferArea.Collection} index {item.TransferArea.Index}) of {Describe(hardware.Target!)} removes its address mapping; "
                    + "partner areas and PLC tags at its addresses are not changed by the server.");
                break;
            case HardwareModuleMethods.DeleteIoSystem:
                yield return new FiredGuard(NetworkGuardDefinitions.DeletesIoSystem, item.OperationId,
                    $"Deleting IO system {item.Target!.Number} on subnet '{item.Target.SubnetId}' disconnects its IO devices"
                    + (effect?.CurrentSettings.GetValueOrDefault("connectedDevices") is { Length: > 0 } devices ? $" ({devices})" : string.Empty)
                    + "; the devices, their nodes and the subnet remain.");
                break;
            case HardwareModuleMethods.DisconnectFromIoSystem:
                yield return new FiredGuard(NetworkGuardDefinitions.DisconnectsIoConnector, item.OperationId,
                    $"{Describe(hardware!.Target!)} leaves its IO system"
                    + (effect?.CurrentSettings.GetValueOrDefault("ioSystems") is { Length: > 0 } systems ? $" (planning snapshot: {systems})" : string.Empty)
                    + "; the device and its subnet connection remain.");
                break;
            case HardwareModuleMethods.ExportCax when effect?.CurrentSettings.GetValueOrDefault("fileExists") == "true":
                yield return new FiredGuard(NetworkGuardDefinitions.CaxExportOverwritesFile, item.OperationId,
                    $"export_cax lets TIA Portal write over the existing file '{item.FilePath}' (overwrite:true); the server checks afterwards that it was rewritten.");
                break;
            case HardwareModuleMethods.ImportCax:
                yield return new FiredGuard(NetworkGuardDefinitions.CaxImportChangesHardware, item.OperationId,
                    $"import_cax adds or changes hardware from '{item.FilePath}' (importOption {item.ImportOption}) with no rollback; "
                    + $"the project currently has {effect?.CurrentSettings.GetValueOrDefault("deviceCount") ?? "?"} device(s). Use a test project or save before.");
                break;
            case HardwareModuleMethods.ExportIoMap when effect?.CurrentSettings.GetValueOrDefault("fileExists") == "true":
                yield return new FiredGuard(NetworkGuardDefinitions.ExportOverwritesFile, item.OperationId,
                    $"export_io_map replaces the existing file '{item.FilePath}' (overwrite:true).");
                break;
        }
    }

    /// <summary>Superseding keys: a device item owns its path key; a container prefix covers its sub-items.</summary>
    public static IReadOnlyList<string> ObjectKeys(NetworkOperationRequest item, NetworkWriteEffect effect)
    {
        var hardware = effect.Hardware;
        string Key(HardwareObjectIdentityInfo? identity)
            => identity is null ? "unknown" : $"device\n{identity.DeviceName.ToUpperInvariant()}"
                + string.Concat(identity.ItemPath.Select(segment => $"\nitem\n{segment.PositionNumber}:{segment.Name}"));
        string Slot(HardwareObjectIdentityInfo? identity) => $"{Key(identity)}\nslot\n{item.PositionNumber}";
        return item.Operation switch
        {
            HardwareModuleMethods.PlugDeviceItem => new[] { Slot(hardware?.Container) },
            HardwareModuleMethods.MoveDeviceItem => new[] { Key(hardware?.Target), Slot(hardware?.Container) },
            HardwareModuleMethods.CopyDeviceItem => new[] { Slot(hardware?.Container) },
            HardwareModuleMethods.DeleteDeviceItem or HardwareModuleMethods.ChangeDeviceItemType => new[] { Key(hardware?.Target) },
            HardwareModuleMethods.SetHardwareAttributes => new[] { $"{Key(hardware?.Target)}\nattributes" },
            HardwareModuleMethods.SetIoAddress => new[] { $"{Key(hardware?.Target)}\naddress\n{item.Address?.IoType}:{item.Address?.StartAddress}" },
            HardwareModuleMethods.SetCustomIdentity or HardwareModuleMethods.RemoveCustomIdentity => new[] { $"{Key(hardware?.Target)}\ncustomIdentity\n{item.Key}" },
            // A link belongs to both ports: either direction of a later item on the same pair supersedes.
            HardwareModuleMethods.ConnectPorts or HardwareModuleMethods.DisconnectPorts => new[] { PortLink(Key(hardware?.Target), Key(hardware?.Container)) },
            HardwareModuleMethods.CreateTransferArea => Array.Empty<string>(),
            // Indexes shift after a delete: any later item on the same composition supersedes.
            HardwareModuleMethods.UpdateTransferArea or HardwareModuleMethods.DeleteTransferArea => new[] { $"{Key(hardware?.Target)}\ntransferAreas\n{item.TransferArea?.Collection}" },
            HardwareModuleMethods.DeleteIoSystem => new[] { $"subnet\n{item.Target?.SubnetId}\nioSystem\n{item.Target?.Number}" },
            HardwareModuleMethods.DisconnectFromIoSystem => new[] { $"{Key(hardware?.Target)}\nioConnector" },
            HardwareModuleMethods.ExportCax => new[] { $"file\n{item.FilePath!.ToUpperInvariant()}" },
            // An import can change any device: it supersedes every earlier hardware item of the call conservatively.
            HardwareModuleMethods.ImportCax => new[] { "device" },
            HardwareModuleMethods.ExportIoMap => new[] { $"file\n{item.FilePath!.ToUpperInvariant()}" },
            _ => Array.Empty<string>(),
        };
    }

    private static string PortLink(string left, string right)
        => string.CompareOrdinal(left, right) <= 0 ? $"portLink\n{left}\n{right}" : $"portLink\n{right}\n{left}";

    private static bool SameItem(HardwareObjectIdentityInfo left, HardwareObjectIdentityInfo right)
        => string.Equals(left.DeviceName, right.DeviceName, StringComparison.OrdinalIgnoreCase) && left.ItemPath.Count == right.ItemPath.Count
            && left.ItemPath.Zip(right.ItemPath).All(pair => pair.First.Index == pair.Second.Index && pair.First.Name == pair.Second.Name
                && pair.First.PositionNumber == pair.Second.PositionNumber && pair.First.TypeIdentifier == pair.Second.TypeIdentifier);

    // ------------------------------------------------------------------
    // Snapshot resolution
    // ------------------------------------------------------------------

    private sealed record SnapshotTarget(DeviceInfo? Device, DeviceItemInfo? Item, HardwareObjectIdentityInfo? Identity, WriteToolError? Error);

    private static SnapshotTarget SnapshotResolve(HardwareConfigInfo state, NetworkObjectTarget target, string role)
    {
        var devices = state.Devices.Where(device => string.Equals(device.Name, target.DeviceName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (state.Devices.Any(device => string.IsNullOrWhiteSpace(device.Name)))
            return Error(WorkerFailureCategories.WorkerOperationFailed, "The project device-name namespace has unreadable identities, so the target is not unique.");
        if (devices.Length == 0) return Error(WorkerFailureCategories.TargetNotFound, $"The {role} device '{target.DeviceName}' was not found.");
        if (devices.Length > 1) return Error(WorkerFailureCategories.TargetAmbiguous, $"Several devices are named '{target.DeviceName}'.");
        var device = devices[0];
        if (target.Kind == NetworkOperationCatalog.DeviceTargetKind || target.ItemPath is null)
            return new SnapshotTarget(device, null, new HardwareObjectIdentityInfo
            {
                DeviceName = device.Name!,
                Name = device.Name,
                TypeIdentifier = device.TypeIdentifier,
            }, null);

        var siblings = device.Items;
        DeviceItemInfo? selected = null;
        var path = new List<DeviceItemPathSegmentInfo>();
        for (var depth = 0; depth < target.ItemPath.Count; depth++)
        {
            var segment = target.ItemPath[depth];
            if (segment.Index is not { } index || index < 0 || index >= siblings.Count)
                return Error(WorkerFailureCategories.TargetNotFound, $"The {role} item path segment {depth} has no sibling at index {segment.Index}.");
            selected = siblings[index];
            if (selected.Name != segment.Name || selected.PositionNumber != segment.PositionNumber || selected.TypeIdentifier != segment.TypeIdentifier)
                return Error(WorkerFailureCategories.TargetEvidenceMismatch,
                    $"The {role} item path segment {depth} no longer matches its name, position number or type identifier. Re-read the hardware configuration.");
            path.Add(new DeviceItemPathSegmentInfo { Index = index, Name = segment.Name, PositionNumber = segment.PositionNumber!.Value, TypeIdentifier = segment.TypeIdentifier });
            siblings = selected.Items;
        }

        return new SnapshotTarget(device, selected, new HardwareObjectIdentityInfo
        {
            DeviceName = device.Name!,
            ItemPath = path,
            Name = selected!.Name,
            TypeIdentifier = selected.TypeIdentifier,
            PositionNumber = selected.PositionNumber,
        }, null);

        SnapshotTarget Error(string category, string message) => new(null, null, null, new WriteToolError(category, message));
    }

    /// <summary>Type identifiers of rack and rail items start with this prefix (S7-1500 rails may also use an order number).</summary>
    internal const string RackTypePrefix = "System:Rack";

    /// <summary>
    /// The advice after a false CanPlugNew on a device target: modules usually plug into the device's rack item (live
    /// 2026-11-02, ET 200SP: list_plug_locations on the device listed positions 1-10 free, CanPlugNew on the device was
    /// false, plugging into the rack item worked; K29). Names the rack item and its selector when exactly one top-level
    /// item has a type identifier starting <see cref="RackTypePrefix"/>. Nothing is refused that TIA Portal allows.
    /// </summary>
    internal static string RackHint(DeviceInfo device)
    {
        var racks = device.Items.Select((entry, index) => (Item: entry, Index: index))
            .Where(candidate => candidate.Item.TypeIdentifier?.StartsWith(RackTypePrefix, StringComparison.Ordinal) == true).ToArray();
        if (racks.Length == 1)
        {
            var rack = racks[0];
            return $"Modules usually plug into the rack item, not the device itself: retry with target {{ kind: 'deviceItem', deviceName: '{device.Name}', "
                + $"itemPath: [{{ index: {rack.Index}, name: '{rack.Item.Name}', positionNumber: {Number(rack.Item.PositionNumber) ?? "null"}, "
                + $"typeIdentifier: '{rack.Item.TypeIdentifier}' }}] }} and list its free positions with network_read list_plug_locations.";
        }

        return racks.Length == 0
            ? "Modules usually plug into a rack or rail item, not the device itself: read the device with network_read read_hardware_config "
                + "and use its rack or rail item as a 'deviceItem' target."
            : $"Modules usually plug into a rack item, not the device itself; device '{device.Name}' has {racks.Length} rack items "
                + $"({string.Join(", ", racks.Select(rack => $"'{rack.Item.Name}'"))}): use the intended one as a 'deviceItem' target.";
    }

    private static NetworkObjectTarget ParentTarget(NetworkObjectTarget target)
    {
        var path = target.ItemPath!;
        return path.Count <= 1
            ? new NetworkObjectTarget { Kind = NetworkOperationCatalog.DeviceTargetKind, DeviceName = target.DeviceName }
            : new NetworkObjectTarget { Kind = NetworkObjectKinds.DeviceItem, DeviceName = target.DeviceName, ItemPath = path.Take(path.Count - 1).ToArray() };
    }

    /// <summary>The target item and every descendant (identities from snapshot selectors) and every node under them.</summary>
    private static IEnumerable<(HardwareObjectIdentityInfo? Identity, NodeInfo? Node)> Subtree(SnapshotTarget target)
    {
        var stack = new Stack<(DeviceItemInfo Item, List<DeviceItemPathSegmentInfo> Path)>();
        stack.Push((target.Item!, target.Identity!.ItemPath));
        while (stack.Count > 0)
        {
            var (item, path) = stack.Pop();
            yield return (new HardwareObjectIdentityInfo
            {
                DeviceName = target.Identity.DeviceName,
                ItemPath = path,
                Name = item.Name,
                TypeIdentifier = item.TypeIdentifier,
                PositionNumber = item.PositionNumber,
            }, null);
            foreach (var node in item.NetworkInterfaces.SelectMany(networkInterface => networkInterface.Nodes)) yield return (null, node);
            for (var index = item.Items.Count - 1; index >= 0; index--)
            {
                var child = item.Items[index];
                stack.Push((child, path.Append(new DeviceItemPathSegmentInfo
                {
                    Index = index,
                    Name = child.Name ?? string.Empty,
                    PositionNumber = child.PositionNumber ?? -1,
                    TypeIdentifier = child.TypeIdentifier ?? string.Empty,
                }).ToList()));
            }
        }
    }

    /// <summary>Tags matched on channels inside one address range, from a device-scoped I/O read; null when unknown.</summary>
    private static async Task<IReadOnlyList<IoTagMatchInfo>?> TagsAtAsync(OpennessWorkerClient client, SnapshotTarget target,
        IReadOnlyList<HardwareSubItemSegmentInfo> subItemPath, string? ioType, int startByte, int length)
    {
        var area = IoLogicalAddressFormatter.NormalizeArea(ioType);
        if (area is null) return Array.Empty<IoTagMatchInfo>();
        var read = await client.ReadHardwareConfigAsync(deviceName: target.Device!.Name, includeIoDetails: true, includeTagMatches: true).ConfigureAwait(false);
        if (!read.Success) return null;
        HardwareConfigInfo config;
        try
        {
            config = NetworkPayloadContract.DecodeHardwareConfig(read.Payload);
        }
        catch (JsonException)
        {
            return null;
        }

        var device = config.Devices.SingleOrDefault(candidate => string.Equals(candidate.Name, target.Device.Name, StringComparison.OrdinalIgnoreCase));
        var siblings = device?.Items;
        DeviceItemInfo? item = null;
        // The target's own path, then the sub-item that owns the address (K06), by the same sibling indexes.
        foreach (var index in target.Identity!.ItemPath.Select(segment => segment.Index).Concat(subItemPath.Select(segment => segment.Index)))
        {
            if (siblings is null || index < 0 || index >= siblings.Count) return null;
            item = siblings[index];
            siblings = item.Items;
        }

        if (item?.IoDetails is not { } details) return null;
        var first = (long)startByte * 8;
        var end = first + (long)length * 8;
        return details.Channels
            .Where(channel => IoLogicalAddressFormatter.NormalizeArea(channel.IoType) == area && channel.ChannelAddressBits is { } bit
                && bit >= first && bit < end)
            .SelectMany(channel => channel.TagMatches)
            .ToArray();
    }

    // ------------------------------------------------------------------
    // delete_io_system planning
    // ------------------------------------------------------------------

    /// <summary>The IO system from the planning snapshot's subnets, by exact subnetId and number (name and index as evidence).</summary>
    private static ItemReplan<NetworkWriteEffect> ResolveIoSystemDelete(NetworkOperationRequest item, HardwareConfigInfo state)
    {
        var target = item.Target!;
        var subnets = state.Subnets.Where(subnet => string.Equals(subnet.SubnetId, target.SubnetId, StringComparison.Ordinal)).ToArray();
        if (subnets.Length == 0) return Fail(WorkerFailureCategories.TargetNotFound, $"Operation '{item.OperationId}': no subnet with subnetId '{target.SubnetId}' was found.");
        if (subnets.Length > 1) return Fail(WorkerFailureCategories.TargetAmbiguous, $"Operation '{item.OperationId}': several subnets report subnetId '{target.SubnetId}'.");
        var subnet = subnets[0];
        var matches = subnet.IoSystems.Select((system, index) => (System: system, Index: index))
            .Where(entry => entry.System.Number == target.Number && (target.IoSystemIndex is null || entry.Index == target.IoSystemIndex)
                && (target.IoSystemName is null || string.Equals(entry.System.Name, target.IoSystemName, StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length == 0)
            return Fail(WorkerFailureCategories.TargetNotFound,
                $"Operation '{item.OperationId}': subnet '{target.SubnetId}' lists no IO system with number {target.Number}"
                + (target.IoSystemName is null ? string.Empty : $" named '{target.IoSystemName}'") + ". Re-read with network_read read_hardware_config.");
        if (matches.Length > 1)
            return Fail(WorkerFailureCategories.TargetAmbiguous, $"Operation '{item.OperationId}': subnet '{target.SubnetId}' lists several IO systems with number {target.Number}; add ioSystemIndex or ioSystemName.");
        var system = matches[0].System;
        var requested = new Dictionary<string, string>(StringComparer.Ordinal) { ["deleted"] = "true" };
        var current = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["name"] = system.Name,
            ["ioControllerName"] = system.IoControllerName,
            ["connectedDevices"] = string.Join(", ", system.ConnectedDeviceNames),
        };
        var effect = new NetworkWriteEffect(item.Operation,
            new NetworkWriteTargetEvidence(item.OperationId, item.Operation, null, null, Array.Empty<string>(), null, null, null, subnet.Name, subnet.SubnetId, system.Name, system.Number),
            requested, current, Array.Empty<NetworkNodeIdentityInfo>(), null,
            new HardwareModuleEffect(null, null, Array.Empty<HardwareObjectIdentityInfo>(), Array.Empty<IoTagMatchInfo>(), Array.Empty<HardwareAttributeConversionInfo>(), null));
        return ItemReplan<NetworkWriteEffect>.Ok(ItemPlan<NetworkWriteEffect>.Resolved(effect, new[]
        {
            new CheckedPrecondition("networkDiscoveryComplete", "true", "true", true),
        }));
    }

    // ------------------------------------------------------------------
    // export_cax / import_cax planning
    // ------------------------------------------------------------------

    private static ItemReplan<NetworkWriteEffect> ResolveCax(NetworkOperationRequest item, HardwareConfigInfo state)
    {
        var requested = new Dictionary<string, string>(StringComparer.Ordinal) { ["filePath"] = item.FilePath! };
        var current = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (item.LogFilePath is { } log)
        {
            requested["logFilePath"] = log;
            // Security review F4: an existing log is replaced only with overwrite:true (export_cax and import_cax).
            if (File.Exists(log) && item.Overwrite != true)
                return Fail(WorkerFailureCategories.ValidationError,
                    $"Operation '{item.OperationId}': the log file '{log}' already exists; pass overwrite:true to let TIA Portal write over it. Nothing was changed.");
            if (Directory.Exists(log))
                return Fail(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': logFilePath '{log}' is a directory. Nothing was changed.");
            current["logFileExists"] = File.Exists(log) ? "true" : "false";
        }

        var exists = File.Exists(item.FilePath!);
        current["fileExists"] = exists ? "true" : "false";
        current["deviceCount"] = state.Devices.Count.ToString(CultureInfo.InvariantCulture);
        if (item.Operation == HardwareModuleMethods.ExportCax)
        {
            requested["scope"] = item.DeviceName is null ? CaxNames.Scopes.Project : CaxNames.Scopes.Device;
            requested["overwrite"] = item.Overwrite == true ? "true" : "false";
            if (item.DeviceName is { } deviceName)
            {
                var matches = state.Devices.Count(device => string.Equals(device.Name, deviceName, StringComparison.OrdinalIgnoreCase));
                if (matches == 0) return Fail(WorkerFailureCategories.TargetNotFound, $"Operation '{item.OperationId}': no device named '{deviceName}' was found.");
                if (matches > 1) return Fail(WorkerFailureCategories.TargetAmbiguous, $"Operation '{item.OperationId}': several devices are named '{deviceName}'.");
                requested["deviceName"] = deviceName;
            }

            if (exists && item.Overwrite != true)
                return Fail(WorkerFailureCategories.ValidationError,
                    $"Operation '{item.OperationId}': '{item.FilePath}' already exists; pass overwrite:true to let TIA Portal write over it. Nothing was changed.");
            if (!Directory.Exists(Path.GetDirectoryName(item.FilePath!)))
                return Fail(WorkerFailureCategories.TargetNotFound, $"Operation '{item.OperationId}': the directory of '{item.FilePath}' does not exist. Nothing was changed.");
        }
        else
        {
            requested["importOption"] = item.ImportOption!;
            if (item.LogFilePath is not null) requested["overwrite"] = item.Overwrite == true ? "true" : "false";
            if (!exists) return Fail(WorkerFailureCategories.TargetNotFound, $"Operation '{item.OperationId}': '{item.FilePath}' does not exist. Nothing was changed.");
        }

        var effect = new NetworkWriteEffect(item.Operation,
            new NetworkWriteTargetEvidence(item.OperationId, item.Operation, item.DeviceName, null, Array.Empty<string>(), null, null, null, null, null, null, null),
            requested, current, Array.Empty<NetworkNodeIdentityInfo>(), null,
            new HardwareModuleEffect(null, null, Array.Empty<HardwareObjectIdentityInfo>(), Array.Empty<IoTagMatchInfo>(), Array.Empty<HardwareAttributeConversionInfo>(), null));
        return ItemReplan<NetworkWriteEffect>.Ok(ItemPlan<NetworkWriteEffect>.Resolved(effect, new[]
        {
            new CheckedPrecondition("networkDiscoveryComplete", "true", "true", true),
        }));
    }

    // ------------------------------------------------------------------
    // export_io_map planning
    // ------------------------------------------------------------------

    private static ItemReplan<NetworkWriteEffect> ResolveExport(NetworkOperationRequest item)
    {
        if (HardwareIoMapExport.Precheck(item.FilePath!, item.Overwrite == true) is { } refusal)
            return Fail(refusal.FailureCategory!, $"Operation '{item.OperationId}': {refusal.Error}");
        var requested = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["filePath"] = item.FilePath!,
            ["overwrite"] = item.Overwrite == true ? "true" : "false",
            ["csvDelimiter"] = item.CsvDelimiter ?? ",",
            ["rawCells"] = item.RawCells == true ? "true" : "false",
            ["includeTagMatches"] = item.IncludeTagMatches == false ? "false" : "true",
        };
        if (item.DeviceName is not null) requested["deviceName"] = item.DeviceName;
        if (item.PlcName is not null) requested["plcName"] = item.PlcName;
        var current = new Dictionary<string, string?>(StringComparer.Ordinal) { ["fileExists"] = File.Exists(item.FilePath!) ? "true" : "false" };
        var effect = new NetworkWriteEffect(item.Operation,
            new NetworkWriteTargetEvidence(item.OperationId, item.Operation, item.DeviceName, null, Array.Empty<string>(), null, null, null, null, null, null, null),
            requested, current, Array.Empty<NetworkNodeIdentityInfo>(), null,
            new HardwareModuleEffect(null, null, Array.Empty<HardwareObjectIdentityInfo>(), Array.Empty<IoTagMatchInfo>(),
                Array.Empty<HardwareAttributeConversionInfo>(), null));
        return ItemReplan<NetworkWriteEffect>.Ok(ItemPlan<NetworkWriteEffect>.Resolved(effect));
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private sealed record Decoded<T>(T? Value, WriteToolError? Error);

    private static async Task<Decoded<T>> DecodeAsync<T>(Task<WorkerCallResult> call, NetworkOperationRequest item, string what) where T : class
    {
        var result = await call.ConfigureAwait(false);
        if (!result.Success)
            return new(null, new WriteToolError(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                $"Operation '{item.OperationId}': {what} could not be read: {result.Error}"));
        try
        {
            return new(CanonicalJson.NormalizeWorkerPayload<T>(result.Payload).Value, null);
        }
        catch (JsonException)
        {
            return new(null, new WriteToolError(WorkerFailureCategories.ProtocolError,
                $"Operation '{item.OperationId}': the payload of {what} did not match its declared result contract and was rejected."));
        }
    }

    private static string FeasibilityMember(string operation) => operation switch
    {
        HardwareModuleMethods.PlugDeviceItem => "CanPlugNew",
        HardwareModuleMethods.MoveDeviceItem => "CanPlugMove",
        _ => "CanPlugCopy",
    };

    private static ItemReplan<NetworkWriteEffect> Fail(string category, string message) => ItemReplan<NetworkWriteEffect>.Fail(category, message);

    internal static string Describe(HardwareObjectIdentityInfo identity)
        => identity.ItemPath.Count == 0
            ? $"device '{identity.DeviceName}'"
            : $"'{identity.DeviceName}/{string.Join("/", identity.ItemPath.Select(segment => segment.Name))}'";

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string SubItemText(IEnumerable<HardwareSubItemSegmentInfo> path)
        => string.Join("/", path.Select(segment => segment.Name ?? $"[{segment.Index}]"));

    private static string Candidate(HardwareIoAddressInfo address)
        => $"index {address.Index}" + (address.SubItemPath.Count == 0 ? string.Empty : $" on sub-item '{SubItemText(address.SubItemPath)}'")
            + $" length {Number(address.Length) ?? "null"}";

    private static string? Flag(bool? value) => value switch { null => null, true => "true", false => "false" };

    private static string ValueText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    private static string ValueText(NetworkAttributeValueInfo value) => value.Value switch
    {
        null => "null",
        JsonElement { ValueKind: JsonValueKind.Object } element when element.TryGetProperty("symbol", out var symbol) => symbol.GetString() ?? string.Empty,
        JsonElement element => ValueText(element),
        NetworkEnumValueInfo symbol => symbol.Symbol,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        var other => other.ToString() ?? string.Empty,
    };
}
