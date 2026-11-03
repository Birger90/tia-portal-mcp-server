using System.Runtime.ExceptionServices;
using TiaMcpServer.Contracts.PlcOnline;

namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// The fail-closed decision loop behind <c>plc_download</c>'s pre- and post-download delegates and the
/// <c>OnlineLegitimation</c> handler on the download provider's connection configuration, shared by the
/// real worker and the FakeWorker and unit-tested without Siemens types. Every configuration is
/// recorded in <see cref="PlcDownloadResultInfo.ConfigurationsSeen"/> (up to
/// <see cref="PlcDownloadPolicy.MaxConfigurations"/> + 1 entries) with the answer given and its source
/// (<see cref="PlcDownloadDecisionSet"/>: caller password or decision, else built-in default). The TLS
/// verification of the connection is answered only by <see cref="PlcTlsTrustPolicy"/> from the caller's
/// trusted fingerprints (<c>Trusted</c>, source <c>caller</c>), otherwise <c>NonTrusted</c> (source <c>policy</c>) and an
/// abort that carries the certificate evidence. When <c>NonTrusted</c> was set the TLS callback does NOT throw: it returns
/// normally, records <see cref="PlcDownloadResultInfo.Abort"/> and lets TIA Portal fail the connection itself (K27: with the
/// former throw from this callback TIA Portal terminated, reproduced live 4x on 2026-11-02; refusing in the TIA UI with
/// "Verbindung abbrechen", the same NonTrusted answer, left TIA Portal running). Only when NonTrusted cannot be set does the
/// TLS callback still throw (a known risk of terminating TIA Portal, but the only remaining refusal). Any other configuration
/// that is not answered sets <see cref="PlcDownloadResultInfo.Abort"/> (the first one wins) and throws
/// <see cref="PlcDownloadAbortedException"/>; every later callback is recorded as aborted and throws again (whether that throw
/// can terminate TIA Portal as well is not known: no live evidence), except a later TLS verification, which is answered
/// NonTrusted again without a throw. The live configuration stays opaque behind <see cref="IPlcDownloadConfigurationAccess"/>.
/// </summary>
public sealed class PlcDownloadCallbackRecorder
{
    private readonly object _sync = new object();
    private readonly PlcDownloadDecisionSet _decisions;
    private readonly PlcDownloadResultInfo _result;
    private readonly IPlcDownloadConfigurationAccess _access;
    private readonly Func<Exception, bool> _isFatal;
    private readonly Func<object, string?> _legitimationCanonicalType;
    private readonly Func<object, IReadOnlyCollection<string>, PlcTlsVerificationInfo> _readTls;

    /// <param name="decisions">The answers this download may give.</param>
    /// <param name="result">The evidence the decisions are recorded into.</param>
    /// <param name="access">Reads and answers the live configuration objects.</param>
    /// <param name="isFatal">
    /// Exceptions of an answer that are rethrown unchanged after the configuration is recorded (for
    /// example a lost TIA Portal), so their own classification survives.
    /// </param>
    /// <param name="legitimationCanonicalType">
    /// The canonical type of an online legitimation configuration; defaults to the exact type walk in
    /// <c>Siemens.Engineering.Online.Configurations</c> (<see cref="PlcDownloadPolicy.CanonicalConfigurationType(string, Type)"/>).
    /// </param>
    /// <param name="readTls">Reads the TLS evidence of a live configuration; defaults to <see cref="PlcTlsTrustPolicy.Read"/>.</param>
    public PlcDownloadCallbackRecorder(PlcDownloadDecisionSet decisions, PlcDownloadResultInfo result,
        IPlcDownloadConfigurationAccess access, Func<Exception, bool>? isFatal = null,
        Func<object, string?>? legitimationCanonicalType = null,
        Func<object, IReadOnlyCollection<string>, PlcTlsVerificationInfo>? readTls = null)
    {
        _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _isFatal = isFatal ?? (_ => false);
        _legitimationCanonicalType = legitimationCanonicalType
            ?? (configuration => PlcDownloadPolicy.CanonicalConfigurationType(PlcDownloadPolicy.Phases.OnlineLegitimation, configuration.GetType()));
        _readTls = readTls ?? ((configuration, trusted) => PlcTlsTrustPolicy.Read(configuration, trusted, _isFatal));
    }

    /// <summary>The exception that rejected the first rejected answer (<c>decision_rejected</c>), if any.</summary>
    public Exception? DecisionFailure { get; private set; }

    public void PreDownload(object configuration) => Handle(PlcDownloadPolicy.Phases.PreDownload, configuration);

    public void PostDownload(object configuration) => Handle(PlcDownloadPolicy.Phases.PostDownload, configuration);

    /// <summary>A configuration TIA Portal presented through <c>ConnectionConfiguration.OnlineLegitimation</c> while the download connects.</summary>
    public void OnlineLegitimation(object configuration) => Handle(PlcDownloadPolicy.Phases.OnlineLegitimation, configuration);

    /// <summary>Records and answers one configuration, or records it as aborted and throws.</summary>
    public void Handle(string phase, object configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        lock (_sync)
        {
            var legitimation = phase == PlcDownloadPolicy.Phases.OnlineLegitimation;
            var type = _access.TypeName(configuration);
            if (string.IsNullOrWhiteSpace(type)) type = configuration.GetType().Name;
            // Online configurations document no Message; the download configuration message reader never sees them.
            var message = legitimation ? null : PlcDownloadPolicy.BoundText(_access.ReadMessage(configuration));
            var canonical = Canonical(phase, configuration);
            var key = PlcDownloadPolicy.DecisionKey(phase, canonical);
            var shape = Shape(configuration);
            PlcTlsVerificationInfo? tls = null;
            Exception? rejected = null;
            string? detail = null;
            string? refusal = null;
            var tlsQuestion = legitimation && PlcTlsTrustPolicy.IsTlsVerification(canonical);
            PlcDownloadCallbackDecision decision;
            if (_result.Abort is not null)
                decision = tlsQuestion ? RefuseTlsAfterAbort(configuration, shape, out tls, out rejected) : PlcDownloadCallbackDecision.Abort(_result.Abort.Reason);
            else if (_result.ConfigurationsSeen.Count >= PlcDownloadPolicy.MaxConfigurations)
                decision = PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.ConfigurationLimitExceeded);
            else if (tlsQuestion)
                decision = AnswerTls(configuration, shape, out tls, out detail, out rejected, out refusal);
            else
            {
                decision = _decisions.Resolve(phase, canonical);
                if (decision.Handled)
                {
                    try
                    {
                        var applied = decision.Decision switch
                        {
                            PlcDownloadPolicy.Decisions.Selected => _access.ApplySelection(configuration, decision.Selection!),
                            PlcDownloadPolicy.Decisions.Checked => _access.ApplyCheck(configuration),
                            PlcDownloadPolicy.Decisions.PasswordSet => _access.ApplyPassword(configuration, decision.PasswordVariable!),
                            _ => PlcDownloadApplyOutcome.NotApplicable("the answer kind is unknown"),
                        };
                        if (!applied.Applied)
                        {
                            detail = PlcDownloadPolicy.BoundText(applied.Detail);
                            decision = PlcDownloadCallbackDecision.Abort(applied.AbortReason!);
                        }
                    }
                    catch (Exception exception)
                    {
                        rejected = exception;
                        decision = PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.DecisionRejected);
                    }
                }
            }

            if (rejected is not null) DecisionFailure ??= rejected;
            if (_result.ConfigurationsSeen.Count <= PlcDownloadPolicy.MaxConfigurations)
                _result.ConfigurationsSeen.Add(new PlcDownloadConfigurationInfo
                {
                    Phase = phase,
                    Type = type,
                    CanonicalType = canonical,
                    DecisionKey = key,
                    Kind = shape.Kind,
                    Message = message,
                    AvailableSelections = shape.AvailableSelections.ToList(),
                    Decision = decision.Handled ? decision.Decision! : PlcDownloadPolicy.Decisions.Aborted,
                    Selection = decision.Selection,
                    Source = decision.Source,
                    PasswordVariable = decision.PasswordVariable,
                    Tls = tls,
                });
            if (decision.Handled && refusal is null) return;

            _result.Abort ??= new PlcDownloadAbortInfo
            {
                Phase = phase,
                Type = type,
                CanonicalType = canonical,
                DecisionKey = key,
                Kind = shape.Kind,
                Message = message,
                AvailableSelections = shape.AvailableSelections.ToList(),
                Reason = refusal ?? decision.AbortReason!,
                Detail = detail,
                Tls = tls,
            };
            // The TLS question was refused with NonTrusted: return normally and let TIA Portal fail the connection (K27).
            if (decision.Handled) return;
            if (rejected is not null && _isFatal(rejected))
                ExceptionDispatchInfo.Capture(rejected).Throw();
            throw new PlcDownloadAbortedException(phase, type, rejected);
        }
    }

    /// <summary>
    /// The TLS trust question of the connection: never answered by a decision or default. The evidence is read
    /// first (for the human); <c>Trusted</c> is set only when <see cref="PlcTlsTrustPolicy.Trusts"/> allows it.
    /// Otherwise <c>NonTrusted</c> is set where possible: then the answer is handled (selection <c>NonTrusted</c>, source
    /// <c>policy</c>) and <paramref name="refusal"/> names the abort reason the caller records without throwing. Only when
    /// NonTrusted could not be set is the answer an abort (the caller throws).
    /// </summary>
    private PlcDownloadCallbackDecision AnswerTls(object configuration, PlcDownloadConfigurationShape shape,
        out PlcTlsVerificationInfo? tls, out string? detail, out Exception? rejected, out string? refusal)
    {
        tls = null;
        detail = null;
        rejected = null;
        refusal = null;
        try
        {
            tls = _readTls(configuration, _decisions.TrustedCertificates);
        }
        catch (Exception exception)
        {
            // Only a fatal exception (a lost Portal) escapes the reader: recorded, then rethrown unchanged.
            rejected = exception;
            return PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.DecisionRejected);
        }

        if (shape.Kind != PlcDownloadPolicy.Kinds.Selection)
        {
            if (!tls.UnreadableMembers.Contains(PlcTlsTrustPolicy.Members.CurrentSelection)) tls.UnreadableMembers.Add(PlcTlsTrustPolicy.Members.CurrentSelection);
            detail = "it has no single public readable and writable enum CurrentSelection property, so neither Trusted nor NonTrusted can be set";
            return PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.DecisionNotApplicable);
        }

        if (!PlcTlsTrustPolicy.Trusts(tls))
        {
            // Nothing is written to a CurrentSelection that is missing or not of the documented type.
            rejected = PlcTlsTrustPolicy.CanSetSelection(tls) ? SetNonTrusted(configuration, tls) : null;
            if (rejected is not null) return PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.DecisionRejected);
            detail = PlcDownloadPolicy.BoundText(PlcTlsTrustPolicy.RefusalDetail(tls, _decisions.TrustedCertificates.Count));
            return Refused(tls, tls.UnreadableMembers.Count > 0
                ? PlcDownloadPolicy.AbortReasons.DecisionNotApplicable : PlcDownloadPolicy.AbortReasons.CertificateNotTrusted, out refusal);
        }

        PlcDownloadApplyOutcome applied;
        try
        {
            applied = _access.ApplySelection(configuration, PlcTlsTrustPolicy.Trusted);
        }
        catch (Exception exception)
        {
            if (_isFatal(exception))
            {
                rejected = exception;
                return PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.DecisionRejected);
            }

            rejected = SetNonTrusted(configuration, tls);
            if (rejected is not null) return PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.DecisionRejected);
            // The Trusted setter's exception stays the decision failure of the refusal.
            rejected = exception;
            return Refused(tls, PlcDownloadPolicy.AbortReasons.DecisionRejected, out refusal);
        }

        if (!applied.Applied)
        {
            rejected = SetNonTrusted(configuration, tls);
            if (rejected is not null) return PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.DecisionRejected);
            detail = PlcDownloadPolicy.BoundText(applied.Detail);
            return Refused(tls, applied.AbortReason!, out refusal);
        }

        tls.SelectionSet = PlcTlsTrustPolicy.Trusted;
        tls.Source = PlcTlsTrustPolicy.CallerSource;
        return PlcDownloadCallbackDecision.Select(PlcTlsTrustPolicy.Trusted, PlcDownloadPolicy.Sources.Caller);
    }

    /// <summary>
    /// A refusal: handled (NonTrusted, source policy, no throw) when <c>NonTrusted</c> was set, otherwise an abort the caller
    /// throws (the only remaining way to refuse; throwing from this callback was observed to precede TIA Portal terminating).
    /// </summary>
    private static PlcDownloadCallbackDecision Refused(PlcTlsVerificationInfo tls, string reason, out string? refusal)
    {
        if (tls.SelectionSet == PlcTlsTrustPolicy.NonTrusted)
        {
            refusal = reason;
            return PlcDownloadCallbackDecision.Select(PlcTlsTrustPolicy.NonTrusted, PlcDownloadPolicy.Sources.Policy);
        }

        refusal = null;
        return PlcDownloadCallbackDecision.Abort(reason);
    }

    /// <summary>
    /// A TLS question after the download was already aborted: never trusted; answered NonTrusted without a throw where it can be
    /// set (the abort already recorded stays the call's abort), otherwise recorded as aborted and thrown like any later callback.
    /// </summary>
    private PlcDownloadCallbackDecision RefuseTlsAfterAbort(object configuration, PlcDownloadConfigurationShape shape,
        out PlcTlsVerificationInfo? tls, out Exception? rejected)
    {
        tls = null;
        rejected = null;
        try
        {
            tls = _readTls(configuration, _decisions.TrustedCertificates);
        }
        catch (Exception exception)
        {
            rejected = exception;
            return PlcDownloadCallbackDecision.Abort(_result.Abort!.Reason);
        }

        if (shape.Kind == PlcDownloadPolicy.Kinds.Selection && PlcTlsTrustPolicy.CanSetSelection(tls))
            rejected = SetNonTrusted(configuration, tls);
        return rejected is null && tls.SelectionSet == PlcTlsTrustPolicy.NonTrusted
            ? PlcDownloadCallbackDecision.Select(PlcTlsTrustPolicy.NonTrusted, PlcDownloadPolicy.Sources.Policy)
            : PlcDownloadCallbackDecision.Abort(_result.Abort!.Reason);
    }

    /// <summary>Sets <c>NonTrusted</c> where possible; returns a fatal exception of that attempt (recorded and rethrown by the caller).</summary>
    private Exception? SetNonTrusted(object configuration, PlcTlsVerificationInfo tls)
    {
        try
        {
            if (_access.ApplySelection(configuration, PlcTlsTrustPolicy.NonTrusted).Applied) tls.SelectionSet = PlcTlsTrustPolicy.NonTrusted;
            return null;
        }
        catch (Exception exception) when (_isFatal(exception))
        {
            return exception;
        }
        catch (Exception)
        {
            // Not set: the abort that follows refuses the connection by throwing.
            return null;
        }
    }

    private string? Canonical(string phase, object configuration)
    {
        try
        {
            var canonical = phase == PlcDownloadPolicy.Phases.OnlineLegitimation
                ? _legitimationCanonicalType(configuration)
                : _access.CanonicalType(configuration);
            return PlcDownloadPolicy.DecisionKey(phase, canonical) is null ? null : canonical;
        }
        catch (Exception)
        {
            // Fail closed: a configuration whose type cannot be determined has no decision key.
            return null;
        }
    }

    private PlcDownloadConfigurationShape Shape(object configuration)
    {
        try
        {
            var shape = _access.Describe(configuration);
            if (shape is null || !PlcDownloadPolicy.Kinds.IsKnown(shape.Kind)) return PlcDownloadConfigurationShape.Other;
            var names = shape.Kind == PlcDownloadPolicy.Kinds.Selection
                ? shape.AvailableSelections.Where(name => !string.IsNullOrWhiteSpace(name)).Take(PlcDownloadPolicy.MaxAvailableSelections).ToArray()
                : Array.Empty<string>();
            return new PlcDownloadConfigurationShape(shape.Kind, names);
        }
        catch (Exception)
        {
            return PlcDownloadConfigurationShape.Other;
        }
    }
}

/// <summary>Thrown by a download callback to abort on a configuration that was not answered.</summary>
public sealed class PlcDownloadAbortedException : Exception
{
    public PlcDownloadAbortedException(string phase, string type, Exception? decisionFailure = null)
        : base($"Download aborted: unanswered {phase} configuration {type}.", decisionFailure)
    {
        Phase = phase;
        Type = type;
    }

    public string Phase { get; }

    public string Type { get; }
}

/// <summary>
/// The TIA Portal <c>Confirmation</c> dialogs raised while one <c>plc_download</c> runs, with the
/// answer the worker session gave each. Thread-safe, because Siemens may raise events on another
/// thread; records at most <see cref="PlcDownloadPolicy.MaxPortalConfirmations"/> entries but counts
/// every one. <see cref="CloseInto"/> first ends the answering scope (<see cref="AttachScope"/>), so
/// the evidence it copies contains every confirmation that was answered under this download's policy.
/// </summary>
public sealed class PlcDownloadPortalConfirmationLog
{
    private readonly object _sync = new object();
    private readonly List<PlcDownloadPortalConfirmationInfo> _entries = new List<PlcDownloadPortalConfirmationInfo>();
    private int _observed;
    private IDisposable? _scope;

    /// <summary>Every confirmation observed, recorded or not.</summary>
    public int Observed
    {
        get
        {
            lock (_sync) return _observed;
        }
    }

    /// <summary>The scope that answers confirmations; <see cref="CloseInto"/> disposes it before copying the evidence.</summary>
    public void AttachScope(IDisposable scope)
    {
        lock (_sync) _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    /// <param name="answer"><c>Yes</c> or <c>No</c>: what the session answered.</param>
    public void Record(string? type, string? text, string answer)
    {
        lock (_sync)
        {
            _observed++;
            if (_entries.Count >= PlcDownloadPolicy.MaxPortalConfirmations) return;
            _entries.Add(new PlcDownloadPortalConfirmationInfo
            {
                Type = string.IsNullOrWhiteSpace(type) ? "(unknown)" : type!,
                Text = PlcDownloadPolicy.BoundText(text),
                Answer = answer == PlcDownloadPolicy.PortalConfirmationAnswers.Yes
                    ? PlcDownloadPolicy.PortalConfirmationAnswers.Yes
                    : PlcDownloadPolicy.PortalConfirmationAnswers.No,
            });
        }
    }

    /// <summary>
    /// Ends the attached answering scope (idempotent), then writes the recorded entries, the observed
    /// total and whether the list is complete into <paramref name="result"/>. A confirmation raised
    /// after this call is not recorded here: the lingering scope answers it by the same policy and reports it as late at the next request.
    /// </summary>
    public void CloseInto(PlcDownloadResultInfo result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        CloseInto((entries, observed, complete) =>
        {
            result.PortalConfirmations = entries;
            result.PortalConfirmationsObserved = observed;
            result.PortalConfirmationsComplete = complete;
        });
    }

    /// <summary>
    /// <see cref="CloseInto(PlcDownloadResultInfo)"/> for another evidence type (the Openness online
    /// actions): ends the scope, then passes a copy of the entries, the observed total and whether the
    /// list is complete to <paramref name="write"/>.
    /// </summary>
    public void CloseInto(Action<List<PlcDownloadPortalConfirmationInfo>, int, bool> write)
    {
        if (write is null) throw new ArgumentNullException(nameof(write));
        IDisposable? scope;
        lock (_sync)
        {
            scope = _scope;
            _scope = null;
        }

        scope?.Dispose();
        lock (_sync)
        {
            write(_entries
                .Select(entry => new PlcDownloadPortalConfirmationInfo { Type = entry.Type, Text = entry.Text, Answer = entry.Answer })
                .ToList(), _observed, _observed == _entries.Count);
        }
    }
}
