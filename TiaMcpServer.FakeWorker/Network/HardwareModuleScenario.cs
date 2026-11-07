using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Network;

/// <summary>
/// Stateful <c>hardware-modules</c> fixture: device <c>PLC_1</c> with rack <c>Rack_0</c> holding the CPU
/// (position 1), a DI module (position 2, input bytes 0..1) and a DQ module (position 3, output bytes
/// 0..1), plus PLC software <c>PLC_1</c> with two tag tables. Reads observe earlier writes in the same
/// process. Scripted refusals: a type identifier containing <c>Unpluggable</c> or an occupied position
/// makes the feasibility probe answer false; an attribute value <c>"reject"</c> is reported by the
/// attribute delegate (ignored under policy ignore, <c>engineering_rejected</c> under abort). Scripted shapes of TIA Portal
/// V21 seen live on 2026-11-02: a plugged type identifier containing <c>DeviceLevel</c> lands directly under the device when
/// the container is the rack (K05); one containing <c>SubItemAddresses</c> gets a sub-item of the same name without type
/// identifier that owns its input addresses, start byte 10, length 4 (K06).
/// This models object access only; it is no evidence of Siemens behavior.
/// </summary>
sealed class HardwareModuleScenario
{
    private sealed class Item(string name, string? type, int position)
    {
        public string Name { get; set; } = name;
        /// <summary>Null models a sub-item that reports no type identifier (live 2026-11-02, S7-1500 DI 32).</summary>
        public string? Type { get; set; } = type;
        public int Position { get; set; } = position;
        public List<Item> Items { get; } = new();
        public List<HardwareIoAddressInfo> Addresses { get; } = new();
        /// <summary>The NetworkPort service of a port item (GetServiceInfos lists it); null for every other item.</summary>
        public Siemens.Engineering.HW.Features.NetworkPort? Port { get; set; }
        /// <summary>The NetworkInterface stand-in of an interface item (transfer areas, IO connector); null for every other item.</summary>
        public FakeNetworkInterface? Interface { get; set; }
        /// <summary>Package G: the hardware feature stand-ins the item lists among its services.</summary>
        public List<object> Features { get; } = new();
        public Dictionary<string, (string Type, object? Value, bool Writable)> Attributes { get; } = new(StringComparer.Ordinal)
        {
            ["Author"] = ("System.String", "", true),
            ["Comment"] = ("System.String", "", true),
            ["IsochronousMode"] = ("System.Boolean", false, true),
            ["IsochronousTi"] = ("System.Double", 0.0, true),
            ["TypeName"] = ("System.String", "fixture", false),
        };

        public Item Clone()
        {
            var copy = new Item(Name, Type, Position);
            copy.Items.AddRange(Items.Select(child => child.Clone()));
            copy.Addresses.AddRange(Addresses.Select(address => new HardwareIoAddressInfo
            {
                Index = address.Index, IoType = address.IoType, StartAddress = address.StartAddress, Length = address.Length,
                Context = address.Context, ProcessImage = address.ProcessImage, IsochronousMode = address.IsochronousMode,
            }));
            return copy;
        }
    }

    private const string DeviceName = "PLC_1";
    private readonly List<Item> _deviceItems;
    private readonly List<TagTableInfo> _tables;

    public HardwareModuleScenario()
    {
        var rack = new Item("Rack_0", "System:Rack.S71500", 0);
        var cpu = new Item("PLC_1", "OrderNumber:6ES7 516-3AN01-0AB0/V2.8", 1);
        // PROFINET interface X1 with two port items, each offering the NetworkPort stand-in (issue #10).
        // X1 is also an I-device whose single IO connector is in IO system 100 of subnet PN/IE_1 (issue #11).
        var x1 = new Item("PROFINET interface_1", "System:InterfacePN", 32768) { Interface = new FakeNetworkInterface { ConnectedIoSystem = 100 } };
        x1.Items.Add(new Item("Port_1", "System:PortPN", 32769) { Port = new Siemens.Engineering.HW.Features.NetworkPort() });
        x1.Items.Add(new Item("Port_2", "System:PortPN", 32770) { Port = new Siemens.Engineering.HW.Features.NetworkPort() });
        cpu.Items.Add(x1);
        // Package G: X1 offers NetworkInterface (one node, one IO connector), ImConnection, PcInterfaceAssignment and CommunicationManagement;
        // the DI module offers ModuleDescriptionUpdater and TelecontrolManagement.
        var features = new Siemens.Engineering.HW.Features.NetworkInterface();
        features.IoConnectors.Add(new Siemens.Engineering.HW.IoConnector(new Siemens.Engineering.HW.IoController(cpu)));
        x1.Features.AddRange(new object[] { features, new Siemens.Engineering.HW.Features.ImConnection(), new Siemens.Engineering.HW.Features.PcInterfaceAssignment(), new Siemens.Engineering.HW.Features.CommunicationManagement() });
        var x2 = new Item("PROFINET interface_2", "System:InterfacePN", 33024) { Interface = new FakeNetworkInterface() };
        x2.Features.Add(new Siemens.Engineering.HW.Features.NetworkInterface());
        cpu.Items.Add(x2);
        rack.Items.Add(cpu);
        var di = new Item("DI 16x24VDC_1", "OrderNumber:6ES7 521-1BH00-0AB0/V2.1", 2);
        di.Addresses.Add(new HardwareIoAddressInfo { Index = 0, IoType = "Input", StartAddress = 0, Length = 2, Context = "None", ProcessImage = 0, IsochronousMode = false });
        var dq = new Item("DQ 16x24VDC_1", "OrderNumber:6ES7 522-1BH00-0AB0/V2.1", 3);
        dq.Addresses.Add(new HardwareIoAddressInfo { Index = 0, IoType = "Output", StartAddress = 0, Length = 2, Context = "None", ProcessImage = 0, IsochronousMode = false });
        di.Features.AddRange(new object[] { new Siemens.Engineering.HW.Features.ModuleDescriptionUpdater(), new Siemens.Engineering.HW.Features.TelecontrolManagement() });
        rack.Items.Add(di);
        rack.Items.Add(dq);
        _deviceItems = new List<Item> { rack };
        _tables = new List<TagTableInfo>
        {
            new()
            {
                Name = "Default tag table", FolderPath = "/", IsDefault = true,
                Tags =
                {
                    Tag("Start", "Bool", "%I0.0"), Tag("Stop", "Bool", "%I0.1"), Tag("Valve", "Bool", "%Q0.0"),
                    Tag("Speed", "Int", "%MW10"), Tag("Overlap", "Byte", "%MB11"), Tag("Orphan", "Bool", "%I100.0"), Tag("Symbolic", "Bool", ""),
                },
            },
            new() { Name = "Motors", FolderPath = "/" },
        };
    }

    private static TagInfo Tag(string name, string type, string address)
        => new() { Name = name, DataType = type, LogicalAddress = address, ExternalAccessible = true, ExternalVisible = true, ExternalWritable = false };

    public string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        try
        {
            return request.Method switch
            {
                "read_hardware_config" => Ok(Hardware(request.IncludeIoDetails == true, request.IncludeTagMatches == true)),
                "list_tag_tables" => Ok(new PlcTagInventoryInfo
                {
                    IsComplete = true,
                    Plcs = { new PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = DeviceName, Tables = _tables } },
                }),
                PlcTagMoveMethods.MoveTag => Ok(MoveTag(request.TagMove!)),
                var method when HardwareModuleMethods.IsWorkerMethod(method) => Ok(HardwareModule(method, request.HardwareModule!)),
                _ => Fail(WorkerFailureCategories.ValidationError, $"unexpected hardware-modules method '{request.Method}'"),
            };
        }
        catch (WorkerOperationRefusal refusal)
        {
            return JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false, FailureCategory = refusal.Category, Error = refusal.Message, Warnings = refusal.Warnings,
                EngineeringError = refusal.Category == WorkerFailureCategories.EngineeringRejected
                    ? new EngineeringErrorInfo { Type = "EngineeringTargetInvocationException", Message = refusal.Message }
                    : null,
            }, WorkerJson.Envelope);
        }
    }

    private sealed class WorkerOperationRefusal(string category, string message, List<string>? warnings = null) : Exception(message)
    {
        public string Category { get; } = category;
        public List<string>? Warnings { get; } = warnings;
    }

    private object HardwareModule(string method, HardwareModuleRequestInfo request)
    {
        switch (method)
        {
            case HardwareModuleMethods.ListPlugLocations:
            {
                var (container, identity) = Resolve(request.Target);
                var used = Children(container).Select(child => child.Position).ToHashSet();
                return new HardwarePlugLocationListInfo
                {
                    Target = identity,
                    PlugLocations = Enumerable.Range(1, 8).Where(position => !used.Contains(position))
                        .Select(position => new HardwarePlugLocationInfo { PositionNumber = position, Label = position.ToString() }).ToList(),
                };
            }
            case HardwareModuleMethods.FindModuleTypes:
                return new HardwareModuleTypeListInfo
                {
                    PartialTypeIdentifier = request.TypeIdentifier!,
                    ModuleTypes = { request.TypeIdentifier + "/V2.0", request.TypeIdentifier + "/V2.1" },
                };
            case HardwareModuleMethods.FindContainerTypes:
                return new HardwareContainerTypeListInfo { TypeIdentifier = request.TypeIdentifier!, ContainerTypes = { "System:Rack.S71500" } };
            case HardwareModuleMethods.ListIoAddresses:
            {
                var (item, identity) = Resolve(request.Target);
                var collected = Collect((Item)item!);
                return new HardwareIoAddressListInfo
                {
                    Target = identity, Addresses = collected.Select(entry => entry.Info).ToList(),
                    Messages = collected.Any(entry => entry.Info.SubItemPath.Count > 0)
                        ? new() { "The selected item has no addresses of its own; the addresses of its sub-items are listed with the subItemPath they belong to." }
                        : new(),
                };
            }
            case HardwareModuleMethods.ProbePlug:
            {
                var position = request.PositionNumber!.Value;
                var (container, containerIdentity) = Resolve(request.ProbeOperation == HardwareModuleMethods.ProbeOperations.Plug ? request.Target : request.Destination);
                HardwareObjectIdentityInfo? source = null;
                if (request.ProbeOperation != HardwareModuleMethods.ProbeOperations.Plug) source = Resolve(request.Target).Identity;
                var occupied = Children(container).Any(child => child.Position == position);
                return new HardwarePlugProbeInfo
                {
                    Operation = request.ProbeOperation!, Container = containerIdentity, Source = source, PositionNumber = position,
                    CanPlug = !occupied && request.TypeIdentifier?.Contains("Unpluggable", StringComparison.Ordinal) != true,
                };
            }
            case HardwareModuleMethods.ReadAttributes:
            {
                var (target, identity) = Resolve(request.Target);
                var attributes = Attributes(target);
                var result = new HardwareAttributeReadInfo { Target = identity };
                foreach (var name in request.AttributeNames ?? attributes.Keys.ToList())
                {
                    if (!attributes.TryGetValue(name, out var entry))
                    {
                        result.Attributes.Add(new NetworkAttributeInfo
                        {
                            Name = name, Access = "unknown", Availability = "unknownAttribute",
                            Diagnostic = new NetworkAttributeDiagnosticInfo { Category = "unknown_attribute", Message = "Attribute not found." },
                        });
                        continue;
                    }

                    result.Attributes.Add(new NetworkAttributeInfo
                    {
                        Name = name, Source = "dynamic", Access = entry.Writable ? "readWrite" : "readOnly", SupportedTypes = { entry.Type },
                        Availability = "available", Value = Typed(entry.Value),
                    });
                }

                foreach (var value in request.Attributes ?? new List<HardwareAttributeValueInfo>())
                {
                    var verdict = new HardwareAttributeConversionInfo { Name = value.Name };
                    if (!attributes.TryGetValue(value.Name, out var entry)) verdict.Error = $"The target has no attribute '{value.Name}'.";
                    else if (!entry.Writable) verdict.Error = $"Attribute '{value.Name}' is not writable.";
                    else
                    {
                        var conversion = HardwareAttributeValueConverter.Convert(value, new[] { Type.GetType(entry.Type)! });
                        verdict.Success = conversion.Success;
                        verdict.TargetType = conversion.TargetType?.FullName;
                        verdict.Error = conversion.Error;
                    }

                    result.Conversions.Add(verdict);
                }

                return result;
            }
            case HardwareModuleMethods.PlugDeviceItem:
            {
                var (container, containerIdentity) = Resolve(request.Target);
                var position = request.PositionNumber!.Value;
                if (Children(container).Any(child => child.Position == position) || request.TypeIdentifier!.Contains("Unpluggable", StringComparison.Ordinal))
                    throw new WorkerOperationRefusal(WorkerFailureCategories.ValidationError, "plug_device_item: CanPlugNew returned false. Nothing was changed.");
                var created = new Item(string.IsNullOrEmpty(request.Name) ? $"Module_{position}" : request.Name!, request.TypeIdentifier!, position);
                if (request.TypeIdentifier!.Contains("SubItemAddresses", StringComparison.Ordinal))
                {
                    var sub = new Item(created.Name, null, 1);
                    sub.Addresses.Add(new HardwareIoAddressInfo { Index = 0, IoType = "Input", StartAddress = 10, Length = 4, Context = "None", ProcessImage = 0, IsochronousMode = false });
                    created.Items.Add(sub);
                }
                // Scripted TIA Portal V21 behaviour (live 2026-11-02, K05): a module plugged into a top-level rack item lands directly
                // under the device. Only type identifiers containing 'DeviceLevel' do this, so the other fixtures keep their shape.
                var deviceLevel = container is not null && _deviceItems.Contains(container)
                    && request.TypeIdentifier!.Contains("DeviceLevel", StringComparison.Ordinal);
                (deviceLevel ? _deviceItems : Children(container)).Add(created);
                return new HardwareDeviceItemMutationResultInfo
                {
                    Operation = method, Container = containerIdentity, Result = IdentityOf(created),
                    PlacedUnder = deviceLevel ? Resolve(new HardwareObjectSelectorInfo { DeviceName = DeviceName }).Identity : containerIdentity,
                    Messages = deviceLevel ? new() { $"TIA Portal placed the item directly under device '{DeviceName}' at position {position}." } : new(),
                };
            }
            case HardwareModuleMethods.MoveDeviceItem:
            case HardwareModuleMethods.CopyDeviceItem:
            {
                var (item, sourceIdentity) = Resolve(request.Target);
                var (container, containerIdentity) = Resolve(request.Destination);
                var position = request.PositionNumber!.Value;
                if (Children(container).Any(child => child.Position == position))
                    throw new WorkerOperationRefusal(WorkerFailureCategories.ValidationError, $"{method}: the position is occupied. Nothing was changed.");
                var moving = (Item)item!;
                Item placed;
                if (method == HardwareModuleMethods.MoveDeviceItem)
                {
                    ParentList(moving).Remove(moving);
                    placed = moving;
                }
                else placed = moving.Clone();
                placed.Position = position;
                Children(container).Add(placed);
                return new HardwareDeviceItemMutationResultInfo
                {
                    Operation = method, Container = containerIdentity, Source = sourceIdentity, Result = IdentityOf(placed), PlacedUnder = containerIdentity,
                };
            }
            case HardwareModuleMethods.DeleteDeviceItem:
            {
                var (item, identity) = Resolve(request.Target);
                ParentList((Item)item!).Remove((Item)item!);
                return new HardwareDeviceItemMutationResultInfo { Operation = method, Source = identity };
            }
            case HardwareModuleMethods.ChangeDeviceItemType:
            {
                var (item, identity) = Resolve(request.Target);
                ((Item)item!).Type = request.TypeIdentifier!;
                return new HardwareDeviceItemMutationResultInfo { Operation = method, Source = identity, Result = IdentityOf((Item)item) };
            }
            case HardwareModuleMethods.SetHardwareAttributes:
            {
                var (target, identity) = Resolve(request.Target);
                var attributes = Attributes(target);
                var result = new HardwareAttributeWriteResultInfo
                {
                    Target = identity, AttributeErrorPolicy = request.AttributeErrorPolicy!, AttributeWriteMode = request.AttributeWriteMode!,
                };
                foreach (var value in request.Attributes!)
                {
                    var entry = attributes[value.Name];
                    var conversion = HardwareAttributeValueConverter.Convert(value, new[] { Type.GetType(entry.Type)! });
                    var before = entry.Value;
                    var rejected = value.StringValue == "reject";
                    if (rejected && request.AttributeErrorPolicy == HardwareModuleMethods.AttributeErrorPolicies.Abort)
                        throw new WorkerOperationRefusal(WorkerFailureCategories.EngineeringRejected,
                            $"TIA Portal rejected the operation: set_hardware_attributes: scripted rejection of '{value.Name}'.",
                            result.Attributes.Select(outcome => $"{outcome.Name}: {outcome.Status}").ToList());
                    if (!rejected) attributes[value.Name] = (entry.Type, conversion.Value, entry.Writable);
                    result.Attributes.Add(new HardwareAttributeWriteOutcomeInfo
                    {
                        Name = value.Name, Requested = Typed(conversion.Value), TargetType = entry.Type, Before = Typed(before),
                        After = Typed(attributes[value.Name].Value), Status = rejected ? "ignored" : "applied",
                        Message = rejected ? "AttributeDelegate: scripted rejection" : null,
                    });
                }

                return result;
            }
            case HardwareModuleMethods.SetIoAddress:
            {
                var (item, identity) = Resolve(request.Target);
                var module = (Item)item!;
                var collected = Collect(module);
                var before = collected.Select(entry => entry.Info).ToList();
                var matches = collected.Where(entry => entry.Info.IoType == request.Address!.IoType && entry.Info.StartAddress == request.Address.StartAddress
                    && (request.Address.Length is null || entry.Info.Length == request.Address.Length)).ToArray();
                if (matches.Length != 1)
                    throw new WorkerOperationRefusal(matches.Length == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.ValidationError,
                        $"set_io_address: {matches.Length} matching addresses.");
                var address = matches[0].Address;
                var changes = request.AddressChanges!;
                address.StartAddress = changes.StartAddress ?? address.StartAddress;
                address.ProcessImage = changes.ProcessImage ?? address.ProcessImage;
                address.IsochronousMode = changes.IsochronousMode ?? address.IsochronousMode;
                return new HardwareIoAddressWriteResultInfo
                {
                    Target = identity, Before = before, After = Collect(module).Select(entry => entry.Info).ToList(), ChangedIndexes = { matches[0].Info.Index },
                };
            }
            case HardwareModuleMethods.GetCustomIdentity:
            case HardwareModuleMethods.SetCustomIdentity:
            case HardwareModuleMethods.RemoveCustomIdentity:
                return CustomIdentity(method, request);
            case var feature when HardwareFeatureMethods.IsFeature(feature):
            {
                var (item, identity) = Resolve(request.Target);
                var partner = request.Partner is null ? null : Resolve(request.Partner).Item;
                var context = new HardwareFeatureContext
                {
                    Target = (object?)item ?? DeviceName,
                    TargetIdentity = identity,
                    Partner = partner,
                    ServiceTypes = owner => owner is Item withFeatures ? withFeatures.Features.Select(f => f.GetType()).ToList() : Array.Empty<Type>(),
                    Service = (owner, type) => owner is Item withFeatures ? withFeatures.Features.FirstOrDefault(f => f.GetType() == type) : null,
                    Describe = value => value is Item described ? $"device item '{described.Name}'" : value?.ToString() ?? "(none)",
                };
                try
                {
                    return HardwareFeatureRun.Run(feature, context, request);
                }
                catch (TiaMcpServer.Contracts.Shared.RuntimeBindingException exception)
                {
                    throw new WorkerOperationRefusal(exception.Category, exception.Message);
                }
            }
            case HardwareModuleMethods.ListPortConnections:
            {
                var (item, identity) = Resolve(request.Target);
                var result = new HardwarePortListInfo { Target = identity };
                foreach (var port in (item is null ? _deviceItems : new List<Item> { item }).SelectMany(root => All(new[] { root })).Where(entry => entry.Port is not null))
                {
                    var (partners, _) = PortTopologyRun.Partners(port.Port!, _ => false);
                    result.Ports.Add(new HardwarePortInfo
                    {
                        Port = IdentityOf(port),
                        InterfaceOwner = IdentityOf(ParentOf(port)),
                        ConnectedPorts = partners!.Select(partner => new HardwarePortPartnerInfo
                        {
                            Port = All(_deviceItems).Where(candidate => Equals(candidate.Port, partner)).Select(IdentityOf).FirstOrDefault(),
                            RuntimeType = partner.GetType().FullName!,
                        }).ToList(),
                    });
                }

                if (result.Ports.Count == 0) result.Messages.Add($"The target lists no {NetworkPortNames.Service}.");
                return result;
            }
            case HardwareModuleMethods.ListTransferAreas:
            case HardwareModuleMethods.CreateTransferArea:
            case HardwareModuleMethods.UpdateTransferArea:
            case HardwareModuleMethods.DeleteTransferArea:
            case HardwareModuleMethods.DeleteIoSystem:
            case HardwareModuleMethods.DisconnectFromIoSystem:
                try
                {
                    return NetworkSystems(method, request);
                }
                catch (InvalidOperationException exception)
                {
                    throw new WorkerOperationRefusal(WorkerFailureCategories.EngineeringRejected, $"TIA Portal rejected the operation: {exception.Message}");
                }
                catch (TiaMcpServer.Contracts.Shared.RuntimeBindingException exception)
                {
                    throw new WorkerOperationRefusal(exception.Category, exception.Message);
                }
            case HardwareModuleMethods.ExportCax:
                return ExportCax(request);
            case HardwareModuleMethods.ImportCax:
                return ImportCax(request);
            case HardwareModuleMethods.ConnectPorts:
            case HardwareModuleMethods.DisconnectPorts:
            {
                var (item, identity) = Resolve(request.Target);
                var (partner, partnerIdentity) = Resolve(request.Partner);
                var port = item?.Port ?? throw new WorkerOperationRefusal(WorkerFailureCategories.UnsupportedCapability,
                    TiaMcpServer.Contracts.Shared.RuntimeServiceValues.NotListed($"device item '{identity.Name}'", NetworkPortNames.Service) + " Nothing was changed.");
                var partnerPort = partner?.Port ?? throw new WorkerOperationRefusal(WorkerFailureCategories.UnsupportedCapability,
                    TiaMcpServer.Contracts.Shared.RuntimeServiceValues.NotListed($"device item '{partnerIdentity.Name}'", NetworkPortNames.Service) + " Nothing was changed.");
                var result = new HardwarePortConnectionResultInfo { Operation = method, Port = identity, Partner = partnerIdentity };
                try
                {
                    PortTopologyRun.Run(port, partnerPort, method == HardwareModuleMethods.ConnectPorts, result, _ => false);
                }
                catch (InvalidOperationException exception)
                {
                    throw new WorkerOperationRefusal(WorkerFailureCategories.EngineeringRejected, $"TIA Portal rejected the operation: {exception.Message}");
                }
                catch (TiaMcpServer.Contracts.Shared.RuntimeBindingException exception)
                {
                    throw new WorkerOperationRefusal(exception.Category, exception.Message);
                }

                return result;
            }
            default:
                throw new WorkerOperationRefusal(WorkerFailureCategories.ValidationError, $"unexpected hardware-module method '{method}'");
        }
    }

    // IO system 100 of subnet PN/IE_1 with its IO controller and connected devices (issue #11).
    private const string SubnetId = "subnet-pn-1";
    private readonly List<(string Name, int Number, List<string> Devices)> _ioSystems = new() { ("PROFINET IO-System", 100, new List<string> { DeviceName }) };

    private object NetworkSystems(string method, HardwareModuleRequestInfo request)
    {
        if (method == HardwareModuleMethods.DeleteIoSystem)
        {
            var selector = request.IoSystem!;
            var index = selector.SubnetId == SubnetId ? _ioSystems.FindIndex(system => system.Number == selector.Number) : -1;
            if (index < 0) throw new WorkerOperationRefusal(WorkerFailureCategories.TargetNotFound, $"No IO system with number {selector.Number} exists on subnet '{selector.SubnetId}'.");
            var deleted = _ioSystems[index];
            _ioSystems.RemoveAt(index);
            foreach (var item in All(_deviceItems).Where(item => item.Interface?.ConnectedIoSystem == deleted.Number)) item.Interface!.ConnectedIoSystem = null;
            return new HardwareIoSystemDeleteResultInfo { SubnetId = SubnetId, Number = deleted.Number, Name = deleted.Name, ConnectedIoDeviceCount = deleted.Devices.Count };
        }

        var (owner, identity) = Resolve(request.Target);
        var networkInterface = owner?.Interface ?? throw new WorkerOperationRefusal(WorkerFailureCategories.UnsupportedCapability,
            $"device item '{identity.Name}' offers no NetworkInterface service. Nothing was changed.");
        if (method == HardwareModuleMethods.DisconnectFromIoSystem)
        {
            if (networkInterface.ConnectedIoSystem is not { } number) throw new InvalidOperationException("The IO connector is not connected to an IO system.");
            networkInterface.ConnectedIoSystem = null;
            var system = _ioSystems.Single(entry => entry.Number == number);
            system.Devices.Remove(DeviceName);
            return new HardwareIoConnectorDisconnectResultInfo { Target = identity, ConnectorIndex = 0, IoSystemName = system.Name, IoSystemNumber = number, DisconnectedAfter = true };
        }

        if (method == HardwareModuleMethods.ListTransferAreas)
            return new HardwareTransferAreaListInfo
            {
                Target = identity,
                TransferAreas = Areas(networkInterface, NetworkSystemNames.TransferAreaCollections.TransferAreas),
                MulticastableTransferAreas = Areas(networkInterface, NetworkSystemNames.TransferAreaCollections.Multicastable),
            };

        var area = request.TransferArea!;
        var (composition, _) = TransferAreaRun.Composition(networkInterface, area.Collection);
        var result = new HardwareTransferAreaWriteResultInfo { Operation = method, Target = identity, Collection = area.Collection };
        if (method == HardwareModuleMethods.CreateTransferArea)
        {
            if (!Enum.TryParse<Siemens.Engineering.HW.TransferAreaType>(area.Type, out var type))
                throw new WorkerOperationRefusal(WorkerFailureCategories.ValidationError, $"transferArea.type '{area.Type}' is not a member of the stand-in TransferAreaType.");
            object? partner = null;
            if (request.Partner is not null)
            {
                var (partnerItem, partnerIdentity) = Resolve(request.Partner);
                result.Partner = partnerIdentity;
                var partnerInterface = partnerItem?.Interface ?? throw new WorkerOperationRefusal(WorkerFailureCategories.UnsupportedCapability, "The partner offers no NetworkInterface service.");
                partner = area.TemplateIndex is { } template ? partnerInterface.MulticastableTransferAreas.ElementAt(template) : partnerInterface;
            }

            var (created, index, before, after) = TransferAreaRun.Create(composition!, area, type, partner);
            result.CountBefore = before;
            result.CountAfter = after;
            result.After = TransferAreaRun.Describe(created, area.Collection, index, _ => false, AreaAttributes);
            return result;
        }

        var elements = TransferAreaRun.Elements(composition!, area.Collection);
        var selected = TransferAreaRun.Select(elements, area.Collection, area.Index!.Value, area.Name!,
            candidate => TiaMcpServer.Contracts.Shared.RuntimeServiceValues.ReadProperty(candidate, "Name", _ => false).Value);
        result.Before = TransferAreaRun.Describe(selected, area.Collection, area.Index.Value, _ => false, AreaAttributes);
        if (method == HardwareModuleMethods.DeleteTransferArea)
        {
            (result.CountBefore, result.CountAfter) = TransferAreaRun.Delete(composition!, area.Collection, selected);
            return result;
        }

        result.CountBefore = result.CountAfter = elements.Count;
        foreach (var value in request.Attributes!)
        {
            var property = selected.GetType().GetProperty(value.Name) ?? throw new WorkerOperationRefusal(WorkerFailureCategories.ValidationError, $"The area has no attribute '{value.Name}'.");
            var before = property.GetValue(selected);
            var conversion = HardwareAttributeValueConverter.Convert(value, new[] { property.PropertyType });
            if (!conversion.Success) throw new WorkerOperationRefusal(WorkerFailureCategories.ValidationError, conversion.Error!);
            property.SetValue(selected, conversion.Value);
            result.Attributes.Add(new HardwareAttributeWriteOutcomeInfo
            {
                Name = value.Name, Requested = Typed(conversion.Value), TargetType = property.PropertyType.FullName!, Before = Typed(before),
                After = Typed(property.GetValue(selected)), Status = "applied",
            });
        }

        result.After = TransferAreaRun.Describe(selected, area.Collection, area.Index.Value, _ => false, AreaAttributes);
        return result;
    }

    private static List<HardwareTransferAreaInfo> Areas(FakeNetworkInterface networkInterface, string collection)
    {
        var (composition, _) = TransferAreaRun.Composition(networkInterface, collection);
        return TransferAreaRun.Elements(composition!, collection).Select((area, index) => TransferAreaRun.Describe(area, collection, index, _ => false, AreaAttributes)).ToList();
    }

    private static List<TransferAreaAttributeInfo> AreaAttributes(object area)
        => area.GetType().GetProperties().Where(property => property.CanWrite && property.PropertyType != typeof(object))
            .Select(property => new TransferAreaAttributeInfo { Name = property.Name, AccessMode = "ReadWrite" }).ToList();

    // CAx (issue #9): the export writes a small AutomationML stand-in; an import of a file containing "Fail" is rejected
    // (TransferResult Error), every other import adds one device named after the file.
    private readonly List<string> _importedDevices = new();

    private object ExportCax(HardwareModuleRequestInfo request)
    {
        var path = request.FilePath!;
        if (File.Exists(path) && request.Overwrite != true)
            throw new WorkerOperationRefusal(WorkerFailureCategories.ValidationError, $"export_cax: '{path}' already exists. Nothing was changed.");
        RefuseExistingLog(request, "export_cax");
        if (request.DeviceName is { } device && !string.Equals(device, DeviceName, StringComparison.OrdinalIgnoreCase))
            throw new WorkerOperationRefusal(WorkerFailureCategories.TargetNotFound, $"No device named '{device}' was found.");
        File.WriteAllText(path, $"<CAEXFile FileName=\"{Path.GetFileName(path)}\"><InstanceHierarchy Name=\"{request.DeviceName ?? "project"}\"/></CAEXFile>");
        var result = new HardwareCaxExportResultInfo
        {
            Scope = request.DeviceName is null ? CaxNames.Scopes.Project : CaxNames.Scopes.Device,
            DeviceName = request.DeviceName is null ? null : DeviceName,
            Overload = request.LogFilePath is null ? "CaxProvider.Export(ProjectBase, FileInfo)" : "CaxProvider.Export(ProjectBase, FileInfo, FileInfo)",
        };
        if (request.LogFilePath is { } log)
        {
            File.WriteAllText(log, "Export finished.");
            result.Succeeded = true;
            result.Log = CaxRun.File(log);
        }
        else result.Transfer = CaxRun.Read(new Siemens.Engineering.Cax.TransferResult(Siemens.Engineering.Cax.TransferResultState.Success,
            new Siemens.Engineering.Cax.TransferResultMessage("Export finished.", Siemens.Engineering.Cax.TransferResultState.Information)), _ => false);
        result.File = CaxRun.File(path);
        return result;
    }

    /// <summary>Like the worker (security review F4): an existing log file is replaced only with overwrite:true.</summary>
    private static void RefuseExistingLog(HardwareModuleRequestInfo request, string operation)
    {
        if (request.LogFilePath is { } log && TiaMcpServer.Contracts.Plc.PlcFileTargets.WriteTargetError(log, request.Overwrite == true) is { } error)
            throw new WorkerOperationRefusal(WorkerFailureCategories.ValidationError, $"{operation}: logFilePath {error}");
    }

    private object ImportCax(HardwareModuleRequestInfo request)
    {
        var path = request.FilePath!;
        if (!File.Exists(path)) throw new WorkerOperationRefusal(WorkerFailureCategories.TargetNotFound, $"import_cax: '{path}' does not exist.");
        RefuseExistingLog(request, "import_cax");
        var before = new[] { DeviceName }.Concat(_importedDevices).ToList();
        var failed = File.ReadAllText(path).Contains("Fail", StringComparison.Ordinal);
        var transfer = CaxRun.Read(failed
            ? new Siemens.Engineering.Cax.TransferResult(Siemens.Engineering.Cax.TransferResultState.Error,
                new Siemens.Engineering.Cax.TransferResultMessage("The AML file is not valid.", Siemens.Engineering.Cax.TransferResultState.Error))
            : new Siemens.Engineering.Cax.TransferResult(Siemens.Engineering.Cax.TransferResultState.Success), _ => false);
        if (failed)
            throw new WorkerOperationRefusal(WorkerFailureCategories.EngineeringRejected, $"TIA Portal rejected the operation: import_cax: {CaxRun.Summary(transfer)}");
        _importedDevices.Add(Path.GetFileNameWithoutExtension(path));
        var after = new[] { DeviceName }.Concat(_importedDevices).ToList();
        var imported = new HardwareCaxImportResultInfo
        {
            FilePath = path, ImportOption = request.CaxImportOption!, Overload = "CaxProvider.Import(FileInfo, CaxImportOptions)", Transfer = transfer,
            DevicesBefore = before, DevicesAfter = after, AddedDevices = CaxRun.Added(before, after), RemovedDevices = CaxRun.Added(after, before),
        };
        if (request.LogFilePath is { } log)
        {
            // The log overload returns only true/false; the details are in the log.
            File.WriteAllText(log, "Import finished.");
            imported.Overload = "CaxProvider.Import(FileInfo, FileInfo, CaxImportOptions)";
            imported.Transfer = null;
            imported.Succeeded = true;
            imported.Log = CaxRun.File(log);
        }

        return imported;
    }

    // One stand-in CustomIdentityProvider per hardware object; the DQ module lists none (unsupported_capability).
    private readonly Dictionary<object, Siemens.Engineering.CustomIdentity.CustomIdentityProvider> _identities = new();
    private readonly object _deviceKey = new();

    private object CustomIdentity(string method, HardwareModuleRequestInfo request)
    {
        var (item, identity) = Resolve(request.Target);
        var key = request.CustomIdentityKey ?? throw new WorkerOperationRefusal(WorkerFailureCategories.ValidationError, "A custom identity key is required.");
        var description = item is null ? $"Device '{identity.DeviceName}'" : $"Device item '{identity.Name}' of '{identity.DeviceName}'";
        var offered = item?.Name == "DQ 16x24VDC_1" ? new List<Type>() : new List<Type> { typeof(Siemens.Engineering.CustomIdentity.CustomIdentityProvider) };
        var anchor = (object?)item ?? _deviceKey;
        try
        {
            var (provider, _) = TiaMcpServer.Contracts.Shared.RuntimeServiceValues.Find(offered, _ =>
            {
                if (!_identities.TryGetValue(anchor, out var existing)) _identities[anchor] = existing = new Siemens.Engineering.CustomIdentity.CustomIdentityProvider();
                return existing;
            }, CustomIdentityNames.Provider);
            if (provider is null)
                throw new WorkerOperationRefusal(WorkerFailureCategories.UnsupportedCapability,
                    TiaMcpServer.Contracts.Shared.RuntimeServiceValues.NotListed(description, CustomIdentityNames.Provider) + " Nothing was changed.");
            var type = provider.GetType().FullName!;
            if (method == HardwareModuleMethods.GetCustomIdentity)
                return new HardwareCustomIdentityReadInfo { Target = identity, Key = key, Observed = CustomIdentityRun.Get(provider, key), ProviderType = type };
            var (before, after) = method == HardwareModuleMethods.SetCustomIdentity
                ? CustomIdentityRun.Set(provider, key, request.CustomIdentityValue!, description)
                : CustomIdentityRun.Remove(provider, key, description);
            return new HardwareCustomIdentityWriteResultInfo
            {
                Operation = method, Target = identity, Key = key, RequestedValue = method == HardwareModuleMethods.SetCustomIdentity ? request.CustomIdentityValue : null,
                Before = before, After = after, ProviderType = type,
            };
        }
        catch (TiaMcpServer.Contracts.Shared.RuntimeBindingException exception)
        {
            throw new WorkerOperationRefusal(exception.Category, exception.Message);
        }
    }

    /// <summary>The item's own addresses, or (none of its own) its sub-items' depth-first, as the worker lists them (K06).</summary>
    private static List<(HardwareIoAddressInfo Address, HardwareIoAddressInfo Info)> Collect(Item item)
    {
        var result = new List<(HardwareIoAddressInfo, HardwareIoAddressInfo)>();
        void Add(Item owner, List<HardwareSubItemSegmentInfo> path)
        {
            foreach (var address in owner.Addresses)
            {
                var info = Copy(address);
                info.Index = result.Count;
                info.SubItemPath = path.Select(segment => new HardwareSubItemSegmentInfo
                { Index = segment.Index, Name = segment.Name, PositionNumber = segment.PositionNumber, TypeIdentifier = segment.TypeIdentifier }).ToList();
                result.Add((address, info));
            }
        }

        void Walk(Item parent, List<HardwareSubItemSegmentInfo> parentPath)
        {
            for (var index = 0; index < parent.Items.Count; index++)
            {
                var child = parent.Items[index];
                var path = parentPath.Append(new HardwareSubItemSegmentInfo { Index = index, Name = child.Name, PositionNumber = child.Position, TypeIdentifier = child.Type }).ToList();
                Add(child, path);
                Walk(child, path);
            }
        }

        Add(item, new List<HardwareSubItemSegmentInfo>());
        if (result.Count == 0) Walk(item, new List<HardwareSubItemSegmentInfo>());
        return result;
    }

    private PlcTagMoveResultInfo MoveTag(PlcTagMoveRequestInfo move)
    {
        var source = _tables.Single(table => string.Equals(table.Name, move.TableName, StringComparison.OrdinalIgnoreCase));
        var target = _tables.Single(table => string.Equals(table.Name, move.TargetTableName, StringComparison.OrdinalIgnoreCase));
        var tag = source.Tags.Single(entry => string.Equals(entry.Name, move.Name, StringComparison.OrdinalIgnoreCase));
        source.Tags.Remove(tag);
        target.Tags.Add(tag);
        return new PlcTagMoveResultInfo
        {
            PlcName = "PLC_1", TagName = tag.Name, SourceTableName = source.Name, SourceFolderPath = "/", TargetTableName = target.Name,
            TargetFolderPath = "/", DataType = tag.DataType, LogicalAddress = tag.LogicalAddress, ExternalAccessible = tag.ExternalAccessible,
            ExternalVisible = tag.ExternalVisible, ExternalWritable = tag.ExternalWritable, TemporaryName = move.TemporaryName,
            Steps = { "created temporary tag", "copied flags", "the source comment is empty in every language; nothing to copy", "deleted source", "renamed", "read back" },
            NotCopied = { "Any attribute other than DataTypeName, LogicalAddress, ExternalAccessible, ExternalVisible, ExternalWritable and Comment." },
        };
    }

    // ------------------------------------------------------------------

    private HardwareConfigInfo Hardware(bool ioDetails, bool tagMatches)
    {
        var device = new DeviceInfo { Name = DeviceName, TypeIdentifier = "System:Device.S71500" };
        for (var index = 0; index < _deviceItems.Count; index++)
            device.Items.Add(Project(_deviceItems[index], new List<DeviceItemPathSegmentInfo>(), index, ioDetails, tagMatches));
        var subnet = new SubnetInfo
        {
            Name = "PN/IE_1", SubnetId = SubnetId, NetworkType = "Ethernet", Selectable = true,
            Selector = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.Subnet, SubnetId = SubnetId },
        };
        for (var index = 0; index < _ioSystems.Count; index++)
            subnet.IoSystems.Add(new IoSystemInfo
            {
                Name = _ioSystems[index].Name, Number = _ioSystems[index].Number, IoControllerName = DeviceName, Selectable = true,
                Selector = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.IoSystem, SubnetId = SubnetId, Number = _ioSystems[index].Number, IoSystemIndex = index, IoSystemName = _ioSystems[index].Name },
                ConnectedDeviceNames = _ioSystems[index].Devices.ToList(),
            });
        return new HardwareConfigInfo
        {
            DiscoveryEvidence = new HardwareDiscoveryEvidenceInfo { Scope = "project", Complete = true },
            RootDeviceCount = 1,
            Devices = { device },
            Subnets = { subnet },
        };
    }

    private DeviceItemInfo Project(Item item, List<DeviceItemPathSegmentInfo> parent, int index, bool ioDetails, bool tagMatches)
    {
        var path = parent.Append(new DeviceItemPathSegmentInfo { Index = index, Name = item.Name, PositionNumber = item.Position, TypeIdentifier = item.Type ?? string.Empty }).ToList();
        var info = item.Type is null
            ? new DeviceItemInfo
            {
                Name = item.Name, PositionNumber = item.Position, Selectable = false,
                SelectorDiagnostics = { "Could not read device item type identifier." },
            }
            : new DeviceItemInfo
            {
                Name = item.Name, TypeIdentifier = item.Type, PositionNumber = item.Position, Selectable = true,
                Selector = new NetworkObjectSelectorInfo { Kind = NetworkObjectKinds.DeviceItem, DeviceName = DeviceName, ItemPath = path },
            };
        if (ioDetails)
        {
            info.IoDetails = new DeviceItemIoDetailsInfo();
            foreach (var address in item.Addresses)
            {
                info.IoDetails.Addresses.Add(new IoAddressInfo
                {
                    IoType = address.IoType, StartAddress = address.StartAddress, Length = address.Length, Context = address.Context,
                    ControllerNames = { DeviceName },
                });
                for (var bit = 0; bit < (address.Length ?? 0) * 8; bit++)
                {
                    var absolute = (address.StartAddress ?? 0) * 8 + bit;
                    var area = address.IoType == "Input" ? "I" : "Q";
                    var logical = $"%{area}{absolute / 8}.{absolute % 8}";
                    var channel = new IoChannelInfo
                    {
                        Number = bit, IoType = address.IoType, Type = "Digital", ChannelAddressBits = absolute, ChannelWidthBits = 1, LogicalAddress = logical,
                    };
                    if (tagMatches)
                        foreach (var table in _tables)
                            foreach (var tag in table.Tags.Where(tag => tag.LogicalAddress == logical))
                                channel.TagMatches.Add(new IoTagMatchInfo { Name = tag.Name, DataType = tag.DataType, LogicalAddress = tag.LogicalAddress, TableName = table.Name, FolderPath = table.FolderPath });
                    info.IoDetails.Channels.Add(channel);
                }
            }
        }

        for (var childIndex = 0; childIndex < item.Items.Count; childIndex++)
            info.Items.Add(Project(item.Items[childIndex], path, childIndex, ioDetails, tagMatches));
        return info;
    }

    /// <summary>Resolves a device (null path, returns null item) or the exact indexed item path, as the worker does.</summary>
    private (Item? Item, HardwareObjectIdentityInfo Identity) Resolve(HardwareObjectSelectorInfo? selector)
    {
        if (selector is null || !string.Equals(selector.DeviceName, DeviceName, StringComparison.OrdinalIgnoreCase))
            throw new WorkerOperationRefusal(WorkerFailureCategories.TargetNotFound, $"No device named '{selector?.DeviceName}' was found.");
        if (selector.ItemPath is null) return (null, new HardwareObjectIdentityInfo { DeviceName = DeviceName, Name = DeviceName, TypeIdentifier = "System:Device.S71500" });
        var siblings = _deviceItems;
        Item? current = null;
        foreach (var segment in selector.ItemPath)
        {
            if (segment.Index < 0 || segment.Index >= siblings.Count) throw new WorkerOperationRefusal(WorkerFailureCategories.TargetNotFound, "No item at that index.");
            current = siblings[segment.Index];
            // K77: a null type identifier (integrated interfaces and ports) is the "" segment, as in the worker's resolver.
            if (current.Name != segment.Name || current.Position != segment.PositionNumber || (current.Type ?? string.Empty) != (segment.TypeIdentifier ?? string.Empty))
                throw new WorkerOperationRefusal(WorkerFailureCategories.TargetEvidenceMismatch, "The item path evidence does not match.");
            siblings = current.Items;
        }

        return (current, IdentityOf(current!));
    }

    private List<Item> Children(Item? container) => container?.Items ?? _deviceItems;

    private Item ParentOf(Item item) => All(_deviceItems).Single(candidate => candidate.Items.Contains(item));

    private List<Item> ParentList(Item item)
    {
        if (_deviceItems.Contains(item)) return _deviceItems;
        return All(_deviceItems).Single(candidate => candidate.Items.Contains(item)).Items;
    }

    private static IEnumerable<Item> All(IEnumerable<Item> items) => items.SelectMany(item => All(item.Items).Prepend(item));

    private HardwareObjectIdentityInfo IdentityOf(Item item)
    {
        var chain = new List<Item> { item };
        while (!_deviceItems.Contains(chain[0])) chain.Insert(0, All(_deviceItems).Single(candidate => candidate.Items.Contains(chain[0])));
        var path = new List<DeviceItemPathSegmentInfo>();
        var siblings = _deviceItems;
        foreach (var level in chain)
        {
            path.Add(new DeviceItemPathSegmentInfo { Index = siblings.IndexOf(level), Name = level.Name, PositionNumber = level.Position, TypeIdentifier = level.Type ?? string.Empty });
            siblings = level.Items;
        }

        return new HardwareObjectIdentityInfo { DeviceName = DeviceName, ItemPath = path, Name = item.Name, TypeIdentifier = item.Type, PositionNumber = item.Position };
    }

    private readonly Dictionary<string, (string Type, object? Value, bool Writable)> _deviceAttributes = new(StringComparer.Ordinal)
    {
        ["Author"] = ("System.String", "", true),
        ["Comment"] = ("System.String", "", true),
    };

    private Dictionary<string, (string Type, object? Value, bool Writable)> Attributes(Item? item) => item?.Attributes ?? _deviceAttributes;

    private static NetworkAttributeValueInfo Typed(object? value) => value switch
    {
        null => new NetworkAttributeValueInfo { Kind = "null" },
        string text => new NetworkAttributeValueInfo { Kind = "string", Value = text, TypeName = "System.String" },
        bool flag => new NetworkAttributeValueInfo { Kind = "boolean", Value = flag, TypeName = "System.Boolean" },
        double number => new NetworkAttributeValueInfo { Kind = "number", Value = number, TypeName = "System.Double" },
        _ => new NetworkAttributeValueInfo { Kind = "string", Value = value.ToString(), TypeName = value.GetType().FullName },
    };

    private static HardwareIoAddressInfo Copy(HardwareIoAddressInfo address) => new()
    {
        Index = address.Index, SubItemPath = address.SubItemPath.ToList(), IoType = address.IoType, StartAddress = address.StartAddress, Length = address.Length, Context = address.Context,
        ProcessImage = address.ProcessImage, IsochronousMode = address.IsochronousMode, InterruptObNumber = address.InterruptObNumber,
    };

    private static string Ok<T>(T payload) => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(payload) }, WorkerJson.Envelope);

    private static string Fail(string category, string message)
        => JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = category, Error = message }, WorkerJson.Envelope);
}
