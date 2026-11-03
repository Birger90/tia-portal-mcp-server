using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// The TLS trust question of the online legitimation end to end through the scripted FakeWorker
/// (plc-download-tls, plc-online-tls), whose stand-in TlsVerificationConfiguration presents two fixed test
/// certificates (a leaf CN=PLC_1 and its CA) and is answered by the production recorders and reflection reader:
/// without trustedCertificates the call aborts with the evidence a human needs; with every presented fingerprint
/// approved it is Trusted (source caller); a decision can never trust it. Like every FakeWorker protocol test these
/// launch TiaMcpServer.FakeWorker.exe.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcTlsTrustMcpTests
{
    // SHA-256 over the DER bytes of the FakeWorker test certificates, cross-checked with openssl when they were generated.
    private const string Leaf = "DE87F8569826C2EC86DC10F9964E0699BD2D833A7CE5085929B05DDA48F5C061";
    private const string Ca = "E7D989934C4BB66B7EFABDADDFED6E9A4977752E05979E8A0BF9F8561A0634AE";
    private const string LeafWithColons = "de:87:f8:56:98:26:c2:ec:86:dc:10:f9:96:4e:06:99:bd:2d:83:3a:7c:e5:08:59:29:b0:5d:da:48:f5:c0:61";

    private sealed class Session : IAsyncDisposable
    {
        private readonly TempAuditDirectory _audit = new();
        private readonly FakeWorkerRequestLog _requests;
        private readonly FakeWorkerUiOpenProject _uiOpen;

        private Session(string scenario)
        {
            Directory.CreateDirectory(_audit.Path);
            _requests = new FakeWorkerRequestLog(_audit.Path);
            _uiOpen = new FakeWorkerUiOpenProject(scenario);
        }

        public McpProtocolTestHarness Harness { get; private set; } = null!;

        public List<string> PromptMessages { get; } = new();

        public string[] Methods => _requests.Methods();

        public string[] AuditLines => NetworkGuardedWriteMcpTests.AuditLines(_audit.Path);

        public static async Task<Session> StartAsync(McpAccessMode mode, string scenario, bool confirm = false)
        {
            var session = new Session(scenario);
            McpClientOptions? options = !confirm ? null : new()
            {
                Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
                Handlers = new McpClientHandlers
                {
                    ElicitationHandler = (request, _) =>
                    {
                        session.PromptMessages.Add(request?.Message ?? string.Empty);
                        return ValueTask.FromResult(new ElicitResult
                        {
                            Action = "accept",
                            Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) },
                        });
                    },
                },
            };
            session.Harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, session._audit.Path, scenario, clientOptions: options);
            return session;
        }

        public async Task<(JsonElement Document, CallToolResult Raw)> CallAsync(string tool, Dictionary<string, object?> arguments)
        {
            var raw = await Harness.Client.CallToolAsync(tool, arguments);
            Assert.Empty(StructuredContractInspector.FindViolations(raw));
            return (raw.StructuredContent!.Value, raw);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            _uiOpen.Dispose();
            _requests.Dispose();
            _audit.Dispose();
        }
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    private static readonly object Plcsim = new { name = "PLCSIM", number = 1 };

    /// <summary>The abort evidence: decision key, reason, both certificates with what a human compares, NonTrusted set.</summary>
    private static void AssertRefusedEvidence(JsonElement abort, string plcName, bool leafApproved = false)
    {
        Assert.Equal("TlsVerificationConfiguration", abort.GetProperty("decisionKey").GetString());
        Assert.Equal("certificate_not_trusted", abort.GetProperty("reason").GetString());
        var tls = abort.GetProperty("tls");
        Assert.Equal(plcName, tls.GetProperty("plcName").GetString());
        Assert.Equal("The IP address of the device does not match the addresses of its certificate.", tls.GetProperty("verificationInfo").GetString());
        Assert.Equal("NonTrusted", tls.GetProperty("selectionSet").GetString());
        Assert.Equal(JsonValueKind.Null, tls.GetProperty("source").ValueKind);
        Assert.Equal(2, tls.GetProperty("certificatesPresented").GetInt32());
        Assert.True(tls.GetProperty("certificatesComplete").GetBoolean());
        var certificates = tls.GetProperty("certificates").EnumerateArray().ToArray();
        Assert.Equal(new[] { Leaf, Ca }, certificates.Select(c => c.GetProperty("sha256").GetString()));
        Assert.Equal(new[] { leafApproved, false }, certificates.Select(c => c.GetProperty("approved").GetBoolean()));
        Assert.Equal("AD553DD89C3D5F711310811BD010F1E699ECA8C0", certificates[0].GetProperty("sha1Thumbprint").GetString());
        Assert.Contains("CN=PLC_1", certificates[0].GetProperty("subject").GetString());
        Assert.Contains("CN=FakeWorker Stand-in CA", certificates[0].GetProperty("issuer").GetString());
        Assert.Equal("2026-11-01T10:29:38Z", certificates[0].GetProperty("notBefore").GetString());
        Assert.Equal("2126-10-08T10:29:38Z", certificates[0].GetProperty("notAfter").GetString());
    }

    private static void AssertRetryGuidance(string? message)
    {
        Assert.Contains(Leaf, message);
        Assert.Contains(Ca, message);
        Assert.Contains("show the user these fingerprints", message);
        Assert.Contains("Zertifikate anzeigen", message);
        Assert.Contains($"trustedCertificates: [\"{Leaf}\", \"{Ca}\"]", message);
    }

    private static void AssertTrusted(JsonElement entry)
    {
        Assert.Equal("selected", entry.GetProperty("decision").GetString());
        Assert.Equal("Trusted", entry.GetProperty("selection").GetString());
        Assert.Equal("Siemens.Engineering.Online.Configurations.TlsVerificationConfiguration", entry.GetProperty("canonicalType").GetString());
        var tls = entry.GetProperty("tls");
        Assert.Equal("Trusted", tls.GetProperty("selectionSet").GetString());
        Assert.Equal("caller", tls.GetProperty("source").GetString());
        Assert.All(tls.GetProperty("certificates").EnumerateArray(), certificate => Assert.True(certificate.GetProperty("approved").GetBoolean()));
    }

    [Fact]
    public async Task Download_WithoutTrustedCertificates_AbortsWithTheEvidenceAHumanNeeds()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-download-tls");
        var (document, raw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1")));

        Assert.False(raw.IsError == true);
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.False(document.GetProperty("success").GetBoolean());
        var result = document.GetProperty("result");
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal("worker_operation_failed", result.GetProperty("failure").GetProperty("category").GetString());
        var message = result.GetProperty("failure").GetProperty("message").GetString();
        Assert.Contains("online_legitimation phase", message);
        AssertRetryGuidance(message);
        var value = result.GetProperty("value");
        var abort = value.GetProperty("abort");
        Assert.Equal("online_legitimation", abort.GetProperty("phase").GetString());
        AssertRefusedEvidence(abort, "PLC_1");
        var seen = Assert.Single(value.GetProperty("configurationsSeen").EnumerateArray());
        // K27: refused with NonTrusted by the server policy without a throw from TIA Portal's callback.
        Assert.Equal("selected", seen.GetProperty("decision").GetString());
        Assert.Equal("NonTrusted", seen.GetProperty("selection").GetString());
        Assert.Equal("policy", seen.GetProperty("source").GetString());
        Assert.Equal(abort.GetProperty("tls").GetRawText(), seen.GetProperty("tls").GetRawText());
        Assert.Empty(document.GetProperty("effects").GetProperty("trustedCertificates").EnumerateArray());
        Assert.Single(session.AuditLines);
    }

    [Fact]
    public async Task Download_EveryPresentedCertificateTrusted_Downloads_AndThePromptAndAuditListTheFingerprints()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, "plc-download-tls", confirm: true);
        var (document, raw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"),
            ("trustedCertificates", new[] { LeafWithColons, Ca.ToLowerInvariant() })));

        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(new[] { Leaf, Ca }, document.GetProperty("effects").GetProperty("trustedCertificates").EnumerateArray().Select(f => f.GetString()));
        var seen = document.GetProperty("result").GetProperty("value").GetProperty("configurationsSeen").EnumerateArray().ToArray();
        Assert.Equal("online_legitimation", seen[0].GetProperty("phase").GetString());
        Assert.Equal("caller", seen[0].GetProperty("source").GetString());
        AssertTrusted(seen[0]);
        Assert.Equal(new[] { "pre_download", "pre_download", "pre_download", "post_download" }, seen.Skip(1).Select(entry => entry.GetProperty("phase").GetString()));
        var prompt = Assert.Single(session.PromptMessages);
        Assert.Contains($"one of these SHA-256 fingerprints (trustedCertificates), otherwise NonTrusted and the download aborts: {Leaf}, {Ca}.", prompt);
        Assert.Contains(Leaf, Assert.Single(session.AuditLines));
        Assert.Contains("plc_download", session.Methods);
    }

    [Fact]
    public async Task Download_OnlyTheLeafTrusted_RefusesTheWholeChain()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-download-tls");
        var (document, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("trustedCertificates", new[] { Leaf })));

        Assert.False(document.GetProperty("success").GetBoolean());
        var abort = document.GetProperty("result").GetProperty("value").GetProperty("abort");
        AssertRefusedEvidence(abort, "PLC_1", leafApproved: true);
        Assert.Contains("1 of 2 presented certificate(s)", abort.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("decisions")]
    [InlineData("passwords")]
    public async Task Download_TlsAsADecisionOrPassword_IsAValidationError(string map)
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-download-tls");
        var (document, raw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"),
            (map, new Dictionary<string, object> { ["TlsVerificationConfiguration"] = map == "decisions" ? "Trusted" : "PATH" })));

        Assert.True(raw.IsError == true);
        Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains($"{map}.TlsVerificationConfiguration is not accepted", document.GetProperty("error").GetProperty("message").GetString());
        Assert.Contains("only through trustedCertificates", document.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("plc_download", session.Methods);
    }

    [Fact]
    public async Task Download_MalformedTrustedCertificates_AreRefusedBeforeTheWorkerIsAsked()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-download-tls");

        var (format, formatRaw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("trustedCertificates", new[] { Leaf, "XYZ" })));
        Assert.True(formatRaw.IsError == true);
        Assert.Equal("validation_error", format.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("trustedCertificates[1] is not a SHA-256 fingerprint", format.GetProperty("error").GetProperty("message").GetString());

        var (repeat, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("trustedCertificates", new[] { Leaf, LeafWithColons })));
        Assert.Contains("trustedCertificates[1] repeats trustedCertificates[0]", repeat.GetProperty("error").GetProperty("message").GetString());
        var entered = session.AuditLines.Length;

        // A non-string element or a non-array is an argument-shape error: the canonical validation_error without an audit record.
        var (shape, shapeRaw) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("trustedCertificates", new object[] { 42 })));
        Assert.True(shapeRaw.IsError == true);
        Assert.Equal("validation_error", shape.GetProperty("error").GetProperty("category").GetString());
        var (single, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("trustedCertificates", Leaf)));
        Assert.Equal("validation_error", single.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(entered, session.AuditLines.Length);
        Assert.DoesNotContain("plc_download", session.Methods);

        // An explicit null means omitted.
        var (omitted, _) = await session.CallAsync("plc_download", Args(("plcName", "PLC_1"), ("trustedCertificates", null)));
        Assert.Equal("certificate_not_trusted", omitted.GetProperty("result").GetProperty("value").GetProperty("abort").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task GoOnline_RefusesUntilTheUserApprovedEveryFingerprint_ThenConnects()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, "plc-online-tls", confirm: true);
        var (refused, _) = await session.CallAsync("plc_tia_online", Args(("action", "go_online"), ("plcName", "PLC_1"), ("pcInterface", Plcsim)));

        Assert.False(refused.GetProperty("success").GetBoolean());
        Assert.Contains("A TLS certificate check of the device (TlsVerificationConfiguration) aborts the connection", session.PromptMessages[0]);
        var result = refused.GetProperty("result");
        AssertRetryGuidance(result.GetProperty("failure").GetProperty("message").GetString());
        AssertRefusedEvidence(result.GetProperty("value").GetProperty("abort"), "PLC_1");
        // K27: the refusal returned normally from the callback (entry answered NonTrusted); GoOnline returned, the connection was
        // refused (NotReachable) and the recorded abort decided: a clean failure, never success.
        var value = result.GetProperty("value");
        var refusedEntry = Assert.Single(value.GetProperty("configurationsSeen").EnumerateArray());
        Assert.Equal(("selected", "NonTrusted"), (refusedEntry.GetProperty("decision").GetString(), refusedEntry.GetProperty("selection").GetString()));
        Assert.Equal("not_reached", value.GetProperty("outcome").GetString());
        Assert.Equal("worker_operation_failed", result.GetProperty("failure").GetProperty("category").GetString());

        var (connected, _) = await session.CallAsync("plc_tia_online", Args(("action", "go_online"), ("plcName", "PLC_1"), ("pcInterface", Plcsim),
            ("trustedCertificates", new[] { Ca, Leaf })));
        Assert.True(connected.GetProperty("success").GetBoolean(), connected.GetRawText());
        Assert.Contains($"{Ca}, {Leaf}", session.PromptMessages[1]);
        AssertTrusted(Assert.Single(connected.GetProperty("result").GetProperty("value").GetProperty("configurationsSeen").EnumerateArray()));
        Assert.Equal(new[] { Ca, Leaf }, connected.GetProperty("effects").GetProperty("trustedCertificates").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task GoOffline_RefusesTrustedCertificates()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-online-tls");
        var (document, raw) = await session.CallAsync("plc_tia_online", Args(("action", "go_offline"), ("plcName", "PLC_1"), ("trustedCertificates", new[] { Leaf })));

        Assert.True(raw.IsError == true);
        Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("trustedCertificates apply to go_online", document.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task StationUpload_SharesOneEvidenceListForTheConnectionAndTheUpload()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-online-tls");
        var decisions = new Dictionary<string, string> { ["UploadMissingProducts"] = "NoAction" };
        var (refused, _) = await session.CallAsync("plc_station_upload", Args(("address", "192.168.0.10"), ("pcInterface", Plcsim), ("decisions", decisions)));

        Assert.False(refused.GetProperty("success").GetBoolean());
        AssertRefusedEvidence(refused.GetProperty("result").GetProperty("value").GetProperty("abort"), "Uploaded_Station");

        var (uploaded, _) = await session.CallAsync("plc_station_upload", Args(("address", "192.168.0.10"), ("pcInterface", Plcsim), ("decisions", decisions),
            ("trustedCertificates", new[] { Leaf, Ca })));
        Assert.True(uploaded.GetProperty("success").GetBoolean(), uploaded.GetRawText());
        var seen = uploaded.GetProperty("result").GetProperty("value").GetProperty("configurationsSeen").EnumerateArray().ToArray();
        AssertTrusted(seen[0]);
        Assert.Equal("Siemens.Engineering.Upload.Configurations.UploadMissingProducts", seen[1].GetProperty("canonicalType").GetString());
        Assert.False(seen[1].TryGetProperty("tls", out _));
    }

    [Fact]
    public async Task StationUpload_TlsAsADecision_IsAValidationError()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, "plc-online-tls");
        var (document, raw) = await session.CallAsync("plc_station_upload", Args(("address", "192.168.0.10"), ("pcInterface", Plcsim),
            ("decisions", new Dictionary<string, string> { ["TlsVerificationConfiguration"] = "Trusted" })));

        Assert.True(raw.IsError == true);
        Assert.Contains("only through trustedCertificates", document.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("plc_station_upload", session.Methods);
    }

    [Fact]
    public async Task CompareToOnline_InReadOnly_IsDiscardedWithEvidence_UntilTheFingerprintsAreApproved()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly, "plc-online-tls");
        var (refusedDocument, _) = await session.CallAsync("plc_read", Args(("operations", new object[]
        {
            new { operationId = "compare", operation = "compare_to_online", plcName = "PLC_1" },
        })));

        var refused = refusedDocument.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("failed", refused.GetProperty("status").GetString());
        Assert.Equal("worker_operation_failed", refused.GetProperty("failure").GetProperty("category").GetString());
        AssertRetryGuidance(refused.GetProperty("failure").GetProperty("message").GetString());
        var evidence = refused.GetProperty("result");
        Assert.Equal(JsonValueKind.Null, evidence.GetProperty("root").ValueKind);
        AssertRefusedEvidence(evidence.GetProperty("abort"), "PLC_1");

        var (trustedDocument, _) = await session.CallAsync("plc_read", Args(("operations", new object[]
        {
            new { operationId = "compare", operation = "compare_to_online", plcName = "PLC_1", trustedCertificates = new[] { Leaf, Ca } },
        })));
        var compared = trustedDocument.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", compared.GetProperty("status").GetString());
        var result = compared.GetProperty("result");
        Assert.NotEqual(JsonValueKind.Null, result.GetProperty("root").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("abort").ValueKind);
        AssertTrusted(Assert.Single(result.GetProperty("configurationsSeen").EnumerateArray()));
    }

    [Fact]
    public async Task CompareSoftware_RefusesTrustedCertificates_AndMalformedOnesAreRefusedForTheCompare()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly, "plc-online-tls");
        var (document, raw) = await session.CallAsync("plc_read", Args(("operations", new object[]
        {
            new { operationId = "software", operation = "compare_software", compareTarget = "project_library", trustedCertificates = new[] { Leaf } },
        })));
        Assert.True(raw.IsError == true);
        Assert.Contains("trustedCertificates", document.GetRawText());

        var (malformed, malformedRaw) = await session.CallAsync("plc_read", Args(("operations", new object[]
        {
            new { operationId = "compare", operation = "compare_to_online", trustedCertificates = new[] { "00" } },
        })));
        Assert.True(malformedRaw.IsError == true);
        Assert.Contains("trustedCertificates[0] is not a SHA-256 fingerprint", malformed.GetRawText());
        Assert.DoesNotContain("compare_to_online", session.Methods);
    }
}
