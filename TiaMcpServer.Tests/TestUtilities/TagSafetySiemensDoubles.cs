// Offline boundary doubles for source-linked readers and the project snapshot walker.
// These model object access only; no Openness assemblies or processes are loaded.
using System.Collections;

namespace Siemens.Engineering
{
    public abstract class NamedObject : IEngineeringServiceProvider, IEngineeringObject
    {
        private IEngineeringObject? parent;
        public Exception? ParentFailure { get; set; }
        public IEngineeringObject? Parent
        {
            get => ParentFailure is null ? parent : throw ParentFailure;
            set => parent = value;
        }
        public virtual object GetAttribute(string attributeName) => attributeName == "Name"
            ? Name
            : throw new EngineeringException($"Attribute '{attributeName}' is not modeled.");
        public virtual void SetAttribute(string attributeName, object value)
            => throw new EngineeringException($"Attribute '{attributeName}' is not modeled.");
        public virtual IList<EngineeringAttributeInfo> GetAttributeInfos() => new List<EngineeringAttributeInfo>();
        private string name = string.Empty;
        public Exception? NameFailure { get; set; }
        /// <summary>Models Portal silently keeping the old name (a colliding rename does not throw).</summary>
        public bool IgnoreNameWrites { get; set; }
        /// <summary>Thrown by the Name setter (models a native rename rejection).</summary>
        public Exception? NameWriteFailure { get; set; }
        public string Name
        {
            get => NameFailure is null ? name : throw NameFailure;
            set
            {
                if (NameWriteFailure is not null) throw NameWriteFailure;
                if (!IgnoreNameWrites) name = value;
            }
        }
        public CrossReference.CrossReferenceService? CrossReferenceService { get; set; }
        public Exception? CrossReferenceServiceFailure { get; set; }
        public int CrossReferenceServiceRequests { get; private set; }
        public virtual T? GetService<T>() where T : class
        {
            if (typeof(T) != typeof(CrossReference.CrossReferenceService)) return null;
            CrossReferenceServiceRequests++;
            if (CrossReferenceServiceFailure is not null) throw CrossReferenceServiceFailure;
            return CrossReferenceService as T;
        }
    }

    public class Composition<T> : IEnumerable<T> where T : NamedObject
    {
        public IEngineeringObject? Parent { get; set; }
        public List<T> Items { get; } = new();
        public Exception? EnumerationFailure { get; set; }
        public int YieldedItemCount { get; private set; }
        public int Count => Items.Count;
        public T? Find(string name) => this.FirstOrDefault(x =>
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in Items)
            {
                if (Parent is not null && item.Parent is null) item.Parent = Parent;
                YieldedItemCount++;
                yield return item;
            }
            if (EnumerationFailure is not null)
                throw EnumerationFailure;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public abstract partial class ProjectBase
    {
        public Composition<HW.Device> Devices { get; } = new();
        public Composition<HW.DeviceUserGroup> DeviceGroups { get; } = new();
    }

    public sealed partial class Project : ProjectBase
    {
        private HW.DeviceSystemGroup ungroupedDevicesGroup = new();
        public Exception? UngroupedDevicesGroupFailure { get; set; }
        public HW.DeviceSystemGroup UngroupedDevicesGroup
        {
            get => UngroupedDevicesGroupFailure is null ? ungroupedDevicesGroup : throw UngroupedDevicesGroupFailure;
            set => ungroupedDevicesGroup = value;
        }
    }

    public enum ExportOptions { None }
    public enum ImportOptions { None = 0, Override = 1 }
    public enum DocumentInfoOptions { None }
    public class EngineeringException : Exception
    {
        public EngineeringException() { }
        public EngineeringException(string message) : base(message) { }
        public EngineeringException(string message, Exception inner) : base(message, inner) { }
    }

    // Match the Openness hierarchy: Portal loss is not an ordinary EngineeringException.
    public sealed class NonRecoverableException : Exception
    {
        public NonRecoverableException(string message) : base(message) { }
    }

    // Openness throws this (not System.NotSupportedException) for an attribute the object does not have.
    public sealed class EngineeringNotSupportedException : EngineeringException
    {
        public EngineeringNotSupportedException(string message) : base(message) { }
    }

    public interface IEngineeringServiceProvider
    {
        T? GetService<T>() where T : class;
    }

    public interface IEngineeringObject
    {
        IEngineeringObject? Parent { get; }
        object GetAttribute(string name);
        void SetAttribute(string name, object value);
        // Default member so doubles that do not model attribute metadata need not implement it.
        IList<EngineeringAttributeInfo> GetAttributeInfos() => new List<EngineeringAttributeInfo>();
    }
}

namespace Siemens.Engineering.CrossReference
{
    public enum CrossReferenceFilter { AllObjects, ObjectsWithReferences, ObjectsWithoutReferences, UnusedObjects }
    public sealed class CrossReferenceService
    {
        public CrossReferenceResult Result { get; set; } = new();
        public Exception? Failure { get; set; }
        public List<CrossReferenceFilter> Queries { get; } = new();
        public CrossReferenceResult GetCrossReferences(CrossReferenceFilter filter)
        {
            Queries.Add(filter);
            if (Failure is not null) throw Failure;
            return Result;
        }
    }
    public sealed class CrossReferenceResult
    {
        public Composition<SourceObject> Sources { get; } = new();
    }
    public class CrossReferenceObject : NamedObject
    {
        private string typeName = string.Empty;
        public Exception? TypeNameFailure { get; set; }
        public string TypeName
        {
            get => TypeNameFailure is null ? typeName : throw TypeNameFailure;
            set => typeName = value;
        }
        public string Path { get; set; } = string.Empty;
        public string Device { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
    }
    public sealed class SourceObject : CrossReferenceObject
    {
        public Composition<ReferenceObject> References { get; } = new();
        public Composition<SourceObject> Children { get; } = new();
    }
    public sealed class ReferenceObject : CrossReferenceObject
    {
        public Composition<Location> Locations { get; } = new();
    }
    public sealed class Location : CrossReferenceObject
    {
        public Access Access { get; set; }
        public ReferenceType ReferenceType { get; set; }
        public string ReferenceLocation { get; set; } = string.Empty;
        public IEngineeringObject? ReferencedAs { get; set; }
        public string ReferencedAsName { get; set; } = string.Empty;
    }

    // Members and values match the V21 metadata and the reference stub.
    public enum Access
    {
        Undefined, Read, Write, RW, Unknown, Definition, Declaration, Interface, Jump, Monitor, Modify, Force,
        Call, UC, CC, Multiinstance, InstanceDB, Open, Interlock, Supervision, Actions, Transition, ReadAndSymbol,
        WriteAndSymbol, ReadWriteAndSymbol, InstanceAndSymbol, MultiinstanceAndSymbol, ProDiagSupervision,
        DefaultValue, ArrayBoundary, StringLength, TypeAlarm, InstanceAlarm, Parameterinstance,
        ParameterinstanceAndSymbol, CreateReference, CreateReferenceAndSymbol
    }

    public enum ReferenceType
    {
        Uses, UsedBy, Undefined, TypeInstance, InstanceType, Assigns, MemberGroup, GroupMember, Defines,
        DefinedBy, OverlapsWith, Scope, Unknown
    }
}

namespace Siemens.Engineering.Compiler
{
    public interface ICompilable
    {
        CompilerResult Compile();
    }

    public enum CompilerResultState { Success, Warning, Error }

    public sealed class CompilerResult
    {
        private readonly List<CompilerResultMessage> messages = new();
        public CompilerResultState State { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public Exception? MessagesFailure { get; set; }
        public List<CompilerResultMessage> Messages => MessagesFailure is null ? messages : throw MessagesFailure;
    }

    public class CompilerResultMessage
    {
        public string Description { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public CompilerResultState State { get; set; }
        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }
        public List<CompilerResultMessage> Messages { get; } = new();
    }
}

namespace Siemens.Engineering.HW
{
    public sealed class Device : HardwareObject
    {
        public Exception? TypeIdentifierFailure { get; set; }
        private string? typeIdentifier;
        public string? TypeIdentifier
        {
            get => TypeIdentifierFailure is null ? typeIdentifier : throw TypeIdentifierFailure;
            set => typeIdentifier = value;
        }
    }
    public sealed class DeviceUserGroup : NamedObject
    {
        public Composition<Device> Devices { get; } = new();
        public Composition<DeviceUserGroup> Groups { get; } = new();
    }
    public sealed class DeviceSystemGroup : NamedObject
    {
        private Composition<Device> devices = new();
        public Exception? DevicesFailure { get; set; }
        public Composition<Device> Devices
        {
            get => DevicesFailure is null ? devices : throw DevicesFailure;
            set => devices = value;
        }
    }
    public sealed class DeviceItemComposition : Composition<DeviceItem> { }
    public sealed partial class DeviceItem : HardwareObject
    {
        public int PositionNumber { get; set; }
        public Features.SoftwareContainer? Container { get; set; }
        public Exception? ServiceFailure { get; set; }
        public Exception? TypeIdentifierFailure { get; set; }
        private string? typeIdentifier;
        public string? TypeIdentifier
        {
            get => TypeIdentifierFailure is null ? typeIdentifier : throw TypeIdentifierFailure;
            set => typeIdentifier = value;
        }
        /// <summary>The CPU device item's hardware compiler (compile_check scope hardware); null offers none.</summary>
        public Compiler.ICompilable? HardwareCompiler { get; set; }
        public override T? GetService<T>() where T : class
        {
            if (ServiceFailure is not null)
                throw ServiceFailure;
            return Container as T ?? HardwareCompiler as T;
        }
    }
}

namespace Siemens.Engineering.HW.Features
{
    public sealed class SoftwareContainer
    {
        public object? Software { get; set; }
    }
}

namespace Siemens.Engineering.SW
{
    public sealed partial class PlcSoftware : NamedObject, IEngineeringServiceProvider
    {
        public PlcSoftware()
        {
            blockGroup.Parent = this;
            tagTableGroup.Parent = this;
            TypeGroup.Parent = this;
            TechnologicalObjectGroup.Parent = this;
        }
        private readonly Blocks.PlcBlockSystemGroup blockGroup = new();
        private readonly Tags.PlcTagTableGroup tagTableGroup = new();
        public Exception? TagTableGroupFailure { get; set; }
        public Tags.PlcTagTableGroup TagTableGroup => TagTableGroupFailure is null ? tagTableGroup : throw TagTableGroupFailure;
        public Exception? BlockGroupFailure { get; set; }
        public Blocks.PlcBlockSystemGroup BlockGroup => BlockGroupFailure is null ? blockGroup : throw BlockGroupFailure;
        public Types.PlcTypeGroup TypeGroup { get; } = new();
        public Units.PlcUnitProvider? UnitProvider { get; set; }
        public ExternalSources.PlcExternalSourceSystemGroup ExternalSourceGroup { get; } = new();
        public Compiler.ICompilable? CompilerService { get; set; }
        public override T? GetService<T>() where T : class => UnitProvider as T ?? CompilerService as T ?? base.GetService<T>();
    }
}

namespace Siemens.Engineering.SW.Tags
{
    public sealed class PlcTagTableGroup : NamedObject
    {
        public PlcTagTableGroup() { TagTables.Parent = this; Groups.Parent = this; }
        public PlcTagTableComposition TagTables { get; } = new();
        public Composition<PlcTagTableGroup> Groups { get; } = new();
    }
    public sealed class PlcTagTableComposition : Composition<PlcTagTable>
    {
        public PlcTagTable Create(string name)
        {
            var table = new PlcTagTable { Name = name, Owner = this };
            Items.Add(table);
            return table;
        }

        /// <summary>Failures of the next Import calls, in order (null entries succeed).</summary>
        public Queue<Exception?> ImportFailures { get; } = new();
        public List<string> ImportedDocuments { get; } = new();
        /// <summary>Re-creates the table <see cref="PlcTagTable.Export(FileInfo, ExportOptions)"/> wrote (name and tag/constant names).</summary>
        public void Import(FileInfo path, ImportOptions importOptions)
        {
            var text = File.ReadAllText(path.FullName);
            ImportedDocuments.Add(text);
            if (ImportFailures.Count > 0 && ImportFailures.Dequeue() is { } failure) throw failure;
            var lines = text.Split('\n');
            var table = Create(lines[0]);
            foreach (var tag in lines.Skip(1).Where(l => l.StartsWith("tag:", StringComparison.Ordinal))) table.Tags.Create(tag.Substring(4), "Bool", "");
            foreach (var constant in lines.Skip(1).Where(l => l.StartsWith("const:", StringComparison.Ordinal))) table.UserConstants.Create(constant.Substring(6));
        }
    }
    public sealed class PlcTagComposition : Composition<PlcTag>
    {
        /// <summary>Cultures a new tag's comment has text items for (Portal creates one per project language).</summary>
        public List<string> CommentCultures { get; } = new();
        public Exception? CreateFailure { get; set; }
        /// <summary>Configures each created tag (failure hooks on tags the code under test creates).</summary>
        public Action<PlcTag>? Created { get; set; }
        public PlcTag Create(string name, string dataTypeName, string logicalAddress)
        {
            if (CreateFailure is not null) throw CreateFailure;
            var tag = new PlcTag { Name = name, DataTypeName = dataTypeName, LogicalAddress = logicalAddress, Owner = this };
            foreach (var culture in CommentCultures) tag.CommentText.With(culture, string.Empty);
            Items.Add(tag);
            Created?.Invoke(tag);
            return tag;
        }
    }
    public sealed class PlcUserConstantComposition : Composition<PlcUserConstant>
    {
        public PlcUserConstant Create(string name)
        {
            var constant = new PlcUserConstant { Name = name, Owner = this };
            Items.Add(constant);
            return constant;
        }
    }
    public sealed class PlcTagTable : NamedObject
    {
        public PlcTagComposition Tags { get; } = new();
        public PlcUserConstantComposition UserConstants { get; } = new();
        internal PlcTagTableComposition? Owner { get; set; }
        public void Delete() => Owner?.Items.Remove(this);
        public Composition<PlcSystemConstant> SystemConstants { get; } = new();
        public bool IsDefault { get; set; }
        public void Export(FileInfo path, ExportOptions options, DocumentInfoOptions documentInfo)
            => throw new NotSupportedException("Export is outside this offline collision fixture.");
        /// <summary>Writes a stand-in document: the name, then one line per tag and user constant.</summary>
        public void Export(FileInfo path, ExportOptions options)
            => File.WriteAllText(path.FullName, string.Join("\n", new[] { Name }
                .Concat(Tags.Items.Select(t => "tag:" + t.Name)).Concat(UserConstants.Items.Select(c => "const:" + c.Name))));
    }
    public sealed class PlcTag : NamedObject
    {
        /// <summary>The documented IsSafety attribute; null models a CPU that does not expose it (GetAttribute throws).</summary>
        public bool? IsSafety { get; set; }
        public override object GetAttribute(string attributeName) => attributeName == "IsSafety"
            ? IsSafety ?? throw new EngineeringNotSupportedException("IsSafety is not available on this tag.")
            : base.GetAttribute(attributeName);
        internal PlcTagComposition? Owner { get; set; }
        public Exception? DeleteFailure { get; set; }
        public void Delete()
        {
            if (DeleteFailure is not null) throw DeleteFailure;
            Owner?.Items.Remove(this);
        }
        /// <summary>The comment's backing text (also reachable through <see cref="Comment"/>).</summary>
        public MultilingualText CommentText { get; } = new();
        public Exception? CommentFailure { get; set; }
        public MultilingualText Comment => CommentFailure is null ? CommentText : throw CommentFailure;
        public string DataTypeName { get; set; } = "Bool";
        public string LogicalAddress { get; set; } = "%I0.0";
        private bool externalAccessible, externalVisible, externalWritable;
        public Exception? ExternalAccessibleFailure { get; set; }
        public Exception? ExternalVisibleFailure { get; set; }
        public Exception? ExternalWritableFailure { get; set; }
        public bool ExternalAccessible
        {
            get => ExternalAccessibleFailure is null ? externalAccessible : throw ExternalAccessibleFailure;
            set => externalAccessible = value;
        }
        public bool ExternalVisible
        {
            get => ExternalVisibleFailure is null ? externalVisible : throw ExternalVisibleFailure;
            set => externalVisible = value;
        }
        public bool ExternalWritable
        {
            get => ExternalWritableFailure is null ? externalWritable : throw ExternalWritableFailure;
            set => externalWritable = value;
        }
    }
    public sealed class PlcSystemConstant : NamedObject { }
    public sealed class PlcUserConstant : NamedObject
    {
        internal PlcUserConstantComposition? Owner { get; set; }
        public void Delete() => Owner?.Items.Remove(this);
        public string DataTypeName { get; set; } = "Int";
        private object value = "25";
        public Exception? ValueFailure { get; set; }
        public object Value
        {
            get => ValueFailure is null ? value : throw ValueFailure;
            set => this.value = value;
        }
    }
}

namespace Siemens.Engineering.SW.Blocks
{
    public class PlcBlock : NamedObject, IEngineeringServiceProvider
    {
        public Compiler.ICompilable? CompilerService { get; set; }
        public override T? GetService<T>() where T : class => CompilerService as T ?? base.GetService<T>();
        public int Number { get; set; }
        public bool AutoNumber { get; set; }
        public bool Deleted { get; private set; }
        /// <summary>Set by <see cref="PlcBlockComposition.Add"/>; Delete then also removes the block from it, as Portal does.</summary>
        public PlcBlockComposition? Owner { get; set; }
        public void Delete()
        {
            Deleted = true;
            Owner?.Items.Remove(this);
        }
        public bool IsKnowHowProtected { get; set; }
        /// <summary>The SimaticML Export writes; null fails the export.</summary>
        public string? ExportDocument { get; set; }
        public void Export(FileInfo path, ExportOptions options)
            => File.WriteAllText(path.FullName, ExportDocument ?? throw new EngineeringException($"Export of '{Name}' is not modeled."));
        public string? InstanceOfName { get; set; }
        /// <summary>Models Portal silently keeping the old number.</summary>
        public bool IgnoreNumberWrites { get; set; }
        public override object GetAttribute(string attributeName) => attributeName switch
        {
            "AutoNumber" => AutoNumber,
            "Number" => Number,
            "InstanceOfName" => InstanceOfName ?? throw new EngineeringException("InstanceOfName is not modeled."),
            _ => base.GetAttribute(attributeName),
        };
        public override void SetAttribute(string attributeName, object value)
        {
            switch (attributeName)
            {
                case "AutoNumber": AutoNumber = (bool)value; break;
                case "Number": if (!IgnoreNumberWrites) Number = (int)value; break;
                case "Name": Name = (string)value; break;
                default: base.SetAttribute(attributeName, value); break;
            }
        }
        public string ProgrammingLanguage { get; set; } = "SCL";
        public string? HeaderAuthor { get; set; }
        public string? HeaderFamily { get; set; }
        public string? HeaderName { get; set; }
        public System.Version? HeaderVersion { get; set; }
    }
    public sealed class OB : PlcBlock { }
    public sealed class FB : PlcBlock { }
    public sealed class FC : PlcBlock { }
    public class DataBlock : PlcBlock
    {
        public Interface.PlcBlockInterface Interface { get; } = new();
    }
    public sealed class GlobalDB : DataBlock { }
    public class InstanceDB : DataBlock { }
    public sealed class ArrayDB : DataBlock { }
    public sealed class PlcBlockComposition : Composition<PlcBlock>
    {
        public List<ImportOptions> ImportCalls { get; } = new();
        public List<string> ImportedDocuments { get; } = new();
        // Blocks the next import yields; they are also added to this composition, as TIA would.
        public List<PlcBlock> ImportResult { get; } = new();
        /// <summary>Failures of the next Import calls, in order (null entries succeed).</summary>
        public Queue<Exception?> ImportFailures { get; } = new();
        public IList<PlcBlock> Import(FileInfo path, ImportOptions importOptions)
        {
            ImportCalls.Add(importOptions);
            if (path.Exists)
                ImportedDocuments.Add(File.ReadAllText(path.FullName));
            if (ImportFailures.Count > 0 && ImportFailures.Dequeue() is { } failure) throw failure;
            foreach (var block in ImportResult) Add(block);
            return ImportResult.ToList();
        }
        public T Add<T>(T block) where T : PlcBlock
        {
            block.Owner = this;
            Items.Add(block);
            return block;
        }
        public InstanceDB CreateInstanceDB(string name, bool isAutoNumbered, int number, string instanceOfName)
            => Add(new InstanceDB { Name = name, AutoNumber = isAutoNumbered, Number = number, InstanceOfName = instanceOfName });
    }
    public sealed class PlcBlockUserGroupComposition : Composition<PlcBlockGroup>
    {
        public PlcBlockUserGroup Create(string name)
        {
            var group = new PlcBlockUserGroup { Name = name };
            Items.Add(group);
            return group;
        }
    }
    public class PlcBlockGroup : NamedObject
    {
        public PlcBlockGroup() { Blocks.Parent = this; Groups.Parent = this; }
        public PlcBlockComposition Blocks { get; } = new();
        public PlcBlockUserGroupComposition Groups { get; } = new();
    }
    public sealed class PlcBlockSystemGroup : PlcBlockGroup
    {
        public Composition<PlcSystemBlockGroup> SystemBlockGroups { get; } = new();
    }
    public sealed class PlcBlockUserGroup : PlcBlockGroup
    {
        public bool Deleted { get; private set; }
        public void Delete() => Deleted = true;
    }
    public sealed class PlcSystemBlockGroup : NamedObject
    {
        public PlcSystemBlockGroup() { Blocks.Parent = this; Groups.Parent = this; }
        public Composition<PlcBlock> Blocks { get; } = new();
        public Composition<PlcSystemBlockGroup> Groups { get; } = new();
    }
}

namespace Siemens.Engineering.SW.Blocks.Interface
{
    public sealed class PlcBlockInterface
    {
        public MemberComposition Members { get; } = new();
    }
    /// <summary>Members by their full Find path (e.g. Motor.Speed or Axis[2].Position).</summary>
    public sealed class MemberComposition : IEnumerable<Member>
    {
        public Dictionary<string, Member> ByPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> FindCalls { get; } = new();
        public Member? Find(string path)
        {
            FindCalls.Add(path);
            return ByPath.TryGetValue(path, out var member) ? member : null;
        }
        public IEnumerator<Member> GetEnumerator() => ByPath.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public sealed class Member : NamedObject
    {
        public Dictionary<string, object> Attributes { get; } = new(StringComparer.Ordinal);
        /// <summary>Attributes whose writes Portal silently ignores.</summary>
        public HashSet<string> IgnoredWrites { get; } = new(StringComparer.Ordinal);
        /// <summary>Stored spellings: a written value is kept as this text (models Portal normalizing a literal).</summary>
        public Dictionary<string, string> StoredAs { get; } = new(StringComparer.Ordinal);
        public override object GetAttribute(string attributeName)
            => Attributes.TryGetValue(attributeName, out var value) ? value : base.GetAttribute(attributeName);
        public override void SetAttribute(string attributeName, object value)
        {
            if (IgnoredWrites.Contains(attributeName)) return;
            Attributes[attributeName] = value is string text && StoredAs.TryGetValue(text, out var stored) ? stored : value;
        }
    }
}

namespace Siemens.Engineering.SW.Types
{
    public sealed class PlcType : NamedObject
    {
        public void Export(FileInfo path, ExportOptions options)
            => throw new EngineeringException($"Export of '{Name}' is not modeled.");
    }
    public sealed class PlcTypeGroup : NamedObject
    {
        public PlcTypeGroup() { Types.Parent = this; Groups.Parent = this; }
        public Composition<PlcType> Types { get; } = new();
        public Composition<PlcTypeGroup> Groups { get; } = new();
    }
}

namespace Siemens.Engineering.SW.ExternalSources
{
    public sealed class PlcExternalSourceSystemGroup { }
}

namespace Siemens.Engineering.SW.Units
{
    public sealed class PlcUnitProvider
    {
        public PlcUnitGroup UnitGroup { get; } = new();
    }
    public sealed class PlcUnitGroup
    {
        public Composition<PlcUnit> Units { get; } = new();
    }
    public sealed class PlcUnit : NamedObject
    {
        public Blocks.PlcBlockSystemGroup BlockGroup { get; } = new();
        public Tags.PlcTagTableGroup TagTableGroup { get; } = new();
        public Types.PlcTypeGroup TypeGroup { get; } = new();
        public ExternalSources.PlcExternalSourceSystemGroup ExternalSourceGroup { get; } = new();
    }
}
