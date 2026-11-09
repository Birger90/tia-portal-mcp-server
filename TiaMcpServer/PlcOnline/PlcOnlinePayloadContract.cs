using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.Tools;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;

namespace TiaMcpServer.PlcOnline;

/// <summary>
/// The only decoder of the compare, online-state, station-upload, watch/force table and online-action
/// worker payloads. Each goes through the required-member worker-payload reader plus semantic checks;
/// any violation is a <see cref="JsonException"/>, which callers turn into <c>protocol_error</c> without
/// echoing it.
/// </summary>
public static class PlcOnlinePayloadContract
{
    public const string ProtocolFailureMessage = "The worker payload did not match the declared PLC online result contract.";

    /// <param name="trustedCertificates">compare_to_online: the normalized fingerprints the caller approved (evidence check).</param>
    public static JsonElement NormalizeCompare(string payload, string operation, IReadOnlyCollection<string>? trustedCertificates = null)
        => CanonicalJson.NormalizeWorkerPayload<PlcCompareResultInfo>(payload,
            result => ValidateCompare(result, operation, trustedCertificates ?? Array.Empty<string>(), failureEvidence: false)).Element;

    /// <summary>
    /// The failure evidence of a compare_to_online that met an online legitimation: the online states and the presented
    /// configurations with the refusal (when there was one), without a compare tree.
    /// </summary>
    public static JsonElement NormalizeCompareFailureEvidence(string payload, IReadOnlyCollection<string>? trustedCertificates,
        string operation = PlcOnlineMethods.CompareToOnline)
        => CanonicalJson.NormalizeWorkerPayload<PlcCompareResultInfo>(payload,
            result => ValidateCompare(result, operation, trustedCertificates ?? Array.Empty<string>(), failureEvidence: true)).Element;

    /// <summary>
    /// Audit N3: get_station_fingerprint_data. The echoed path and address must be the request's, the item list within the cap and
    /// its count, every identifier and value a <see cref="Contracts.Shared.RuntimeValueInfo"/>, the provider owner portal or project,
    /// and the online legitimation evidence valid under the compare rules (a delivered result carries no abort).
    /// </summary>
    public static JsonElement NormalizeFingerprintData(string payload, string? mode, string? pcInterfaceName, int? pcInterfaceNumber, string? targetAddress,
        int maxItems, IReadOnlyCollection<string>? trustedCertificates)
        => CanonicalJson.NormalizeWorkerPayload<PlcStationFingerprintDataInfo>(payload, result =>
        {
            Require(result.Items);
            StandalonePayloadContract.RejectNullElements(result.Items);
            if (result.Operation != PlcOnlineMethods.GetStationFingerprintData || result.ProviderOwner is not ("portal" or "project")
                || result.Mode != mode || result.PcInterfaceName != pcInterfaceName || result.PcInterfaceNumber != pcInterfaceNumber
                || result.TargetAddress != targetAddress || result.MaxItems != maxItems || result.Items.Count > maxItems
                || result.ItemCount < result.Items.Count || result.Truncated != (result.ItemCount > result.Items.Count)
                || result.Abort is not null
                || result.Items.Any(item => item.Identifier is null || item.Value is null
                    || !Contracts.Shared.RuntimeValueStates.All.Contains(item.Identifier.State) || !Contracts.Shared.RuntimeValueStates.All.Contains(item.Value.State)))
                throw new JsonException(ProtocolFailureMessage);
            ValidateCallbacks(result.ConfigurationsSeen, result.Abort, Array.Empty<PlcCallbackDecisionInfo>(), Array.Empty<PlcCallbackPasswordInfo>(),
                trustedCertificates ?? Array.Empty<string>(), passwordsUnknown: true);
        }).Element;

    public static PlcOnlineStateInventoryInfo DecodeOnlineState(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcOnlineStateInventoryInfo>(payload, ValidateOnlineState).Value;

    public static JsonElement NormalizeOnlineState(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcOnlineStateInventoryInfo>(payload, ValidateOnlineState).Element;

    public static PlcStationUploadInterfacesInfo DecodeUploadInterfaces(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcStationUploadInterfacesInfo>(payload, ValidateUploadInterfaces).Value;

    public static JsonElement NormalizeUploadInterfaces(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcStationUploadInterfacesInfo>(payload, ValidateUploadInterfaces).Element;

    public static PlcWatchTableInventoryInfo DecodeWatchTables(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcWatchTableInventoryInfo>(payload, ValidateWatchTables).Value;

    public static JsonElement NormalizeWatchTables(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcWatchTableInventoryInfo>(payload, ValidateWatchTables).Element;

    public static JsonElement NormalizeWatchTable(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcWatchTableReadInfo>(payload, table =>
        {
            Require(table.Entries);
            foreach (var entry in table.Entries) Require(entry.UnreadableAttributes);
            CheckTable(table.PlcName, table.GroupPath, table.Name, table.EntryCount, table.Entries.Count, table.Entries.Select(e => e.Index));
        }).Element;

    public static JsonElement NormalizeForceTable(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcForceTableReadInfo>(payload, table =>
        {
            Require(table.Entries);
            foreach (var entry in table.Entries) Require(entry.UnreadableAttributes);
            CheckTable(table.PlcName, table.GroupPath, table.Name, table.EntryCount, table.Entries.Count, table.Entries.Select(e => e.Index));
        }).Element;

    public static JsonElement NormalizeWatchTableMutation(string payload, string operation)
        => CanonicalJson.NormalizeWorkerPayload<PlcWatchTableMutationResultInfo>(payload, result =>
        {
            Require(result.ImportedTables);
            if (result.Operation != operation || string.IsNullOrEmpty(result.PlcName) || result.GroupPath is null || !result.GroupPath.StartsWith("/", StringComparison.Ordinal))
                throw new JsonException(ProtocolFailureMessage);
            Require(result.ReturnedMembers);
            if (operation != PlcOnlineMethods.UpdateProgram
                    ? result.ReturnedType is not null || result.Outcome is not null || result.ReturnedMembers.Count != 0
                    : result.Outcome is not (PlcUpdateProgramOutcome.Succeeded or PlcUpdateProgramOutcome.Uninterpreted)
                      || result.ReturnedMembers.Count > PlcUpdateProgramOutcome.MaxMembers
                      || result.ReturnedType is null && (result.Outcome != PlcUpdateProgramOutcome.Uninterpreted || result.ReturnedMembers.Count != 0))
                throw new JsonException(ProtocolFailureMessage);
        }).Element;

    /// <summary>Decodes a go-online/go-offline result and checks it describes exactly the planned request.</summary>
    public static PlcOnlineActionResultInfo DecodeOnlineAction(string payload, string action, string plcName, string deviceName,
        PlcConnectionPathInfo? path, IReadOnlyList<PlcCallbackPasswordInfo> passwords, string portalConfirmations,
        IReadOnlyCollection<string>? trustedCertificates = null, bool allowInsecurePasswordTransfer = false)
    {
        var result = CanonicalJson.DeserializeWorkerPayload<PlcOnlineActionResultInfo>(payload);
        ValidatePortalConfirmations(portalConfirmations, result.PortalConfirmationPolicy, result.PortalConfirmations,
            result.PortalConfirmationsObserved, result.PortalConfirmationsComplete);
        Require(result.StatesObserved);
        if (result.Action != action || result.PlcName != plcName || result.DeviceName != deviceName
            || result.StatesObserved.Count > PlcOnlinePolicy.MaxObservedStates || result.ElapsedMilliseconds < 0
            || result.GoalReached && result.TimedOut)
            throw new JsonException(ProtocolFailureMessage);
        var outcomes = action == "go_online" ? PlcOnlinePolicy.ActionOutcomes.ForGoOnline : PlcOnlinePolicy.ActionOutcomes.ForGoOffline;
        if (result.Outcome is not null && (!outcomes.Contains(result.Outcome) || PlcOnlinePolicy.ActionOutcomes.ReachesGoal(result.Outcome) != result.GoalReached))
            throw new JsonException(ProtocolFailureMessage);
        if (result.PathVerified != (result.Outcome == PlcOnlinePolicy.ActionOutcomes.Connected)) throw new JsonException(ProtocolFailureMessage);
        // A path is reported exactly when one was applied (failure evidence may carry it); already_online applied nothing.
        var applied = path is not null && (result.Outcome is null ? result.Path is not null : PlcOnlinePolicy.ActionOutcomes.AppliesPath(result.Outcome));
        if (applied ? !SamePath(result.Path, path!) : result.Path is not null) throw new JsonException(ProtocolFailureMessage);
        if (result.GoalReached && result.StateAfter != (action == "go_online" ? PlcOnlinePolicy.States.Online : PlcOnlinePolicy.States.Offline))
            throw new JsonException(ProtocolFailureMessage);
        ValidateCallbacks(result.ConfigurationsSeen, result.Abort, Array.Empty<PlcCallbackDecisionInfo>(), passwords, trustedCertificates,
            PlcTlsTrustPolicy.IsPlcSimInterface(path?.PcInterface?.Name), allowInsecurePasswordTransfer);
        return result;
    }

    /// <summary>Decodes a station-upload result and checks it describes exactly the planned request and the caller's answers.</summary>
    public static PlcStationUploadResultInfo DecodeUpload(string payload, string address, PlcConnectionPathInfo path,
        IReadOnlyList<PlcCallbackDecisionInfo> decisions, IReadOnlyList<PlcCallbackPasswordInfo> passwords, bool requireUploadResult,
        string portalConfirmations, IReadOnlyCollection<string>? trustedCertificates = null, bool allowInsecurePasswordTransfer = false)
    {
        var result = CanonicalJson.DeserializeWorkerPayload<PlcStationUploadResultInfo>(payload);
        ValidatePortalConfirmations(portalConfirmations, result.PortalConfirmationPolicy, result.PortalConfirmations,
            result.PortalConfirmationsObserved, result.PortalConfirmationsComplete);
        Require(result.UnusedDecisions);
        if (result.Address != address || !SamePath(result.Path, path) || !result.ProjectModified
            || result.MessagesComplete != (result.MessageOmission is null))
            throw new JsonException(ProtocolFailureMessage);
        if (result.State is null)
        {
            if (result.ErrorCount is not null || result.UploadedStationName is not null || result.Messages.Count != 0
                || requireUploadResult && result.Abort is null)
                throw new JsonException(ProtocolFailureMessage);
        }
        else if (string.IsNullOrWhiteSpace(result.State) || result.ErrorCount is not >= 0)
        {
            throw new JsonException(ProtocolFailureMessage);
        }

        ValidateMessages(result.Messages);
        ValidateCallbacks(result.ConfigurationsSeen, result.Abort, decisions, passwords, trustedCertificates,
            PlcTlsTrustPolicy.IsPlcSimInterface(path.PcInterface?.Name), allowInsecurePasswordTransfer);
        var keys = decisions.Select(d => d.Type).Concat(passwords.Select(p => p.Type)).ToHashSet(StringComparer.Ordinal);
        if (result.UnusedDecisions.Any(key => !keys.Contains(key))) throw new JsonException(ProtocolFailureMessage);
        return result;
    }

    private static void ValidateCompare(PlcCompareResultInfo result, string operation, IReadOnlyCollection<string> trusted, bool failureEvidence)
    {
        // Online legitimation evidence exists only for compare_to_online; a delivered compare never refused one, and
        // failure evidence carries no tree (the compare was discarded).
        if (!PlcOnlineMethods.IsOnlineCompare(operation))
        {
            Require(result.ConfigurationsSeen);
            if (result.ConfigurationsSeen.Count != 0 || result.Abort is not null) throw new JsonException(ProtocolFailureMessage);
        }
        else
        {
            // The merged password list (item sources plus the server map) is built at the call, not known here: a password_set entry
            // only has to name a valid source (audit N2 made such answers reachable for compares).
            ValidateCallbacks(result.ConfigurationsSeen, result.Abort, Array.Empty<PlcCallbackDecisionInfo>(), Array.Empty<PlcCallbackPasswordInfo>(), trusted,
                passwordsUnknown: true);
            if (failureEvidence ? result.Root is not null || result.ConfigurationsSeen.Count == 0 : result.Abort is not null)
                throw new JsonException(ProtocolFailureMessage);
        }

        if (result.Operation != operation || string.IsNullOrEmpty(result.PlcName) || result.DeviceName is null
            || result.ElementsListed < 0 || result.IdenticalElementsSkipped < 0 || result.ElementsListed > result.MaxElements
            || result.Complete != (result.Omission is null) || result.Identical != PlcOnlinePolicy.IsIdenticalState(result.RootState))
            throw new JsonException(ProtocolFailureMessage);
        var online = PlcOnlineMethods.IsOnlineCompare(operation);
        var targets = PlcOnlineMethods.IsHardwareCompare(operation) ? PlcOnlinePolicy.HardwareCompareTargets : PlcOnlinePolicy.CompareTargets.All;
        if (online ? result.CompareTarget != "online" || result.TargetName is not null
                : !targets.Contains(result.CompareTarget)
                  || PlcOnlinePolicy.CompareTargets.NeedsName(result.CompareTarget) == (result.TargetName is null))
            throw new JsonException(ProtocolFailureMessage);
        if (!online && (result.OnlineStateBefore is not null || result.OnlineStateAfter is not null || result.DisconnectedAfterCompare is not null
                || result.RestoreAttempted is not null))
            throw new JsonException(ProtocolFailureMessage);
        if ((result.DisconnectedAfterCompare is null) != (result.RestoreAttempted is null)
            || result.DisconnectedAfterCompare == true && result.RestoreAttempted != true
            || result.DisconnectedAfterCompare == true && result.OnlineStateAfter != PlcOnlinePolicy.States.Offline)
            throw new JsonException(ProtocolFailureMessage);
        if (result.Root is null ? result.RootState is not null || result.ElementsListed != 0 : result.Root.ComparisonResult != result.RootState)
            throw new JsonException(ProtocolFailureMessage);
        var count = 0;
        void Walk(PlcCompareElementInfo element, int depth)
        {
            Require(element.Elements);
            if (++count > result.MaxElements || depth > result.MaxDepth) throw new JsonException(ProtocolFailureMessage);
            foreach (var child in element.Elements) Walk(child, depth + 1);
        }

        if (result.Root is not null) Walk(result.Root, 1);
        if (count != result.ElementsListed) throw new JsonException(ProtocolFailureMessage);
    }

    private static void ValidateOnlineState(PlcOnlineStateInventoryInfo inventory)
    {
        Require(inventory.Plcs);
        foreach (var plc in inventory.Plcs)
        {
            Require(plc.Modes);
            if (plc.PlcName is null || plc.DeviceName is null) throw new JsonException(ProtocolFailureMessage);
            if (!plc.OnlineProviderAvailable && (plc.State is not null || plc.IsConfigured is not null || plc.Modes.Count != 0))
                throw new JsonException(ProtocolFailureMessage);
            if (plc.OnlineProviderAvailable && (string.IsNullOrWhiteSpace(plc.State) || plc.IsConfigured is null))
                throw new JsonException(ProtocolFailureMessage);
            ValidateModes(plc.Modes, targets: true);
        }
    }

    private static void ValidateUploadInterfaces(PlcStationUploadInterfacesInfo interfaces)
    {
        Require(interfaces.Modes);
        if (!interfaces.Available && interfaces.Modes.Count != 0) throw new JsonException(ProtocolFailureMessage);
        ValidateModes(interfaces.Modes, targets: false);
    }

    private static void ValidateModes(List<PlcConnectionModeInfo> modes, bool targets)
    {
        foreach (var mode in modes)
        {
            Require(mode.PcInterfaces);
            if (mode.Name is null) throw new JsonException(ProtocolFailureMessage);
            foreach (var pcInterface in mode.PcInterfaces)
            {
                Require(pcInterface.TargetInterfaces);
                if (pcInterface.Name is null || pcInterface.TargetInterfaces.Any(target => target.Name is null)
                    || !targets && pcInterface.TargetInterfaces.Count != 0)
                    throw new JsonException(ProtocolFailureMessage);
            }
        }
    }

    private static void ValidateWatchTables(PlcWatchTableInventoryInfo inventory)
    {
        Require(inventory.Plcs);
        foreach (var plc in inventory.Plcs)
        {
            Require(plc.Groups);
            Require(plc.WatchTables);
            Require(plc.ForceTables);
            if (plc.PlcName is null || plc.DeviceName is null
                || plc.Groups.Any(path => !path.StartsWith("/", StringComparison.Ordinal) || path == "/")
                || plc.WatchTables.Concat(plc.ForceTables).Any(table => table.Name is null || table.GroupPath is null
                    || !table.GroupPath.StartsWith("/", StringComparison.Ordinal)))
                throw new JsonException(ProtocolFailureMessage);
        }
    }

    private static void CheckTable(string? plcName, string? groupPath, string? name, int entryCount, int listed, IEnumerable<int> indexes)
    {
        if (string.IsNullOrEmpty(plcName) || groupPath is null || !groupPath.StartsWith("/", StringComparison.Ordinal) || name is null
            || entryCount != listed || !indexes.SequenceEqual(Enumerable.Range(0, listed)))
            throw new JsonException(ProtocolFailureMessage);
    }

    private static void ValidateMessages(List<PlcEngineeringMessageInfo>? messages)
    {
        var count = 0;
        void Walk(List<PlcEngineeringMessageInfo>? level, int depth)
        {
            Require(level);
            if (level!.Count > 0 && depth > PlcEngineeringMessageProjector.MaxDepth) throw new JsonException(ProtocolFailureMessage);
            foreach (var message in level)
            {
                Require(message.Attributes);
                if (++count > PlcEngineeringMessageProjector.MaxMessages || string.IsNullOrEmpty(message.Type)
                    || message.Attributes.Count > PlcEngineeringMessageProjector.MaxAttributes || message.Attributes.Any(a => string.IsNullOrEmpty(a.Name)))
                    throw new JsonException(ProtocolFailureMessage);
                Walk(message.Messages, depth + 1);
            }
        }

        Walk(messages, 1);
    }

    /// <summary>
    /// The portal-confirmation evidence rules of plc_download's contract 2.0: the policy is the requested one, the
    /// list is capped at <see cref="PlcDownloadPolicy.MaxPortalConfirmations"/>, complete exactly when every observed
    /// dialog is listed, and every entry carries a type and the policy's answer.
    /// </summary>
    internal static void ValidatePortalConfirmations(string expectedPolicy, string? policy, List<PlcDownloadPortalConfirmationInfo>? entries,
        int observed, bool complete)
    {
        Require(entries);
        var count = entries!.Count;
        var answer = PlcDownloadPolicy.PortalConfirmationPolicies.AnswerFor(policy);
        if (!PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(policy) || policy != expectedPolicy
            || observed < count || complete != (observed == count)
            || !complete && count != PlcDownloadPolicy.MaxPortalConfirmations
            || count > PlcDownloadPolicy.MaxPortalConfirmations
            || entries.Any(entry => string.IsNullOrWhiteSpace(entry.Type) || entry.Answer != answer
                || entry.Text is { Length: > PlcDownloadPolicy.MaxTextChars }))
            throw new JsonException(ProtocolFailureMessage);
    }

    /// <summary>
    /// A recorded answer is trusted only when it is exactly the caller's entry for that key and comes
    /// before any abort; an abort needs an aborted entry and a known reason. The TLS verification is never a
    /// caller decision: it may be <c>Trusted</c> only with evidence in which every presented certificate is in
    /// <paramref name="trustedCertificates"/> (<see cref="PlcTlsTrustPolicy.IsConsistent"/>); no other entry carries
    /// TLS evidence, and the abort carries exactly the evidence of the first aborted entry.
    /// </summary>
    internal static void ValidateCallbacks(List<PlcCallbackConfigurationInfo>? seen, PlcCallbackAbortInfo? abort,
        IReadOnlyList<PlcCallbackDecisionInfo> decisions, IReadOnlyList<PlcCallbackPasswordInfo> passwords,
        IReadOnlyCollection<string>? trustedCertificates = null, bool plcSimInterface = false, bool allowInsecurePasswordTransfer = false,
        bool passwordsUnknown = false)
    {
        Require(seen);
        if (seen!.Count > PlcCallbackPolicy.MaxConfigurations + 1) throw new JsonException(ProtocolFailureMessage);
        var trusted = trustedCertificates ?? Array.Empty<string>();
        var selections = decisions.ToDictionary(d => d.Type, d => d.Selection, StringComparer.Ordinal);
        var variables = passwords.ToDictionary(p => p.Type, p => p.Variable, StringComparer.Ordinal);
        var aborted = false;
        PlcCallbackConfigurationInfo? firstAborted = null;
        foreach (var entry in seen)
        {
            Require(entry.AvailableSelections);
            if (string.IsNullOrWhiteSpace(entry.Type) || entry.Kind is not (PlcCallbackPolicy.Kinds.Selection or PlcCallbackPolicy.Kinds.Password or PlcCallbackPolicy.Kinds.Other)
                || (entry.CanonicalType is null) != (entry.DecisionKey is null)
                || entry.DecisionKey is not null && PlcCallbackPolicy.DecisionKey(entry.CanonicalType) != entry.DecisionKey)
                throw new JsonException(ProtocolFailureMessage);
            // Audit T1: IsSecureCommunication is read only for a password configuration the caller has a password for.
            // (compare_to_online validates without the merged password list, so the key is not checked against it here.)
            if (entry.SecureCommunication is not null && (entry.Kind != PlcCallbackPolicy.Kinds.Password || entry.DecisionKey is null)
                || entry.Decision == PlcCallbackPolicy.Decisions.PasswordSet && entry.SecureCommunication != true && !allowInsecurePasswordTransfer)
                throw new JsonException(ProtocolFailureMessage);
            var tlsEntry = PlcTlsTrustPolicy.IsTlsVerification(entry.CanonicalType);
            var trustedAnswer = tlsEntry && entry.Decision == PlcCallbackPolicy.Decisions.Selected && entry.Selection == PlcTlsTrustPolicy.Trusted;
            if (!tlsEntry && entry.Tls is not null
                || tlsEntry && !PlcTlsTrustPolicy.IsConsistent(entry.Tls, trustedAnswer, trusted, plcSimInterface))
                throw new JsonException(ProtocolFailureMessage);
            switch (entry.Decision)
            {
                case PlcCallbackPolicy.Decisions.Selected when tlsEntry && entry.Selection == PlcTlsTrustPolicy.NonVerified:
                    // K52: PLCSIM without any certificate, answered by the owner's rule (IsConsistent checked the evidence).
                    if (aborted || entry.Kind != PlcCallbackPolicy.Kinds.Selection || entry.PasswordVariable is not null
                        || entry.Tls?.SelectionSet != PlcTlsTrustPolicy.NonVerified)
                        throw new JsonException(ProtocolFailureMessage);
                    break;
                case PlcCallbackPolicy.Decisions.Selected when tlsEntry && entry.Selection == PlcTlsTrustPolicy.NonTrusted:
                    // A refused TLS question (K27): answered NonTrusted without a throw; it is (or follows) the abort.
                    if (entry.Kind != PlcCallbackPolicy.Kinds.Selection || entry.PasswordVariable is not null
                        || entry.Tls?.SelectionSet != PlcTlsTrustPolicy.NonTrusted || abort is null)
                        throw new JsonException(ProtocolFailureMessage);
                    aborted = true;
                    firstAborted ??= entry;
                    break;
                case PlcCallbackPolicy.Decisions.Selected when tlsEntry:
                    if (aborted || entry.Kind != PlcCallbackPolicy.Kinds.Selection || entry.PasswordVariable is not null
                        || entry.Selection != PlcTlsTrustPolicy.Trusted)
                        throw new JsonException(ProtocolFailureMessage);
                    break;
                case PlcCallbackPolicy.Decisions.Selected when entry.Selection == PlcPasswordChannel.NoActionSelection && abort is not null
                    && entry.DecisionKey is not null
                    && (aborted || !selections.TryGetValue(entry.DecisionKey, out var callerChoice) || callerChoice != PlcPasswordChannel.NoActionSelection):
                    // Audit T2: an unanswered selection answered NoAction by the server instead of a throw; it is (or follows) the abort.
                    if (entry.Kind != PlcCallbackPolicy.Kinds.Selection || entry.PasswordVariable is not null
                        || !entry.AvailableSelections.Contains(PlcPasswordChannel.NoActionSelection, StringComparer.Ordinal))
                        throw new JsonException(ProtocolFailureMessage);
                    aborted = true;
                    firstAborted ??= entry;
                    break;
                case PlcCallbackPolicy.Decisions.Selected:
                    if (aborted || entry.Kind != PlcCallbackPolicy.Kinds.Selection || entry.PasswordVariable is not null || entry.DecisionKey is null
                        || !selections.TryGetValue(entry.DecisionKey, out var selection) || entry.Selection != selection)
                        throw new JsonException(ProtocolFailureMessage);
                    break;
                case PlcCallbackPolicy.Decisions.PasswordSet:
                    if (aborted || tlsEntry || entry.Kind != PlcCallbackPolicy.Kinds.Password || entry.Selection is not null || entry.DecisionKey is null
                        || (passwordsUnknown
                            ? !Contracts.Shared.PasswordSources.IsSource(entry.PasswordVariable)
                            : !variables.TryGetValue(entry.DecisionKey, out var variable) || entry.PasswordVariable != variable))
                        throw new JsonException(ProtocolFailureMessage);
                    break;
                case PlcCallbackPolicy.Decisions.Aborted:
                    if (entry.Selection is not null || entry.PasswordVariable is not null || abort is null) throw new JsonException(ProtocolFailureMessage);
                    aborted = true;
                    firstAborted ??= entry;
                    break;
                default:
                    throw new JsonException(ProtocolFailureMessage);
            }
        }

        if (abort is not null && (!aborted || string.IsNullOrWhiteSpace(abort.Type) || !PlcCallbackPolicy.AbortReasons.All.Contains(abort.Reason)
            || abort.AvailableSelections is null
            || !string.Equals(CanonicalJson.Serialize(abort.Tls), CanonicalJson.Serialize(firstAborted!.Tls), StringComparison.Ordinal)
            || abort.Tls is not null && !PlcTlsTrustPolicy.IsTlsVerificationKey(abort.DecisionKey)
            || abort.Reason == PlcCallbackPolicy.AbortReasons.InsecureCommunication
                && (allowInsecurePasswordTransfer || firstAborted!.Kind != PlcCallbackPolicy.Kinds.Password || firstAborted.SecureCommunication == true)
            || abort.Reason == PlcCallbackPolicy.AbortReasons.CertificateNotTrusted
                && (!PlcTlsTrustPolicy.IsTlsVerificationKey(abort.DecisionKey) || abort.Tls is not { } refused || PlcTlsTrustPolicy.Trusts(refused))))
            throw new JsonException(ProtocolFailureMessage);
    }

    private static bool SamePath(PlcConnectionPathInfo? actual, PlcConnectionPathInfo expected)
        => actual?.PcInterface is not null && actual.Mode == expected.Mode && actual.TargetInterface == expected.TargetInterface
            && actual.PcInterface.Name == expected.PcInterface.Name && actual.PcInterface.Number == expected.PcInterface.Number;

    private static void Require<T>(List<T>? values)
    {
        if (values is null) throw new JsonException(ProtocolFailureMessage);
        StandalonePayloadContract.RejectNullElements(values);
    }
}
