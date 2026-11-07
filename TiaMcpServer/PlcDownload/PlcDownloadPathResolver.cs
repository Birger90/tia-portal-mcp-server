using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.PlcDownload;

/// <summary>The plan-time verdict: resolved effects, or a call-level failure naming the candidates.</summary>
public sealed record PlcDownloadPathResolution(PlcDownloadEffects? Effects, WriteToolError? Error)
{
    public bool Success => Effects is not null;
}

/// <summary>
/// Pure and Siemens-free: resolves one plc_download request against the offline
/// <c>list_download_targets</c> inventory. Every level matches exactly (ordinal); there is no
/// fallback to another mode, interface or target. An omitted target interface is chosen only when
/// the PC interface offers exactly one, and an omitted PC interface (default <c>{ PLCSIM, 1 }</c>) is
/// refused when the mode offers several PC interfaces with that name. Any PC interface may be
/// resolved, hardware included; the effects say whether it is the simulation interface.
/// </summary>
public static class PlcDownloadPathResolver
{
    private const int MaxCandidates = 20;

    public static PlcDownloadPathResolution Resolve(PlcDownloadTargetInventoryInfo inventory, PlcDownloadWriteItem item)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(item);
        var named = item.PlcName is null ? string.Empty : $" named '{item.PlcName}'";
        if (inventory.Plcs.Count == 0)
            return Fail(WorkerFailureCategories.TargetNotFound, $"No PLC software{named} was found in the project.");
        if (inventory.Plcs.Count > 1)
            return Fail(WorkerFailureCategories.TargetAmbiguous,
                $"Several PLC software instances match{named}. Specify an unambiguous plcName. Candidates: "
                + Candidates(inventory.Plcs.Select(plc => $"'{plc.PlcName}' (device '{plc.DeviceName}')")));

        var plc = inventory.Plcs[0];
        if (!plc.Downloadable)
            return Fail(WorkerFailureCategories.TargetKindUnsupported,
                $"PLC '{plc.PlcName}' (device '{plc.DeviceName}') offers no download provider; it is not downloadable.");

        var modes = plc.Modes.Where(mode => string.Equals(mode.Name, item.Mode, StringComparison.Ordinal)).ToList();
        if (modes.Count != 1)
            return Fail(modes.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                $"Connection mode '{item.Mode}' {(modes.Count == 0 ? "is not configured" : "is configured more than once")} for PLC '{plc.PlcName}'. Modes: "
                + Candidates(plc.Modes.Select(mode => $"'{mode.Name}'")));

        var mode = modes[0];
        if (PlcDownloadPolicy.IsImplicitInterfaceNumberAmbiguous(mode.PcInterfaces.Select(pc => pc.Name), item.PcInterfaceName, item.PcInterfaceNumberExplicit))
            return Fail(WorkerFailureCategories.TargetAmbiguous,
                $"Mode '{mode.Name}' for PLC '{plc.PlcName}' offers several PC interfaces named '{item.PcInterfaceName}' "
                + "and pcInterface was omitted, so the default number is not used. Specify pcInterface { name, number }. Interfaces with that name: "
                + Candidates(mode.PcInterfaces.Where(pc => string.Equals(pc.Name, item.PcInterfaceName, StringComparison.Ordinal))
                    .Select(pc => $"'{pc.Name}' #{pc.Number}")));

        var pcInterfaces = mode.PcInterfaces.Where(pc => string.Equals(pc.Name, item.PcInterfaceName, StringComparison.Ordinal)
            && pc.Number == item.PcInterfaceNumber).ToList();
        if (pcInterfaces.Count != 1)
            return Fail(pcInterfaces.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                $"PC interface '{item.PcInterfaceName}' #{item.PcInterfaceNumber} {(pcInterfaces.Count == 0 ? "is not offered" : "is offered more than once")} "
                + $"in mode '{mode.Name}' for PLC '{plc.PlcName}'. PC interfaces: "
                + Candidates(mode.PcInterfaces.Select(pc => $"'{pc.Name}' #{pc.Number}")));

        var pcInterface = pcInterfaces[0];
        var where = $"under PC interface '{pcInterface.Name}' #{pcInterface.Number} in mode '{mode.Name}' for PLC '{plc.PlcName}'";
        var candidates = item.TargetInterface is null
            ? pcInterface.TargetInterfaces
            : pcInterface.TargetInterfaces.Where(target => string.Equals(target.Name, item.TargetInterface, StringComparison.Ordinal)).ToList();
        if (candidates.Count != 1)
        {
            var listed = Candidates(pcInterface.TargetInterfaces.Select(target => $"'{target.Name}'"));
            if (item.TargetInterface is null)
                return Fail(candidates.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                    candidates.Count == 0
                        ? $"No target interface is offered {where}."
                        : $"Several target interfaces are offered {where}. Specify targetInterface. Candidates: {listed}");
            return Fail(candidates.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                $"Target interface '{item.TargetInterface}' {(candidates.Count == 0 ? "is not offered" : "is offered more than once")} {where}. Target interfaces: {listed}");
        }

        PlcDownloadPolicy.TryGetDownloadOptionNames(item.Scope, out var options);
        // Validated before planning; an invalid list never reaches here, and an unnormalizable one trusts nothing.
        var trusted = PlcTlsTrustPolicy.NormalizeList(item.TrustedCertificates, out var normalized) is null ? normalized : new List<string>();
        return new PlcDownloadPathResolution(new PlcDownloadEffects(
            plc.PlcName,
            plc.DeviceName,
            new PlcDownloadPathInfo
            {
                Mode = mode.Name,
                PcInterface = new PlcDownloadPcInterfaceRefInfo { Name = pcInterface.Name, Number = pcInterface.Number },
                TargetInterface = candidates[0].Name,
            },
            TargetInterfaceSelectedAutomatically: item.TargetInterface is null,
            item.Scope,
            options,
            item.StartAfterDownload,
            SimulationTarget: PlcDownloadPolicy.IsSimulationInterface(pcInterface.Name),
            item.Decisions,
            item.Passwords,
            item.PortalConfirmations,
            trusted) { RhTarget = item.RhTarget, AllowInsecurePasswordTransfer = item.AllowInsecurePasswordTransfer ? true : null }, Error: null);
    }

    private static string Candidates(IEnumerable<string> names)
    {
        var all = names.ToList();
        if (all.Count == 0) return "(none).";
        var shown = string.Join(", ", all.Take(MaxCandidates));
        return all.Count > MaxCandidates ? $"{shown} and {all.Count - MaxCandidates} more." : shown + ".";
    }

    private static PlcDownloadPathResolution Fail(string category, string message)
        => new(Effects: null, new WriteToolError(category, message));
}
