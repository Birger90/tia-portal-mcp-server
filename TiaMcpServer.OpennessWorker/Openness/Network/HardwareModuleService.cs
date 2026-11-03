using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Utilities;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// Hardware-module operations on devices and device items: rack/slot plugging, move, copy, delete,
/// change type, typed attribute writes and I/O start addresses, plus their reads. Every Siemens member
/// is either declared in the reference stubs from the V21 documentation or, where the documentation
/// gives no return/declaring/property type, bound by exact name and parameter signature at runtime
/// (<see cref="InvokeExact"/>); a missing member fails closed and is never searched fuzzily.
/// </summary>
internal static partial class HardwareModuleService
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    // ------------------------------------------------------------------
    // Reads
    // ------------------------------------------------------------------

    public static HardwarePlugLocationListInfo ListPlugLocations(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var target = Resolve(project, request.Target, "target");
        var result = new HardwarePlugLocationListInfo { Target = target.Identity };
        foreach (var location in target.Object.GetPlugLocations() ?? (IList<Siemens.Engineering.HW.Extensions.PlugLocation>)Array.Empty<Siemens.Engineering.HW.Extensions.PlugLocation>())
        {
            if (location is null) continue;
            result.PlugLocations.Add(new HardwarePlugLocationInfo
            {
                PositionNumber = ReadInt32Property(location, "PositionNumber", "plug location position number", result.Messages),
                Label = ReadStringProperty(location, "Label", "plug location label", result.Messages),
            });
        }

        return result;
    }

    public static HardwareModuleTypeListInfo FindModuleTypes(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var partial = RequireText(request.TypeIdentifier, "TypeIdentifier");
        var provider = ModuleInformation(project);
        return new HardwareModuleTypeListInfo
        {
            PartialTypeIdentifier = partial,
            ModuleTypes = (provider.FindModuleTypes(partial) ?? Array.Empty<string>()).Where(value => value is not null).ToList(),
        };
    }

    public static HardwareContainerTypeListInfo FindContainerTypes(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var typeIdentifier = RequireText(request.TypeIdentifier, "TypeIdentifier");
        var provider = ModuleInformation(project);
        return new HardwareContainerTypeListInfo
        {
            TypeIdentifier = typeIdentifier,
            ContainerTypes = (provider.FindContainerTypes(typeIdentifier) ?? Array.Empty<string>()).Where(value => value is not null).ToList(),
        };
    }

    public static HardwarePlugProbeInfo ProbePlug(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var operation = request.ProbeOperation;
        var position = request.PositionNumber ?? throw Validation("PositionNumber is required.");
        switch (operation)
        {
            case HardwareModuleMethods.ProbeOperations.Plug:
            {
                var container = Resolve(project, request.Target, "container");
                var typeIdentifier = RequireText(request.TypeIdentifier, "TypeIdentifier");
                return new HardwarePlugProbeInfo
                {
                    Operation = operation,
                    Container = container.Identity,
                    PositionNumber = position,
                    CanPlug = container.Object.CanPlugNew(typeIdentifier, request.Name ?? string.Empty, position),
                };
            }
            case HardwareModuleMethods.ProbeOperations.Move:
            case HardwareModuleMethods.ProbeOperations.Copy:
            {
                var source = Resolve(project, request.Target, "target");
                var item = RequireItem(source, $"probe {operation}");
                var container = Resolve(project, request.Destination, "destination");
                return new HardwarePlugProbeInfo
                {
                    Operation = operation!,
                    Container = container.Identity,
                    Source = source.Identity,
                    PositionNumber = position,
                    CanPlug = operation == HardwareModuleMethods.ProbeOperations.Move
                        ? container.Object.CanPlugMove(item, position)
                        : container.Object.CanPlugCopy(item, position),
                };
            }
            default:
                throw Validation($"ProbeOperation must be one of: {string.Join(", ", HardwareModuleMethods.ProbeOperations.All)}.");
        }
    }

    public static HardwareAttributeReadInfo ReadAttributes(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var target = Resolve(project, request.Target, "target");
        var inspected = EngineeringAttributeInspector.Inspect(target.Object, request.AttributeNames);
        var result = new HardwareAttributeReadInfo
        {
            Target = target.Identity,
            Attributes = NetworkAttributeResultBuilder.Build(Array.Empty<NetworkAttributeObservation>(), inspected.Observations,
                request.AttributeNames).ToList(),
            Messages = inspected.Diagnostics.Distinct(StringComparer.Ordinal).ToList(),
        };
        if (request.Attributes is { Count: > 0 } values)
        {
            var infos = ((IEngineeringObject)target.Object).GetAttributeInfos().ToDictionary(info => info.Name, StringComparer.Ordinal);
            foreach (var value in values)
            {
                var verdict = new HardwareAttributeConversionInfo { Name = value.Name };
                if (!infos.TryGetValue(value.Name, out var info))
                    verdict.Error = $"The target has no attribute '{value.Name}' (GetAttributeInfos, case-sensitive).";
                else if (info.AccessMode is not (EngineeringAttributeAccessMode.Write or EngineeringAttributeAccessMode.ReadWrite))
                    verdict.Error = $"Attribute '{value.Name}' reports access mode {info.AccessMode}; it is not writable.";
                else
                {
                    var conversion = HardwareAttributeValueConverter.Convert(value, info.SupportedTypes.ToArray());
                    verdict.Success = conversion.Success;
                    verdict.TargetType = conversion.TargetType?.FullName;
                    verdict.Error = conversion.Error;
                }

                result.Conversions.Add(verdict);
            }
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Writes
    // ------------------------------------------------------------------

    public static HardwareDeviceItemMutationResultInfo Plug(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var container = Resolve(project, request.Target, "container");
        var typeIdentifier = RequireText(request.TypeIdentifier, "TypeIdentifier");
        var position = request.PositionNumber ?? throw Validation("PositionNumber is required.");
        var name = request.Name ?? string.Empty;
        if (!container.Object.CanPlugNew(typeIdentifier, name, position))
            throw Validation(PlugRefusal(container, typeIdentifier, name, position));
        var rackDevice = RackOwner(container);
        var deviceIdentity = rackDevice is null ? null : DeviceIdentity(container.Identity.DeviceName, rackDevice);
        var deviceItemsBefore = rackDevice?.DeviceItems.ToList();
        var containerItemsBefore = container.Object.DeviceItems.ToList();
        var plugged = container.Object.PlugNew(typeIdentifier, name, position);
        var result = new HardwareDeviceItemMutationResultInfo { Operation = HardwareModuleMethods.PlugDeviceItem, Container = container.Identity };
        result.Result = IdentityOf(plugged, result.Messages);
        // PlugNew is documented to return the new plugged device item. It must be a direct child of the container at
        // the requested position (and carry the requested name when one was given); for a rack or rail container
        // TIA Portal V21 places the module directly under the device instead (live 2026-11-02, S7-1500 rail and
        // ET 200SP rack), which is accepted when type identifier, name and position match (K05).
        var placement = Placement(result.Result, container.Identity, position, deviceIdentity, typeIdentifier, name);
        if (placement.PlacedUnder is null && result.Result is not { ItemPath.Count: > 0 })
            placement = FindNewItem(container, rackDevice, deviceIdentity, containerItemsBefore, deviceItemsBefore, typeIdentifier, name, position, result, placement);
        if (placement.PlacedUnder is null)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"plug_device_item: PlugNew ran but the result could not be confirmed: {string.Join("; ", placement.Mismatches)}. "
                + $"The item may be plugged; inspect {Describe(container.Identity)} with network_read read_hardware_config before retrying.", result.Messages);
        result.PlacedUnder = placement.PlacedUnder;
        if (placement.AtDeviceLevel)
            result.Messages.Add($"TIA Portal placed the item directly under device '{container.Identity.DeviceName}' at position {position}, "
                + $"not under {Describe(container.Identity)}; use result.itemPath as its selector.");
        return result;
    }

    public static HardwareDeviceItemMutationResultInfo Move(ProjectBase project, HardwareModuleRequestInfo request)
        => MoveOrCopy(project, request, copy: false);

    public static HardwareDeviceItemMutationResultInfo Copy(ProjectBase project, HardwareModuleRequestInfo request)
        => MoveOrCopy(project, request, copy: true);

    private static HardwareDeviceItemMutationResultInfo MoveOrCopy(ProjectBase project, HardwareModuleRequestInfo request, bool copy)
    {
        var operation = copy ? HardwareModuleMethods.CopyDeviceItem : HardwareModuleMethods.MoveDeviceItem;
        var source = Resolve(project, request.Target, "target");
        var item = RequireItem(source, operation);
        var container = Resolve(project, request.Destination, "destination");
        var position = request.PositionNumber ?? throw Validation("PositionNumber is required.");
        var feasible = copy ? container.Object.CanPlugCopy(item, position) : container.Object.CanPlugMove(item, position);
        if (!feasible)
            throw Validation($"{operation}: {(copy ? "CanPlugCopy" : "CanPlugMove")} returned false for {Describe(source.Identity)} to position {position} of "
                + $"{Describe(container.Identity)}. Nothing was changed. List free positions with network_read list_plug_locations.");
        var placed = copy ? container.Object.PlugCopy(item, position) : container.Object.PlugMove(item, position);
        var result = new HardwareDeviceItemMutationResultInfo { Operation = operation, Container = container.Identity, Source = source.Identity };
        result.Result = IdentityOf(placed, result.Messages);
        // As for plug_device_item, a rack-level destination may receive the item directly under its device (K05).
        var placement = Placement(result.Result, container.Identity, position, RackOwner(container) is { } owner ? DeviceIdentity(container.Identity.DeviceName, owner) : null,
            source.Identity.TypeIdentifier, name: null);
        var mismatches = placement.PlacedUnder is null ? placement.Mismatches : new List<string>();
        // A moved item must no longer be found at its original exact path (a copy leaves the source in place).
        if (!copy && NetworkObjectSelectorResolver.Resolve(project, ItemSelector(request.Target!)).Success)
            mismatches.Add($"an item with the original name, type identifier and position is still at {Describe(source.Identity)}");
        if (mismatches.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"{operation}: {(copy ? "PlugCopy" : "PlugMove")} ran but the result could not be confirmed: {string.Join("; ", mismatches)}. "
                + $"Inspect {Describe(container.Identity)} with network_read read_hardware_config before retrying.", result.Messages);
        result.PlacedUnder = placement.PlacedUnder;
        if (placement.AtDeviceLevel)
            result.Messages.Add($"TIA Portal placed the item directly under device '{container.Identity.DeviceName}' at position {position}, "
                + $"not under {Describe(container.Identity)}; use result.itemPath as its selector.");
        return result;
    }

    public static HardwareDeviceItemMutationResultInfo Delete(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var source = Resolve(project, request.Target, "target");
        var item = RequireItem(source, HardwareModuleMethods.DeleteDeviceItem);
        item.Delete();
        var result = new HardwareDeviceItemMutationResultInfo { Operation = HardwareModuleMethods.DeleteDeviceItem, Source = source.Identity };
        // Re-resolve by the same exact path: the deleted reference must not be reused (V21 documentation).
        var after = NetworkObjectSelectorResolver.Resolve(project, ItemSelector(request.Target!));
        if (after.Success)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"delete_device_item: an item with the same name, type identifier and position is still at {Describe(source.Identity)} after Delete().");
        return result;
    }

    public static HardwareDeviceItemMutationResultInfo ChangeType(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var source = Resolve(project, request.Target, "target");
        var item = RequireItem(source, HardwareModuleMethods.ChangeDeviceItemType);
        var typeIdentifier = RequireText(request.TypeIdentifier, "TypeIdentifier");
        // Documented as DeviceItem.ChangeType(System.String); its return type is not documented, so it is
        // invoked by exact parameter signature and the item is re-resolved by position afterwards.
        var returned = InvokeExact(item, "ChangeType", new[] { typeof(string) }, new object[] { typeIdentifier }, "DeviceItem.ChangeType(System.String)");
        var result = new HardwareDeviceItemMutationResultInfo { Operation = HardwareModuleMethods.ChangeDeviceItemType, Source = source.Identity };
        var changed = returned as DeviceItem ?? FindSiblingAtPosition(project, request.Target!, source.Identity.PositionNumber, result.Messages);
        if (changed is null)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"change_device_item_type: no item could be found at position {source.Identity.PositionNumber} of the original parent after ChangeType. Inspect the device before retrying.");
        result.Result = IdentityOf(changed, result.Messages);
        if (result.Result is not { TypeIdentifier: not null })
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"change_device_item_type: the item's type identifier could not be read after ChangeType('{typeIdentifier}'), so the change is unconfirmed. "
                + "Inspect the device before retrying.", result.Messages);
        if (result.Result is { TypeIdentifier: { } actual } && string.Equals(actual, source.Identity.TypeIdentifier, StringComparison.Ordinal)
            && !string.Equals(actual, typeIdentifier, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"change_device_item_type: the item still reports type identifier '{actual}' after ChangeType('{typeIdentifier}').");
        if (result.Result is { TypeIdentifier: { } reported } && !string.Equals(reported, typeIdentifier, StringComparison.Ordinal))
            result.Messages.Add($"The item reports type identifier '{reported}' (requested '{typeIdentifier}'); TIA Portal may normalize identifiers.");
        return result;
    }

    public static HardwareAttributeWriteResultInfo SetAttributes(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var target = Resolve(project, request.Target, "target");
        var policy = request.AttributeErrorPolicy ?? HardwareModuleMethods.AttributeErrorPolicies.Abort;
        var mode = request.AttributeWriteMode ?? HardwareModuleMethods.AttributeWriteModes.Bulk;
        if (!HardwareModuleMethods.AttributeErrorPolicies.All.Contains(policy, StringComparer.Ordinal))
            throw Validation($"AttributeErrorPolicy must be one of: {string.Join(", ", HardwareModuleMethods.AttributeErrorPolicies.All)}.");
        if (!HardwareModuleMethods.AttributeWriteModes.All.Contains(mode, StringComparer.Ordinal))
            throw Validation($"AttributeWriteMode must be one of: {string.Join(", ", HardwareModuleMethods.AttributeWriteModes.All)}.");
        var requested = request.Attributes ?? throw Validation("Attributes are required.");
        if (requested.Count == 0) throw Validation("At least one attribute is required.");

        var engineeringObject = (IEngineeringObject)target.Object;
        var infos = engineeringObject.GetAttributeInfos().ToDictionary(info => info.Name, StringComparer.Ordinal);
        var planned = new List<(HardwareAttributeValueInfo Request, object Value, Type Type, object? Before)>();
        foreach (var attribute in requested)
        {
            if (!infos.TryGetValue(attribute.Name, out var info))
                throw Validation($"set_hardware_attributes: {Describe(target.Identity)} has no attribute '{attribute.Name}' (GetAttributeInfos, case-sensitive). Nothing was changed.");
            if (info.AccessMode is not (EngineeringAttributeAccessMode.Write or EngineeringAttributeAccessMode.ReadWrite))
                throw Validation($"set_hardware_attributes: attribute '{attribute.Name}' reports access mode {info.AccessMode}; it is not writable. Nothing was changed.");
            var conversion = HardwareAttributeValueConverter.Convert(attribute, info.SupportedTypes.ToArray());
            if (!conversion.Success) throw Validation($"set_hardware_attributes: {conversion.Error} Nothing was changed.");
            planned.Add((attribute, conversion.Value!, conversion.TargetType!, TryRead(engineeringObject, attribute.Name)));
        }

        var result = new HardwareAttributeWriteResultInfo { Target = target.Identity, AttributeErrorPolicy = policy, AttributeWriteMode = mode };
        var rejected = new Dictionary<string, string>(StringComparer.Ordinal);
        var handler = new AttributeErrorHandler(policy, rejected);
        try
        {
            if (mode == HardwareModuleMethods.AttributeWriteModes.Bulk)
                InvokeBulkSet(engineeringObject, planned.Select(p => new KeyValuePair<string, object>(p.Request.Name, p.Value)).ToList(), handler);
            else
                foreach (var entry in planned) engineeringObject.SetAttribute(entry.Request.Name, entry.Value);
        }
        catch (EngineeringException exception)
        {
            var readBack = Outcomes(engineeringObject, planned, rejected, policy, result.Messages);
            throw new WorkerOperationException(WorkerExecution.Classify(exception, mutates: true),
                $"set_hardware_attributes: {exception.Message}{RejectedText(rejected)}{HandlerText(handler)} Earlier attributes may already be applied; "
                + "the warnings list the read-back of every requested attribute.",
                readBack.Select(Describe).Concat(result.Messages).ToList(), engineeringError: WorkerExecution.Describe(exception));
        }

        // SetAttributes returned, but the delegate could not give TIA Portal the caller's answer for at least one
        // attribute, so Portal continued with its own default: report the read-back like any partial application.
        if (handler.FailureMessage is { } handlerFailure)
        {
            var readBack = Outcomes(engineeringObject, planned, rejected, policy, result.Messages);
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"set_hardware_attributes: {handlerFailure} SetAttributes ran, so TIA Portal answered that attribute with its own default; "
                + $"attributes may already be applied.{RejectedText(rejected)} The warnings list the read-back of every requested attribute; "
                + "retry with attributeWriteMode 'sequential'.",
                readBack.Select(Describe).Concat(result.Messages).ToList());
        }

        result.Attributes = Outcomes(engineeringObject, planned, rejected, policy, result.Messages);
        var notApplied = result.Attributes.Where(outcome => outcome.Status == "notApplied").ToArray();
        if (notApplied.Length > 0)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"set_hardware_attributes: {notApplied.Length} attribute(s) did not read back the written value: "
                + string.Join("; ", notApplied.Select(Describe)) + ".", result.Attributes.Select(Describe).ToList());
        return result;
    }

    // ------------------------------------------------------------------
    // Resolution and identities
    // ------------------------------------------------------------------

    private sealed record ResolvedHardwareObject(HardwareObject Object, DeviceItem? Item, HardwareObjectIdentityInfo Identity);

    private static ResolvedHardwareObject Resolve(ProjectBase project, HardwareObjectSelectorInfo? selector, string role)
    {
        if (selector is null || string.IsNullOrWhiteSpace(selector.DeviceName)) throw Validation($"A {role} with a device name is required.");
        if (selector.ItemPath is null)
        {
            foreach (var candidate in ProjectDeviceEnumerator.Enumerate(project))
                if (string.IsNullOrWhiteSpace(candidate.Name))
                    throw new WorkerOperationException(WorkerFailureCategories.TargetEvidenceMismatch,
                        "The device-name namespace has unreadable required identity evidence.");
            var matches = ProjectDeviceNameMatcher.FindMatches(project, selector.DeviceName);
            if (matches.Count == 0) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No device named '{selector.DeviceName}' was found.");
            if (matches.Count > 1) throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Multiple devices are named '{selector.DeviceName}'.");
            var device = matches[0].Device;
            return new ResolvedHardwareObject(device, null, new HardwareObjectIdentityInfo
            {
                DeviceName = matches[0].Name,
                Name = matches[0].Name,
                TypeIdentifier = device.TypeIdentifier,
                PositionNumber = null,
            });
        }

        if (selector.ItemPath.Count == 0) throw Validation($"The {role} item path must be non-empty; omit it to name the device.");
        var resolution = NetworkObjectSelectorResolver.Resolve(project, ItemSelector(selector));
        if (!resolution.Success)
            throw new WorkerOperationException(resolution.FailureCategory ?? WorkerFailureCategories.TargetNotFound,
                $"The {role} could not be resolved: {resolution.Error}");
        var item = (DeviceItem)resolution.Resolved!.Value;
        var path = resolution.Resolved.Target.ItemPath!.Select(Clone).ToList();
        var leaf = path[path.Count - 1];
        return new ResolvedHardwareObject(item, item, new HardwareObjectIdentityInfo
        {
            DeviceName = resolution.Resolved.Target.DeviceName ?? selector.DeviceName,
            ItemPath = path,
            Name = leaf.Name,
            TypeIdentifier = leaf.TypeIdentifier,
            PositionNumber = leaf.PositionNumber,
        });
    }

    private static NetworkObjectSelectorInfo ItemSelector(HardwareObjectSelectorInfo selector) => new()
    {
        Kind = NetworkObjectKinds.DeviceItem,
        DeviceName = selector.DeviceName,
        ItemPath = selector.ItemPath!.Select(Clone).ToList(),
    };

    private static DeviceItem RequireItem(ResolvedHardwareObject resolved, string operation)
        => resolved.Item ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"{operation} acts on a device item; '{resolved.Identity.DeviceName}' names a device. Add the item path.");

    /// <summary>Builds the forwardable identity of a live item by walking <c>Parent</c> up to its device.</summary>
    private static HardwareObjectIdentityInfo? IdentityOf(DeviceItem? item, List<string> messages)
    {
        if (item is null)
        {
            messages.Add("TIA Portal returned no device item.");
            return null;
        }

        try
        {
            var chain = new List<DeviceItem>();
            IEngineeringObject? current = item;
            Device? device = null;
            while (current is not null)
            {
                if (chain.Any(seen => ReferenceEquals(seen, current))) throw new InvalidOperationException("The device item hierarchy contains a cycle.");
                if (current is Device owner) { device = owner; break; }
                if (current is DeviceItem level) chain.Add(level);
                else throw new InvalidOperationException($"Unexpected parent type '{current.GetType().Name}' in the device item hierarchy.");
                current = current.Parent;
            }

            if (device is null) throw new InvalidOperationException("The owning device was not found.");
            chain.Reverse();
            var path = new List<DeviceItemPathSegmentInfo>();
            IEnumerable<DeviceItem> siblings = device.DeviceItems;
            foreach (var level in chain)
            {
                var index = 0;
                var found = -1;
                foreach (var sibling in siblings)
                {
                    if (Equals(sibling, level)) { found = index; break; }
                    index++;
                }

                if (found < 0) throw new InvalidOperationException("A device item was not found among its parent's items.");
                path.Add(new DeviceItemPathSegmentInfo
                {
                    Index = found,
                    Name = level.Name,
                    PositionNumber = level.PositionNumber,
                    // K77: integrated items (interfaces, ports) read TypeIdentifier null live; the segment carries "" like
                    // read_hardware_config's path segments, and the selector resolver treats null and "" alike.
                    TypeIdentifier = level.TypeIdentifier ?? string.Empty,
                });
                siblings = level.DeviceItems;
            }

            var leaf = path[path.Count - 1];
            return new HardwareObjectIdentityInfo
            {
                DeviceName = device.Name,
                ItemPath = path,
                Name = leaf.Name,
                TypeIdentifier = leaf.TypeIdentifier,
                PositionNumber = leaf.PositionNumber,
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException || exception is EngineeringException)
        {
            // K77: an identity without its device name and path was rejected by the host contract and voided the whole payload;
            // an item whose selector cannot be read has no identity (null), and the message names how to inspect it.
            messages.Add($"A device item's selector could not be read ({exception.Message}); inspect it with network_read read_hardware_config.");
            return null;
        }
    }

    private static DeviceItem? FindSiblingAtPosition(ProjectBase project, HardwareObjectSelectorInfo original, int? position, List<string> messages)
    {
        if (position is null || original.ItemPath is not { Count: > 0 } path) return null;
        var parentSelector = new HardwareObjectSelectorInfo
        {
            DeviceName = original.DeviceName,
            ItemPath = path.Count == 1 ? null : path.Take(path.Count - 1).Select(Clone).ToList(),
        };
        var parent = Resolve(project, parentSelector, "parent");
        var candidates = parent.Object.DeviceItems.Where(item => item.PositionNumber == position.Value).ToArray();
        if (candidates.Length == 1) return candidates[0];
        messages.Add($"{candidates.Length} items are at position {position} of the original parent after ChangeType.");
        return null;
    }

    private static DeviceItemPathSegmentInfo Clone(DeviceItemPathSegmentInfo segment) => new()
    {
        Index = segment.Index,
        Name = segment.Name,
        PositionNumber = segment.PositionNumber,
        TypeIdentifier = segment.TypeIdentifier,
    };

    private static string Describe(HardwareObjectIdentityInfo identity)
        => identity.ItemPath.Count == 0
            ? $"device '{identity.DeviceName}'"
            : $"'{identity.DeviceName}/{string.Join("/", identity.ItemPath.Select(segment => segment.Name))}'";

    // ------------------------------------------------------------------
    // Module information
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>project.HwUtilities</c> is documented on the project without its declaring type, so the
    /// property is read by name and its documented <c>HardwareUtilityComposition</c> is enumerated for
    /// the one <see cref="ModuleInformationProvider"/>.
    /// </summary>
    private static ModuleInformationProvider ModuleInformation(ProjectBase project)
    {
        var property = project.GetType().GetProperty("HwUtilities", PublicInstance)
            ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                "The open project exposes no HwUtilities property, so ModuleInformationProvider is unavailable for this container.");
        object? utilities;
        try
        {
            utilities = property.GetValue(project);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        var providers = OpennessReflection.Enumerate(utilities, "project hardware utilities").OfType<ModuleInformationProvider>().ToArray();
        return providers.Length == 1
            ? providers[0]
            : throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"Expected exactly one ModuleInformationProvider in project.HwUtilities, found {providers.Length}.");
    }

    // ------------------------------------------------------------------
    // Attributes
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>SetAttributes(IEnumerable&lt;KeyValuePair&lt;string, object&gt;&gt;, AttributeDelegate)</c>, documented on the
    /// <c>IEngineeringObject</c> pattern (V19+). Bound at runtime by exact first parameter type and the
    /// documented delegate type name; the delegate answers every <c>AttributeConfiguration</c> with the
    /// caller's policy (<c>Abort</c> or <c>Ignore</c>) and records its name and message.
    /// </summary>
    private static void InvokeBulkSet(IEngineeringObject target, List<KeyValuePair<string, object>> values, AttributeErrorHandler handler)
    {
        var method = FindBulkSetAttributes(target)
            ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                "This TIA Portal installation exposes no SetAttributes(IEnumerable<KeyValuePair<string, object>>, AttributeDelegate) on the target. "
                + "Nothing was changed; retry with attributeWriteMode 'sequential'.");
        var delegateType = method.GetParameters()[1].ParameterType;
        Delegate callback;
        try
        {
            callback = Delegate.CreateDelegate(delegateType, handler,
                typeof(AttributeErrorHandler).GetMethod(nameof(AttributeErrorHandler.Handle), PublicInstance)!);
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"The AttributeDelegate signature differs from the documented one ({exception.Message}). Nothing was changed; retry with attributeWriteMode 'sequential'.");
        }

        try
        {
            method.Invoke(target, new object[] { values, callback });
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static MethodInfo? FindBulkSetAttributes(IEngineeringObject target)
    {
        bool Matches(MethodInfo candidate)
        {
            if (candidate.Name != "SetAttributes") return false;
            var parameters = candidate.GetParameters();
            return parameters.Length == 2
                && parameters[0].ParameterType == typeof(IEnumerable<KeyValuePair<string, object>>)
                && parameters[1].ParameterType.FullName == "Siemens.Engineering.AttributeDelegate"
                && typeof(Delegate).IsAssignableFrom(parameters[1].ParameterType);
        }

        var candidates = target.GetType().GetMethods(PublicInstance).Where(Matches)
            .Concat(target.GetType().GetInterfaces().SelectMany(type => type.GetMethods()).Where(Matches))
            .ToArray();
        return candidates.Length == 0 ? null : candidates[0];
    }

    /// <summary>
    /// Answers each <c>AttributeConfiguration</c>; members are read and set by their documented names. It never
    /// throws back into TIA Portal: a configuration it cannot answer is recorded in <see cref="FailureMessage"/>
    /// and the caller reports it with the read-back after SetAttributes returns.
    /// </summary>
    internal sealed class AttributeErrorHandler(string policy, Dictionary<string, string> rejected)
    {
        public string? FailureMessage { get; private set; }

        public void Handle(object configuration)
        {
            var name = OpennessReflection.ReadProperty(configuration, "Name", "attribute configuration name")?.ToString() ?? "(unknown)";
            var message = OpennessReflection.ReadProperty(configuration, "Message", "attribute configuration message")?.ToString() ?? string.Empty;
            rejected[name] = message;
            var choice = policy == HardwareModuleMethods.AttributeErrorPolicies.Ignore ? "Ignore" : "Abort";
            try
            {
                var selection = configuration.GetType().GetProperty("CurrentSelection", PublicInstance);
                if (selection is null || !selection.CanWrite || !selection.PropertyType.IsEnum
                    || !Enum.GetNames(selection.PropertyType).Contains(choice, StringComparer.Ordinal))
                {
                    FailureMessage ??= $"AttributeConfiguration.CurrentSelection cannot be set to '{choice}' on this installation; attribute '{name}' was reported as: {message}";
                    return;
                }

                selection.SetValue(configuration, Enum.Parse(selection.PropertyType, choice));
            }
            catch (Exception exception) when (exception is TargetInvocationException || exception is ArgumentException
                || exception is EngineeringException || exception is InvalidOperationException)
            {
                var detail = (exception as TargetInvocationException)?.InnerException?.Message ?? exception.Message;
                FailureMessage ??= $"Setting AttributeConfiguration.CurrentSelection to '{choice}' for attribute '{name}' failed ({detail}); it was reported as: {message}";
            }
        }
    }

    private static string HandlerText(AttributeErrorHandler handler)
        => handler.FailureMessage is { } failure ? $" {failure}" : string.Empty;

    private static List<HardwareAttributeWriteOutcomeInfo> Outcomes(IEngineeringObject target,
        List<(HardwareAttributeValueInfo Request, object Value, Type Type, object? Before)> planned,
        Dictionary<string, string> rejected, string policy, List<string> messages)
    {
        var outcomes = new List<HardwareAttributeWriteOutcomeInfo>();
        foreach (var entry in planned)
        {
            object? after = null;
            string? readError = null;
            try
            {
                after = target.GetAttribute(entry.Request.Name);
            }
            catch (EngineeringException exception)
            {
                readError = exception.Message;
            }

            var applied = readError is null && HardwareAttributeValueConverter.ReadBackEquals(entry.Value, after);
            // Only an attribute the delegate reported under policy ignore is "ignored"; anything else that does not
            // read back the written value is "notApplied" and fails the postcondition.
            var status = applied ? "applied"
                : policy == HardwareModuleMethods.AttributeErrorPolicies.Ignore && rejected.ContainsKey(entry.Request.Name) ? "ignored"
                : "notApplied";
            outcomes.Add(new HardwareAttributeWriteOutcomeInfo
            {
                Name = entry.Request.Name,
                Requested = NetworkAttributeValueNormalizer.Normalize(entry.Value).Value ?? new NetworkAttributeValueInfo(),
                TargetType = entry.Type.FullName ?? entry.Type.Name,
                Before = NetworkAttributeValueNormalizer.Normalize(entry.Before).Value,
                After = readError is null ? NetworkAttributeValueNormalizer.Normalize(after).Value : null,
                Status = status,
                Message = readError is not null ? $"Read-back failed: {readError}"
                    : rejected.TryGetValue(entry.Request.Name, out var reported) ? $"AttributeDelegate: {reported}" : null,
            });
        }

        foreach (var name in rejected.Keys.Where(name => planned.All(entry => entry.Request.Name != name)))
            messages.Add($"AttributeDelegate reported attribute '{name}' that was not requested: {rejected[name]}");
        return outcomes;
    }

    private static object? TryRead(IEngineeringObject target, string name)
    {
        try
        {
            return target.GetAttribute(name);
        }
        catch (EngineeringException)
        {
            return null;
        }
    }

    private static string RejectedText(Dictionary<string, string> rejected)
        => rejected.Count == 0 ? string.Empty : " AttributeDelegate reported: " + string.Join("; ", rejected.Select(pair => $"'{pair.Key}': {pair.Value}")) + ".";

    private static string Describe(HardwareAttributeWriteOutcomeInfo outcome)
        => $"{outcome.Name}: {outcome.Status}, requested {ValueText(outcome.Requested)}, read back {ValueText(outcome.After)}"
            + (outcome.Message is null ? string.Empty : $" ({outcome.Message})");

    private static string ValueText(NetworkAttributeValueInfo? value) => value switch
    {
        null => "(unreadable)",
        { Kind: "null" } => "null",
        { Value: NetworkEnumValueInfo symbol } => symbol.Symbol,
        { Value: IFormattable formattable } => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        { Value: { } other } => other.ToString() ?? string.Empty,
        _ => "null",
    };

    // ------------------------------------------------------------------
    // Reflection and small helpers
    // ------------------------------------------------------------------

    /// <summary>Invokes a documented member by its exact name and parameter types; a TIA exception is rethrown unwrapped.</summary>
    private static object? InvokeExact(object target, string name, Type[] parameterTypes, object[] arguments, string documented)
    {
        var method = target.GetType().GetMethod(name, PublicInstance, null, parameterTypes, null)
            ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"This TIA Portal installation exposes no {documented} on '{target.GetType().FullName}'. Nothing was changed.");
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static int? ReadInt32Property(object instance, string property, string description, List<string> messages)
    {
        var value = OpennessReflection.ReadProperty(instance, property, description);
        switch (value)
        {
            case null:
                messages.Add($"Could not read {description}.");
                return null;
            case int number:
                return number;
            case short or ushort or byte or sbyte or long or uint or ulong:
                try
                {
                    return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    messages.Add($"{description} {value} does not fit Int32.");
                    return null;
                }
            default:
                messages.Add($"{description} has unexpected type '{value.GetType().FullName}'.");
                return null;
        }
    }

    private static string? ReadStringProperty(object instance, string property, string description, List<string> messages)
    {
        var value = OpennessReflection.ReadProperty(instance, property, description);
        if (value is null) messages.Add($"Could not read {description}.");
        return value?.ToString();
    }

    private static string RequireText(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw Validation($"{name} is required.") : value!;

    private static WorkerOperationException Validation(string message) => new(WorkerFailureCategories.ValidationError, message);
}
