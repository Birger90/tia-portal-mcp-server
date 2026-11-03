using TiaMcpServer.Contracts.Plc.Settings;

namespace TiaMcpServer.Plc;

/// <summary>The settings operations beyond issue #15: their field sets and shape rules.</summary>
public static partial class PlcOperationCatalog
{
    private static void MoreSettingsSpecs(Dictionary<string, Spec> specs)
    {
        // Issue #13: web server and OPC UA users.
        specs[PlcCpuUserMethods.CreateWebserverUser] = Write(new[] { "userName", "passwordVariable" }, new[] { "plcName", "permissions" });
        specs[PlcCpuUserMethods.UpdateWebserverUser] = Write(new[] { "userName" }, new[] { "plcName", "permissions", "passwordVariable" });
        specs[PlcCpuUserMethods.DeleteWebserverUser] = Write(new[] { "userName" }, new[] { "plcName" });
        specs[PlcCpuUserMethods.UpdateSimpleWebserverUser] = Write(new[] { "userName" }, new[] { "plcName", "settings", "passwordVariable" });
        specs[PlcCpuUserMethods.CreateOpcUaUser] = Write(new[] { "userName", "passwordVariable" }, new[] { "plcName" });
        specs[PlcCpuUserMethods.SetOpcUaUserPassword] = Write(new[] { "userName", "passwordVariable" }, new[] { "plcName" });
        specs[PlcCpuUserMethods.DeleteOpcUaUser] = Write(new[] { "userName" }, new[] { "plcName" });
        // Issue #12: UMAC (project-wide except the device function rights, which name a PLC).
        specs[PlcUmacMethods.GetUmac] = Read(Array.Empty<string>(), new[] { "plcName" });
        specs[PlcUmacMethods.CreateCustomRole] = Write(new[] { "name" }, new[] { "settings" });
        specs[PlcUmacMethods.UpdateCustomRole] = Write(new[] { "name" }, new[] { "settings", "newName" });
        specs[PlcUmacMethods.DeleteCustomRole] = Write(new[] { "name" }, Array.Empty<string>());
        specs[PlcUmacMethods.AssignEngineeringRight] = Write(new[] { "roleName", "rightName" }, Array.Empty<string>());
        specs[PlcUmacMethods.UnassignEngineeringRight] = Write(new[] { "roleName", "rightName" }, Array.Empty<string>());
        specs[PlcUmacMethods.AssignDeviceFunctionRight] = Write(new[] { "roleName", "rightName" }, new[] { "plcName" });
        specs[PlcUmacMethods.UnassignDeviceFunctionRight] = Write(new[] { "roleName", "rightName" }, new[] { "plcName" });
        specs[PlcUmacMethods.CreateProjectUser] = Write(new[] { "userName", "passwordVariable" }, new[] { "settings" });
        specs[PlcUmacMethods.UpdateProjectUser] = Write(new[] { "userName" }, new[] { "settings", "newName", "aliasName", "active", "runtimeSessionTimeout", "passwordVariable" });
        specs[PlcUmacMethods.DeleteProjectUser] = Write(new[] { "userName" }, Array.Empty<string>());
        specs[PlcUmacMethods.AssignUserRole] = Write(new[] { "userName", "roleName" }, new[] { "userKind" });
        specs[PlcUmacMethods.UnassignUserRole] = Write(new[] { "userName", "roleName" }, new[] { "userKind" });
        specs[PlcUmacMethods.SetAnonymousUser] = Write(Array.Empty<string>(), new[] { "active" });
        specs[PlcUmacMethods.CreateCustomDeviceFunctionRight] = Write(new[] { "name", "group" }, new[] { "settings" });
        specs[PlcUmacMethods.UpdateCustomDeviceFunctionRight] = Write(new[] { "name" }, new[] { "settings", "newName" });
        specs[PlcUmacMethods.DeleteCustomDeviceFunctionRight] = Write(new[] { "name" }, Array.Empty<string>());
        specs[PlcUmacMethods.UpdateUmcUser] = Write(new[] { "userName" }, new[] { "userKind", "active", "newName" });
        specs[PlcUmacMethods.DeleteUmcUser] = Write(new[] { "userName" }, new[] { "userKind" });
        specs[PlcUmacMethods.SetUmacPasswordPolicy] = Write(Array.Empty<string>(), new[] { "settings" });
        // Issue #14: certificates and dynamic certificate settings of the CPU.
        specs[PlcCertificateMethods.GetCpuCertificates] = Read(Array.Empty<string>(), new[] { "plcName" });
        specs[PlcCertificateMethods.SetCertificateManager] = Write(Array.Empty<string>(), new[] { "plcName", "settings" });
        specs[PlcCertificateMethods.CreateCpuCertificate] = Write(new[] { "usage" }, new[] { "plcName", "settings", "subjectAlternativeNames" });
        specs[PlcCertificateMethods.ImportCpuCertificate] = Write(new[] { "filePath" }, new[] { "plcName", "passwordVariable" });
        specs[PlcCertificateMethods.ExportCpuCertificate] = Write(new[] { "certificateId", "filePath" }, new[] { "plcName", "overwrite" });
        specs[PlcCertificateMethods.DeleteCpuCertificate] = Write(new[] { "certificateId" }, new[] { "plcName" });
        specs[PlcCertificateMethods.AssignCpuCertificate] = Write(new[] { "certificateId", "certificateRole" }, new[] { "plcName" });
        specs[PlcCertificateMethods.UnassignCpuCertificate] = Write(new[] { "certificateRole" }, new[] { "plcName" });
        specs[PlcCertificateMethods.SetCertificateManagement] = Write(Array.Empty<string>(), new[] { "plcName", "settings" });
        specs[PlcCertificateMethods.AddCertificateSupportedService] = Write(new[] { "certificateId", "serviceType" }, new[] { "plcName", "name" });
        specs[PlcCertificateMethods.UpdateCertificateSupportedService] = Write(new[] { "certificateId" }, new[] { "plcName", "settings" });
        specs[PlcCertificateMethods.RemoveCertificateSupportedService] = Write(new[] { "certificateId" }, new[] { "plcName" });
        // Issue #21: the safety printout (a caller file).
        specs[PlcSafetyPrintoutMethods.GenerateSafetyPrintout] = Write(new[] { "filePath", "documentLayout" }, new[] { "plcName", "printer", "printOption", "overwrite" });
        // Issue #25: the Safety Validation Assistant (project-wide); nothing records a test result.
        specs[PlcSafetyValidationMethods.GetSafetyValidation] = Read(Array.Empty<string>(), Array.Empty<string>());
        specs[PlcSafetyValidationMethods.CheckActivationTest] = Read(new[] { "testName" }, new[] { "functionName", "position" });
        specs[PlcSafetyValidationMethods.CreateActivationTest] = Write(new[] { "name", "evaluationDevice" }, new[] { "folderPath" });
        specs[PlcSafetyValidationMethods.DeleteActivationTest] = Write(new[] { "name" }, Array.Empty<string>());
        specs[PlcSafetyValidationMethods.ChangeActivationTestEvaluationDevice] = Write(new[] { "name", "evaluationDevice" }, Array.Empty<string>());
        specs[PlcSafetyValidationMethods.CreateActivationTestGroup] = Write(new[] { "folderPath" }, Array.Empty<string>());
        specs[PlcSafetyValidationMethods.DeleteActivationTestGroup] = Write(new[] { "folderPath" }, Array.Empty<string>());
        specs[PlcSafetyValidationMethods.CreateSafetyFunction] = Write(new[] { "testName" }, new[] { "settings", "traced" });
        specs[PlcSafetyValidationMethods.UpdateSafetyFunction] = Write(new[] { "testName", "functionName" }, new[] { "settings", "traced" });
        specs[PlcSafetyValidationMethods.DeleteSafetyFunction] = Write(new[] { "testName", "functionName" }, Array.Empty<string>());
        specs[PlcSafetyValidationMethods.ResetSafetyFunctionTestResult] = Write(new[] { "testName", "functionName" }, Array.Empty<string>());
        specs[PlcSafetyValidationMethods.CreateSafetyFunctionCondition] = Write(new[] { "testName", "functionName", "signalDevice", "signalUsage", "signalName" }, new[] { "settings" });
        specs[PlcSafetyValidationMethods.UpdateSafetyFunctionCondition] = Write(new[] { "testName", "functionName" }, new[] { "position", "settings" });
        specs[PlcSafetyValidationMethods.DeleteSafetyFunctionCondition] = Write(new[] { "testName", "functionName" }, new[] { "position" });
        specs[PlcSafetyValidationMethods.ExportActivationTest] = Write(new[] { "testName", "filePath" }, new[] { "overwrite" });
        specs[PlcSafetyValidationMethods.GenerateActivationTestReport] = Write(new[] { "testName", "filePath" }, new[] { "overwrite" });
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, string?> Get)> MoreSettingsStringFields()
        => new (string, Func<PlcOperationRequest, string?>)[]
        {
            ("userName", op => op.UserName),
            ("roleName", op => op.RoleName),
            ("rightName", op => op.RightName),
            ("userKind", op => op.UserKind),
            ("aliasName", op => op.AliasName),
            ("group", op => op.Group),
            ("usage", op => op.Usage),
            ("certificateId", op => op.CertificateId),
            ("certificateRole", op => op.CertificateRole),
            ("serviceType", op => op.ServiceType),
            ("printer", op => op.Printer),
            ("documentLayout", op => op.DocumentLayout),
            ("printOption", op => op.PrintOption),
            ("evaluationDevice", op => op.EvaluationDevice),
            ("testName", op => op.TestName),
            ("functionName", op => op.FunctionName),
            ("signalDevice", op => op.SignalDevice),
            ("signalUsage", op => op.SignalUsage),
            ("signalName", op => op.SignalName),
        };

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> MoreSettingsOtherFields()
        => new (string, Func<PlcOperationRequest, bool>)[]
        {
            ("permissions", op => op.Permissions is not null),
            ("active", op => op.Active is not null),
            ("runtimeSessionTimeout", op => op.RuntimeSessionTimeout is not null),
            ("subjectAlternativeNames", op => op.SubjectAlternativeNames is not null),
            ("traced", op => op.Traced is not null),
        };

    private static void ValidateMoreSettings(PlcSettingsDefinition definition, PlcOperationRequest op, Action<string> error)
    {
        if (definition.Issue == 25)
            foreach (var entry in op.Settings?.Entries ?? Array.Empty<KeyValuePair<string, string>>())
                if (PlcSafetyValidationNames.ResultProperties.Contains(entry.Key, StringComparer.Ordinal))
                    error($"settings: '{entry.Key}' records a test result; this server never sets one (the safety acceptance is the qualified person's, in TIA Portal).");

        if (op.Permissions is { } permissions)
        {
            if (permissions.Count is < 1 or > PlcCpuUserNames.MaxPermissions) error($"permissions must hold 1 to {PlcCpuUserNames.MaxPermissions} member names.");
            if (permissions.Any(string.IsNullOrWhiteSpace)) error("permissions entries must be nonblank.");
            if (permissions.Where(p => p is not null).Distinct(StringComparer.Ordinal).Count() != permissions.Count(p => p is not null)) error("permissions must not repeat a member.");
        }

        switch (op.Operation)
        {
            case PlcCpuUserMethods.CreateWebserverUser when op.Permissions is null:
                error("create_webserver_user needs permissions (WebserverUserPermissions member names; None for no rights).");
                break;
            case PlcCpuUserMethods.UpdateWebserverUser when op.Permissions is null && op.PasswordVariable is null:
                error("update_webserver_user needs permissions and/or passwordVariable.");
                break;
            case PlcCpuUserMethods.UpdateSimpleWebserverUser when op.Settings is null && op.PasswordVariable is null:
                error("update_simple_webserver_user needs settings (Active, Permissions) and/or passwordVariable.");
                break;
            case PlcCertificateMethods.AssignCpuCertificate or PlcCertificateMethods.UnassignCpuCertificate
                when op.CertificateRole is not null && !PlcCertificateNames.Roles.Contains(op.CertificateRole, StringComparer.Ordinal):
                error($"certificateRole must be one of {string.Join(", ", PlcCertificateNames.Roles)}.");
                break;
            case PlcCertificateMethods.CreateCpuCertificate when op.SubjectAlternativeNames is { } sans
                && (sans.Count > PlcCertificateNames.MaxSubjectAlternativeNames || sans.Any(s => s is null || s.IndexOf(':') <= 0 || s.IndexOf(':') == s.Length - 1)):
                error($"subjectAlternativeNames takes at most {PlcCertificateNames.MaxSubjectAlternativeNames} entries, each Type:Value (e.g. IP:192.168.0.1).");
                break;
            case PlcCertificateMethods.AddCertificateSupportedService or PlcCertificateMethods.UpdateCertificateSupportedService or PlcCertificateMethods.RemoveCertificateSupportedService
                when op.CertificateId is not null && !uint.TryParse(op.CertificateId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _):
                error("certificateId of a certificate supported service must be an unsigned integer (UInt32).");
                break;
            case PlcSafetyPrintoutMethods.GenerateSafetyPrintout when op.Printer is not null && !PlcSafetyPrintoutNames.Printers.Contains(op.Printer, StringComparer.Ordinal):
                error($"printer must be one of {string.Join(", ", PlcSafetyPrintoutNames.Printers)}.");
                break;
            case PlcSafetyPrintoutMethods.GenerateSafetyPrintout when op.PrintOption is not null && !PlcSafetyPrintoutNames.PrintOptions.Contains(op.PrintOption, StringComparer.Ordinal):
                error($"printOption must be one of {string.Join(", ", PlcSafetyPrintoutNames.PrintOptions)}.");
                break;
            case PlcSafetyValidationMethods.CreateSafetyFunctionCondition when op.SignalUsage is not null && !PlcSafetyValidationNames.SignalUsages.Contains(op.SignalUsage, StringComparer.Ordinal):
                error($"signalUsage must be one of {string.Join(", ", PlcSafetyValidationNames.SignalUsages)}.");
                break;
            case PlcSafetyValidationMethods.UpdateSafetyFunctionCondition or PlcSafetyValidationMethods.DeleteSafetyFunctionCondition when op.Position is null:
                error($"{op.Operation} needs position (the 1-based condition of the safety function, as get_safety_validation lists them).");
                break;
            case PlcSafetyValidationMethods.CreateSafetyFunction or PlcSafetyValidationMethods.UpdateSafetyFunction when op.Settings is null && op.Traced is null && op.Operation == PlcSafetyValidationMethods.UpdateSafetyFunction:
                error("update_safety_function needs settings and/or traced.");
                break;
            case PlcSafetyValidationMethods.CheckActivationTest when op.Position is not null && op.FunctionName is null:
                error("check_activation_test takes position (a condition) only with functionName.");
                break;
            case PlcUmacMethods.SetAnonymousUser when op.Active is null:
                error("set_anonymous_user needs active (true: ActivateAnonymousUser, false: DeactivateAnonymousUser).");
                break;
            case PlcUmacMethods.AssignUserRole or PlcUmacMethods.UnassignUserRole when op.UserKind is not null && !PlcUmacNames.UserKinds.Contains(op.UserKind, StringComparer.Ordinal):
                error($"userKind must be one of {string.Join(", ", PlcUmacNames.UserKinds)}.");
                break;
            case PlcUmacMethods.UpdateUmcUser or PlcUmacMethods.DeleteUmcUser when op.UserKind is not null && !PlcUmacNames.UmcKinds.Contains(op.UserKind, StringComparer.Ordinal):
                error($"userKind must be one of {string.Join(", ", PlcUmacNames.UmcKinds)}.");
                break;
            case PlcUmacMethods.UpdateUmcUser when op.Active is null && op.NewName is null:
                error("update_umc_user needs active and/or newName.");
                break;
            case PlcUmacMethods.UpdateCustomRole or PlcUmacMethods.UpdateCustomDeviceFunctionRight when op.Settings is null && op.NewName is null:
                error($"{op.Operation} needs settings and/or newName.");
                break;
            case PlcUmacMethods.UpdateProjectUser when op.Settings is null && op.NewName is null && op.AliasName is null && op.Active is null
                && op.RuntimeSessionTimeout is null && op.PasswordVariable is null:
                error("update_project_user needs at least one of settings, newName, aliasName, active, runtimeSessionTimeout, passwordVariable.");
                break;
        }
    }
}
