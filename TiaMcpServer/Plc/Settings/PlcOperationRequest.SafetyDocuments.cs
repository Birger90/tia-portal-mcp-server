using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The safety document fields of the settings family (issues #21, #25); filePath, overwrite and plcName are shared.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("generate_safety_printout: the SafetyPrintoutFilePrinter driver that prints to the file: MicrosoftPrintToPdf (default) or MicrosoftXpsDocumentWriter (it must be installed on the TIA Portal computer).")]
    public string? Printer { get; set; }

    [Description("generate_safety_printout (required): the TIA Portal documentation layout of the printout, e.g. DocuInfo_ISO_A4_Portrait or DocuInfo_Simple_A4_Portrait_Small_Footer (the documentLayout parameter of SafetyPrintout.Print). The names are TIA Portal document information objects (project tree: Documentation settings > Document information, in the project or a global library such as the system library); Openness V21 lists none, so take the exact name from TIA Portal; a failed printout repeats this.")]
    public string? DocumentLayout { get; set; }

    [Description("generate_safety_printout: the SafetyPrintoutOption: All (default; all configuration data of the selected objects) or Compact (an abbreviated version).")]
    public string? PrintOption { get; set; }

    [Description("Safety Validation Assistant (issue #25): create_activation_test and change_activation_test_evaluation_device (required): the evaluation device, a device item name as get_safety_validation lists it under EvaluationDevices (DeviceQuery.EvaluationDevices()).")]
    public string? EvaluationDevice { get; set; }

    [Description("Safety Validation Assistant: the activation test (by name, in any group). check_activation_test and every safety function, condition, export and report operation: required.")]
    public string? TestName { get; set; }

    [Description("Safety Validation Assistant: the safety function of testName (by Name). update_safety_function, delete_safety_function, reset_safety_function_test_result and the condition operations: required; check_activation_test: optional (checks the function, with position a condition).")]
    public string? FunctionName { get; set; }

    [Description("create_safety_function_condition (required): the device item of the signal, by name from ActivationTest.AvailableDevices().")]
    public string? SignalDevice { get; set; }

    [Description("create_safety_function_condition (required): the SignalUsage: InputCondition, OperatingMode or Response.")]
    public string? SignalUsage { get; set; }

    [Description("create_safety_function_condition (required): the name of the signal.")]
    public string? SignalName { get; set; }

    [Description("create_safety_function, update_safety_function: TraceConfiguration.IsTraced (trace during test execution).")]
    public bool? Traced { get; set; }
}
