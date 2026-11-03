using Siemens.Engineering.Online.Configurations;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Tests.TestUtilities;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// The TLS verification answer of the shared recorders (online legitimation of go_online, compare_to_online and
/// station upload; the online legitimation phase of plc_download): Trusted only when every presented certificate's
/// SHA-256 fingerprint is in trustedCertificates, NonTrusted and an abort with the human-decision evidence otherwise,
/// and never through a decision. K27 (live 2026-11-02): a refusal whose NonTrusted could be set returns normally from the
/// callback (no exception inside TIA Portal's online legitimation); only a refusal without NonTrusted still throws.
/// </summary>
public sealed class PlcCallbackRecorderTlsTests
{
    private static readonly string[] OnlineAndUpload = { PlcCallbackPolicy.UploadConfigurationsNamespace, PlcCallbackPolicy.OnlineConfigurationsNamespace };

    private static PlcCallbackRecorder Online(IReadOnlyList<string>? trusted = null, IReadOnlyList<PlcCallbackDecisionInfo>? decisions = null,
        IPlcCallbackAccess? access = null, Func<Exception, bool>? isFatal = null)
        => new(decisions, null, access ?? new PlcCallbackReflectionAccess(OnlineAndUpload, _ => null, _ => null), isFatal, trusted);

    [Fact]
    public void WithoutTrustedCertificates_TheCertificateIsRefused_AndTheAbortCarriesTheEvidence()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var configuration = new TlsVerificationConfiguration("PLC_1", "The IP address does not match the certificate.", leaf);
        var recorder = Online();

        // K27: NonTrusted is set, so the callback returns normally; TIA Portal fails the connection itself.
        recorder.Handle(configuration);

        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, configuration.CurrentSelection);
        Assert.Equal(new[] { TlsVerificationConfigurationSelection.NonTrusted }, configuration.Assigned);
        var seen = Assert.Single(recorder.Seen);
        Assert.Equal(PlcCallbackPolicy.Decisions.Selected, seen.Decision);
        Assert.Equal(PlcTlsTrustPolicy.CanonicalType, seen.CanonicalType);
        Assert.Equal(PlcTlsTrustPolicy.ConfigurationKey, seen.DecisionKey);
        Assert.Equal(PlcCallbackPolicy.Kinds.Selection, seen.Kind);
        Assert.Equal(new[] { "NonVerified", "Trusted", "NonTrusted" }, seen.AvailableSelections);
        Assert.Equal(PlcTlsTrustPolicy.NonTrusted, seen.Selection);
        var abort = recorder.Abort!;
        var thrown = new PlcCallbackAbortedException(PlcCallbackRecorder.AbortMessage(abort));
        Assert.Equal(PlcCallbackPolicy.AbortReasons.CertificateNotTrusted, abort.Reason);
        Assert.Equal(PlcTlsTrustPolicy.ConfigurationKey, abort.DecisionKey);
        Assert.Same(seen.Tls, abort.Tls);
        Assert.Equal("PLC_1", abort.Tls!.PlcName);
        Assert.Equal("The IP address does not match the certificate.", abort.Tls.VerificationInfo);
        Assert.Equal(PlcTlsTrustPolicy.NonTrusted, abort.Tls.SelectionSet);
        Assert.Null(abort.Tls.Source);
        var certificate = Assert.Single(abort.Tls.Certificates);
        Assert.Equal(TestCertificates.Sha256(leaf), certificate.Sha256);
        Assert.False(certificate.Approved);
        Assert.Contains(TestCertificates.Sha256(leaf), thrown.Message);
        Assert.Contains("trustedCertificates", thrown.Message);
        Assert.Contains("show the user these fingerprints", PlcCallbackRecorder.AbortMessage(abort));
    }

    [Fact]
    public void EveryPresentedCertificateApproved_IsTrusted_WithSourceCaller()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var ca = TestCertificates.Create("Plant CA");
        var configuration = new TlsVerificationConfiguration("PLC_1", "info", leaf, ca);
        var recorder = Online(new[] { TestCertificates.Sha256(ca), TestCertificates.Sha256(leaf) });

        recorder.Handle(configuration);

        Assert.Equal(TlsVerificationConfigurationSelection.Trusted, configuration.CurrentSelection);
        Assert.Null(recorder.Abort);
        var seen = Assert.Single(recorder.Seen);
        Assert.Equal(PlcCallbackPolicy.Decisions.Selected, seen.Decision);
        Assert.Equal(PlcTlsTrustPolicy.Trusted, seen.Selection);
        Assert.Equal(PlcTlsTrustPolicy.Trusted, seen.Tls!.SelectionSet);
        Assert.Equal(PlcTlsTrustPolicy.CallerSource, seen.Tls.Source);
        Assert.All(seen.Tls.Certificates, certificate => Assert.True(certificate.Approved));
        Assert.True(PlcTlsTrustPolicy.IsConsistent(seen.Tls, trustedApplied: true, new[] { TestCertificates.Sha256(leaf), TestCertificates.Sha256(ca) }));
    }

    [Fact]
    public void OneUnapprovedCertificate_RefusesTheWholeChain()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var ca = TestCertificates.Create("Plant CA");
        var configuration = new TlsVerificationConfiguration("PLC_1", "info", leaf, ca);
        var recorder = Online(new[] { TestCertificates.Sha256(leaf) });

        recorder.Handle(configuration);

        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, configuration.CurrentSelection);
        Assert.Equal(PlcCallbackPolicy.AbortReasons.CertificateNotTrusted, recorder.Abort!.Reason);
        Assert.Contains("1 of 2 presented certificate(s)", recorder.Abort.Detail);
        Assert.Equal(new[] { true, false }, recorder.Abort.Tls!.Certificates.Select(c => c.Approved));
    }

    [Fact]
    public void NoCertificate_IsNeverTrusted()
    {
        var configuration = new TlsVerificationConfiguration("PLC_1", "info");
        var recorder = Online(new[] { new string('A', 64) });

        recorder.Handle(configuration);

        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, configuration.CurrentSelection);
        Assert.Equal("the device presented no certificate", recorder.Abort!.Detail);
    }

    [Fact]
    public void ADecisionEntry_CanNeverSelectTrusted()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var configuration = new TlsVerificationConfiguration("PLC_1", "info", leaf);
        // Bypasses validation, which refuses the key (PlcTlsTrustPolicyTests): the recorder still ignores it.
        var recorder = Online(decisions: new[] { new PlcCallbackDecisionInfo { Type = PlcTlsTrustPolicy.ConfigurationKey, Selection = "Trusted" } });

        recorder.Handle(configuration);

        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, configuration.CurrentSelection);
        Assert.DoesNotContain(TlsVerificationConfigurationSelection.Trusted, configuration.Assigned);
        Assert.Equal(PlcCallbackPolicy.AbortReasons.CertificateNotTrusted, recorder.Abort!.Reason);
    }

    [Fact]
    public void UnreadableEvidence_FailsClosedEvenWhenTheFingerprintIsApproved()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var configuration = new TlsVerificationConfiguration(() => throw new InvalidOperationException("unreadable"), "info",
            new Siemens.Engineering.Online.Security.VerificationCertificateComposition(new[] { leaf }));
        var recorder = Online(new[] { TestCertificates.Sha256(leaf) });

        recorder.Handle(configuration);

        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, configuration.CurrentSelection);
        Assert.Equal(PlcCallbackPolicy.AbortReasons.DecisionNotApplicable, recorder.Abort!.Reason);
        Assert.Equal(new[] { "PlcName" }, recorder.Abort.Tls!.UnreadableMembers);
    }

    [Fact]
    public void RejectedTrustedSelection_IsAnswerRejected_AndNonTrustedIsStillSet()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var configuration = new TlsVerificationConfiguration("PLC_1", "info", leaf) { RejectTrusted = true };
        var recorder = Online(new[] { TestCertificates.Sha256(leaf) });

        // NonTrusted could be set after the rejected Trusted, so the refusal does not throw (K27).
        recorder.Handle(configuration);
        Assert.Equal(PlcTlsTrustPolicy.NonTrusted, recorder.Seen.Single().Selection);

        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, configuration.CurrentSelection);
        Assert.Equal(PlcCallbackPolicy.AbortReasons.AnswerRejected, recorder.Abort!.Reason);
        Assert.Equal(PlcTlsTrustPolicy.NonTrusted, recorder.Abort.Tls!.SelectionSet);
        Assert.IsType<InvalidOperationException>(recorder.AnswerFailure);
    }

    [Fact]
    public void WithoutCurrentSelection_NothingCanBeSet_AndItAborts()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var recorder = Online(new[] { TestCertificates.Sha256(leaf) }, access: new OtherKindTlsAccess());

        Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(new TlsVerificationConfiguration("PLC_1", "info", leaf)));

        Assert.Equal(PlcCallbackPolicy.AbortReasons.DecisionNotApplicable, recorder.Abort!.Reason);
        Assert.Contains("CurrentSelection", recorder.Abort.Tls!.UnreadableMembers);
        Assert.Null(recorder.Abort.Tls.SelectionSet);
    }

    [Fact]
    public void ASelectionOfAnotherEnumType_IsNeverWritten_AndItAborts()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var shape = new ForeignSelectionTlsShape(leaf);
        var trusted = new[] { TestCertificates.Sha256(leaf) };
        var recorder = Online(trusted, access: new CanonicalTlsAccess());

        var thrown = Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(shape));

        Assert.Empty(shape.Assigned);
        Assert.Equal(PlcCallbackPolicy.Kinds.Selection, recorder.Seen.Single().Kind);
        Assert.Equal(PlcCallbackPolicy.AbortReasons.DecisionNotApplicable, recorder.Abort!.Reason);
        Assert.Equal(new[] { "CurrentSelection" }, recorder.Abort.Tls!.UnreadableMembers);
        Assert.Null(recorder.Abort.Tls.SelectionSet);
        Assert.Contains("not of the documented type", thrown.Message);
        Assert.True(PlcTlsTrustPolicy.IsConsistent(recorder.Abort.Tls, trustedApplied: false, trusted));
    }

    [Fact]
    public void Download_ASelectionOfAnotherEnumType_IsNeverWritten_AndTheDownloadAborts()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var shape = new ForeignSelectionTlsShape(leaf);
        var result = new PlcDownloadResultInfo();
        var recorder = new PlcDownloadCallbackRecorder(new PlcDownloadDecisionSet(null, null, true, "PLCSIM", new[] { TestCertificates.Sha256(leaf) }),
            result, new PlcDownloadReflectionAccess(_ => null, _ => null), legitimationCanonicalType: _ => PlcTlsTrustPolicy.CanonicalType);

        Assert.Throws<PlcDownloadAbortedException>(() => recorder.OnlineLegitimation(shape));

        Assert.Empty(shape.Assigned);
        Assert.Equal(PlcDownloadPolicy.AbortReasons.DecisionNotApplicable, result.Abort!.Reason);
        Assert.Equal(new[] { "CurrentSelection" }, result.Abort.Tls!.UnreadableMembers);
        Assert.Null(result.Abort.Tls.SelectionSet);
    }

    [Fact]
    public void AFatalReadIsRecordedAndRethrownUnchanged()
    {
        var recorder = Online(isFatal: ex => ex is FatalTestException);
        var configuration = new TlsVerificationConfiguration(() => throw new FatalTestException(), "info",
            new Siemens.Engineering.Online.Security.VerificationCertificateComposition(Array.Empty<System.Security.Cryptography.X509Certificates.X509Certificate2>()));

        Assert.Throws<FatalTestException>(() => recorder.Handle(configuration));

        Assert.Equal(PlcCallbackPolicy.AbortReasons.AnswerRejected, recorder.Abort!.Reason);
        Assert.Equal(TlsVerificationConfigurationSelection.NonVerified, configuration.CurrentSelection);
    }

    [Fact]
    public void UploadAndLegitimationShareOneRecorder_AndALaterTlsAfterAnAbortIsRefusedWithoutAThrow()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var recorder = new PlcCallbackRecorder(null, null, new PlcCallbackReflectionAccess(OnlineAndUpload, _ => null, _ => null),
            trustedCertificates: new[] { TestCertificates.Sha256(leaf) });

        Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(new Siemens.Engineering.Upload.Configurations.UploadMissingProducts()));
        // Even an approved certificate is never trusted after an abort; NonTrusted is set and nothing is thrown (K27).
        var late = new TlsVerificationConfiguration("PLC_1", "info", leaf);
        recorder.Handle(late);

        Assert.Equal(PlcCallbackPolicy.AbortReasons.DecisionMissing, recorder.Abort!.Reason);
        Assert.Null(recorder.Abort.Tls);
        Assert.Equal(PlcTlsTrustPolicy.NonTrusted, recorder.Seen[1].Selection);
        Assert.Equal(PlcTlsTrustPolicy.NonTrusted, recorder.Seen[1].Tls!.SelectionSet);
        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, late.CurrentSelection);
        Assert.DoesNotContain(TlsVerificationConfigurationSelection.Trusted, late.Assigned);
    }

    [Fact]
    public void ARefusalWithoutAThrow_PassesTheHostContract_AndAForgedOneDoesNot()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var recorder = Online();
        recorder.Handle(new TlsVerificationConfiguration("PLC_1", "info", leaf));
        var none = Array.Empty<PlcCallbackDecisionInfo>();
        var noPasswords = Array.Empty<PlcCallbackPasswordInfo>();

        TiaMcpServer.PlcOnline.PlcOnlinePayloadContract.ValidateCallbacks(recorder.Seen, recorder.Abort, none, noPasswords, Array.Empty<string>());

        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            TiaMcpServer.PlcOnline.PlcOnlinePayloadContract.ValidateCallbacks(recorder.Seen, null, none, noPasswords, Array.Empty<string>()));
        var forged = recorder.Seen;
        forged[0].Tls!.SelectionSet = null;
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            TiaMcpServer.PlcOnline.PlcOnlinePayloadContract.ValidateCallbacks(forged, recorder.Abort, none, noPasswords, Array.Empty<string>()));
    }

    /// <summary>K52 (live V21 2026-11-02, owner decision): PLCSIM asked the TLS question without any certificate.</summary>
    [Fact]
    public void PlcSim_WithoutAnyCertificate_IsAnsweredNonVerified_AndPassesTheHostContractOnlyOnPlcSim()
    {
        var configuration = new TlsVerificationConfiguration("PLC_F1 [192.168.0.1]", "certificate not matching");
        var recorder = new PlcCallbackRecorder(null, null, new PlcCallbackReflectionAccess(OnlineAndUpload, _ => null, _ => null), plcSimInterface: true);

        recorder.Handle(configuration);

        Assert.Equal(TlsVerificationConfigurationSelection.NonVerified, configuration.CurrentSelection);
        Assert.Equal(new[] { TlsVerificationConfigurationSelection.NonVerified }, configuration.Assigned);
        Assert.Null(recorder.Abort);
        var seen = Assert.Single(recorder.Seen);
        Assert.Equal((PlcTlsTrustPolicy.NonVerified, PlcTlsTrustPolicy.NonVerified, PlcTlsTrustPolicy.PlcSimNoCertificateSource),
            (seen.Selection, seen.Tls!.SelectionSet, seen.Tls.Source));
        var none = Array.Empty<PlcCallbackDecisionInfo>();
        var noPasswords = Array.Empty<PlcCallbackPasswordInfo>();
        TiaMcpServer.PlcOnline.PlcOnlinePayloadContract.ValidateCallbacks(recorder.Seen, null, none, noPasswords, Array.Empty<string>(), plcSimInterface: true);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            TiaMcpServer.PlcOnline.PlcOnlinePayloadContract.ValidateCallbacks(recorder.Seen, null, none, noPasswords, Array.Empty<string>()));
    }

    [Fact]
    public void WithoutAnyCertificate_OtherInterfacesAndPresentedCertificatesKeepTheFingerprintRule()
    {
        var noCertificate = new TlsVerificationConfiguration("PLC_1", "certificate not matching");
        Online().Handle(noCertificate);
        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, noCertificate.CurrentSelection);

        var presented = new TlsVerificationConfiguration("PLC_1", "info", TestCertificates.Create("PLC_1"));
        var plcSim = new PlcCallbackRecorder(null, null, new PlcCallbackReflectionAccess(OnlineAndUpload, _ => null, _ => null), plcSimInterface: true);
        plcSim.Handle(presented);
        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, presented.CurrentSelection);
        Assert.Equal(PlcCallbackPolicy.AbortReasons.CertificateNotTrusted, plcSim.Abort!.Reason);
    }

    [Fact]
    public void NonTrustedThatCannotBeSet_StillThrows()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var configuration = new TlsVerificationConfiguration("PLC_1", "info", leaf) { RejectNonTrusted = true };
        var recorder = Online();

        Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(configuration));

        Assert.Equal(PlcCallbackPolicy.Decisions.Aborted, recorder.Seen.Single().Decision);
        Assert.Equal(PlcCallbackPolicy.AbortReasons.CertificateNotTrusted, recorder.Abort!.Reason);
        Assert.Null(recorder.Abort.Tls!.SelectionSet);
        Assert.Contains("the callback threw", PlcCallbackRecorder.AbortMessage(recorder.Abort));
    }

    [Fact]
    public void Download_NonTrustedThatCannotBeSet_StillThrows()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var result = new PlcDownloadResultInfo();
        var recorder = DownloadRecorder(result, Array.Empty<string>());
        var configuration = new TlsVerificationConfiguration("PLC_1", "info", leaf) { RejectNonTrusted = true };

        Assert.Throws<PlcDownloadAbortedException>(() => recorder.OnlineLegitimation(configuration));

        Assert.Equal(PlcDownloadPolicy.Decisions.Aborted, result.ConfigurationsSeen.Single().Decision);
        Assert.Equal(PlcDownloadPolicy.AbortReasons.CertificateNotTrusted, result.Abort!.Reason);
        Assert.Null(result.Abort.Tls!.SelectionSet);
    }

    [Fact]
    public void Download_RejectedTrustedSelection_RefusesWithoutAThrow_AndKeepsTheSetterFailure()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var result = new PlcDownloadResultInfo();
        var recorder = DownloadRecorder(result, new[] { TestCertificates.Sha256(leaf) });
        var configuration = new TlsVerificationConfiguration("PLC_1", "info", leaf) { RejectTrusted = true };

        recorder.OnlineLegitimation(configuration);

        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, configuration.CurrentSelection);
        Assert.Equal(PlcDownloadPolicy.AbortReasons.DecisionRejected, result.Abort!.Reason);
        Assert.Null(result.Abort.Detail);
        Assert.IsType<InvalidOperationException>(recorder.DecisionFailure);
        var seen = result.ConfigurationsSeen.Single();
        Assert.Equal((PlcDownloadPolicy.Decisions.Selected, PlcTlsTrustPolicy.NonTrusted, PlcDownloadPolicy.Sources.Policy), (seen.Decision, seen.Selection, seen.Source));
    }

    [Fact]
    public void Download_TrustedTlsInTheLegitimationPhase_IsRecordedWithSourceCaller()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var result = new PlcDownloadResultInfo();
        var recorder = DownloadRecorder(result, new[] { TestCertificates.Sha256(leaf) });
        var configuration = new TlsVerificationConfiguration("PLC_1", "info", leaf);

        recorder.OnlineLegitimation(configuration);

        Assert.Equal(TlsVerificationConfigurationSelection.Trusted, configuration.CurrentSelection);
        var seen = Assert.Single(result.ConfigurationsSeen);
        Assert.Equal(PlcDownloadPolicy.Phases.OnlineLegitimation, seen.Phase);
        Assert.Equal(PlcTlsTrustPolicy.CanonicalType, seen.CanonicalType);
        Assert.Equal(PlcTlsTrustPolicy.ConfigurationKey, seen.DecisionKey);
        Assert.Equal(PlcDownloadPolicy.Decisions.Selected, seen.Decision);
        Assert.Equal(PlcTlsTrustPolicy.Trusted, seen.Selection);
        Assert.Equal(PlcDownloadPolicy.Sources.Caller, seen.Source);
        Assert.Null(seen.Message);
        Assert.Equal(PlcTlsTrustPolicy.CallerSource, seen.Tls!.Source);
        Assert.Null(result.Abort);
    }

    [Fact]
    public void Download_UntrustedTls_AbortsTheDownloadWithTheEvidence()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var result = new PlcDownloadResultInfo();
        var recorder = DownloadRecorder(result, Array.Empty<string>());
        var configuration = new TlsVerificationConfiguration("PLC_1", "info", leaf);

        // K27: NonTrusted is set, so the callback returns normally (no exception inside TIA Portal) and the abort is recorded.
        recorder.OnlineLegitimation(configuration);

        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, configuration.CurrentSelection);
        var refused = result.ConfigurationsSeen.Single();
        Assert.Equal(PlcDownloadPolicy.Decisions.Selected, refused.Decision);
        Assert.Equal(PlcTlsTrustPolicy.NonTrusted, refused.Selection);
        Assert.Equal(PlcDownloadPolicy.Sources.Policy, refused.Source);
        Assert.Same(refused.Tls, result.Abort!.Tls);
        var abort = result.Abort!;
        Assert.Equal(PlcDownloadPolicy.Phases.OnlineLegitimation, abort.Phase);
        Assert.Equal(PlcDownloadPolicy.AbortReasons.CertificateNotTrusted, abort.Reason);
        Assert.Equal(PlcTlsTrustPolicy.ConfigurationKey, abort.DecisionKey);
        Assert.Equal(TestCertificates.Sha256(leaf), abort.Tls!.Certificates.Single().Sha256);
        var message = PlcDownloadPolicy.AbortMessage(abort);
        Assert.Contains("online_legitimation phase", message);
        Assert.Contains(TestCertificates.Sha256(leaf), message);
        Assert.Contains("trustedCertificates", message);

        // Every later download configuration is recorded as aborted, answered nothing and thrown (unchanged; no live evidence).
        Assert.Throws<PlcDownloadAbortedException>(() => recorder.PreDownload(new Siemens.Engineering.Download.Configurations.StopModules()));
        Assert.Equal(PlcDownloadPolicy.Decisions.Aborted, result.ConfigurationsSeen[1].Decision);
        // A later TLS question is refused again without a throw, and the first abort stays.
        var again = new TlsVerificationConfiguration("PLC_1", "info", leaf);
        recorder.OnlineLegitimation(again);
        Assert.Equal(TlsVerificationConfigurationSelection.NonTrusted, again.CurrentSelection);
        Assert.Equal(PlcTlsTrustPolicy.NonTrusted, result.ConfigurationsSeen[2].Selection);
        Assert.Same(abort, result.Abort);
    }

    [Fact]
    public void Download_LegitimationPasswordsAndOtherConfigurations_FollowTheDownloadRules()
    {
        var result = new PlcDownloadResultInfo();
        var recorder = new PlcDownloadCallbackRecorder(
            new PlcDownloadDecisionSet(null, new[] { new PlcDownloadPasswordInfo { Type = "OnlineReadAccessPassword", Variable = "PLC_READ" } }, true, "PLCSIM"),
            result, new PlcDownloadReflectionAccess(_ => throw new InvalidCastException("not a download configuration"), name => name == "PLC_READ" ? "secret" : null));
        var password = new OnlineReadAccessPassword();

        recorder.OnlineLegitimation(password);
        Assert.Throws<PlcDownloadAbortedException>(() => recorder.OnlineLegitimation(new OnlineAuthenticationConfiguration()));

        Assert.True(password.PasswordSupplied);
        Assert.Equal(PlcDownloadPolicy.Decisions.PasswordSet, result.ConfigurationsSeen[0].Decision);
        Assert.Equal("PLC_READ", result.ConfigurationsSeen[0].PasswordVariable);
        Assert.Equal(PlcDownloadPolicy.AbortReasons.DecisionRequired, result.Abort!.Reason);
        Assert.Equal("OnlineAuthenticationConfiguration", result.Abort.DecisionKey);
        Assert.Null(result.Abort.Tls);
    }

    [Fact]
    public void Download_TlsKeyIsRefusedAsADecision_AndTheDecisionSetNeverResolvesIt()
    {
        Assert.Contains("only through trustedCertificates", PlcDownloadPolicy.ValidateDecisions(
            new[] { new PlcDownloadDecisionInfo { Type = PlcTlsTrustPolicy.ConfigurationKey, Selection = "Trusted" } }, Array.Empty<PlcDownloadPasswordInfo>()));
        Assert.Contains("only through trustedCertificates", PlcDownloadPolicy.ValidateDecisions(
            Array.Empty<PlcDownloadDecisionInfo>(), new[] { new PlcDownloadPasswordInfo { Type = PlcTlsTrustPolicy.ConfigurationKey, Variable = "X" } }));
        var set = new PlcDownloadDecisionSet(new[] { new PlcDownloadDecisionInfo { Type = PlcTlsTrustPolicy.ConfigurationKey, Selection = "Trusted" } },
            null, true, "PLCSIM", new[] { new string('A', 64) });
        Assert.Equal(PlcDownloadPolicy.AbortReasons.CertificateNotTrusted,
            set.Resolve(PlcDownloadPolicy.Phases.OnlineLegitimation, PlcTlsTrustPolicy.CanonicalType).AbortReason);
        Assert.Equal(PlcDownloadPolicy.AbortReasons.UnsupportedConfiguration,
            set.Resolve(PlcDownloadPolicy.Phases.PreDownload, PlcTlsTrustPolicy.CanonicalType).AbortReason);
        Assert.Equal(PlcTlsTrustPolicy.ConfigurationKey, PlcDownloadPolicy.DecisionKey(PlcDownloadPolicy.Phases.OnlineLegitimation, PlcTlsTrustPolicy.CanonicalType));
        Assert.Null(PlcDownloadPolicy.DecisionKey(PlcDownloadPolicy.Phases.PreDownload, PlcTlsTrustPolicy.CanonicalType));
        Assert.Equal(PlcTlsTrustPolicy.CanonicalType,
            PlcDownloadPolicy.CanonicalConfigurationType(PlcDownloadPolicy.Phases.OnlineLegitimation, typeof(TlsVerificationConfiguration)));
        Assert.Null(PlcDownloadPolicy.CanonicalConfigurationType(PlcDownloadPolicy.Phases.PreDownload, typeof(TlsVerificationConfiguration)));
    }

    [Fact]
    public void Download_WorkerRequestRefusesUnnormalizedFingerprints()
    {
        var request = new PlcDownloadRequestInfo
        {
            PlcName = "PLC_1", DeviceName = "Station_1", Mode = "PN/IE", PcInterfaceName = "PLCSIM", PcInterfaceNumber = 1,
            TargetInterface = "1 X1", Scope = PlcDownloadPolicy.Scopes.Software, TrustedCertificates = { new string('a', 64) },
        };
        Assert.False(PlcDownloadPolicy.ValidateWorkerRequest(request).IsValid);
        request.TrustedCertificates = new List<string> { new string('A', 64) };
        Assert.True(PlcDownloadPolicy.ValidateWorkerRequest(request).IsValid);
    }

    private static PlcDownloadCallbackRecorder DownloadRecorder(PlcDownloadResultInfo result, IEnumerable<string> trusted)
        => new(new PlcDownloadDecisionSet(null, null, true, "PLCSIM", trusted), result,
            new PlcDownloadReflectionAccess(configuration => configuration is Siemens.Engineering.Download.Configurations.StopModules ? "stop" : null, _ => null));

    /// <summary>Reflection access that reports every configuration as the TLS verification type (for shapes outside its namespace).</summary>
    private sealed class CanonicalTlsAccess : IPlcCallbackAccess
    {
        private readonly PlcCallbackReflectionAccess _inner = new(OnlineAndUpload, _ => null, _ => null);

        public string TypeName(object configuration) => _inner.TypeName(configuration);

        public string? CanonicalType(object configuration) => PlcTlsTrustPolicy.CanonicalType;

        public string? ReadMessage(object configuration) => null;

        public PlcCallbackShape Describe(object configuration) => _inner.Describe(configuration);

        public bool ApplySelection(object configuration, string selection) => _inner.ApplySelection(configuration, selection);

        public bool ApplyPassword(object configuration, string variable) => _inner.ApplyPassword(configuration, variable);
    }

    /// <summary>A TLS verification configuration whose live object exposes no selection (kind other).</summary>
    private sealed class OtherKindTlsAccess : IPlcCallbackAccess
    {
        public string TypeName(object configuration) => configuration.GetType().FullName!;

        public string? CanonicalType(object configuration) => PlcTlsTrustPolicy.CanonicalType;

        public string? ReadMessage(object configuration) => null;

        public PlcCallbackShape Describe(object configuration) => PlcCallbackShape.Other;

        public bool ApplySelection(object configuration, string selection) => throw new InvalidOperationException("No selection property.");

        public bool ApplyPassword(object configuration, string variable) => throw new InvalidOperationException("No password setter.");
    }
}
