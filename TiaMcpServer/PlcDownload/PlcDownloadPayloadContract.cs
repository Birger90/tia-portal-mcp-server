using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.Tools;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;

namespace TiaMcpServer.PlcDownload;

/// <summary>
/// The only decoder of <c>list_download_targets</c> and <c>plc_download</c> worker payloads. Each
/// goes through the required-member worker-payload reader plus semantic checks; any violation is a
/// <see cref="JsonException"/>, which callers turn into <c>protocol_error</c> without echoing it.
/// </summary>
public static class PlcDownloadPayloadContract
{
    public const string ProtocolFailureMessage = "The worker payload did not match the declared plc_download result contract.";

    public static PlcDownloadTargetInventoryInfo DecodeInventory(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcDownloadTargetInventoryInfo>(payload, ValidateInventory).Value;

    public static JsonElement NormalizeInventory(string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcDownloadTargetInventoryInfo>(payload, ValidateInventory).Element;

    /// <summary>
    /// Decodes a download result and checks it describes exactly <paramref name="expected"/>.
    /// <paramref name="requireDownloadResult"/> is true for a worker success, which always projects a
    /// returned <c>DownloadResult</c> (or records an abort).
    /// </summary>
    public static PlcDownloadResultInfo DecodeResult(string payload, PlcDownloadEffects expected, bool requireDownloadResult)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var result = CanonicalJson.DeserializeWorkerPayload<PlcDownloadResultInfo>(payload);
        if (result.Operation != PlcDownloadMethods.Download
            || result.PlcName != expected.PlcName || result.DeviceName != expected.DeviceName
            || result.Path is null || result.Path.PcInterface is null
            || result.Path.Mode != expected.Path.Mode || result.Path.TargetInterface != expected.Path.TargetInterface
            || result.Path.PcInterface.Name != expected.Path.PcInterface.Name
            || result.Path.PcInterface.Number != expected.Path.PcInterface.Number
            || result.Scope != expected.Scope || result.DownloadOptions != expected.DownloadOptions
            || result.StartAfterDownload != expected.StartAfterDownload
            || result.PortalConfirmationPolicy != expected.PortalConfirmations
            // Completion is never verified; a worker claiming otherwise is not trusted.
            || result.CompletionVerified || string.IsNullOrWhiteSpace(result.CompletionNote))
            throw new JsonException(ProtocolFailureMessage);

        if (result.State is null)
        {
            if (result.ErrorCount is not null || result.WarningCount is not null || result.Messages.Count != 0
                || (requireDownloadResult && result.Abort is null))
                throw new JsonException(ProtocolFailureMessage);
        }
        else if (string.IsNullOrWhiteSpace(result.State) || result.ErrorCount is not >= 0 || result.WarningCount is not >= 0)
        {
            throw new JsonException(ProtocolFailureMessage);
        }

        ValidateMessages(result);
        ValidateConfigurations(result, expected);
        ValidatePortalConfirmations(result);
        return result;
    }

    private static void ValidateInventory(PlcDownloadTargetInventoryInfo inventory)
    {
        Require(inventory.Plcs);
        foreach (var plc in inventory.Plcs)
        {
            Require(plc.Modes);
            if (plc.PlcName is null || plc.DeviceName is null || !plc.Downloadable && plc.Modes.Count != 0)
                throw new JsonException(ProtocolFailureMessage);
            foreach (var mode in plc.Modes)
            {
                Require(mode.PcInterfaces);
                if (mode.Name is null) throw new JsonException(ProtocolFailureMessage);
                foreach (var pcInterface in mode.PcInterfaces)
                {
                    Require(pcInterface.TargetInterfaces);
                    if (pcInterface.Name is null || pcInterface.TargetInterfaces.Any(target => target.Name is null))
                        throw new JsonException(ProtocolFailureMessage);
                }
            }
        }
    }

    private static void ValidateMessages(PlcDownloadResultInfo result)
    {
        if (result.MessagesComplete != (result.MessageOmission is null)) throw new JsonException(ProtocolFailureMessage);
        var count = 0;
        void Walk(List<PlcDownloadMessageInfo>? messages, int depth)
        {
            Require(messages);
            if (messages!.Count > 0 && depth > PlcDownloadPolicy.MaxMessageDepth) throw new JsonException(ProtocolFailureMessage);
            foreach (var message in messages)
            {
                if (++count > PlcDownloadPolicy.MaxMessages) throw new JsonException(ProtocolFailureMessage);
                Walk(message.Messages, depth + 1);
            }
        }

        Walk(result.Messages, 1);
    }

    /// <summary>
    /// Every applied answer must be exactly what the planned decision set prescribes for that
    /// configuration (caller password or decision, else the built-in default for its phase, with the
    /// matching source), and only before the first abort; the abort must describe the first aborted
    /// entry, and its reason must fit what the decision set would have answered. The TLS verification of
    /// the online legitimation is checked against its own evidence instead: a <c>Trusted</c> answer (source
    /// <c>caller</c>) needs evidence in which every presented certificate is in the planned trusted list
    /// (<see cref="PlcTlsTrustPolicy.IsConsistent"/>), and no other entry may carry TLS evidence. A refused TLS question is
    /// recorded as answered <c>NonTrusted</c> (source <c>policy</c>, no throw) and anchors the abort like an aborted entry.
    /// </summary>
    private static void ValidateConfigurations(PlcDownloadResultInfo result, PlcDownloadEffects expected)
    {
        Require(result.ConfigurationsSeen);
        if (result.ConfigurationsSeen.Count > PlcDownloadPolicy.MaxConfigurations + 1) throw new JsonException(ProtocolFailureMessage);
        var decisions = new PlcDownloadDecisionSet(expected.Decisions, expected.Passwords, expected.StartAfterDownload, expected.Path.PcInterface.Name,
            expected.TrustedCertificates);
        PlcDownloadConfigurationInfo? firstAborted = null;
        var firstAbortedIndex = -1;
        for (var index = 0; index < result.ConfigurationsSeen.Count; index++)
        {
            var seen = result.ConfigurationsSeen[index];
            if (!PlcDownloadPolicy.Phases.IsKnown(seen.Phase) || string.IsNullOrWhiteSpace(seen.Type)
                || !PlcDownloadPolicy.Decisions.IsKnown(seen.Decision))
                throw new JsonException(ProtocolFailureMessage);
            ValidateShape(seen.Phase, seen.CanonicalType, seen.DecisionKey, seen.Kind, seen.AvailableSelections, seen.Message);
            var tlsEntry = IsTls(seen.Phase, seen.CanonicalType);
            var trustedAnswer = tlsEntry && seen.Decision == PlcDownloadPolicy.Decisions.Selected && seen.Selection == PlcTlsTrustPolicy.Trusted;
            if (!tlsEntry && seen.Tls is not null
                || tlsEntry && !PlcTlsTrustPolicy.IsConsistent(seen.Tls, trustedAnswer, decisions.TrustedCertificates))
                throw new JsonException(ProtocolFailureMessage);
            if (tlsEntry && seen.Decision == PlcDownloadPolicy.Decisions.Selected && seen.Selection == PlcTlsTrustPolicy.NonTrusted)
            {
                // A refused TLS question (K27): answered NonTrusted by the server policy without a throw; the call is aborted, and
                // the first refusal or aborted entry is the one the abort describes.
                if (seen.Kind != PlcDownloadPolicy.Kinds.Selection || seen.Source != PlcDownloadPolicy.Sources.Policy || seen.PasswordVariable is not null
                    || seen.Tls?.SelectionSet != PlcTlsTrustPolicy.NonTrusted || result.Abort is null)
                    throw new JsonException(ProtocolFailureMessage);
                if (firstAborted is null)
                {
                    firstAborted = seen;
                    firstAbortedIndex = index;
                }
            }
            else if (tlsEntry && seen.Decision != PlcDownloadPolicy.Decisions.Aborted)
            {
                if (firstAborted is not null || seen.Decision != PlcDownloadPolicy.Decisions.Selected || seen.Kind != PlcDownloadPolicy.Kinds.Selection
                    || seen.Selection != PlcTlsTrustPolicy.Trusted || seen.Source != PlcDownloadPolicy.Sources.Caller || seen.PasswordVariable is not null)
                    throw new JsonException(ProtocolFailureMessage);
            }
            else if (seen.Decision != PlcDownloadPolicy.Decisions.Aborted)
            {
                var prescribed = decisions.Resolve(seen.Phase, seen.CanonicalType);
                if (firstAborted is not null || !prescribed.Handled || prescribed.Decision != seen.Decision
                    || !string.Equals(seen.Selection, prescribed.Selection, StringComparison.Ordinal)
                    || !string.Equals(seen.Source, prescribed.Source, StringComparison.Ordinal)
                    || !string.Equals(seen.PasswordVariable, prescribed.PasswordVariable, StringComparison.Ordinal))
                    throw new JsonException(ProtocolFailureMessage);
            }
            else
            {
                if (seen.Selection is not null || seen.Source is not null || seen.PasswordVariable is not null || result.Abort is null)
                    throw new JsonException(ProtocolFailureMessage);
                if (firstAborted is null)
                {
                    firstAborted = seen;
                    firstAbortedIndex = index;
                }
            }
        }

        if (result.Abort is not { } abort) return;
        if (firstAborted is null || !PlcDownloadPolicy.Phases.IsKnown(abort.Phase) || string.IsNullOrWhiteSpace(abort.Type)
            || !PlcDownloadPolicy.AbortReasons.IsKnown(abort.Reason)
            || abort.Detail is { Length: > PlcDownloadPolicy.MaxTextChars })
            throw new JsonException(ProtocolFailureMessage);
        ValidateShape(abort.Phase, abort.CanonicalType, abort.DecisionKey, abort.Kind, abort.AvailableSelections, abort.Message);
        if (abort.Phase != firstAborted.Phase || abort.Type != firstAborted.Type || abort.CanonicalType != firstAborted.CanonicalType
            || abort.DecisionKey != firstAborted.DecisionKey || abort.Kind != firstAborted.Kind || abort.Message != firstAborted.Message
            || !abort.AvailableSelections.SequenceEqual(firstAborted.AvailableSelections, StringComparer.Ordinal)
            // The abort carries exactly the TLS evidence of the entry it describes.
            || !string.Equals(CanonicalJson.Serialize(abort.Tls), CanonicalJson.Serialize(firstAborted.Tls), StringComparison.Ordinal))
            throw new JsonException(ProtocolFailureMessage);

        var tlsAbort = IsTls(abort.Phase, abort.CanonicalType);
        var prescribedForAbort = decisions.Resolve(abort.Phase, abort.CanonicalType);
        var fits = abort.Reason switch
        {
            PlcDownloadPolicy.AbortReasons.ConfigurationLimitExceeded => firstAbortedIndex == PlcDownloadPolicy.MaxConfigurations,
            PlcDownloadPolicy.AbortReasons.UnsupportedConfiguration => abort.CanonicalType is null,
            // Never trusted: the evidence (when read) does not allow Trusted.
            PlcDownloadPolicy.AbortReasons.CertificateNotTrusted => tlsAbort && abort.Tls is { } refused && !PlcTlsTrustPolicy.Trusts(refused),
            // The TLS answer was attempted (Trusted or NonTrusted could not be set, or a member was missing).
            _ when tlsAbort => abort.Reason is PlcDownloadPolicy.AbortReasons.DecisionNotApplicable or PlcDownloadPolicy.AbortReasons.SelectionUnavailable
                or PlcDownloadPolicy.AbortReasons.DecisionRejected,
            PlcDownloadPolicy.AbortReasons.DecisionRequired => prescribedForAbort.AbortReason == PlcDownloadPolicy.AbortReasons.DecisionRequired,
            PlcDownloadPolicy.AbortReasons.SelectionUnavailable => prescribedForAbort.Decision == PlcDownloadPolicy.Decisions.Selected,
            PlcDownloadPolicy.AbortReasons.PasswordUnavailable => prescribedForAbort.Decision == PlcDownloadPolicy.Decisions.PasswordSet,
            _ => prescribedForAbort.Handled, // decision_not_applicable, decision_rejected: an answer was attempted.
        };
        var detailAllowed = abort.Reason is PlcDownloadPolicy.AbortReasons.DecisionNotApplicable
            or PlcDownloadPolicy.AbortReasons.SelectionUnavailable or PlcDownloadPolicy.AbortReasons.PasswordUnavailable
            or PlcDownloadPolicy.AbortReasons.CertificateNotTrusted;
        if (!fits || abort.Detail is not null && !detailAllowed || firstAbortedIndex > PlcDownloadPolicy.MaxConfigurations)
            throw new JsonException(ProtocolFailureMessage);
    }

    private static bool IsTls(string? phase, string? canonicalType)
        => phase == PlcDownloadPolicy.Phases.OnlineLegitimation && PlcTlsTrustPolicy.IsTlsVerification(canonicalType);

    private static void ValidateShape(string? phase, string? canonicalType, string? decisionKey, string? kind, List<string>? availableSelections, string? message)
    {
        Require(availableSelections);
        if (canonicalType is not null && PlcDownloadPolicy.DecisionKey(phase, canonicalType) is null
            || !string.Equals(decisionKey, PlcDownloadPolicy.DecisionKey(phase, canonicalType), StringComparison.Ordinal)
            || !PlcDownloadPolicy.Kinds.IsKnown(kind)
            || availableSelections!.Count > PlcDownloadPolicy.MaxAvailableSelections
            || availableSelections.Any(string.IsNullOrWhiteSpace)
            || kind != PlcDownloadPolicy.Kinds.Selection && availableSelections.Count != 0
            || message is { Length: > PlcDownloadPolicy.MaxTextChars }
            // Online configurations document no Message; the recorder never reads one for them.
            || phase == PlcDownloadPolicy.Phases.OnlineLegitimation && message is not null)
            throw new JsonException(ProtocolFailureMessage);
    }

    private static void ValidatePortalConfirmations(PlcDownloadResultInfo result)
    {
        Require(result.PortalConfirmations);
        var count = result.PortalConfirmations.Count;
        var answer = PlcDownloadPolicy.PortalConfirmationPolicies.AnswerFor(result.PortalConfirmationPolicy);
        if (!PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(result.PortalConfirmationPolicy)
            || result.PortalConfirmationsObserved < count
            || result.PortalConfirmationsComplete != (result.PortalConfirmationsObserved == count)
            || !result.PortalConfirmationsComplete && count != PlcDownloadPolicy.MaxPortalConfirmations
            || count > PlcDownloadPolicy.MaxPortalConfirmations
            || result.PortalConfirmations.Any(confirmation => string.IsNullOrWhiteSpace(confirmation.Type)
                || !string.Equals(confirmation.Answer, answer, StringComparison.Ordinal)
                || confirmation.Text is { Length: > PlcDownloadPolicy.MaxTextChars }))
            throw new JsonException(ProtocolFailureMessage);
    }

    private static void Require<T>(List<T>? values)
    {
        if (values is null) throw new JsonException(ProtocolFailureMessage);
        StandalonePayloadContract.RejectNullElements(values);
    }
}
