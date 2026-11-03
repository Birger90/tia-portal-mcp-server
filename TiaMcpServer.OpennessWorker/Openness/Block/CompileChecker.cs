using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

public static class CompileChecker
{
    private static readonly JsonSerializerOptions ObservationJson = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static CompileCheckReport Compile(ProjectBase project, string? plcName, string? blockPath)
    {
        var report = CompileCore(project, plcName, blockPath, out _, out _);
        CompileReportProjection.BoundSerializedReport(report);
        return report;
    }

    /// <summary>
    /// compile_check with its <c>scope</c> (<see cref="CompileScopes"/>): <c>software</c> (or null) is <see cref="Compile(ProjectBase, string?, string?)"/>;
    /// <c>hardware</c> compiles, for every PLC (or the PLCs <paramref name="plcName"/> names), the hardware configuration
    /// through the <c>ICompilable</c> service of the CPU device item that hosts its software.
    /// </summary>
    public static CompileCheckReport Compile(ProjectBase project, string? plcName, string? blockPath, string? scope)
    {
        if (CompileScopes.Error(scope, blockPath) is { } error)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error);
        if (scope != CompileScopes.Hardware) return Compile(project, plcName, blockPath);
        var report = CompilePlcHardware(project, plcName);
        CompileReportProjection.BoundSerializedReport(report);
        return report;
    }

    private static CompileCheckReport CompilePlcHardware(ProjectBase project, string? plcName)
    {
        var report = new CompileCheckReport { Scope = CompileScopes.HardwareReportScope, OverallState = "Success" };
        var budget = new CompileReportProjection.Budget();
        var discoveryFailures = new List<string>();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, plcName, discoveryFailures).ToList();
        if (discoveryFailures.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, discoveryFailures[0] + " Nothing was compiled.");
        if (plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"No PLC software{(plcName is null ? string.Empty : $" named '{plcName}'")} was found in the project.");

        foreach (var plc in plcs)
        {
            if (plc.HostDeviceItem is null)
            {
                var unknown = BuildPlcCompileInfo(plc, null, budget, out _);
                unknown.DiagnosticNotes.Add("The CPU device item hosting this PLC software is unknown; its hardware was not compiled.");
                report.Plcs.Add(unknown);
                continue;
            }

            report.Plcs.Add(CompileTarget(plc, plc.HostDeviceItem, budget, out _, out _));
        }

        foreach (var plc in report.Plcs)
        {
            report.TotalErrorCount += plc.ErrorCount;
            report.TotalWarningCount += plc.WarningCount;
            report.OverallState = WorstState(report.OverallState, plc.State);
        }

        return report;
    }

    internal static CompileObservationInput CompileObserved(
        ProjectBase project,
        string? plcName,
        string? blockPath)
    {
        try
        {
            var report = CompileCore(
                project,
                plcName,
                blockPath,
                out var allInvocationsAvailable,
                out var detailsOmitted);
            detailsOmitted |= JsonSerializer.Serialize(report, ObservationJson).Length >= 60_000;
            CompileReportProjection.BoundSerializedReport(report);
            return new CompileObservationInput(
                report,
                targetSelectionAvailable: true,
                sessionAvailable: true,
                allInvocationsAvailable,
                detailsOmitted);
        }
        catch (NonRecoverableException)
        {
            return new CompileObservationInput(
                report: null,
                targetSelectionAvailable: true,
                sessionAvailable: false,
                allInvocationsAvailable: false);
        }
        catch (Exception)
        {
            return new CompileObservationInput(
                report: null,
                targetSelectionAvailable: false,
                sessionAvailable: true,
                allInvocationsAvailable: false);
        }
    }

    private static CompileCheckReport CompileCore(
        ProjectBase project,
        string? plcName,
        string? blockPath,
        out bool allInvocationsAvailable,
        out bool detailsOmitted) =>
        !string.IsNullOrWhiteSpace(blockPath)
            ? CompileBlock(
                project,
                plcName,
                blockPath!,
                out allInvocationsAvailable,
                out detailsOmitted)
            : CompilePlcSoftware(
                project,
                plcName,
                out allInvocationsAvailable,
                out detailsOmitted);

    private static CompileCheckReport CompileBlock(
        ProjectBase project,
        string? plcName,
        string blockPath,
        out bool allInvocationsAvailable,
        out bool detailsOmitted)
    {
        var address = BlockAddress.Parse(blockPath);
        if (address.PlcName == null && !string.IsNullOrWhiteSpace(plcName))
        {
            address = BlockAddress.Parse(plcName + "/" + blockPath);
        }

        // Without a PLC qualifier this requires a single PLC in the project (target_ambiguous otherwise).
        var selectedPlc = PlcSoftwareLocator.FindUnique(project, address.PlcName);
        // ResolveForExport throws target_not_found on a miss, so Block is set here.
        var target = BlockTargetResolver.ResolveForExport(selectedPlc.Software, address);

        var plc = CompileTarget(
            selectedPlc,
            target.Block!,
            new CompileReportProjection.Budget(),
            out allInvocationsAvailable,
            out detailsOmitted);

        var report = new CompileCheckReport
        {
            Scope = "block",
            BlockPath = blockPath,
            TotalErrorCount = plc.ErrorCount,
            TotalWarningCount = plc.WarningCount,
            OverallState = plc.State
        };

        report.Plcs.Add(plc);
        return report;
    }

    private static CompileCheckReport CompilePlcSoftware(
        ProjectBase project,
        string? plcName,
        out bool allInvocationsAvailable,
        out bool detailsOmitted)
    {
        var report = new CompileCheckReport
        {
            Scope = "plc",
            OverallState = "Success"
        };
        var budget = new CompileReportProjection.Budget();
        allInvocationsAvailable = true;
        detailsOmitted = false;

        // Discover every PLC before compiling any: an unreadable device item could hide one.
        var discoveryFailures = new List<string>();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, plcName, discoveryFailures).ToList();
        if (discoveryFailures.Count > 0)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                discoveryFailures[0] + " Nothing was compiled.");
        }

        foreach (var plc in plcs)
        {
            report.Plcs.Add(CompileTarget(
                plc,
                plc.Software,
                budget,
                out var invocationAvailable,
                out var plcDetailsOmitted));
            allInvocationsAvailable &= invocationAvailable;
            detailsOmitted |= plcDetailsOmitted;
        }

        if (report.Plcs.Count == 0)
        {
            var detail = plcName is null ? string.Empty : $" named '{plcName}'";
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"No PLC software{detail} was found in the project.");
        }

        foreach (var plc in report.Plcs)
        {
            report.TotalErrorCount += plc.ErrorCount;
            report.TotalWarningCount += plc.WarningCount;
            report.OverallState = WorstState(report.OverallState, plc.State);
        }

        return report;
    }

    private static PlcCompileInfo CompileTarget(PlcSoftwareLocator.DiscoveredPlcSoftware selectedPlc,
        object target,
        CompileReportProjection.Budget budget,
        out bool invocationAvailable,
        out bool detailsOmitted)
    {
        CompilerResult? result;
        invocationAvailable = true;
        try
        {
            result = CompileObject(target);
        }
        catch (Exception ex) when (IsExpectedCompileFailure(ex))
        {
            // Compiler/service invocation failed. Keep the selected identity, but no compiler
            // result is known. Never forward exception text across the worker boundary.
            result = null;
            invocationAvailable = false;
        }

        // Diagnostic access failures must not be mistaken for compiler invocation failures.
        return BuildPlcCompileInfo(selectedPlc, result, budget, out detailsOmitted);
    }

    private static bool IsExpectedCompileFailure(Exception exception) =>
        // Session loss must propagate even if Siemens derives it from EngineeringException.
        exception is not NonRecoverableException &&
        (exception is EngineeringException || exception.GetType() == typeof(InvalidOperationException));

    private static PlcCompileInfo BuildPlcCompileInfo(PlcSoftwareLocator.DiscoveredPlcSoftware selectedPlc,
        CompilerResult? result,
        CompileReportProjection.Budget budget,
        out bool detailsOmitted)
    {
        detailsOmitted = false;
        var plc = new PlcCompileInfo
        {
            PlcName = selectedPlc.Software.Name,
            DeviceName = selectedPlc.DeviceName,
            State = result == null ? "Error" : MapState(result.State),
            ErrorCount = result?.ErrorCount ?? 0,
            WarningCount = result?.WarningCount ?? 0
        };
        if (result == null)
        {
            plc.DiagnosticNotes.Add("Compilation failed; compiler details are unavailable.");
            return plc;
        }

        IEnumerable<CompilerResultMessage> messages;
        try
        {
            messages = result.Messages;
        }
        catch (Exception ex) when (IsExpectedCompileFailure(ex))
        {
            // State and totals above are already known, even when message acquisition fails.
            CompileReportProjection.NoteOmission(plc);
            detailsOmitted = true;
            return plc;
        }

        var projection = CompileReportProjection.Flatten(messages,
            message => message.Description,
            ReadMessagePath,
            MapMessageSeverity,
            message => message.Messages,
            budget);
        plc.Messages = projection.Messages;
        if (projection.WasTruncated)
        {
            CompileReportProjection.NoteOmission(plc);
            detailsOmitted = true;
        }
        return plc;
    }

    private static CompilerResult CompileObject(object compilable)
    {
        // Most compilable Openness objects (e.g. PlcSoftware) do not implement
        // ICompilable directly - the capability is exposed as a service via
        // IEngineeringServiceProvider.GetService<T>(), not as a type-level
        // interface, so reflecting over GetInterfaces() never finds it.
        if (compilable is IEngineeringServiceProvider serviceProvider)
        {
            var compilableService = serviceProvider.GetService<ICompilable>();
            if (compilableService != null)
            {
                return compilableService.Compile();
            }
        }

        var compileMethod = FindCompileMethod(compilable.GetType());
        if (compileMethod == null)
        {
            throw new InvalidOperationException($"Object '{compilable.GetType().Name}' does not expose a Compile method.");
        }

        try
        {
            return (CompilerResult)compileMethod.Invoke(compilable, null)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo? FindCompileMethod(Type type)
    {
        var compileMethod = type.GetMethod("Compile", BindingFlags.Instance | BindingFlags.Public);
        if (compileMethod != null)
        {
            return compileMethod;
        }

        foreach (var interfaceType in type.GetInterfaces())
        {
            compileMethod = interfaceType.GetMethod("Compile", BindingFlags.Instance | BindingFlags.Public);
            if (compileMethod != null)
            {
                return compileMethod;
            }
        }

        return null;
    }

    private static string MapState(CompilerResultState state)
    {
        switch (state)
        {
            case CompilerResultState.Success:
                return "Success";
            case CompilerResultState.Warning:
                return "Warning";
            case CompilerResultState.Error:
                return "Error";
            default:
                return state.ToString();
        }
    }

    private static string MapMessageSeverity(CompilerResultMessage message)
    {
        // Counts may include child messages. The row's own State is authoritative.
        // Resolve reflectively because the compile-time stubs may omit this property.
        var property = message.GetType().GetProperty("State")
            ?? throw new InvalidOperationException("Compiler message state is unavailable.");
        var state = ReadMessageProperty(property, message)?.ToString();
        return state == "Error" ? "Error" : state == "Warning" ? "Warning" : "Information";
    }

    private static string ReadMessagePath(CompilerResultMessage message)
    {
        // Path is not declared on the compile-time Openness stub; resolved at runtime from the full V21 assembly.
        var property = message.GetType().GetProperty("Path")
            ?? throw new InvalidOperationException("Compiler message path is unavailable.");
        return ReadMessageProperty(property, message)?.ToString() ?? string.Empty;
    }

    private static object? ReadMessageProperty(PropertyInfo property, CompilerResultMessage message)
    {
        try
        {
            return property.GetValue(message, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            Exception failure = ex.InnerException;
            while (failure is TargetInvocationException reflection && reflection.InnerException != null)
                failure = reflection.InnerException;
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    private static string WorstState(string current, string candidate)
    {
        if (current == "Error" || candidate == "Error")
        {
            return "Error";
        }

        if (current == "Warning" || candidate == "Warning")
        {
            return "Warning";
        }

        return "Success";
    }
}
