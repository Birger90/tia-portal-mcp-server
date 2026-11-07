namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>The dispatch of the settings operations beyond issue #15 (each issue's partial adds its branch here).</summary>
public static partial class PlcSettingsRun
{
    private static IEnumerable<PlcSettingsSectionInfo> MoreCpuSections(PlcSettingsContext context)
        => UserSections(context);

    private static PlcSettingsReadInfo ReadMore(string method, IReadOnlyList<PlcSettingsContext> plcs, PlcSettingsContext? project, PlcSettingsReadInfo result,
        PlcSettingsRequestInfo info)
        => method switch
        {
            PlcUmacMethods.GetUmac => ReadUmac(plcs, project, result),
            PlcUmacMethods.GetUmcServerUser or PlcUmacMethods.CheckUmcConsistency => ReadUmcServer(method, project, result, info),
            _ when PlcSettingsCatalog.Find(method) is { Issue: PlcSettingsCatalog.PackageG } => ReadObjects(method, project, result, info),
            PlcCertificateMethods.GetCpuCertificates => ReadCertificates(plcs, result),
            PlcSafetyValidationMethods.GetSafetyValidation or PlcSafetyValidationMethods.CheckActivationTest => ReadSafetyValidation(method, project, result, info),
            _ => throw new Shared.RuntimeBindingException(Worker.WorkerFailureCategories.ValidationError, $"Unsupported settings read '{method}'."),
        };

    private static bool WriteMore(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info,
        PlcSettingsResultInfo result, List<string> warnings)
        => WriteUsers(method, definition, context, info, result, warnings)
            || WriteUmac(method, definition, context, info, result, warnings)
            || WriteCertificates(method, definition, context, info, result, warnings)
            || WriteSafetyPrintout(method, definition, context, info, result, warnings)
            || WriteSafetyValidation(method, definition, context, info, result, warnings)
            || WriteObjects(method, definition, context, info, result, warnings);
}
