using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>The host parts of the settings operations beyond issue #15.</summary>
internal static partial class PlcSettings
{
    private static void MoreRequest(PlcOperationRequest op, PlcSettingsRequestInfo request)
    {
        request.UserName = op.UserName;
        request.Permissions = op.Permissions?.ToList();
        request.RoleName = op.RoleName;
        request.RightName = op.RightName;
        request.UserKind = op.UserKind;
        request.AliasName = op.AliasName;
        request.Active = op.Active;
        request.RuntimeSessionTimeout = op.RuntimeSessionTimeout;
        request.Group = op.Group;
        request.Usage = op.Usage;
        request.CertificateId = op.CertificateId;
        request.CertificateRole = op.CertificateRole;
        request.SubjectAlternativeNames = op.SubjectAlternativeNames?.ToList();
        request.ServiceType = op.ServiceType;
        request.Printer = op.Printer;
        request.DocumentLayout = op.DocumentLayout;
        request.PrintOption = op.PrintOption;
        request.EvaluationDevice = op.EvaluationDevice;
        request.TestName = op.TestName;
        request.FunctionName = op.FunctionName;
        request.SignalDevice = op.SignalDevice;
        request.SignalUsage = op.SignalUsage;
        request.SignalName = op.SignalName;
        request.Traced = op.Traced;
        request.ObjectPath = op.ObjectPath;
        request.ObjectPaths = op.ObjectPaths?.ToList();
        request.CamFormat = op.CamFormat;
        request.CamSeparator = op.CamSeparator;
        request.SamplePoints = op.SamplePoints;
    }

    private static string? MoreObjectName(PlcOperationRequest item) => item.UserName ?? item.TestName ?? item.ObjectPath;

    private static IEnumerable<(string Field, string? Value)> MoreRequested(PlcOperationRequest item)
    {
        if (item.Permissions is { } permissions) yield return ("permissions", string.Join(", ", permissions));
        if (item.RoleName is not null) yield return ("roleName", item.RoleName);
        if (item.RightName is not null) yield return ("rightName", item.RightName);
        if (item.UserKind is not null) yield return ("userKind", item.UserKind);
        if (item.AliasName is not null) yield return ("aliasName", item.AliasName);
        if (item.Active is { } active) yield return ("active", active ? "true" : "false");
        if (item.RuntimeSessionTimeout is { } timeout) yield return ("runtimeSessionTimeout", timeout ? "true" : "false");
        if (item.Group is not null) yield return ("group", item.Group);
        if (item.Usage is not null) yield return ("usage", item.Usage);
        if (item.CertificateId is not null) yield return ("certificateId", item.CertificateId);
        if (item.CertificateRole is not null) yield return ("certificateRole", item.CertificateRole);
        if (item.SubjectAlternativeNames is { } sans) yield return ("subjectAlternativeNames", string.Join(", ", sans));
        if (item.ServiceType is not null) yield return ("serviceType", item.ServiceType);
        if (item.Printer is not null) yield return ("printer", item.Printer);
        if (item.DocumentLayout is not null) yield return ("documentLayout", item.DocumentLayout);
        if (item.PrintOption is not null) yield return ("printOption", item.PrintOption);
        if (item.EvaluationDevice is not null) yield return ("evaluationDevice", item.EvaluationDevice);
        if (item.TestName is not null) yield return ("testName", item.TestName);
        if (item.FunctionName is not null) yield return ("functionName", item.FunctionName);
        if (item.SignalDevice is not null) yield return ("signalDevice", item.SignalDevice);
        if (item.SignalUsage is not null) yield return ("signalUsage", item.SignalUsage);
        if (item.SignalName is not null) yield return ("signalName", item.SignalName);
        if (item.Traced is { } traced) yield return ("traced", traced ? "true" : "false");
        if (item.ObjectPath is not null) yield return ("objectPath", item.ObjectPath);
        if (item.ObjectPaths is { } paths) yield return ("objectPaths", string.Join(", ", paths));
        if (item.CamFormat is not null) yield return ("camFormat", item.CamFormat);
        if (item.CamSeparator is not null) yield return ("camSeparator", item.CamSeparator);
        if (item.SamplePoints is { } samples) yield return ("samplePoints", samples.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static IEnumerable<FiredGuard> MoreGuards(PlcOperationRequest item, string where)
    {
        if (PlcSettingsCatalog.Find(item.Operation) is { Issue: 25, IsWrite: true })
        {
            if (item.Operation is PlcSafetyValidationMethods.ExportActivationTest or PlcSafetyValidationMethods.GenerateActivationTestReport)
                yield return new FiredGuard(PlcGuardDefinitions.SafetyPrintoutIsNoAcceptance, item.OperationId,
                    $"{item.Operation} writes activation test '{item.TestName}' to '{item.FilePath}'. " + Contracts.Plc.Safety.PlcSafetyPolicy.AcceptanceNotice);
            else
                yield return new FiredGuard(PlcGuardDefinitions.SafetyAcceptance, item.OperationId,
                    $"{item.Operation} changes the Safety Validation Assistant's activation tests (the documentation of the safety acceptance tests). This server prepares tests and never records a result. "
                    + Contracts.Plc.Safety.PlcSafetyPolicy.AcceptanceNotice);
            if (item.Overwrite == true)
                yield return new FiredGuard(PlcGuardDefinitions.ExportOverwritesFile, item.OperationId, $"{item.Operation} replaces '{item.FilePath}' if it exists (overwrite:true).");
        }

        switch (item.Operation)
        {
            case PlcSafetyPrintoutMethods.GenerateSafetyPrintout:
                yield return new FiredGuard(PlcGuardDefinitions.SafetyPrintoutIsNoAcceptance, item.OperationId,
                    $"generate_safety_printout writes the safety printout of {where} to '{item.FilePath}'. " + Contracts.Plc.Safety.PlcSafetyPolicy.AcceptanceNotice);
                if (item.Overwrite == true)
                    yield return new FiredGuard(PlcGuardDefinitions.ExportOverwritesFile, item.OperationId, $"generate_safety_printout replaces '{item.FilePath}' if it exists (overwrite:true).");
                break;
            case PlcCertificateMethods.ImportCpuCertificate:
                yield return new FiredGuard(PlcGuardDefinitions.ImportsCertificate, item.OperationId,
                    $"import_cpu_certificate imports '{item.FilePath}' into the local certificate store of {where}; compare the reported sha256 with the source. Nothing is trusted.");
                break;
            case PlcCertificateMethods.DeleteCpuCertificate:
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"delete_cpu_certificate deletes certificate {item.CertificateId} of {where}; services that use it (secure communication, web server, OPC UA) lose it after a download.");
                break;
            case PlcCertificateMethods.UnassignCpuCertificate:
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"unassign_cpu_certificate removes the {item.CertificateRole} certificate assignment of {where}.");
                break;
            case PlcCertificateMethods.RemoveCertificateSupportedService:
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"remove_certificate_supported_service removes the certificate supported service with Id {item.CertificateId} from the certificate management of {where}; the service loses its certificate configuration after a download.");
                break;
            case PlcCertificateMethods.SetCertificateManager:
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"set_certificate_manager changes EnableGlobalCertificatesStore of {where} ({string.Join(", ", (item.Settings?.Entries ?? Array.Empty<KeyValuePair<string, string>>()).Select(e => $"{e.Key}={e.Value}"))}): "
                    + "which certificate store the CPU's certificates come from (global or local); check that no certificate the CPU relies on is lost.");
                break;
            case PlcCpuUserMethods.UpdateSimpleWebserverUser:
                foreach (var entry in (item.Settings?.Entries ?? Array.Empty<KeyValuePair<string, string>>()).Where(e => e.Key == "Permissions"))
                    if (!IsMostRestrictiveSimplePermission(entry.Value))
                        yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                            $"update_simple_webserver_user gives web server user '{item.UserName}' of {where} the permissions '{entry.Value}' (anything but None grants access).");
                break;
            case PlcCertificateMethods.ExportCpuCertificate when item.Overwrite == true:
                yield return new FiredGuard(PlcGuardDefinitions.ExportOverwritesFile, item.OperationId, $"export_cpu_certificate replaces '{item.FilePath}' if it exists (overwrite:true).");
                break;
            case PlcCpuUserMethods.CreateWebserverUser:
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"create_webserver_user adds web server user '{item.UserName}' to {where} with the permissions {string.Join(", ", item.Permissions ?? Array.Empty<string>())} (password from source {item.PasswordVariable}).");
                break;
            case PlcCpuUserMethods.UpdateWebserverUser when item.Permissions is not null:
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"update_webserver_user replaces the permissions of web server user '{item.UserName}' of {where} with {string.Join(", ", item.Permissions)}.");
                break;
            case PlcCpuUserMethods.CreateOpcUaUser:
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"create_opcua_user adds OPC UA user '{item.UserName}' to the OPC UA server of {where} (password from source {item.PasswordVariable}).");
                break;
        }

        if (PlcSettingsCatalog.Find(item.Operation) is { Issue: 12, IsWrite: true } && item.Operation != PlcUmacMethods.SetUmacPasswordPolicy)
            yield return new FiredGuard(PlcGuardDefinitions.ChangesProjectAccess, item.OperationId,
                $"{item.Operation} changes the project's user management and access control (UMAC) in the offline project"
                + (item.Operation is PlcUmacMethods.AssignEngineeringRight or PlcUmacMethods.UnassignEngineeringRight
                    ? $" (engineering function right '{item.RightName}' of role '{item.RoleName}'): engineering rights decide who may open and change the project; a protected project needs \"Modify project via Openness API\" for this server's writes."
                    : item.Operation is PlcUmacMethods.AssignDeviceFunctionRight or PlcUmacMethods.UnassignDeviceFunctionRight
                        ? $" (device function right '{item.RightName}' of role '{item.RoleName}' on PLC '{item.PlcName}'); it takes effect on the device after a download of the user management data."
                        : "."));
        switch (item.Operation)
        {
            case PlcObjectMethods.MarkObjects or PlcObjectMethods.UnmarkObjects:
                yield return new FiredGuard(PlcGuardDefinitions.ChangesMarkings, item.OperationId,
                    $"{item.Operation} {(item.Operation == PlcObjectMethods.MarkObjects ? "marks" : "unmarks")} {item.ObjectPaths?.Count ?? 0} object(s) of the bound Multiuser local session for check-in; nothing is checked in or sent to the server.");
                break;
            case PlcObjectMethods.LoadCamData:
                yield return new FiredGuard(PlcGuardDefinitions.LoadsCamData, item.OperationId,
                    $"load_cam_data replaces the cam data of '{item.ObjectPath}' with '{item.FilePath}' (CamDataSupport); no documented member compares them afterwards.");
                break;
            case PlcObjectMethods.SaveCamData when item.Overwrite == true:
                yield return new FiredGuard(PlcGuardDefinitions.ExportOverwritesFile, item.OperationId, $"save_cam_data replaces '{item.FilePath}' if it exists (overwrite:true).");
                break;
        }

        if (item.Operation == PlcUmacMethods.ProtectProject)
            yield return new FiredGuard(PlcGuardDefinitions.ProtectsProject, item.OperationId,
                $"protect_project protects the project with the administrator '{item.UserName}' (password from source {item.PasswordVariable}); keep the source available, or the project may become inaccessible.");
        if (item.Operation == PlcUmacMethods.ImportUmcUser)
            yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                $"import_umc_user adds the {(item.UserKind == PlcUmacNames.UmcUserGroup ? "UMC user group" : "UMC user")} '{item.Name}' from the UMC server to the project; it gets the rights of the roles assigned to it.");
        if (item.Operation == PlcUmacMethods.SetAnonymousUser && item.Active == true)
            yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                "set_anonymous_user activates the project's anonymous user: access without a user name and password, with the anonymous user's roles.");
        if (item.Operation is PlcUmacMethods.AssignEngineeringRight or PlcUmacMethods.AssignDeviceFunctionRight or PlcUmacMethods.AssignUserRole)
            yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                $"{item.Operation} grants more rights ({item.RightName ?? item.RoleName}).");
        if (item.Operation is PlcUmacMethods.CreateProjectUser || item.Operation == PlcUmacMethods.UpdateProjectUser && item.PasswordVariable is not null)
            yield return new FiredGuard(PlcGuardDefinitions.RaisesCpuProtection, item.OperationId,
                $"{item.Operation} sets the password of project user '{item.UserName}' from source {item.PasswordVariable}; keep the source available.");
        if (item.Operation is PlcCpuUserMethods.UpdateWebserverUser or PlcCpuUserMethods.UpdateSimpleWebserverUser && item.PasswordVariable is not null)
            yield return new FiredGuard(PlcGuardDefinitions.RaisesCpuProtection, item.OperationId,
                $"{item.Operation} sets a new password for user '{item.UserName}' of {where} from source {item.PasswordVariable}; keep the source available.");
    }

    private static IEnumerable<string?> MoreSources(PlcOperationRequest operation) => Array.Empty<string?>();

    /// <summary>
    /// The most restrictive <c>SimpleWebserverUserPermissions</c> value: empty or exactly <c>None</c>. Every other value, a
    /// combination of flags included (<c>ReadOnly, ReadWrite</c>), grants access (security review F8).
    /// </summary>
    internal static bool IsMostRestrictiveSimplePermission(string? value)
    {
        var parts = (value ?? string.Empty).Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        return parts.All(p => string.Equals(p, "None", StringComparison.OrdinalIgnoreCase) || p == "0");
    }

    private static string MoreReadFor(string operation)
        => PlcSettingsCatalog.Find(operation)?.Issue switch
        {
            12 => PlcUmacMethods.GetUmac,
            14 => PlcCertificateMethods.GetCpuCertificates,
            21 => "get_safety_administration",
            25 => PlcSafetyValidationMethods.GetSafetyValidation,
            _ => PlcCpuSettingsMethods.GetCpuSettings,
        };
}
