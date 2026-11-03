namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Issue #25, the Safety Validation Assistant. Evidence: Siemens.Engineering.SafetyValidation.xml (V21):
/// <c>SafetyValidationAssistant</c> (<c>ActivationTests</c>, <c>ActivationTestGroups</c>), <c>ActivationTestComposition.Create(String, DeviceItem)</c>,
/// <c>.Find(String)</c>, <c>ActivationTest</c> (<c>Name</c>, <c>Author</c>, <c>EvaluationDeviceName</c>, <c>OverallState</c>, <c>SafetyFunctions</c>,
/// <c>AvailableDevices()</c>, <c>ChangeEvaluationDevice(DeviceItem)</c>, <c>Export(FileInfo, ExportOptions)</c>, <c>Delete()</c>),
/// <c>ActivationTestUserGroupComposition.Create(String)</c>/<c>.Find(String)</c>, <c>ActivationTestUserGroup</c> (<c>Name</c>, <c>ActivationTests</c>,
/// <c>Groups</c>, <c>Delete()</c>), <c>SafetyFunctionComposition.Create()</c>, <c>SafetyFunction</c> (<c>Name</c>, <c>TestName</c>, <c>Description</c>,
/// <c>TestState</c>, <c>TraceConfiguration</c>, <c>Conditions</c>, <c>ResetTestResult()</c>, <c>Delete()</c>), <c>TraceConfiguration.IsTraced</c>,
/// <c>ConditionComposition.Create(DeviceItem, SignalUsage, String)</c>, <c>Condition</c> (<c>DeviceName</c>, <c>SignalName</c>, <c>SignalUsage</c>,
/// <c>InitialInput</c>, <c>ExecutedInput</c>, <c>Response</c>, <c>Comment</c>, <c>Delete()</c>), <c>TestValidity.CheckValidity()</c> ->
/// <c>TestValidationResult</c> (<c>State</c>, <c>ErrorCount</c>, <c>Messages</c>), <c>ActivationTestPrintout.Generate(FileInfo)</c> ("If the file
/// already exists, it is overridden"), <c>DeviceQuery.EvaluationDevices()</c>.
///
/// The MCP never replaces the safety acceptance: it reads, prepares and generates printouts and reports. Nothing here sets a test
/// state or result (<c>TestState</c> and <c>OverallState</c> are never written), and the imports and copies that could carry
/// executed test results (<c>ActivationTestComposition.Import</c>, <c>CreateFrom</c>, <c>SafetyFunctionComposition.Import</c>,
/// <c>CreateFrom</c>) are not built.
/// </summary>
public static class PlcSafetyValidationMethods
{
    public const string GetSafetyValidation = "get_safety_validation";
    public const string CheckActivationTest = "check_activation_test";
    public const string CreateActivationTest = "create_activation_test";
    public const string DeleteActivationTest = "delete_activation_test";
    public const string ChangeActivationTestEvaluationDevice = "change_activation_test_evaluation_device";
    public const string CreateActivationTestGroup = "create_activation_test_group";
    public const string DeleteActivationTestGroup = "delete_activation_test_group";
    public const string CreateSafetyFunction = "create_safety_function";
    public const string UpdateSafetyFunction = "update_safety_function";
    public const string DeleteSafetyFunction = "delete_safety_function";
    public const string ResetSafetyFunctionTestResult = "reset_safety_function_test_result";
    public const string CreateSafetyFunctionCondition = "create_safety_function_condition";
    public const string UpdateSafetyFunctionCondition = "update_safety_function_condition";
    public const string DeleteSafetyFunctionCondition = "delete_safety_function_condition";
    public const string ExportActivationTest = "export_activation_test";
    public const string GenerateActivationTestReport = "generate_activation_test_report";
}

public static class PlcSafetyValidationNames
{
    public const string Assistant = "Siemens.Engineering.SafetyValidation.SafetyValidationAssistant";
    public const string DeviceQuery = "Siemens.Engineering.SafetyValidation.DeviceQuery";
    public const string TestValidity = "Siemens.Engineering.SafetyValidation.TestValidity";
    public const string TestPrintout = "Siemens.Engineering.SafetyValidation.ActivationTestPrintout";

    /// <summary>The safety function properties a write may set. TestState is deliberately absent: the MCP never records a test result.</summary>
    public static readonly string[] FunctionProperties = { "Name", "TestName", "Description" };

    /// <summary>The condition properties a write may set (expected values and the comment; no result).</summary>
    public static readonly string[] ConditionProperties = { "InitialInput", "ExecutedInput", "Response", "Comment" };

    /// <summary>Names that would record or fake a test result; refused in settings with a clear message.</summary>
    public static readonly string[] ResultProperties = { "TestState", "OverallState", "State" };

    public static readonly string[] SignalUsages = { "InputCondition", "OperatingMode", "Response" };
}

public static partial class PlcSettingsCatalog
{
    private static void AddSafetyValidation()
    {
        const int Issue = 25;
        Add(new(PlcSafetyValidationMethods.GetSafetyValidation, false, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.CheckActivationTest, false, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.CreateActivationTest, true, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.DeleteActivationTest, true, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.ChangeActivationTestEvaluationDevice, true, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.CreateActivationTestGroup, true, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.DeleteActivationTestGroup, true, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.CreateSafetyFunction, true, Issue, true, PlcSafetyValidationNames.Assistant, PlcSafetyValidationNames.FunctionProperties));
        Add(new(PlcSafetyValidationMethods.UpdateSafetyFunction, true, Issue, true, PlcSafetyValidationNames.Assistant, PlcSafetyValidationNames.FunctionProperties));
        Add(new(PlcSafetyValidationMethods.DeleteSafetyFunction, true, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.ResetSafetyFunctionTestResult, true, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.CreateSafetyFunctionCondition, true, Issue, true, PlcSafetyValidationNames.Assistant, PlcSafetyValidationNames.ConditionProperties));
        Add(new(PlcSafetyValidationMethods.UpdateSafetyFunctionCondition, true, Issue, true, PlcSafetyValidationNames.Assistant, PlcSafetyValidationNames.ConditionProperties, requiresSettings: true));
        Add(new(PlcSafetyValidationMethods.DeleteSafetyFunctionCondition, true, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.ExportActivationTest, true, Issue, true, PlcSafetyValidationNames.Assistant));
        Add(new(PlcSafetyValidationMethods.GenerateActivationTestReport, true, Issue, true, PlcSafetyValidationNames.Assistant));
    }
}
