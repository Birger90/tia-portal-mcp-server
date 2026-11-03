// Stand-ins for the Safety Validation Assistant (issue #25) with the member names of Siemens.Engineering.SafetyValidation.xml (V21).
// Objects that offer services carry them (IFakeServices). Member shapes and a simple state model only; no evidence of Siemens behavior.
using System.Collections;

namespace Siemens.Engineering.SafetyValidation
{
    public interface IFakeServices
    {
        IEnumerable<object> Services { get; }
    }

    public enum TestState { NotTested, Succeeded, Failed }

    public enum SignalUsage { InputCondition, OperatingMode, Response }

    public enum ConditionValue { NotRelevant, TRUE, FALSE }

    public enum TestValidationResultState { Okay, Error }

    public enum FakeExportOptions { None, WithDefaults, WithReadOnly }

    public sealed class TestValidationResult(TestValidationResultState state, int errors, IList<string> messages)
    {
        public TestValidationResultState State { get; } = state;

        public int ErrorCount { get; } = errors;

        public IList<string> Messages { get; } = messages;
    }

    public sealed class TestValidity(Func<TestValidationResult> check)
    {
        public TestValidationResult CheckValidity() => check();
    }

    public sealed class ActivationTestPrintout(ActivationTest test)
    {
        public void Generate(FileInfo file) => File.WriteAllText(file.FullName, $"%PDF-1.4 fake activation test report {test.Name}");
    }

    public sealed class DeviceQuery(IList<Siemens.Engineering.HW.FakeDeviceItem> devices)
    {
        public IList<Siemens.Engineering.HW.FakeDeviceItem> EvaluationDevices() => devices;
    }

    public sealed class TraceConfiguration
    {
        public bool IsTraced { get; set; }
    }

    public sealed class Condition(ConditionComposition owner, string device, SignalUsage usage, string signal)
    {
        public string DeviceName { get; } = device;

        public string SignalName { get; } = signal;

        public SignalUsage SignalUsage { get; } = usage;

        public ConditionValue InitialInput { get; set; } = ConditionValue.NotRelevant;

        public ConditionValue ExecutedInput { get; set; } = ConditionValue.NotRelevant;

        public ConditionValue Response { get; set; } = ConditionValue.NotRelevant;

        public string Comment { get; set; } = string.Empty;

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class ConditionComposition : IEnumerable<Condition>
    {
        public List<Condition> Items { get; } = new();

        public Condition Create(Siemens.Engineering.HW.FakeDeviceItem device, SignalUsage usage, string signal)
        {
            var condition = new Condition(this, device.Name, usage, signal);
            Items.Add(condition);
            return condition;
        }

        public IEnumerator<Condition> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SafetyFunction(SafetyFunctionComposition owner, string name) : IFakeServices
    {
        public string Name { get; set; } = name;

        public string TestName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        /// <summary>Read only here: a test result is recorded in TIA Portal by the person who executes the test.</summary>
        public TestState TestState { get; internal set; } = TestState.Succeeded;

        public TraceConfiguration TraceConfiguration { get; } = new();

        public ConditionComposition Conditions { get; } = new();

        public void ResetTestResult() => TestState = TestState.NotTested;

        public void Delete() => owner.Items.Remove(this);

        public IEnumerable<object> Services => new object[] { new TestValidity(() => new TestValidationResult(
            Conditions.Items.Count == 0 ? TestValidationResultState.Error : TestValidationResultState.Okay, Conditions.Items.Count == 0 ? 1 : 0,
            Conditions.Items.Count == 0 ? new List<string> { "The safety function has no condition." } : new List<string>())) };
    }

    public sealed class SafetyFunctionComposition : IEnumerable<SafetyFunction>
    {
        public List<SafetyFunction> Items { get; } = new();

        public SafetyFunction Create()
        {
            var function = new SafetyFunction(this, $"SF {Items.Count + 1}");
            Items.Add(function);
            return function;
        }

        public IEnumerator<SafetyFunction> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ActivationTest(ActivationTestComposition owner, string name, Siemens.Engineering.HW.FakeDeviceItem device, IList<Siemens.Engineering.HW.FakeDeviceItem> available) : IFakeServices
    {
        public string Name { get; } = name;

        public string Author { get; } = "FakeAuthor";

        public string EvaluationDeviceName { get; private set; } = device.Name;

        public string OverallState => SafetyFunctions.Items.Count == 0 ? "NotTested" : SafetyFunctions.Items.All(f => f.TestState == TestState.Succeeded) ? "Succeeded" : "NotTested";

        public SafetyFunctionComposition SafetyFunctions { get; } = new();

        public IList<Siemens.Engineering.HW.FakeDeviceItem> AvailableDevices() => available;

        public void ChangeEvaluationDevice(Siemens.Engineering.HW.FakeDeviceItem device) => EvaluationDeviceName = device.Name;

        public void Export(FileInfo file, FakeExportOptions options) => File.WriteAllText(file.FullName, $"<Document><ActivationTest Name=\"{Name}\" /></Document>");

        public void Delete() => owner.Items.Remove(this);

        public IEnumerable<object> Services => new object[] { new ActivationTestPrintout(this), new TestValidity(() => new TestValidationResult(TestValidationResultState.Okay, 0, new List<string>())) };
    }

    public sealed class ActivationTestComposition(IList<Siemens.Engineering.HW.FakeDeviceItem> available) : IEnumerable<ActivationTest>
    {
        public List<ActivationTest> Items { get; } = new();

        public ActivationTest Create(string name, Siemens.Engineering.HW.FakeDeviceItem device)
        {
            var test = new ActivationTest(this, name, device, available);
            Items.Add(test);
            return test;
        }

        public ActivationTest? Find(string name) => Items.FirstOrDefault(t => t.Name == name);

        public IEnumerator<ActivationTest> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ActivationTestUserGroup(ActivationTestUserGroupComposition owner, string name, IList<Siemens.Engineering.HW.FakeDeviceItem> available)
    {
        public string Name { get; } = name;

        public ActivationTestComposition ActivationTests { get; } = new(available);

        public ActivationTestUserGroupComposition Groups { get; } = new(available);

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class ActivationTestUserGroupComposition(IList<Siemens.Engineering.HW.FakeDeviceItem> available) : IEnumerable<ActivationTestUserGroup>
    {
        public List<ActivationTestUserGroup> Items { get; } = new();

        public ActivationTestUserGroup Create(string name)
        {
            var group = new ActivationTestUserGroup(this, name, available);
            Items.Add(group);
            return group;
        }

        public ActivationTestUserGroup? Find(string name) => Items.FirstOrDefault(g => g.Name == name);

        public IEnumerator<ActivationTestUserGroup> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SafetyValidationAssistant(IList<Siemens.Engineering.HW.FakeDeviceItem> evaluation, IList<Siemens.Engineering.HW.FakeDeviceItem> signals) : IFakeServices
    {
        public ActivationTestComposition ActivationTests { get; } = new(signals);

        public ActivationTestUserGroupComposition ActivationTestGroups { get; } = new(signals);

        public IEnumerable<object> Services => new object[] { new DeviceQuery(evaluation) };
    }
}
