using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.PlcDownload;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>Inventories, items and results shared by the worker-independent plc_download tests.</summary>
internal static class PlcDownloadTestData
{
    public const string Hardware = "Intel(R) Ethernet Connection";

    public const string Configurations = PlcDownloadPolicy.ConfigurationsNamespace + ".";

    public static PlcDownloadTargetPlcInfo PlcEntry(string software, string device, bool downloadable = true,
        params PlcDownloadPcInterfaceInfo[] interfaces)
        => new()
        {
            PlcName = software, DeviceName = device, Downloadable = downloadable,
            Modes = downloadable ? new() { new PlcDownloadModeInfo { Name = "PN/IE", PcInterfaces = interfaces.ToList() } } : new(),
        };

    public static PlcDownloadPcInterfaceInfo Pc(string name, int number, params string[] targets)
        => new() { Name = name, Number = number, TargetInterfaces = targets.Select(t => new PlcDownloadTargetInterfaceInfo { Name = t }).ToList() };

    /// <summary>PLC_1 with PLCSIM #1 → "1 X1" and a hardware NIC with two targets.</summary>
    public static PlcDownloadTargetInventoryInfo SinglePlc() => new()
    {
        Plcs = { PlcEntry("PLC_1", "Station_1", true, Pc("PLCSIM", 1, "1 X1"), Pc(Hardware, 1, "1 X1", "1 X2")) },
    };

    public static PlcDownloadDecisionInfo Select(string type, string selection) => new() { Type = type, Selection = selection };

    public static PlcDownloadDecisionInfo Check(string type) => new() { Type = type, Check = true };

    public static PlcDownloadPasswordInfo Password(string type, string variable) => new() { Type = type, Variable = variable };

    public static PlcDownloadWriteItem Item(string? plc = "PLC_1", string pc = "PLCSIM", int number = 1, string? target = null,
        string mode = "PN/IE", string scope = PlcDownloadPolicy.Scopes.HardwareAndSoftware, bool start = true, bool numberExplicit = false,
        PlcDownloadDecisionInfo[]? decisions = null, PlcDownloadPasswordInfo[]? passwords = null,
        string portalConfirmations = PlcDownloadPolicy.PortalConfirmationPolicies.Decline, string[]? trustedCertificates = null)
        => new()
        {
            PlcName = plc, PcInterfaceName = pc, PcInterfaceNumber = number, PcInterfaceNumberExplicit = numberExplicit, TargetInterface = target,
            Mode = mode, Scope = scope, StartAfterDownload = start,
            Decisions = decisions ?? Array.Empty<PlcDownloadDecisionInfo>(),
            Passwords = passwords ?? Array.Empty<PlcDownloadPasswordInfo>(),
            PortalConfirmations = portalConfirmations,
            TrustedCertificates = trustedCertificates ?? Array.Empty<string>(),
        };

    /// <summary>PLC_1 whose PN/IE mode offers two PLCSIM PC interfaces (#1 and #2), each with one target.</summary>
    public static PlcDownloadTargetInventoryInfo TwoPlcsimInterfaces() => new()
    {
        Plcs = { PlcEntry("PLC_1", "Station_1", true, Pc("PLCSIM", 1, "1 X1"), Pc("PLCSIM", 2, "1 X1")) },
    };

    public static PlcDownloadEffects Effects(PlcDownloadWriteItem? item = null)
        => PlcDownloadPathResolver.Resolve(SinglePlc(), item ?? Item()).Effects!;

    /// <summary>An applied entry exactly as the recorder writes it for a selection configuration.</summary>
    public static PlcDownloadConfigurationInfo Selected(string phase, string canonicalType, string selection, string source, params string[] available)
        => new()
        {
            Phase = phase, Type = canonicalType, CanonicalType = canonicalType, DecisionKey = PlcDownloadPolicy.DecisionKey(canonicalType),
            Kind = PlcDownloadPolicy.Kinds.Selection, Message = "message", AvailableSelections = available.Length == 0 ? new() { selection } : available.ToList(),
            Decision = PlcDownloadPolicy.Decisions.Selected, Selection = selection, Source = source,
        };

    /// <summary>A result exactly describing <paramref name="effects"/> with a successful returned download.</summary>
    public static PlcDownloadResultInfo Result(PlcDownloadEffects effects, string state = "Success", int errors = 0)
    {
        var result = new PlcDownloadResultInfo
        {
            PlcName = effects.PlcName,
            DeviceName = effects.DeviceName,
            Path = new PlcDownloadPathInfo
            {
                Mode = effects.Path.Mode,
                PcInterface = new PlcDownloadPcInterfaceRefInfo { Name = effects.Path.PcInterface.Name, Number = effects.Path.PcInterface.Number },
                TargetInterface = effects.Path.TargetInterface,
            },
            Scope = effects.Scope,
            DownloadOptions = effects.DownloadOptions,
            StartAfterDownload = effects.StartAfterDownload,
            PortalConfirmationPolicy = effects.PortalConfirmations,
            State = state,
            ErrorCount = errors,
            WarningCount = 0,
            CompletionVerified = false,
            CompletionNote = PlcDownloadPolicy.CompletionNote,
        };
        result.ConfigurationsSeen.Add(Selected(PlcDownloadPolicy.Phases.PreDownload, PlcDownloadPolicy.ConfigurationTypes.StopModules,
            "StopAll", PlcDownloadPolicy.Sources.Default, "StopAll", "NoAction"));
        result.ConfigurationsSeen.Add(Selected(PlcDownloadPolicy.Phases.PostDownload, PlcDownloadPolicy.ConfigurationTypes.StartModules,
            effects.StartAfterDownload ? "StartModule" : "NoAction", PlcDownloadPolicy.Sources.Default, "StartModule", "NoAction"));
        result.Messages.Add(new PlcDownloadMessageInfo
        {
            Message = "Download completed.", State = state,
            Messages = { new PlcDownloadMessageInfo { Message = "Software loaded.", State = state } },
        });
        return result;
    }

    /// <summary>
    /// TLS verification evidence as the recorder writes it for one presented certificate with fingerprint
    /// <paramref name="sha256"/>; <paramref name="approved"/> marks it as listed in trustedCertificates.
    /// </summary>
    public static TiaMcpServer.Contracts.PlcOnline.PlcTlsVerificationInfo Tls(string sha256, bool approved, string? selectionSet)
        => new()
        {
            PlcName = "PLC_1", VerificationInfo = "The IP address does not match the certificate.",
            Certificates = { new TiaMcpServer.Contracts.PlcOnline.PlcTlsCertificateInfo
            {
                Sha256 = sha256, Sha1Thumbprint = new string('1', 40), Subject = "CN=PLC_1", Issuer = "CN=PLC_1",
                NotBefore = "2026-01-01T00:00:00Z", NotAfter = "2036-01-01T00:00:00Z", Approved = approved,
            } },
            CertificatesPresented = 1, CertificatesComplete = true, SelectionSet = selectionSet,
            Source = selectionSet == TiaMcpServer.Contracts.PlcOnline.PlcTlsTrustPolicy.Trusted ? TiaMcpServer.Contracts.PlcOnline.PlcTlsTrustPolicy.CallerSource : null,
        };

    /// <summary>An abort evidence record: StopModules answered, then a configuration without a decision, no returned result.</summary>
    public static PlcDownloadResultInfo Aborted(PlcDownloadEffects effects, string phase = PlcDownloadPolicy.Phases.PreDownload)
    {
        var result = Result(effects);
        const string type = Configurations + "DataBlockReinitialization";
        result.State = null;
        result.ErrorCount = null;
        result.WarningCount = null;
        result.Messages.Clear();
        result.ConfigurationsSeen.RemoveAt(1);
        result.ConfigurationsSeen.Add(new PlcDownloadConfigurationInfo
        {
            Phase = phase, Type = type, CanonicalType = type, DecisionKey = "DataBlockReinitialization", Kind = PlcDownloadPolicy.Kinds.Selection,
            Message = "Reinitialize data blocks?", AvailableSelections = new() { "StopPlcAndReinitialize", "NoAction" },
            Decision = PlcDownloadPolicy.Decisions.Aborted,
        });
        result.Abort = new PlcDownloadAbortInfo
        {
            Phase = phase, Type = type, CanonicalType = type, DecisionKey = "DataBlockReinitialization", Kind = PlcDownloadPolicy.Kinds.Selection,
            Message = "Reinitialize data blocks?", AvailableSelections = new() { "StopPlcAndReinitialize", "NoAction" },
            Reason = PlcDownloadPolicy.AbortReasons.DecisionRequired,
        };
        return result;
    }
}
