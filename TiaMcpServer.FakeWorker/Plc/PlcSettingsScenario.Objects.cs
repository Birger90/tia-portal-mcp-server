// Package G stand-ins with the member names of Siemens.Engineering.Base.xml / .Step7.xml (V21): LocalSession.MarkingService,
// MarkingService (GetMarkings, GetMarkStateInfo, IsMarkable, MarkObjects, UnmarkObjects), Markings (AllMarkings, ConflictedMarkings),
// Marking (MarkedObject, MarkState), MarkStateInfo (IsMarkable, MarkState), ObjectIdentifierProvider (GetIdentifier, Find),
// GetCompositionInfos/GetCreationInfos/GetComposition, EngineeringCreationInfo (Type, ParameterInfos), PlcTagProvider.GetLinkedTags,
// CamDataSupport (LoadCamData, LoadCamDataBinary, SaveCamData, SaveCamDataBinary, SaveCamDataPointList). Member shapes and a simple
// state model only; no evidence of Siemens behavior.
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.FakeWorker.Plc
{
    sealed partial class PlcSettingsScenario
    {
        /// <summary>Scenario plc-settings-standalone: the bound container is a standalone project (no local session, no markings).</summary>
        public bool Standalone { get; init; }

        private readonly Siemens.Engineering.Multiuser.LocalSessionStandIn _localSession = new();
        private readonly Dictionary<string, Siemens.Engineering.FakeObject> _objects = new(StringComparer.Ordinal)
        {
            ["block:PLC_1/Main"] = new("Main", "PlcBlock"),
            ["device:Station_1"] = new("Station_1", "Device"),
            ["technologyObject:PLC_1/Cam_1"] = new("Cam_1", "TechnologicalInstanceDB"),
            ["channel:Station_1/DI 16x24VDC/Digital/Input/0"] = new("Digital/Input/0", "Channel"),
        };

        partial void ConfigureObjects(PlcSettingsContext context)
        {
            if (!_services.ContainsKey(_objects["technologyObject:PLC_1/Cam_1"]))
            {
                _services[_objects["technologyObject:PLC_1/Cam_1"]] = new List<object> { new Siemens.Engineering.SW.TechnologicalObjects.Motion.CamDataSupport() };
                _services[_objects["channel:Station_1/DI 16x24VDC/Digital/Input/0"]] = new List<object> { new Siemens.Engineering.SW.PlcTagProvider() };
                _services[_project].Add(new Siemens.Engineering.ObjectIdentifierProvider(_objects));
            }

            context.EngineeringRoot = _project;
            // The fake project doubles as a Multiuser local session; scenario plc-settings-standalone binds a standalone project (K92).
            context.LocalSession = Standalone ? null : _localSession;
            context.ResolveObject = path => path == "project" ? _project
                : _objects.TryGetValue(path, out var found) ? found
                : throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"No object '{path}' in the fake project. Nothing was changed.");
            context.DescribeObject = value => value is Siemens.Engineering.FakeObject fake ? $"{fake.Kind} '{fake.Name}'" : value?.GetType().Name ?? "(null)";
        }
    }
}

namespace Siemens.Engineering
{
    public sealed class FakeObject(string name, string kind) : IEngineeringObject
    {
        public string Name { get; } = name;

        public string Kind { get; } = kind;

        public IList<EngineeringCompositionInfo> GetCompositionInfos() => new[] { new EngineeringCompositionInfo("Items") };

        public IList<EngineeringCreationInfo> GetCreationInfos(string compositionName)
            => compositionName == "Items" ? new[] { new EngineeringCreationInfo(typeof(FakeObject), new[] { new EngineeringCreationParameterInfo("Name", true, new[] { typeof(string) }) }) } : Array.Empty<EngineeringCreationInfo>();

        public object? GetComposition(string name) => name == "Items" ? new FakeComposition() : null;
    }

    public sealed class FakeComposition
    {
        public IList<EngineeringCreationInfo> GetCreationInfos() => new[] { new EngineeringCreationInfo(typeof(FakeObject), Array.Empty<EngineeringCreationParameterInfo>()) };
    }

    public sealed class EngineeringCompositionInfo(string name)
    {
        public string Name { get; } = name;

        public Type Type => typeof(FakeComposition);
    }

    public sealed class EngineeringCreationInfo(Type type, IList<EngineeringCreationParameterInfo> parameters)
    {
        public Type Type { get; } = type;

        public IList<EngineeringCreationParameterInfo> ParameterInfos { get; } = parameters;
    }

    public sealed class EngineeringCreationParameterInfo(string name, bool mandatory, IList<Type> types)
    {
        public string Name { get; } = name;

        public bool IsMandatory { get; } = mandatory;

        public IList<Type> SupportedTypes { get; } = types;
    }

    public sealed class ObjectIdentifierProvider(IReadOnlyDictionary<string, FakeObject> objects)
    {
        public string GetIdentifier(IEngineeringObject engineeringObject) => "id:" + ((FakeObject)engineeringObject).Name;

        public IEngineeringObject? Find(string identifier) => objects.Values.FirstOrDefault(o => "id:" + o.Name == identifier);
    }
}

namespace Siemens.Engineering.Multiuser
{
    public enum MarkState
    {
        None,
        IsMarkedByMe,
        IsMarkedByOthers,
        IsUptoDate,
    }

    public sealed class MarkStateInfo(bool markable, MarkState state)
    {
        public bool IsMarkable { get; } = markable;

        public MarkState MarkState { get; } = state;
    }

    public sealed class Marking(IEngineeringObject marked)
    {
        public IEngineeringObject MarkedObject { get; } = marked;

        public MarkState MarkState => MarkState.IsMarkedByMe;
    }

    public sealed class Markings(IList<Marking> all)
    {
        public IList<Marking> AllMarkings { get; } = all;

        public IList<Marking> ConflictedMarkings { get; } = new List<Marking>();
    }

    public sealed class MarkingService
    {
        private readonly List<IEngineeringObject> _marked = new();

        public Markings GetMarkings() => new(_marked.Select(o => new Marking(o)).ToList());

        public MarkStateInfo GetMarkStateInfo(IEngineeringObject engineeringObject)
            => new(IsMarkable(engineeringObject), _marked.Contains(engineeringObject) ? MarkState.IsMarkedByMe : MarkState.IsUptoDate);

        // The fake refuses to mark a device (TIA Portal decides which objects are markable).
        public bool IsMarkable(IEngineeringObject engineeringObject) => ((FakeObject)engineeringObject).Kind != "Device";

        public void MarkObjects(IEnumerable<IEngineeringObject> engineeringObject)
        {
            foreach (var item in engineeringObject)
            {
                if (!IsMarkable(item)) throw new InvalidOperationException("The object is not markable.");
                if (!_marked.Contains(item)) _marked.Add(item);
            }
        }

        public void UnmarkObjects(IEnumerable<IEngineeringObject> engineeringObject)
        {
            foreach (var item in engineeringObject) _marked.Remove(item);
        }
    }

    public sealed class LocalSessionStandIn
    {
        public MarkingService MarkingService { get; } = new();
    }
}

namespace Siemens.Engineering.SW
{
    public sealed class FakePlcTag(string name, string address)
    {
        public string Name { get; } = name;

        public string LogicalAddress { get; } = address;

        public string DataTypeName => "Bool";
    }

    public sealed class PlcTagProvider
    {
        public IList<FakePlcTag> GetLinkedTags() => new[] { new FakePlcTag("Start", "%I0.0") };
    }
}

namespace Siemens.Engineering.SW.TechnologicalObjects.Motion
{
    public enum CamDataFormat
    {
        MCD,
        PointList,
        Scout,
    }

    public enum CamDataFormatSeparator
    {
        Comma,
        Tab,
    }

    public sealed class CamDataSupport
    {
        public string Loaded { get; private set; } = string.Empty;

        public void LoadCamData(FileInfo sourceFile, CamDataFormatSeparator separator) => Loaded = File.ReadAllText(sourceFile.FullName) + "|" + separator;

        public void LoadCamDataBinary(FileInfo sourceFile) => Loaded = Convert.ToBase64String(File.ReadAllBytes(sourceFile.FullName));

        public void SaveCamData(FileInfo destinationFile, CamDataFormat format, CamDataFormatSeparator separator)
            => File.WriteAllText(destinationFile.FullName, $"{format}{(separator == CamDataFormatSeparator.Comma ? "," : "\t")}0,0");

        public void SaveCamDataBinary(FileInfo destinationFile) => File.WriteAllBytes(destinationFile.FullName, new byte[] { 1, 2, 3 });

        public void SaveCamDataPointList(FileInfo destinationFile, CamDataFormatSeparator separator, int samplePoints)
            => File.WriteAllText(destinationFile.FullName, string.Join("\n", Enumerable.Range(0, samplePoints).Select(i => $"{i},{i}")));
    }
}
