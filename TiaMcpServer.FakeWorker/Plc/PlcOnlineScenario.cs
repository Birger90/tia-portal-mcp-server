using System.Security;
using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// Stateful <c>plc-online-roundtrip</c> fixture for the compare, online, station-upload, watch/force
/// table and update_program operations. PLC_1 (Station_1) is offline and offers PN/IE with PLCSIM #1
/// (target "1 X1") and a hardware NIC (targets "1 X1", "1 X2"); PLC_2 (Station_2) is online; PLC_3
/// (Station_3) has no online provider. Going online over the NIC target "1 X2" ends NotReachable.
/// Callback configurations are stand-ins of the documented public types and are answered by the
/// real <see cref="PlcCallbackRecorder"/> and <see cref="PlcCallbackReflectionAccess"/>: station upload
/// always presents <c>UploadMissingProducts</c>; going online presents <c>OnlinePasswordConfiguration</c>
/// when a password entry for it is supplied (environment variable <c>FAKE_PLC_PASSWORD</c> is set,
/// any other is unset). Compare trees go through the real <see cref="PlcOnlinePolicy.ProjectTree{T}"/>.
/// Scenario <c>plc-online-tls</c> first presents the TLS verification (<see cref="FakeTlsCertificates"/>, a leaf
/// and a CA certificate) through the online legitimation of go_online, station upload and compare_to_online,
/// answered only from the request's trusted fingerprints, like the real worker.
/// </summary>
sealed class PlcOnlineScenario
{
    public const string HardwareInterface = "Intel(R) Ethernet Connection";
    public const string PasswordVariable = "FAKE_PLC_PASSWORD";

    private sealed record Node(string Left, string Right, string State, string Detail, params Node[] Children);

    private sealed record Plc(string Software, string Device, bool Online, string State);

    private readonly bool _raiseConfirmation;
    private readonly bool _presentTls;

    /// <param name="raiseConfirmation">
    /// Scenario <c>plc-online-confirmation</c>: TIA Portal raises one confirmation dialog during every go online,
    /// go offline, station upload and compare_to_online, answered as the request's policy says (compare: No).
    /// </param>
    /// <param name="presentTls">Scenario <c>plc-online-tls</c>: every connecting action first presents the TLS verification.</param>
    public PlcOnlineScenario(bool raiseConfirmation = false, bool presentTls = false)
    {
        _raiseConfirmation = raiseConfirmation;
        _presentTls = presentTls;
    }

    /// <summary>The evidence of one action's confirmation scope, as the worker's log would close it.</summary>
    private PlcDownloadPortalConfirmationLog Confirmations(string? policy)
    {
        var log = new PlcDownloadPortalConfirmationLog();
        if (_raiseConfirmation)
            log.Record("Siemens.Engineering.ConfirmationEventArgs", "Do you want to continue?", PlcDownloadPolicy.PortalConfirmationPolicies.AnswerFor(policy));
        return log;
    }

    private static T Close<T>(T result, PlcDownloadPortalConfirmationLog log) where T : class
    {
        log.CloseInto((entries, observed, complete) =>
        {
            if (result is PlcOnlineActionResultInfo action)
            {
                action.PortalConfirmations = entries;
                action.PortalConfirmationsObserved = observed;
                action.PortalConfirmationsComplete = complete;
            }
            else if (result is PlcStationUploadResultInfo upload)
            {
                upload.PortalConfirmations = entries;
                upload.PortalConfirmationsObserved = observed;
                upload.PortalConfirmationsComplete = complete;
            }
        });
        return result;
    }

    private readonly List<Plc> _plcs = new()
    {
        new("PLC_1", "Station_1", true, PlcOnlinePolicy.States.Offline),
        new("PLC_2", "Station_2", true, PlcOnlinePolicy.States.Online),
        new("PLC_3", "Station_3", false, PlcOnlinePolicy.States.Offline),
    };

    private readonly List<string> _groups = new() { "/Line" };
    private readonly List<(string Group, string Name)> _watchTables = new() { ("/", "Watch_1"), ("/Line", "Watch_Line") };
    private readonly List<(string Group, string Name)> _forceTables = new() { ("/", "Force_1") };
    private readonly Dictionary<string, string> _content = new(StringComparer.OrdinalIgnoreCase)
    {
        ["watch:/Watch_1"] = "<Document><SW.WatchAndForceTables.PlcWatchTable><AttributeList><Name>Watch_1</Name></AttributeList></SW.WatchAndForceTables.PlcWatchTable></Document>",
        ["force:/Force_1"] = "<Document><SW.WatchAndForceTables.PlcForceTable><AttributeList><Name>Force_1</Name></AttributeList></SW.WatchAndForceTables.PlcForceTable></Document>",
    };

    public string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        try
        {
            return request.Method switch
            {
                "read_hardware_config" => Ok(WorkerJson.SerializePayload(new HardwareConfigInfo())),
                "list_tag_tables" => Ok(WorkerJson.SerializePayload(new PlcTagInventoryInfo
                {
                    Plcs = _plcs.Select(p => new PlcTagInventoryPlcInfo { PlcName = p.Software, DeviceName = p.Device }).ToList(),
                })),
                PlcOnlineMethods.GetOnlineState => Ok(WorkerJson.SerializePayload(OnlineState(request.PlcName))),
                PlcOnlineMethods.ListStationUploadInterfaces => Ok(WorkerJson.SerializePayload(new PlcStationUploadInterfacesInfo
                {
                    Available = true,
                    Modes = { new PlcConnectionModeInfo { Name = PlcOnlinePolicy.DefaultMode, PcInterfaces =
                        { new PlcConnectionPcInterfaceInfo { Name = "PLCSIM", Number = 1 }, new PlcConnectionPcInterfaceInfo { Name = HardwareInterface, Number = 1 } } } },
                })),
                PlcOnlineMethods.CompareToOnline when _raiseConfirmation => throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    "TIA Portal raised 1 confirmation dialog(s) during compare_to_online and each was answered No "
                    + "(Siemens.Engineering.ConfirmationEventArgs: Do you want to continue?), so the compare result was discarded."),
                PlcOnlineMethods.CompareToOnline => Ok(WorkerJson.SerializePayload(Compare(request, online: true))),
                PlcOnlineMethods.CompareSoftware => Ok(WorkerJson.SerializePayload(Compare(request, online: false))),
                PlcOnlineMethods.CompareHardware => Ok(WorkerJson.SerializePayload(Compare(request, online: false, hardware: true))),
                PlcOnlineMethods.CompareHardwareToOnline => Ok(WorkerJson.SerializePayload(Compare(request, online: true, hardware: true))),
                PlcOnlineMethods.GoOnline => GoOnline(request),
                PlcOnlineMethods.GoOffline => GoOffline(request),
                PlcOnlineMethods.StationUpload => StationUpload(request),
                PlcOnlineMethods.ListWatchAndForceTables => Ok(WorkerJson.SerializePayload(WatchTables(request.PlcName))),
                PlcOnlineMethods.ReadWatchTable => Ok(WorkerJson.SerializePayload(ReadWatch(request))),
                PlcOnlineMethods.ReadForceTable => Ok(WorkerJson.SerializePayload(ReadForce(request))),
                PlcOnlineMethods.GetWatchTableContent => Ok(Content("watch", request)),
                PlcOnlineMethods.GetForceTableContent => Ok(Content("force", request)),
                var write when PlcOnlineMethods.ContentWriteMethods.Contains(write) => Ok(WorkerJson.SerializePayload(Write(request))),
                _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"plc-online scenario does not script '{request.Method}'."),
            };
        }
        catch (WorkerOperationException ex)
        {
            return JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false, FailureCategory = ex.FailureCategory, Error = ex.Message,
                EngineeringError = ex.EngineeringError, PlcDownloadOutcome = ex.PlcDownloadOutcome,
            }, WorkerJson.Envelope);
        }
    }

    private Plc Find(string? name)
    {
        var matches = _plcs.Where(p => name is null || Same(p.Software, name) || Same(p.Device, name)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{name}' was found in the project."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, "Several PLC software instances match. Specify an unambiguous plcName."),
        };
    }

    private PlcOnlineStateInventoryInfo OnlineState(string? plcName)
    {
        var plcs = _plcs.Where(p => plcName is null || Same(p.Software, plcName) || Same(p.Device, plcName)).ToList();
        if (plcName is not null && plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{plcName}' was found in the project.");
        return new PlcOnlineStateInventoryInfo
        {
            Plcs = plcs.Select(p => new PlcOnlineStatePlcInfo
            {
                PlcName = p.Software, DeviceName = p.Device, OnlineProviderAvailable = p.Online,
                State = p.Online ? p.State : null, IsConfigured = p.Online ? true : null,
                Modes = p.Online ? new() { new PlcConnectionModeInfo { Name = PlcOnlinePolicy.DefaultMode, PcInterfaces =
                {
                    new PlcConnectionPcInterfaceInfo { Name = "PLCSIM", Number = 1, TargetInterfaces = { new PlcConnectionTargetInterfaceInfo { Name = "1 X1" } } },
                    new PlcConnectionPcInterfaceInfo { Name = HardwareInterface, Number = 1, TargetInterfaces =
                        { new PlcConnectionTargetInterfaceInfo { Name = "1 X1" }, new PlcConnectionTargetInterfaceInfo { Name = "1 X2" } } },
                } } } : new(),
            }).ToList(),
        };
    }

    private PlcCompareResultInfo Compare(WorkerRequest request, bool online, bool hardware = false)
    {
        var compare = request.PlcOnline ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "The compare request is missing.");
        if ((hardware
                ? PlcOnlinePolicy.ValidateHardwareCompare(compare.CompareTarget, compare.TargetName, compare.HardwareScope, compare.MaxDepth, compare.MaxElements, online)
                : PlcOnlinePolicy.ValidateCompare(compare.CompareTarget, compare.TargetName, compare.MaxDepth, compare.MaxElements, online)) is { } error)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error);
        if (hardware) return CompareHardware(compare, online);
        if (online && PlcTlsTrustPolicy.ValidateNormalized(compare.TrustedCertificates) is { } trustError)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, trustError);
        var plc = Find(compare.PlcName);
        string? targetName = null;
        if (!online && compare.CompareTarget == PlcOnlinePolicy.CompareTargets.Plc) targetName = Find(compare.TargetName).Software;
        if (!online && compare.CompareTarget == PlcOnlinePolicy.CompareTargets.MasterCopy)
            targetName = Same(compare.TargetName!, "PlcTemplate") ? compare.TargetName
                : throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No master copy named '{compare.TargetName}' is in the root master copy folder of the project library.");
        var root = new Node(plc.Software, online ? plc.Software + " (online)" : targetName ?? "Project library", "FolderContainsDifferencesOwnStateDifferent", "",
            new Node("Program blocks", "Program blocks", "FolderContainsDifferencesOwnStateDifferent", "",
                new Node("Main", "Main", "ObjectsDifferent", "Code differs"),
                new Node("Block_2", "Block_2", "ObjectsIdentical", ""),
                new Node("Block_3", "", "RightMissing", "Only offline")),
            new Node("PLC tags", "PLC tags", "FolderContentsIdentical", ""));
        var result = new PlcCompareResultInfo
        {
            Operation = online ? PlcOnlineMethods.CompareToOnline : PlcOnlineMethods.CompareSoftware,
            PlcName = plc.Software, DeviceName = plc.Device,
            CompareTarget = online ? "online" : compare.CompareTarget!, TargetName = targetName,
            MaxDepth = compare.MaxDepth, MaxElements = compare.MaxElements, DifferencesOnly = compare.DifferencesOnly,
        };
        if (online && _presentTls && plc.Online)
        {
            // Like the worker's fail-closed legitimation handler: no decisions or passwords, TLS only from trustedCertificates.
            var legitimation = new PlcCallbackRecorder(null, null,
                new PlcCallbackReflectionAccess(PlcCallbackPolicy.OnlineConfigurationsNamespace, _ => null, _ => null), trustedCertificates: compare.TrustedCertificates);
            try
            {
                legitimation.Handle(FakeTlsCertificates.Configuration(plc.Software));
                // K27: a refused TLS question returns normally (NonTrusted, no throw); TIA Portal then fails the compare's connection.
                if (legitimation.Abort is not null) throw new PlcCallbackAbortedException("TIA Portal refused the connection after NonTrusted.");
            }
            catch (PlcCallbackAbortedException)
            {
                result.OnlineStateBefore = result.OnlineStateAfter = plc.State;
                result.RestoreAttempted = result.DisconnectedAfterCompare = false;
                result.ConfigurationsSeen = legitimation.Seen;
                result.Abort = legitimation.Abort;
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    "compare_to_online refused an online legitimation prompt, so the compare result was discarded. "
                    + PlcCallbackRecorder.AbortMessage(legitimation.Abort!), plcDownloadOutcome: WorkerJson.SerializePayload(result));
            }

            result.ConfigurationsSeen = legitimation.Seen;
        }
        if (online && plc.Online)
        {
            // The compare connected an offline PLC; the worker restores the earlier state with GoOffline.
            // The compare connects an offline PLC; the worker's PlcCompareOnlineRun restores Offline afterwards.
            var states = new Queue<string>(plc.State == PlcOnlinePolicy.States.Online
                ? new[] { plc.State, plc.State }
                : new[] { plc.State, PlcOnlinePolicy.States.Online, PlcOnlinePolicy.States.Offline });
            long clock = 0;
            var run = new PlcCompareOnlineRun(() => states.Count > 1 ? states.Dequeue() : states.Peek(), () => { }, ms => clock += ms, () => clock);
            run.Run(() => 0);
            run.ApplyTo(result);
        }

        return PlcOnlinePolicy.ProjectTree(root, n => n.Left, n => n.Right, n => n.State, n => n.Detail, n => n.Children,
            compare.MaxDepth, compare.MaxElements, compare.DifferencesOnly).ApplyTo(result);
    }

    /// <summary>
    /// Hardware compare stand-in (issue #7): the stations or CPUs of two PLCs differ in the CPU firmware; a master copy and PLC_3's
    /// missing online provider model a target that offers no IHardwareCompareTarget (unsupported_capability, nothing compared).
    /// The online variant has no TLS or restore modelling of its own: those rules are the shared PlcCompareService.RunOnline.
    /// </summary>
    private PlcCompareResultInfo CompareHardware(PlcOnlineRequestInfo compare, bool online)
    {
        var plc = Find(compare.PlcName);
        var cpu = compare.HardwareScope == PlcOnlinePolicy.HardwareScopes.Cpu;
        string? targetName = null;
        string right;
        if (online)
        {
            if (plc.Software == "PLC_3")
                throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                    $"PLC '{plc.Software}' offers no OnlineProvider on its CPU device item, so there is no online hardware to compare. Nothing was contacted.");
            right = (cpu ? plc.Software : plc.Device) + " (online)";
        }
        else if (compare.CompareTarget == PlcOnlinePolicy.CompareTargets.MasterCopy)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"The master_copy compare target is a 'Siemens.Engineering.Library.MasterCopies.MasterCopy', which does not implement {PlcOnlinePolicy.HardwareCompareTargetType} "
                + "on this installation; TIA Portal Openness offers no hardware compare against it. Nothing was compared.");
        else
        {
            var other = Find(compare.TargetName);
            targetName = other.Software;
            right = cpu ? other.Software : other.Device;
        }

        var root = new Node(cpu ? plc.Software : plc.Device, right, "FolderContainsDifferencesOwnStateDifferent", "",
            new Node("Rack_0", "Rack_0", "FolderContainsDifferencesOwnStateDifferent", "",
                new Node("CPU 1516-3 PN/DP", "CPU 1516-3 PN/DP", "ObjectsDifferent", "Firmware V2.8 / V3.0"),
                new Node("DI 16x24VDC", "DI 16x24VDC", "ObjectsIdentical", "")));
        var result = new PlcCompareResultInfo
        {
            Operation = online ? PlcOnlineMethods.CompareHardwareToOnline : PlcOnlineMethods.CompareHardware,
            PlcName = plc.Software, DeviceName = plc.Device,
            CompareTarget = online ? "online" : compare.CompareTarget!, TargetName = targetName,
            MaxDepth = compare.MaxDepth, MaxElements = compare.MaxElements, DifferencesOnly = compare.DifferencesOnly,
        };
        if (online)
        {
            result.OnlineStateBefore = result.OnlineStateAfter = plc.State;
            result.RestoreAttempted = result.DisconnectedAfterCompare = false;
        }

        return PlcOnlinePolicy.ProjectTree(root, n => n.Left, n => n.Right, n => n.State, n => n.Detail, n => n.Children,
            compare.MaxDepth, compare.MaxElements, compare.DifferencesOnly).ApplyTo(result);
    }

    private string GoOnline(WorkerRequest request)
    {
        var action = RequireConfirmed(request);
        if (PlcOnlinePolicy.ValidatePath(action.Mode, action.PcInterfaceName, action.PcInterfaceNumber, action.TargetInterface, requireTarget: true) is { } pathError)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, pathError);
        if (PlcCallbackPolicy.ValidateEntries(null, action.Passwords) is { } entryError)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, entryError);
        if (PlcTlsTrustPolicy.ValidateNormalized(action.TrustedCertificates) is { } trustError)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, trustError);
        var plc = Exact(action);
        var confirmations = Confirmations(action.PortalConfirmations);
        var result = new PlcOnlineActionResultInfo
        {
            Action = "go_online", PlcName = plc.Software, DeviceName = plc.Device, TimeoutSeconds = action.TimeoutSeconds,
            PortalConfirmationPolicy = action.PortalConfirmations,
            Path = new PlcConnectionPathInfo
            {
                Mode = action.Mode!, PcInterface = new PlcConnectionPcInterfaceRefInfo { Name = action.PcInterfaceName!, Number = action.PcInterfaceNumber },
                TargetInterface = action.TargetInterface,
            },
            StateBefore = plc.State,
        };
        if (plc.State == PlcOnlinePolicy.States.Online)
        {
            result.Path = null;
            result.Outcome = PlcOnlinePolicy.ActionOutcomes.AlreadyOnline;
            result.StateAfter = plc.State;
            result.GoalReached = true;
            result.StatesObserved.Add(plc.State);
            result.Note = "The PLC was already online. ApplyConfiguration and GoOnline were not called.";
            return Ok(WorkerJson.SerializePayload(Close(result, confirmations)));
        }

        var recorder = new PlcCallbackRecorder(null, action.Passwords,
            new PlcCallbackReflectionAccess(PlcCallbackPolicy.OnlineConfigurationsNamespace, _ => null,
                variable => variable == PasswordVariable ? "fake-password" : null), trustedCertificates: action.TrustedCertificates,
            plcSimInterface: PlcTlsTrustPolicy.IsPlcSimInterface(action.PcInterfaceName));
        try
        {
            if (_presentTls) recorder.Handle(FakeTlsCertificates.Configuration(plc.Software));
            if (action.Passwords.Any(p => p.Type == nameof(Siemens.Engineering.Online.Configurations.OnlinePasswordConfiguration)))
                recorder.Handle(new Siemens.Engineering.Online.Configurations.OnlinePasswordConfiguration());
        }
        catch (PlcCallbackAbortedException)
        {
            result.ConfigurationsSeen = recorder.Seen;
            result.Abort = recorder.Abort;
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, PlcCallbackRecorder.AbortMessage(recorder.Abort!),
                plcDownloadOutcome: WorkerJson.SerializePayload(Close(result, confirmations)));
        }

        result.ConfigurationsSeen = recorder.Seen;
        result.ConfigurationApplied = true;
        result.GoOnlineReturned = PlcOnlinePolicy.States.Connecting;
        if (recorder.Abort is { } refused)
        {
            // K27: the refused TLS question returned normally (NonTrusted, no throw); GoOnline returns, TIA Portal fails the
            // connection itself (NotReachable), and the recorded abort decides, exactly like the worker.
            result.StatesObserved.AddRange(new[] { PlcOnlinePolicy.States.Connecting, PlcOnlinePolicy.States.NotReachable });
            result.StateAfter = PlcOnlinePolicy.States.NotReachable;
            result.Outcome = PlcOnlinePolicy.ActionOutcomes.NotReached;
            result.Abort = refused;
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, PlcCallbackRecorder.AbortMessage(refused)
                + " Read get_online_state; the connection was refused.", plcDownloadOutcome: WorkerJson.SerializePayload(Close(result, confirmations)));
        }

        var states = action.TargetInterface == "1 X2"
            ? new Queue<string>(new[] { PlcOnlinePolicy.States.Connecting, PlcOnlinePolicy.States.NotReachable })
            : new Queue<string>(new[] { PlcOnlinePolicy.States.Connecting, PlcOnlinePolicy.States.Connecting, PlcOnlinePolicy.States.Online });
        long clock = 0;
        var wait = PlcOnlinePolicy.Wait(() => states.Count > 1 ? states.Dequeue() : states.Peek(), PlcOnlinePolicy.States.Online,
            action.TimeoutSeconds * 1000, () => clock, milliseconds => clock += milliseconds);
        result.StateAfter = wait.FinalState;
        result.GoalReached = wait.GoalReached;
        result.TimedOut = wait.TimedOut;
        result.ElapsedMilliseconds = wait.ElapsedMilliseconds;
        result.StatesObserved = wait.Observed.ToList();
        result.Outcome = wait.GoalReached ? PlcOnlinePolicy.ActionOutcomes.Connected : PlcOnlinePolicy.ActionOutcomes.NotReached;
        result.PathVerified = wait.GoalReached;
        if (wait.GoalReached) Replace(plc, PlcOnlinePolicy.States.Online);
        return Ok(WorkerJson.SerializePayload(Close(result, confirmations)));
    }

    private string GoOffline(WorkerRequest request)
    {
        var action = RequireConfirmed(request);
        var plc = Exact(action);
        var confirmations = Confirmations(action.PortalConfirmations);
        var result = new PlcOnlineActionResultInfo
        {
            Action = "go_offline", PlcName = plc.Software, DeviceName = plc.Device, TimeoutSeconds = action.TimeoutSeconds,
            PortalConfirmationPolicy = action.PortalConfirmations,
            StateBefore = plc.State, StateAfter = PlcOnlinePolicy.States.Offline, GoalReached = true,
        };
        result.StatesObserved.Add(PlcOnlinePolicy.States.Offline);
        result.Outcome = plc.State == PlcOnlinePolicy.States.Offline ? PlcOnlinePolicy.ActionOutcomes.AlreadyOffline : PlcOnlinePolicy.ActionOutcomes.Disconnected;
        if (plc.State == PlcOnlinePolicy.States.Offline) result.Note = "The PLC was already offline; GoOffline was not called.";
        Replace(plc, PlcOnlinePolicy.States.Offline);
        return Ok(WorkerJson.SerializePayload(Close(result, confirmations)));
    }

    private string StationUpload(WorkerRequest request)
    {
        var upload = RequireConfirmed(request);
        if (PlcOnlinePolicy.ValidateAddress(upload.Address) is { } addressError)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, addressError);
        if (PlcCallbackPolicy.ValidateEntries(upload.Decisions, upload.Passwords) is { } entryError)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, entryError);
        if (PlcTlsTrustPolicy.ValidateNormalized(upload.TrustedCertificates) is { } trustError)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, trustError);
        var confirmations = Confirmations(upload.PortalConfirmations);
        var result = new PlcStationUploadResultInfo
        {
            Address = upload.Address!,
            PortalConfirmationPolicy = upload.PortalConfirmations,
            Path = new PlcConnectionPathInfo
            {
                Mode = upload.Mode!, PcInterface = new PlcConnectionPcInterfaceRefInfo { Name = upload.PcInterfaceName!, Number = upload.PcInterfaceNumber },
            },
        };
        var recorder = new PlcCallbackRecorder(upload.Decisions, upload.Passwords,
            new PlcCallbackReflectionAccess(new[] { PlcCallbackPolicy.UploadConfigurationsNamespace, PlcCallbackPolicy.OnlineConfigurationsNamespace },
                configuration => configuration is Siemens.Engineering.Upload.Configurations.UploadMissingProducts ? "Products are missing." : null,
                variable => variable == PasswordVariable ? "fake-password" : null), trustedCertificates: upload.TrustedCertificates,
            plcSimInterface: PlcTlsTrustPolicy.IsPlcSimInterface(upload.PcInterfaceName));
        try
        {
            // The upload's connection asks for trust first (online legitimation), then the upload callbacks follow.
            if (_presentTls) recorder.Handle(FakeTlsCertificates.Configuration("Uploaded_Station"));
            // K27: a refused TLS question returns normally (NonTrusted, no throw); TIA Portal then fails the upload's connection.
            if (recorder.Abort is not null) throw new PlcCallbackAbortedException("TIA Portal refused the connection after NonTrusted.");
            recorder.Handle(new Siemens.Engineering.Upload.Configurations.UploadMissingProducts());
            if (upload.Passwords.Any(p => p.Type == nameof(Siemens.Engineering.Upload.Configurations.ModuleReadAccessPassword)))
                recorder.Handle(new Siemens.Engineering.Upload.Configurations.ModuleReadAccessPassword());
        }
        catch (PlcCallbackAbortedException)
        {
            result.ConfigurationsSeen = recorder.Seen;
            result.Abort = recorder.Abort;
            result.UnusedDecisions = recorder.UnusedKeys().ToList();
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                PlcCallbackRecorder.AbortMessage(recorder.Abort!) + " A partly uploaded station may have been added to the project.",
                plcDownloadOutcome: WorkerJson.SerializePayload(Close(result, confirmations)));
        }

        result.ConfigurationsSeen = recorder.Seen;
        result.UnusedDecisions = recorder.UnusedKeys().ToList();
        result.State = "Success";
        result.ErrorCount = 0;
        result.UploadedStationName = "Uploaded_Station";
        result.Messages.Add(new PlcEngineeringMessageInfo
        {
            Type = "Siemens.Engineering.Upload.UploadResultMessage",
            Attributes = { new PlcEngineeringAttributeInfo { Name = "Message", Value = "Upload completed." }, new PlcEngineeringAttributeInfo { Name = "State", Value = "Success" } },
        });
        return Ok(WorkerJson.SerializePayload(Close(result, confirmations)));
    }

    private PlcWatchTableInventoryInfo WatchTables(string? plcName)
    {
        var plcs = _plcs.Where(p => plcName is null || Same(p.Software, plcName) || Same(p.Device, plcName)).ToList();
        if (plcName is not null && plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{plcName}' was found in the project.");
        return new PlcWatchTableInventoryInfo
        {
            Plcs = plcs.Select(p => p.Software == "PLC_1"
                ? new PlcWatchTablePlcInfo
                {
                    PlcName = p.Software, DeviceName = p.Device, Groups = _groups.ToList(),
                    WatchTables = _watchTables.Select(t => new PlcWatchTableSummaryInfo { GroupPath = t.Group, Name = t.Name, IsConsistent = true }).ToList(),
                    ForceTables = _forceTables.Select(t => new PlcWatchTableSummaryInfo { GroupPath = t.Group, Name = t.Name, IsConsistent = true }).ToList(),
                }
                : new PlcWatchTablePlcInfo { PlcName = p.Software, DeviceName = p.Device }).ToList(),
        };
    }

    private PlcWatchTableReadInfo ReadWatch(WorkerRequest request)
    {
        var (plc, group, name) = Table(request, _watchTables, "Watch table");
        var entries = new[]
        {
            PlcWatchTablePolicy.WatchEntry(0, attribute => attribute switch
            {
                "Name" => (true, "Motor_On"), "Address" => (true, "%Q0.0"), "DisplayFormat" => (true, "Bool"), "MonitorTrigger" => (true, "Permanent"),
                "ModifyIntention" => (true, true), "ModifyTrigger" => (true, "Permanent"), "ModifyValue" => (true, "TRUE"), _ => (false, null),
            }),
            PlcWatchTablePolicy.WatchEntry(1, attribute => attribute switch
            {
                "Name" => (true, "Speed"), "Address" => (true, "%MW10"), "ModifyIntention" => (true, false), "ModifyValue" => (true, null),
                _ => (false, (object?)null),
            }),
        };
        var max = request.PlcOnline?.MaxElements ?? PlcWatchTablePolicy.DefaultMaxEntries;
        var listed = entries.Take(max).ToList();
        return new PlcWatchTableReadInfo
        {
            PlcName = plc.Software, DeviceName = plc.Device, GroupPath = group, Name = name, IsConsistent = true,
            Entries = listed, EntryCount = listed.Count, EntriesComplete = listed.Count == entries.Length,
        };
    }

    private PlcForceTableReadInfo ReadForce(WorkerRequest request)
    {
        var (plc, group, name) = Table(request, _forceTables, "Force table");
        var entry = PlcWatchTablePolicy.ForceEntry(0, attribute => attribute switch
        {
            "Name" => (true, "Valve_Open"), "Address" => (true, "%Q1.0"), "DisplayFormat" => (true, "Bool"), "MonitorTrigger" => (true, "Permanent"),
            "ForceIntention" => (true, true), "ForceValue" => (true, "TRUE"), _ => (false, null),
        });
        return new PlcForceTableReadInfo
        {
            PlcName = plc.Software, DeviceName = plc.Device, GroupPath = group, Name = name, IsConsistent = true,
            Entries = { entry }, EntryCount = 1, EntriesComplete = true,
        };
    }

    private string Content(string kind, WorkerRequest request)
    {
        var (_, group, name) = Table(request, kind == "watch" ? _watchTables : _forceTables, kind == "watch" ? "Watch table" : "Force table");
        return _content.TryGetValue($"{kind}:{Combine(group, name)}", out var content) ? content
            : $"<Document><Table>{name}</Table></Document>";
    }

    private PlcWatchTableMutationResultInfo Write(WorkerRequest request)
    {
        var plc = Find(request.PlcName);
        var group = PlcWatchTablePolicy.NormalizeGroupPath(request.FolderPath);
        var result = new PlcWatchTableMutationResultInfo { Operation = request.Method, PlcName = plc.Software, GroupPath = group, TableName = request.TableName };
        switch (request.Method)
        {
            case PlcOnlineMethods.CreateWatchTable:
                if (_watchTables.Any(t => Same(t.Group, group) && Same(t.Name, request.TableName!)))
                    throw new WorkerOperationException(WorkerFailureCategories.EngineeringRejected, $"TIA Portal rejected the operation: the name '{request.TableName}' is already used.");
                _watchTables.Add((group, request.TableName!));
                break;
            case PlcOnlineMethods.DeleteWatchTable:
                var (_, deleteGroup, deleteName) = Table(request, _watchTables, "Watch table");
                _watchTables.RemoveAll(t => Same(t.Group, deleteGroup) && Same(t.Name, deleteName));
                break;
            case PlcOnlineMethods.CreateWatchTableGroup:
                _groups.Add(group);
                result.TableName = null;
                break;
            case PlcOnlineMethods.DeleteWatchTableGroup:
                _groups.RemoveAll(g => Same(g, group) || g.StartsWith(group + "/", StringComparison.OrdinalIgnoreCase));
                _watchTables.RemoveAll(t => Same(t.Group, group));
                result.TableName = null;
                break;
            case PlcOnlineMethods.UpdateWatchTableContent:
            case PlcOnlineMethods.UpdateForceTableContent:
                var kind = request.Method == PlcOnlineMethods.UpdateWatchTableContent ? "watch" : "force";
                var current = Content(kind, request);
                if (!string.Equals(ContentHashRules.Compute("xml", current), request.ExpectedContentHash, StringComparison.Ordinal))
                    throw new WorkerOperationException(WorkerFailureCategories.StateChanged, "The content changed after the write was planned; nothing was imported.");
                var (_, contentGroup, contentName) = Table(request, kind == "watch" ? _watchTables : _forceTables, "Table");
                if (PlcWatchTablePolicy.CheckImportDocument(current, request.Content, contentName) is { } refusal)
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"The import was refused before anything changed: {refusal}.");
                _content[$"{kind}:{Combine(contentGroup, contentName)}"] = request.Content!;
                result.TableName = contentName;
                result.ImportedTables.Add(contentName);
                break;
            case PlcOnlineMethods.UpdateProgram:
                // Like a V21 UpdateProgram that returns nothing: the call succeeds, but its outcome is uninterpreted.
                var interpretation = PlcUpdateProgramOutcome.Interpret(null);
                result.TableName = null;
                result.GroupPath = "/";
                result.Outcome = interpretation.Outcome;
                result.ReturnedMembers = interpretation.Members;
                break;
        }

        return result;
    }

    private (Plc Plc, string Group, string Name) Table(WorkerRequest request, List<(string Group, string Name)> tables, string what)
    {
        var plc = Find(request.PlcName);
        var group = PlcWatchTablePolicy.NormalizeGroupPath(request.FolderPath);
        var match = plc.Software == "PLC_1" ? tables.Where(t => Same(t.Group, group) && Same(t.Name, request.TableName ?? string.Empty)).ToList() : new();
        if (match.Count != 1)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"{what} '{request.TableName}' in group '{group}' was not found.");
        return (plc, match[0].Group, match[0].Name);
    }

    private PlcOnlineRequestInfo RequireConfirmed(WorkerRequest request)
    {
        return request.PlcOnline ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "The online request is missing.");
    }

    private Plc Exact(PlcOnlineRequestInfo action)
    {
        var plc = Find(action.PlcName);
        if (plc.Software != action.PlcName || plc.Device != action.DeviceName)
            throw new WorkerOperationException(WorkerFailureCategories.StateChanged, $"PLC '{action.PlcName}' on device '{action.DeviceName}' no longer resolves exactly.");
        if (!plc.Online) throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"PLC '{plc.Software}' offers no OnlineProvider.");
        return plc;
    }

    private void Replace(Plc plc, string state) => _plcs[_plcs.IndexOf(plc)] = plc with { State = state };

    private static string Combine(string group, string name) => group == "/" ? "/" + name : group + "/" + name;

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string Ok(string payload)
        => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload }, WorkerJson.Envelope);
}
