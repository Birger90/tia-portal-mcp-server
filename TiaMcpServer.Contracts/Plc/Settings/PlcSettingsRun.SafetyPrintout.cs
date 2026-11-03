using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using N = TiaMcpServer.Contracts.Plc.Settings.PlcSafetyPrintoutNames;

namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Issue #21: the safety printout of an F-CPU as a file (see <see cref="PlcSafetyPrintoutMethods"/>). The printout is a document for
/// the safety acceptance; generating it accepts nothing (<see cref="PlcSafetyPolicy.AcceptanceNotice"/>).
/// </summary>
public static partial class PlcSettingsRun
{
    private static bool WriteSafetyPrintout(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info,
        PlcSettingsResultInfo result, List<string> warnings)
    {
        if (method != PlcSafetyPrintoutMethods.GenerateSafetyPrintout) return false;
        var (printout, owner) = RequireService(context, context.CpuOwners(), N.Printout, CpuWhere(context) + " (the service is documented for S7-1500/ET 200SP F-CPUs, \"Plus PLCs\")");
        Describe(context, result, owner, N.Printout);
        var print = RequireMethodByShape(printout, "Print", p => p.Length == 4 && p[0].ParameterType.IsEnum && p[1].ParameterType == typeof(FileInfo)
            && p[2].ParameterType == typeof(string) && p[3].ParameterType.IsEnum, "Print(SafetyPrintoutFilePrinter, FileInfo, String, SafetyPrintoutOption)");
        var parameters = print.GetParameters();
        var printer = info.Printer ?? N.DefaultPrinter;
        var option = info.PrintOption ?? N.DefaultPrintOption;
        var printerValue = ConvertValue(parameters[0].ParameterType, printer, "printer");
        var optionValue = ConvertValue(parameters[3].ParameterType, option, "printOption");
        var layout = info.DocumentLayout ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "documentLayout is required (e.g. DocuInfo_ISO_A4_Portrait). Nothing was written.");
        result.Target = $"safety printout ({printer}, {option}, {layout})";
        result.File = WriteFile(context, info.FilePath, info.Overwrite, "SafetyPrintout.Print", temporary =>
        {
            var returned = Call(print, printout, printerValue, new FileInfo(temporary), layout, optionValue);
            if (returned is false)
                throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                    "SafetyPrintout.Print returned false (documented: true on success). Nothing was placed at the target.");
        }, result.Messages);
        result.Calls.Add($"SafetyPrintout.Print({printer}, FileInfo, \"{layout}\", {option})");
        warnings.Add(PlcSafetyPolicy.AcceptanceNotice);
        return true;
    }
}
