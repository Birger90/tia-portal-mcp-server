using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.PlcDownload;
using TiaMcpServer.Worker;
using Xunit;
using static TiaMcpServer.Tests.PlcDownload.PlcDownloadTestData;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>Strict decoding of list_download_targets and plc_download worker payloads.</summary>
public sealed class PlcDownloadPayloadContractTests
{
    private static string Payload<T>(T value) => WorkerJson.SerializePayload(value);

    private static string Mutate<T>(T value, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(Payload(value))!.AsObject();
        change(node);
        return node.ToJsonString();
    }

    [Fact]
    public void Inventory_RoundTripsWithExplicitMembers()
    {
        var decoded = PlcDownloadPayloadContract.DecodeInventory(Payload(SinglePlc()));
        var plc = Assert.Single(decoded.Plcs);
        Assert.Equal("PLC_1", plc.PlcName);
        Assert.Equal(new[] { "PLCSIM", Hardware }, plc.Modes[0].PcInterfaces.Select(pc => pc.Name));
    }

    public static TheoryData<string> MalformedInventories => new()
    {
        """{"plcs":[{"plcName":"PLC_1","deviceName":"S","downloadable":true}]}""",
        """{"plcs":[{"plcName":"PLC_1","deviceName":"S","downloadable":true,"modes":[],"extra":1}]}""",
        """{"plcs":[null]}""",
        """{"plcs":[{"plcName":null,"deviceName":"S","downloadable":true,"modes":[]}]}""",
        """{"plcs":[{"plcName":"PLC_3","deviceName":"S","downloadable":false,"modes":[{"name":"PN/IE","pcInterfaces":[]}]}]}""",
        """{"plcs":[{"plcName":"PLC_1","deviceName":"S","downloadable":true,"modes":[{"name":"PN/IE","pcInterfaces":[{"name":"PLCSIM","number":1,"targetInterfaces":[null]}]}]}]}""",
        """{"plcs":[{"plcName":"PLC_1","deviceName":"S","downloadable":true,"modes":[{"name":"PN/IE","pcInterfaces":[{"name":"PLCSIM","number":"1","targetInterfaces":[]}]}]}]}""",
        """{"PLCS":[]}""",
        """[]""",
    };

    [Theory]
    [MemberData(nameof(MalformedInventories))]
    public void MalformedInventory_IsRejected(string payload)
        => Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeInventory(payload));

    [Fact]
    public void PlcRead_ListDownloadTargets_ProjectsTypedResultOrProtocolError()
    {
        var operation = new PlcOperationRequest { OperationId = "targets", Operation = PlcDownloadMethods.ListTargets };
        var ok = PlcPayloadContract.Project(operation, WorkerCallResult.Ok(Payload(SinglePlc())), _ => { });
        Assert.Equal(OperationBatchStatus.Succeeded, ok.Status);
        Assert.Equal("1 X1", ok.Result!.Value.GetProperty("plcs")[0].GetProperty("modes")[0].GetProperty("pcInterfaces")[0]
            .GetProperty("targetInterfaces")[0].GetProperty("name").GetString());

        var diagnostics = new List<string>();
        var bad = PlcPayloadContract.Project(operation, WorkerCallResult.Ok("""{"plcs":[{"secret":"PRIVATE"}]}"""), diagnostics.Add);
        Assert.Equal(OperationBatchStatus.Failed, bad.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, bad.Failure!.Category);
        Assert.Null(bad.Result);
        Assert.DoesNotContain("PRIVATE", bad.Failure.Message);
    }

    [Fact]
    public void PlcRead_ListDownloadTargets_IsAValidReadWithOptionalPlcNameOnly()
    {
        Assert.Contains(PlcDownloadMethods.ListTargets, PlcOperationCatalog.ReadOperationNames);
        Assert.DoesNotContain(PlcDownloadMethods.ListTargets, PlcOperationCatalog.WriteOperationNames);
        Assert.True(PlcOperationCatalog.ValidateRead(new[]
        {
            new PlcOperationRequest { OperationId = "a", Operation = PlcDownloadMethods.ListTargets },
            new PlcOperationRequest { OperationId = "b", Operation = PlcDownloadMethods.ListTargets, PlcName = "PLC_1" },
        }).IsValid);
        var invalid = PlcOperationCatalog.ValidateRead(new[]
        {
            new PlcOperationRequest { OperationId = "c", Operation = PlcDownloadMethods.ListTargets, TableName = "T" },
        });
        Assert.False(invalid.IsValid);
        Assert.Contains("'tableName' is not valid for list_download_targets", invalid.Error);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[]
        {
            new PlcOperationRequest { OperationId = "d", Operation = PlcDownloadMethods.ListTargets },
        }).IsValid);
    }

    [Fact]
    public void Result_DecodesWhenItDescribesThePlan()
    {
        var effects = Effects();
        var decoded = PlcDownloadPayloadContract.DecodeResult(Payload(Result(effects)), effects, requireDownloadResult: true);
        Assert.Equal("Success", decoded.State);
        Assert.False(decoded.CompletionVerified);
        Assert.Equal(2, decoded.ConfigurationsSeen.Count);
    }

    public static TheoryData<string> ResultViolations => new()
    {
        "completionVerified", "blankNote", "otherPlc", "otherDevice", "otherTarget", "otherPcNumber", "otherScope",
        "otherOptions", "otherStart", "disallowedSelection", "abortedWithoutAbort", "selectedWithoutSelection",
        "unknownPhase", "unknownDecision", "unknownReason", "negativeErrors", "stateWithoutCounts", "countsWithoutState",
        "incompleteWithoutNote", "tooDeep", "unknownMember", "missingMember", "nullConfiguration", "wrongOperation",
        // The recorded selection must be exactly the prescribed answer for its canonical type (not merely for its phase).
        "selectionOfAnotherType", "startSelectionAgainstRequest", "selectedWithoutCanonicalType", "canonicalTypeOutsideNamespace",
        "selectedAfterAbort", "abortWithoutAbortedEntry",
        // Sources and answer kinds must match what the plan prescribes.
        "defaultClaimedAsCaller", "checkedWithoutCallerDecision", "passwordWithoutEntry", "passwordVariableOnSelection", "sourceOnAbortedEntry",
        // Evidence shape.
        "decisionKeyMismatch", "unknownKind", "selectionsListedForCheck", "nullAvailableSelections", "blankAvailableSelection",
        "tooManyAvailableSelections", "configurationMessageTooLong",
        // The abort must describe the first aborted entry and its reason must fit.
        "abortTypeDiffersFromEntry", "abortKindDiffersFromEntry", "abortSelectionsDifferFromEntry", "reasonDoesNotFit",
        "unsupportedWithDecisionKey", "detailOnDecisionRequired", "limitBeforeTheLimit",
        "confirmationAnsweredYes", "confirmationWithoutType", "tooManyConfirmations", "nullConfirmations", "confirmationTextTooLong",
        "observedBelowListed", "incompleteWithoutTruncation", "completeButMoreObserved", "otherPortalPolicy", "unknownPortalPolicy",
    };

    [Theory]
    [MemberData(nameof(ResultViolations))]
    public void Result_ViolatingTheContractOrThePlan_IsRejected(string violation)
    {
        var effects = Effects();
        var result = Result(effects);
        string? payload = null;
        switch (violation)
        {
            case "completionVerified": result.CompletionVerified = true; break;
            case "blankNote": result.CompletionNote = " "; break;
            case "otherPlc": result.PlcName = "PLC_9"; break;
            case "otherDevice": result.DeviceName = "Station_9"; break;
            case "otherTarget": result.Path.TargetInterface = "1 X2"; break;
            case "otherPcNumber": result.Path.PcInterface.Number = 2; break;
            case "otherScope": result.Scope = PlcDownloadPolicy.Scopes.Software; break;
            case "otherOptions": result.DownloadOptions = "Hardware"; break;
            case "otherStart": result.StartAfterDownload = false; break;
            case "disallowedSelection": result.ConfigurationsSeen[0].Selection = "StopPlcAndReinitialize"; break;
            case "abortedWithoutAbort":
                result.ConfigurationsSeen[0].Decision = PlcDownloadPolicy.Decisions.Aborted;
                result.ConfigurationsSeen[0].Selection = null;
                break;
            case "selectedWithoutSelection": result.ConfigurationsSeen[0].Selection = null; break;
            case "unknownPhase": result.ConfigurationsSeen[0].Phase = "during_download"; break;
            case "unknownDecision": result.ConfigurationsSeen[0].Decision = "ignored"; break;
            case "unknownReason":
                result = Aborted(effects);
                result.Abort!.Reason = "operator_said_so";
                break;
            case "negativeErrors": result.ErrorCount = -1; break;
            case "stateWithoutCounts": result.WarningCount = null; break;
            case "countsWithoutState": result.State = null; break;
            case "incompleteWithoutNote": result.MessagesComplete = false; break;
            case "tooDeep":
                var message = result.Messages[0];
                for (var depth = 0; depth < PlcDownloadPolicy.MaxMessageDepth; depth++)
                {
                    var child = new PlcDownloadMessageInfo { Message = "deeper" };
                    message.Messages.Add(child);
                    message = child;
                }
                break;
            case "unknownMember": payload = Mutate(result, node => node["completedOnDevice"] = true); break;
            case "missingMember": payload = Mutate(result, node => node.Remove("abort")); break;
            case "nullConfiguration": payload = Mutate(result, node => node["configurationsSeen"]!.AsArray().Add(null)); break;
            case "wrongOperation": result.Operation = "plc_upload"; break;
            case "selectionOfAnotherType":
                // ConsistentDownload is a valid pre-download answer, but not for StopModules.
                result.ConfigurationsSeen[0].Selection = "ConsistentDownload";
                break;
            case "startSelectionAgainstRequest": result.ConfigurationsSeen[1].Selection = "NoAction"; break;
            case "selectedWithoutCanonicalType": result.ConfigurationsSeen[0].CanonicalType = null; break;
            case "canonicalTypeOutsideNamespace":
                result.ConfigurationsSeen[0].CanonicalType = "Other.Namespace.StopModules";
                result.ConfigurationsSeen[0].DecisionKey = "StopModules";
                break;
            case "selectedAfterAbort":
                result = Aborted(effects);
                result.ConfigurationsSeen.Add(new PlcDownloadConfigurationInfo
                {
                    Phase = PlcDownloadPolicy.Phases.PreDownload, Type = PlcDownloadPolicy.ConfigurationTypes.StopModules,
                    CanonicalType = PlcDownloadPolicy.ConfigurationTypes.StopModules, Decision = PlcDownloadPolicy.Decisions.Selected, Selection = "StopAll",
                });
                break;
            case "abortWithoutAbortedEntry":
                result.Abort = new PlcDownloadAbortInfo { Phase = PlcDownloadPolicy.Phases.PreDownload, Type = "X.Unknown", Reason = PlcDownloadPolicy.AbortReasons.UnsupportedConfiguration };
                break;
            case "confirmationAnsweredYes":
                result.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo { Type = "T", Answer = "Yes" });
                result.PortalConfirmationsObserved = 1;
                break;
            case "confirmationWithoutType":
                result.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo { Type = " " });
                result.PortalConfirmationsObserved = 1;
                break;
            case "tooManyConfirmations":
                for (var index = 0; index <= PlcDownloadPolicy.MaxPortalConfirmations; index++)
                    result.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo { Type = "T" });
                result.PortalConfirmationsObserved = result.PortalConfirmations.Count;
                break;
            case "observedBelowListed":
                result.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo { Type = "T" });
                break;
            case "incompleteWithoutTruncation":
                result.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo { Type = "T" });
                result.PortalConfirmationsObserved = 5;
                result.PortalConfirmationsComplete = false;
                break;
            case "completeButMoreObserved":
                result.PortalConfirmationsObserved = 3;
                break;
            case "nullConfirmations": payload = Mutate(result, node => node["portalConfirmations"] = null); break;
            case "defaultClaimedAsCaller": result.ConfigurationsSeen[0].Source = PlcDownloadPolicy.Sources.Caller; break;
            case "checkedWithoutCallerDecision":
                result.ConfigurationsSeen[0].Decision = PlcDownloadPolicy.Decisions.Checked;
                result.ConfigurationsSeen[0].Selection = null;
                result.ConfigurationsSeen[0].Source = PlcDownloadPolicy.Sources.Caller;
                break;
            case "passwordWithoutEntry":
                result.ConfigurationsSeen.Insert(0, new PlcDownloadConfigurationInfo
                {
                    Phase = PlcDownloadPolicy.Phases.PreDownload, Type = Configurations + "ModuleWriteAccessPassword",
                    CanonicalType = Configurations + "ModuleWriteAccessPassword", DecisionKey = "ModuleWriteAccessPassword",
                    Kind = PlcDownloadPolicy.Kinds.Password, Decision = PlcDownloadPolicy.Decisions.PasswordSet,
                    Source = PlcDownloadPolicy.Sources.Caller, PasswordVariable = "PLC_PW",
                });
                break;
            case "passwordVariableOnSelection": result.ConfigurationsSeen[0].PasswordVariable = "PLC_PW"; break;
            case "sourceOnAbortedEntry":
                result = Aborted(effects);
                result.ConfigurationsSeen[1].Source = PlcDownloadPolicy.Sources.Default;
                break;
            case "decisionKeyMismatch": result.ConfigurationsSeen[0].DecisionKey = "StartModules"; break;
            case "unknownKind": result.ConfigurationsSeen[0].Kind = "enum"; break;
            case "selectionsListedForCheck":
                result = Aborted(effects);
                result.ConfigurationsSeen[1].Kind = PlcDownloadPolicy.Kinds.Check;
                result.Abort!.Kind = PlcDownloadPolicy.Kinds.Check;
                break;
            case "nullAvailableSelections": payload = Mutate(result, node => node["configurationsSeen"]![0]!["availableSelections"] = null); break;
            case "blankAvailableSelection": result.ConfigurationsSeen[0].AvailableSelections.Add(" "); break;
            case "tooManyAvailableSelections":
                result.ConfigurationsSeen[0].AvailableSelections.AddRange(Enumerable.Range(0, PlcDownloadPolicy.MaxAvailableSelections).Select(i => "M" + i));
                break;
            case "configurationMessageTooLong": result.ConfigurationsSeen[0].Message = new string('m', PlcDownloadPolicy.MaxTextChars + 1); break;
            case "abortTypeDiffersFromEntry":
                result = Aborted(effects);
                result.Abort!.Type = Configurations + "ResetModule";
                break;
            case "abortKindDiffersFromEntry":
                result = Aborted(effects);
                result.Abort!.Kind = PlcDownloadPolicy.Kinds.Other;
                result.Abort.AvailableSelections.Clear();
                break;
            case "abortSelectionsDifferFromEntry":
                result = Aborted(effects);
                result.Abort!.AvailableSelections.Reverse();
                break;
            case "reasonDoesNotFit":
                // No caller decision was given, so nothing can have been unavailable.
                result = Aborted(effects);
                result.Abort!.Reason = PlcDownloadPolicy.AbortReasons.SelectionUnavailable;
                break;
            case "unsupportedWithDecisionKey":
                result = Aborted(effects);
                result.Abort!.Reason = PlcDownloadPolicy.AbortReasons.UnsupportedConfiguration;
                break;
            case "detailOnDecisionRequired":
                result = Aborted(effects);
                result.Abort!.Detail = "detail";
                break;
            case "limitBeforeTheLimit":
                result = Aborted(effects);
                result.Abort!.Reason = PlcDownloadPolicy.AbortReasons.ConfigurationLimitExceeded;
                break;
            case "otherPortalPolicy": result.PortalConfirmationPolicy = PlcDownloadPolicy.PortalConfirmationPolicies.Accept; break;
            case "unknownPortalPolicy": result.PortalConfirmationPolicy = "ask"; break;
            case "confirmationTextTooLong":
                result.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo { Type = "T", Text = new string('t', PlcDownloadPolicy.MaxTextChars + 1) });
                result.PortalConfirmationsObserved = 1;
                break;
        }

        Assert.ThrowsAny<JsonException>(() =>
            PlcDownloadPayloadContract.DecodeResult(payload ?? Payload(result), effects, requireDownloadResult: true));
    }

    public static TheoryData<string> AbortReasonNames => new(PlcDownloadPolicy.AbortReasons.All);

    /// <summary>One consistent piece of abort evidence per reason, as the recorder would write it.</summary>
    private static (PlcDownloadResultInfo Result, PlcDownloadEffects Effects) AbortEvidence(string reason)
    {
        var effects = reason switch
        {
            PlcDownloadPolicy.AbortReasons.DecisionNotApplicable => Effects(Item(decisions: new[] { Check("DataBlockReinitialization") })),
            PlcDownloadPolicy.AbortReasons.SelectionUnavailable => Effects(Item(decisions: new[] { Select("DataBlockReinitialization", "KeepActualValues") })),
            PlcDownloadPolicy.AbortReasons.PasswordUnavailable or PlcDownloadPolicy.AbortReasons.InsecureCommunication
                => Effects(Item(passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") })),
            _ => Effects(),
        };
        var result = Aborted(effects);
        var entry = result.ConfigurationsSeen[1];
        var abort = result.Abort!;
        abort.Reason = reason;

        void Retype(string type, string? canonical, string kind)
        {
            entry.Type = abort.Type = type;
            entry.CanonicalType = abort.CanonicalType = canonical;
            entry.DecisionKey = abort.DecisionKey = PlcDownloadPolicy.DecisionKey(canonical);
            entry.Kind = abort.Kind = kind;
            if (kind == PlcDownloadPolicy.Kinds.Selection) return;
            entry.AvailableSelections.Clear();
            abort.AvailableSelections.Clear();
        }

        switch (reason)
        {
            case PlcDownloadPolicy.AbortReasons.UnsupportedConfiguration:
                Retype("Vendor.Proxy", null, PlcDownloadPolicy.Kinds.Other);
                break;
            case PlcDownloadPolicy.AbortReasons.DecisionNotApplicable:
            case PlcDownloadPolicy.AbortReasons.SelectionUnavailable:
                abort.Detail = "why it did not apply";
                break;
            case PlcDownloadPolicy.AbortReasons.PasswordUnavailable:
                Retype(Configurations + "ModuleWriteAccessPassword", Configurations + "ModuleWriteAccessPassword", PlcDownloadPolicy.Kinds.Password);
                abort.Detail = "Environment variable 'PLC_PW' is not set or is empty in the worker process.";
                break;
            case PlcDownloadPolicy.AbortReasons.InsecureCommunication:
                // Audit T1: IsSecureCommunication false, no allowInsecurePasswordTransfer: the password was not sent.
                Retype(Configurations + "ModuleWriteAccessPassword", Configurations + "ModuleWriteAccessPassword", PlcDownloadPolicy.Kinds.Password);
                entry.SecureCommunication = false;
                abort.Detail = "IsSecureCommunication is false; the password was not sent.";
                break;
            case PlcDownloadPolicy.AbortReasons.DecisionRejected:
                // A built-in default was attempted and its setter threw.
                Retype(PlcDownloadPolicy.ConfigurationTypes.OverwriteSystemData, PlcDownloadPolicy.ConfigurationTypes.OverwriteSystemData, PlcDownloadPolicy.Kinds.Selection);
                break;
            case PlcDownloadPolicy.AbortReasons.ConfigurationLimitExceeded:
                // The aborted entry is the 101st configuration.
                result.ConfigurationsSeen.InsertRange(0, Enumerable.Range(0, PlcDownloadPolicy.MaxConfigurations - 1).Select(_ =>
                    Selected(PlcDownloadPolicy.Phases.PreDownload, PlcDownloadPolicy.ConfigurationTypes.StopModules, "StopAll", PlcDownloadPolicy.Sources.Default)));
                break;
            case PlcDownloadPolicy.AbortReasons.CertificateNotTrusted:
                // The connection's TLS check: no trustedCertificates, so NonTrusted was set and the download aborted.
                TlsAbort(entry, abort, PlcDownloadTestData.Tls(new string('A', 64), approved: false, PlcTlsTrustPolicy.NonTrusted));
                abort.Detail = "no trustedCertificates were given, so the device's certificate is not trusted";
                break;
        }

        return (result, effects);
    }

    private static void TlsAbort(PlcDownloadConfigurationInfo entry, PlcDownloadAbortInfo abort, PlcTlsVerificationInfo tls)
    {
        entry.Phase = abort.Phase = PlcDownloadPolicy.Phases.OnlineLegitimation;
        entry.Type = abort.Type = PlcTlsTrustPolicy.CanonicalType;
        entry.CanonicalType = abort.CanonicalType = PlcTlsTrustPolicy.CanonicalType;
        entry.DecisionKey = abort.DecisionKey = PlcTlsTrustPolicy.ConfigurationKey;
        entry.Kind = abort.Kind = PlcDownloadPolicy.Kinds.Selection;
        entry.Message = abort.Message = null;
        entry.AvailableSelections = new() { "NonVerified", "Trusted", "NonTrusted" };
        abort.AvailableSelections = new() { "NonVerified", "Trusted", "NonTrusted" };
        entry.Tls = abort.Tls = tls;
    }

    private const string Approved = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public void TrustedTls_IsAcceptedOnlyWhenEveryCertificateIsInThePlannedList()
    {
        var effects = Effects(Item(trustedCertificates: new[] { Approved.ToLowerInvariant() }));
        Assert.Equal(new[] { Approved }, effects.TrustedCertificates);
        PlcDownloadResultInfo Trusted(PlcTlsVerificationInfo tls)
        {
            var result = Result(effects);
            result.ConfigurationsSeen.Insert(0, new PlcDownloadConfigurationInfo
            {
                Phase = PlcDownloadPolicy.Phases.OnlineLegitimation, Type = PlcTlsTrustPolicy.CanonicalType, CanonicalType = PlcTlsTrustPolicy.CanonicalType,
                DecisionKey = PlcTlsTrustPolicy.ConfigurationKey, Kind = PlcDownloadPolicy.Kinds.Selection,
                AvailableSelections = new() { "NonVerified", "Trusted", "NonTrusted" }, Decision = PlcDownloadPolicy.Decisions.Selected,
                Selection = PlcTlsTrustPolicy.Trusted, Source = PlcDownloadPolicy.Sources.Caller, Tls = tls,
            });
            return result;
        }

        var accepted = PlcDownloadPayloadContract.DecodeResult(Payload(Trusted(PlcDownloadTestData.Tls(Approved, approved: true, PlcTlsTrustPolicy.Trusted))),
            effects, requireDownloadResult: true);
        Assert.Equal(PlcTlsTrustPolicy.CallerSource, accepted.ConfigurationsSeen[0].Tls!.Source);

        // A certificate the plan did not approve, a forged approval flag, missing evidence, or a default source are protocol errors.
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(
            Payload(Trusted(PlcDownloadTestData.Tls(new string('B', 64), approved: true, PlcTlsTrustPolicy.Trusted))), effects, requireDownloadResult: true));
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(
            Payload(Trusted(PlcDownloadTestData.Tls(Approved, approved: true, PlcTlsTrustPolicy.Trusted))), Effects(), requireDownloadResult: true));
        var withoutEvidence = Trusted(PlcDownloadTestData.Tls(Approved, approved: true, PlcTlsTrustPolicy.Trusted));
        withoutEvidence.ConfigurationsSeen[0].Tls = null;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(withoutEvidence), effects, requireDownloadResult: true));
        var defaultSource = Trusted(PlcDownloadTestData.Tls(Approved, approved: true, PlcTlsTrustPolicy.Trusted));
        defaultSource.ConfigurationsSeen[0].Source = PlcDownloadPolicy.Sources.Default;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(defaultSource), effects, requireDownloadResult: true));
        // TLS evidence on any other configuration is a protocol error too.
        var stray = Result(effects);
        stray.ConfigurationsSeen[0].Tls = PlcDownloadTestData.Tls(Approved, approved: true, null);
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(stray), effects, requireDownloadResult: true));
    }

    [Fact]
    public void RefusedTls_AbortMustCarryTheEntrysEvidence_AndNeverClaimTrusted()
    {
        var (aborted, effects) = AbortEvidence(PlcDownloadPolicy.AbortReasons.CertificateNotTrusted);
        Assert.NotNull(PlcDownloadPayloadContract.DecodeResult(Payload(aborted), effects, requireDownloadResult: false).Abort!.Tls);

        aborted.Abort!.Tls = PlcDownloadTestData.Tls(new string('C', 64), approved: false, PlcTlsTrustPolicy.NonTrusted);
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(aborted), effects, requireDownloadResult: false));

        var (claimed, _) = AbortEvidence(PlcDownloadPolicy.AbortReasons.CertificateNotTrusted);
        claimed.ConfigurationsSeen[1].Tls!.SelectionSet = claimed.Abort!.Tls!.SelectionSet = PlcTlsTrustPolicy.Trusted;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(claimed), effects, requireDownloadResult: false));

        var (wrongPhase, _) = AbortEvidence(PlcDownloadPolicy.AbortReasons.CertificateNotTrusted);
        wrongPhase.ConfigurationsSeen[1].Phase = wrongPhase.Abort!.Phase = PlcDownloadPolicy.Phases.PreDownload;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(wrongPhase), effects, requireDownloadResult: false));
    }

    /// <summary>
    /// K27: a refused TLS question whose NonTrusted was set is recorded as answered NonTrusted by the server policy (no throw)
    /// and anchors the abort; anything that claims more, or a handled download configuration after it, is a protocol error.
    /// </summary>
    [Fact]
    public void RefusedTlsAnsweredNonTrustedWithoutAThrow_IsAccepted_AndAnchorsTheAbort()
    {
        (PlcDownloadResultInfo Result, PlcDownloadEffects Effects) Refused()
        {
            var (result, effects) = AbortEvidence(PlcDownloadPolicy.AbortReasons.CertificateNotTrusted);
            var entry = result.ConfigurationsSeen[1];
            entry.Decision = PlcDownloadPolicy.Decisions.Selected;
            entry.Selection = PlcTlsTrustPolicy.NonTrusted;
            entry.Source = PlcDownloadPolicy.Sources.Policy;
            return (result, effects);
        }

        var (accepted, effects) = Refused();
        var decoded = PlcDownloadPayloadContract.DecodeResult(Payload(accepted), effects, requireDownloadResult: false);
        Assert.Equal(PlcDownloadPolicy.AbortReasons.CertificateNotTrusted, decoded.Abort!.Reason);
        Assert.Equal(PlcTlsTrustPolicy.NonTrusted, decoded.ConfigurationsSeen[1].Selection);
        // Download returned (the recorder did not throw): a projected DownloadResult may accompany the abort.
        var returned = Refused().Result;
        returned.State = "Error";
        returned.ErrorCount = 1;
        returned.WarningCount = 0;
        Assert.NotNull(PlcDownloadPayloadContract.DecodeResult(Payload(returned), effects, requireDownloadResult: true).Abort);

        var callerSource = Refused().Result;
        callerSource.ConfigurationsSeen[1].Source = PlcDownloadPolicy.Sources.Caller;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(callerSource), effects, requireDownloadResult: false));
        var notSet = Refused().Result;
        notSet.ConfigurationsSeen[1].Tls!.SelectionSet = notSet.Abort!.Tls!.SelectionSet = null;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(notSet), effects, requireDownloadResult: false));
        var withoutAbort = Refused().Result;
        withoutAbort.Abort = null;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(withoutAbort), effects, requireDownloadResult: false));
        var answeredAfter = Refused().Result;
        answeredAfter.ConfigurationsSeen.Add(Selected(PlcDownloadPolicy.Phases.PreDownload, PlcDownloadPolicy.ConfigurationTypes.StopModules, "StopAll",
            PlcDownloadPolicy.Sources.Default));
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(answeredAfter), effects, requireDownloadResult: false));
        var policyElsewhere = Result(effects);
        policyElsewhere.ConfigurationsSeen[0].Source = PlcDownloadPolicy.Sources.Policy;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(policyElsewhere), effects, requireDownloadResult: true));
    }

    [Theory]
    [MemberData(nameof(AbortReasonNames))]
    public void EveryAbortReason_IsAcceptedWhenItFitsThePlan(string reason)
    {
        var (aborted, effects) = AbortEvidence(reason);
        Assert.Equal(reason, PlcDownloadPayloadContract.DecodeResult(Payload(aborted), effects, requireDownloadResult: false).Abort!.Reason);
        Assert.Equal(PlcDownloadPolicy.AbortReasons.All.Order(StringComparer.Ordinal), new[]
        {
            "certificate_not_trusted", "configuration_limit_exceeded", "decision_not_applicable", "decision_rejected", "decision_required",
            "insecure_communication", "password_unavailable", "selection_unavailable", "unsupported_configuration",
        });
    }

    public static TheoryData<string> CallerDecisionViolations => new()
    {
        "defaultUsedDespiteCallerDecision", "callerSelectionDiffersFromRequest", "checkRecordedAsSelection", "passwordVariableDiffers",
        "callerSourceForDefaultedType", "noAnswerForCallerDecidedType",
    };

    private static PlcDownloadEffects DecidedEffects() => Effects(Item(pc: Hardware, target: "1 X1", numberExplicit: true,
        decisions: new[] { Select("StopModules", "NoAction"), Check("UpgradeTargetDevice") }, passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") }));

    /// <summary>Hardware download evidence with a caller-decided StopModules, a check, a password and the CPU default.</summary>
    private static PlcDownloadResultInfo DecidedResult(PlcDownloadEffects effects)
    {
        var result = Result(effects);
        result.ConfigurationsSeen[0].Selection = "NoAction";
        result.ConfigurationsSeen[0].Source = PlcDownloadPolicy.Sources.Caller;
        result.ConfigurationsSeen.Insert(1, new PlcDownloadConfigurationInfo
        {
            Phase = PlcDownloadPolicy.Phases.PreDownload, Type = Configurations + "UpgradeTargetDevice", CanonicalType = Configurations + "UpgradeTargetDevice",
            DecisionKey = "UpgradeTargetDevice", Kind = PlcDownloadPolicy.Kinds.Check, Decision = PlcDownloadPolicy.Decisions.Checked,
            Source = PlcDownloadPolicy.Sources.Caller,
        });
        result.ConfigurationsSeen.Insert(2, new PlcDownloadConfigurationInfo
        {
            Phase = PlcDownloadPolicy.Phases.PreDownload, Type = Configurations + "ModuleWriteAccessPassword",
            CanonicalType = Configurations + "ModuleWriteAccessPassword", DecisionKey = "ModuleWriteAccessPassword",
            Kind = PlcDownloadPolicy.Kinds.Password, Decision = PlcDownloadPolicy.Decisions.PasswordSet,
            Source = PlcDownloadPolicy.Sources.Caller, PasswordVariable = "PLC_PW", SecureCommunication = true,
        });
        result.ConfigurationsSeen.Insert(3, Selected(PlcDownloadPolicy.Phases.PreDownload, PlcDownloadPolicy.ConfigurationTypes.TargetForSoftware,
            "CPU", PlcDownloadPolicy.Sources.Default, "CPU", "PlcSimulationAdvanced"));
        return result;
    }

    [Fact]
    public void CallerDecisions_AreAcceptedExactlyAsRecorded()
    {
        var effects = DecidedEffects();
        var decoded = PlcDownloadPayloadContract.DecodeResult(Payload(DecidedResult(effects)), effects, requireDownloadResult: true);
        Assert.Equal(new[] { "caller", "caller", "caller", "default", "default" }, decoded.ConfigurationsSeen.Select(seen => seen.Source));
        Assert.Equal("PLC_PW", decoded.ConfigurationsSeen[2].PasswordVariable);
    }

    [Fact]
    public void PasswordSentOverANonSecureChannel_IsAcceptedOnlyWithTheOptIn()
    {
        // Audit T1: a password_set entry whose IsSecureCommunication is false or unread needs allowInsecurePasswordTransfer.
        var effects = DecidedEffects();
        var insecure = DecidedResult(effects);
        insecure.ConfigurationsSeen[2].SecureCommunication = false;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(insecure), effects, requireDownloadResult: true));
        var unread = DecidedResult(effects);
        unread.ConfigurationsSeen[2].SecureCommunication = null;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(unread), effects, requireDownloadResult: true));

        var allowed = effects with { AllowInsecurePasswordTransfer = true };
        Assert.False(PlcDownloadPayloadContract.DecodeResult(Payload(insecure), allowed, requireDownloadResult: true).ConfigurationsSeen[2].SecureCommunication);
        // The flag belongs to password entries only.
        var misplaced = DecidedResult(effects);
        misplaced.ConfigurationsSeen[0].SecureCommunication = true;
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(misplaced), effects, requireDownloadResult: true));
    }

    [Theory]
    [MemberData(nameof(CallerDecisionViolations))]
    public void CallerDecisions_RecordedDifferentlyThanRequested_AreRejected(string violation)
    {
        var effects = DecidedEffects();
        var result = DecidedResult(effects);
        switch (violation)
        {
            case "defaultUsedDespiteCallerDecision":
                result.ConfigurationsSeen[0].Selection = "StopAll";
                result.ConfigurationsSeen[0].Source = PlcDownloadPolicy.Sources.Default;
                break;
            case "callerSelectionDiffersFromRequest": result.ConfigurationsSeen[0].Selection = "StopAll"; break;
            case "checkRecordedAsSelection":
                result.ConfigurationsSeen[1].Decision = PlcDownloadPolicy.Decisions.Selected;
                result.ConfigurationsSeen[1].Selection = "true";
                break;
            case "passwordVariableDiffers": result.ConfigurationsSeen[2].PasswordVariable = "OTHER"; break;
            case "callerSourceForDefaultedType": result.ConfigurationsSeen[3].Source = PlcDownloadPolicy.Sources.Caller; break;
            case "noAnswerForCallerDecidedType":
                // TargetForSoftware on hardware is the CPU; PlcSimulationAdvanced was not requested.
                result.ConfigurationsSeen[3].Selection = "PlcSimulationAdvanced";
                break;
        }

        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(result), effects, requireDownloadResult: true));
    }

    [Fact]
    public void StartModulesSelection_FollowsTheRequest()
    {
        var effects = Effects(Item(start: false));
        var decoded = PlcDownloadPayloadContract.DecodeResult(Payload(Result(effects)), effects, requireDownloadResult: true);
        Assert.Equal("NoAction", decoded.ConfigurationsSeen[1].Selection);
    }

    [Fact]
    public void PortalConfirmations_DecodeAndFailTheOutcomeWithTheEvidence()
    {
        var effects = Effects();
        var confirmed = Result(effects);
        confirmed.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo { Type = "Siemens.Engineering.ConfirmationEventArgs", Text = "Continue?" });
        confirmed.PortalConfirmationsObserved = 1;

        var outcome = PlcDownloadWriteDomain.ProjectOutcome(WorkerCallResult.Ok(Payload(confirmed)), effects);

        Assert.Equal(OperationBatchStatus.Failed, outcome.Status);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, outcome.Failure!.Category);
        Assert.Contains("answered each one No", outcome.Failure.Message);
        Assert.Contains("portalConfirmations: \"accept\"", outcome.Failure.Message);
        // Unambiguous although the download itself reported success.
        Assert.Contains("reported state 'Success' with 0 error(s)", outcome.Failure.Message);
        Assert.Equal("Success", outcome.Value!.State);
        var entry = Assert.Single(outcome.Value.PortalConfirmations);
        Assert.Equal("No", entry.Answer);
        Assert.Equal("Continue?", entry.Text);
    }

    [Fact]
    public void AcceptedPortalConfirmations_DecodeAndDoNotFailTheOutcome()
    {
        var effects = Effects(Item(portalConfirmations: PlcDownloadPolicy.PortalConfirmationPolicies.Accept));
        var accepted = Result(effects);
        accepted.PortalConfirmations.Add(new PlcDownloadPortalConfirmationInfo { Type = "Siemens.Engineering.ConfirmationEventArgs", Answer = "Yes" });
        accepted.PortalConfirmationsObserved = 1;

        var outcome = PlcDownloadWriteDomain.ProjectOutcome(WorkerCallResult.Ok(Payload(accepted)), effects);

        Assert.Equal(OperationBatchStatus.Succeeded, outcome.Status);
        Assert.Null(outcome.Failure);
        Assert.Equal("Yes", Assert.Single(outcome.Value!.PortalConfirmations).Answer);

        accepted.PortalConfirmations[0].Answer = "No";
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(accepted), effects, requireDownloadResult: true));
    }

    [Fact]
    public void PortalConfirmations_BeyondTheListAreCountedAndMarkedIncomplete()
    {
        var effects = Effects();
        var many = Result(effects);
        var log = new PlcDownloadPortalConfirmationLog();
        for (var index = 0; index < PlcDownloadPolicy.MaxPortalConfirmations + 7; index++)
            log.Record("Siemens.Engineering.ConfirmationEventArgs", "Continue?", PlcDownloadPolicy.PortalConfirmationAnswers.No);
        log.CloseInto(many);

        var outcome = PlcDownloadWriteDomain.ProjectOutcome(WorkerCallResult.Ok(Payload(many)), effects);

        Assert.Equal(OperationBatchStatus.Failed, outcome.Status);
        Assert.Equal(PlcDownloadPolicy.MaxPortalConfirmations, outcome.Value!.PortalConfirmations.Count);
        Assert.Equal(PlcDownloadPolicy.MaxPortalConfirmations + 7, outcome.Value.PortalConfirmationsObserved);
        Assert.False(outcome.Value.PortalConfirmationsComplete);
        Assert.Contains($"raised {PlcDownloadPolicy.MaxPortalConfirmations + 7} confirmation", outcome.Failure!.Message);
        Assert.Contains($"only the first {PlcDownloadPolicy.MaxPortalConfirmations} are listed", outcome.Failure.Message);
    }

    [Fact]
    public void SuccessPayloadWithoutResultOrAbort_IsRejected_ButFailureEvidenceMayOmitIt()
    {
        var effects = Effects();
        var noResult = Result(effects);
        noResult.State = null;
        noResult.ErrorCount = null;
        noResult.WarningCount = null;
        noResult.Messages.Clear();
        Assert.ThrowsAny<JsonException>(() => PlcDownloadPayloadContract.DecodeResult(Payload(noResult), effects, requireDownloadResult: true));
        Assert.Null(PlcDownloadPayloadContract.DecodeResult(Payload(noResult), effects, requireDownloadResult: false).State);
    }

    [Fact]
    public void ProjectOutcome_Success()
    {
        var effects = Effects();
        var outcome = PlcDownloadWriteDomain.ProjectOutcome(WorkerCallResult.Ok(Payload(Result(effects))), effects);
        Assert.Equal(OperationBatchStatus.Succeeded, outcome.Status);
        Assert.Null(outcome.Failure);
        Assert.Equal("Success", outcome.Value!.State);
    }

    [Theory]
    [InlineData("Error", 1)]
    [InlineData("Error", 0)]
    [InlineData("Warning", 1)]
    public void ProjectOutcome_ErrorStateOrErrors_FailWithTheEvidence(string state, int errors)
    {
        var effects = Effects();
        var outcome = PlcDownloadWriteDomain.ProjectOutcome(WorkerCallResult.Ok(Payload(Result(effects, state, errors))), effects);
        Assert.Equal(OperationBatchStatus.Failed, outcome.Status);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, outcome.Failure!.Category);
        Assert.Equal(state, outcome.Value!.State);
    }

    [Fact]
    public void ProjectOutcome_Warning_WithoutErrors_Succeeds()
    {
        var effects = Effects();
        var outcome = PlcDownloadWriteDomain.ProjectOutcome(WorkerCallResult.Ok(Payload(Result(effects, "Warning"))), effects);
        Assert.Equal(OperationBatchStatus.Succeeded, outcome.Status);
    }

    [Fact]
    public void ProjectOutcome_AbortInASuccessPayload_Fails()
    {
        var effects = Effects();
        var aborted = Result(effects);
        aborted.ConfigurationsSeen.Add(new PlcDownloadConfigurationInfo
        {
            Phase = PlcDownloadPolicy.Phases.PostDownload, Type = "X.Unknown", Decision = PlcDownloadPolicy.Decisions.Aborted,
        });
        aborted.Abort = new PlcDownloadAbortInfo { Phase = PlcDownloadPolicy.Phases.PostDownload, Type = "X.Unknown", Reason = PlcDownloadPolicy.AbortReasons.UnsupportedConfiguration };
        var outcome = PlcDownloadWriteDomain.ProjectOutcome(WorkerCallResult.Ok(Payload(aborted)), effects);
        Assert.Equal(OperationBatchStatus.Failed, outcome.Status);
        Assert.Contains("post_download", outcome.Failure!.Message);
        Assert.Equal("X.Unknown", outcome.Value!.Abort!.Type);
    }

    [Fact]
    public void ProjectOutcome_FailureWithEvidence_KeepsCategoryEngineeringErrorAndEvidence()
    {
        var effects = Effects();
        var engineering = new EngineeringErrorInfo { Type = "EngineeringTargetInvocationException", Message = "rejected" };
        var failed = WorkerCallResult.Fail(WorkerFailureCategories.EngineeringRejected, "TIA Portal rejected the operation: rejected")
            with { EngineeringError = engineering, PlcDownloadOutcome = Payload(Aborted(effects)) };

        var outcome = PlcDownloadWriteDomain.ProjectOutcome(failed, effects);

        Assert.Equal(OperationBatchStatus.Failed, outcome.Status);
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, outcome.Failure!.Category);
        Assert.Same(engineering, outcome.Failure.EngineeringError);
        Assert.Equal(PlcDownloadPolicy.AbortReasons.DecisionRequired, outcome.Value!.Abort!.Reason);
    }

    [Fact]
    public void ProjectOutcome_MalformedEvidenceOrPayload_IsProtocolErrorWithoutEcho()
    {
        var effects = Effects();
        var failed = WorkerCallResult.Fail(WorkerFailureCategories.PortalLost, "lost") with { PlcDownloadOutcome = """{"secret":"PRIVATE"}""" };
        var outcome = PlcDownloadWriteDomain.ProjectOutcome(failed, effects);
        Assert.Equal(WorkerFailureCategories.ProtocolError, outcome.Failure!.Category);
        Assert.Contains("portal_lost", outcome.Failure.Message);
        Assert.DoesNotContain("PRIVATE", outcome.Failure.Message);
        Assert.Null(outcome.Value);

        var malformed = PlcDownloadWriteDomain.ProjectOutcome(WorkerCallResult.Ok("""{"state":"Success","secret":"PRIVATE"}"""), effects);
        Assert.Equal(WorkerFailureCategories.ProtocolError, malformed.Failure!.Category);
        Assert.DoesNotContain("PRIVATE", malformed.Failure.Message);
    }

    [Fact]
    public void ProjectOutcome_PortalLost_SaysTiaPortalMayHaveTerminated_AndHowToCheck()
    {
        var outcome = PlcDownloadWriteDomain.ProjectOutcome(
            WorkerCallResult.Fail(WorkerFailureCategories.PortalLost, "The TIA Portal connection was lost during the download."), Effects());

        Assert.Equal(WorkerFailureCategories.PortalLost, outcome.Failure!.Category);
        Assert.StartsWith("The TIA Portal connection was lost during the download.", outcome.Failure.Message);
        Assert.Contains("TIA Portal may have terminated", outcome.Failure.Message);
        Assert.Contains("bind_project action list_portals", outcome.Failure.Message);
        Assert.Contains("open_project", outcome.Failure.Message);
        Assert.Contains("does not restart", outcome.Failure.Message);

        var other = PlcDownloadWriteDomain.ProjectOutcome(WorkerCallResult.Fail(WorkerFailureCategories.WorkerOperationFailed, "failed"), Effects());
        Assert.DoesNotContain("may have terminated", other.Failure!.Message);
    }

    [Fact]
    public void ProjectOutcome_FailureWithoutEvidence_KeepsTheWorkerCategory()
    {
        var outcome = PlcDownloadWriteDomain.ProjectOutcome(
            WorkerCallResult.Fail(WorkerFailureCategories.StateChanged, "path changed"), Effects());
        Assert.Equal(WorkerFailureCategories.StateChanged, outcome.Failure!.Category);
        Assert.Null(outcome.Value);
    }

    [Fact]
    public void BoundOutcome_ShedsMessagesBeforeOmittingTheEvidence()
    {
        var effects = Effects();
        var big = Result(effects);
        big.Messages.Clear();
        for (var index = 0; index < 400; index++)
            big.Messages.Add(new PlcDownloadMessageInfo { Message = new string('m', 900), State = "Information" });
        var outcome = new TiaMcpServer.Tools.StandaloneToolOutcome<PlcDownloadResultInfo>(OperationBatchStatus.Succeeded, big, null, null);

        var bounded = PlcDownloadWriteDomain.BoundOutcome(outcome, 60_000, "itemLimit")!;

        Assert.Equal(OperationBatchStatus.Succeeded, bounded.Status);
        Assert.False(bounded.Value!.MessagesComplete);
        Assert.Contains("of 400 top-level download messages were omitted", bounded.Value.MessageOmission);
        Assert.True(TiaMcpServer.Json.CanonicalJson.Serialize(bounded.Value).Length <= 60_000);
        Assert.Equal(2, bounded.Value.ConfigurationsSeen.Count);
        Assert.Equal(400, big.Messages.Count); // The original evidence is not mutated.

        var omitted = PlcDownloadWriteDomain.BoundOutcome(outcome, 200, "itemLimit")!;
        Assert.Equal(OperationBatchStatus.Omitted, omitted.Status);
        Assert.Null(omitted.Value);
        Assert.Contains("do not replay the download", omitted.Omission!.Guidance);
    }
}
