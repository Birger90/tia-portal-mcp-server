using System.Collections;
using System.Xml.Linq;

// Classic WinCC doubles (issue #28): the worker binds every classic member at runtime by name, so these model only the
// documented V21 shape the worker reaches (system folders, Folders/Create, compositions with Import, objects with
// Export/Delete, HmiTarget.ImportScreenGlobalElements). An import reads <Object Name="..."/> elements of the file.
namespace Siemens.Engineering.Hmi
{
    public sealed partial class HmiTarget
    {
        public Screen.ScreenSystemFolder ScreenFolder { get; } = new();
        public Tag.TagSystemFolder TagFolder { get; } = new();
        public TextGraphicList.TextListComposition TextLists { get; } = new();
        public TextGraphicList.GraphicListComposition GraphicLists { get; } = new();
        public Screen.ScreenGlobalElements ScreenGlobalElements { get; } = new();
        public List<string> GlobalElementImports { get; } = new();
        public void ImportScreenGlobalElements(FileInfo file, ImportOptions options) => GlobalElementImports.Add(File.ReadAllText(file.FullName) + "|" + options);
    }

    public abstract class ClassicObject : NamedObject
    {
        public IList? Owner { get; set; }
        public int Exports { get; private set; }
        public virtual string Content() => $"<Document><Object Name=\"{Name}\"/></Document>";
        public void Export(FileInfo path, ExportOptions options) { Exports++; File.WriteAllText(path.FullName, Content()); }
        public void Delete() => Owner!.Remove(this);
    }

    public class ClassicComposition<T> : IEnumerable<T> where T : ClassicObject, new()
    {
        public List<T> Items { get; } = new();
        public T Add(string name) { var item = new T { Name = name, Owner = Items }; Items.Add(item); return item; }
        public void Import(FileInfo path, ImportOptions options)
        {
            foreach (var element in XDocument.Load(path.FullName).Descendants("Object"))
            {
                var name = (string)element.Attribute("Name")!;
                var existing = Items.FirstOrDefault(i => i.Name == name);
                if (existing is not null && options != ImportOptions.Override) throw new EngineeringTargetInvocationException($"'{name}' already exists.");
                if (existing is not null) Items.Remove(existing);
                Add(name);
            }
        }
        public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class ClassicFolderComposition<T> : IEnumerable<T> where T : NamedObject, new()
    {
        public List<T> Items { get; } = new();
        public T Create(string name) { var folder = new T { Name = name }; if (folder is IOwned owned) owned.Owner = Items; Items.Add(folder); return folder; }
        public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public interface IOwned { IList? Owner { get; set; } }
}

namespace Siemens.Engineering.Hmi.Screen
{
    public sealed class Screen : ClassicObject
    {
        public int Number { get; set; }
        /// <summary>Models an installation that refuses the generic attribute read (the worker falls back to an export).</summary>
        public bool RefuseNumberAttribute { get; set; }
        public override object GetAttribute(string attributeName)
            => attributeName == "Number" && !RefuseNumberAttribute ? Number : base.GetAttribute(attributeName);
        public override string Content()
            => $"<Document><Hmi.Screen.Screen ID=\"0\"><AttributeList><Name>{Name}</Name><Number>{Number}</Number></AttributeList></Hmi.Screen.Screen></Document>";
    }

    public sealed class ScreenComposition : ClassicComposition<Screen> { }
    public sealed class ScreenUserFolderComposition : ClassicFolderComposition<ScreenUserFolder> { }

    public class ScreenSystemFolder : NamedObject
    {
        public ScreenComposition Screens { get; } = new();
        public ScreenUserFolderComposition Folders { get; } = new();
    }

    public sealed class ScreenUserFolder : ScreenSystemFolder, IOwned
    {
        public IList? Owner { get; set; }
        public void Delete() => Owner!.Remove(this);
    }

    public sealed class ScreenGlobalElements : ClassicObject { }
}

namespace Siemens.Engineering.Hmi.Tag
{
    public sealed class Tag : ClassicObject { }
    public sealed class TagComposition : ClassicComposition<Tag> { }

    public sealed class TagTable : ClassicObject
    {
        public TagComposition Tags { get; } = new();
        public bool IsSystemObject { get; set; }
    }

    public sealed class TagTableComposition : ClassicComposition<TagTable> { }
    public sealed class TagUserFolderComposition : ClassicFolderComposition<TagUserFolder> { }

    public class TagSystemFolder : NamedObject
    {
        public TagTableComposition TagTables { get; } = new();
        public TagUserFolderComposition Folders { get; } = new();
    }

    public sealed class TagUserFolder : TagSystemFolder, IOwned
    {
        public IList? Owner { get; set; }
        public void Delete() => Owner!.Remove(this);
    }
}

namespace Siemens.Engineering.Hmi.TextGraphicList
{
    public sealed class TextList : ClassicObject { }
    public sealed class TextListComposition : ClassicComposition<TextList> { }
    public sealed class GraphicList : ClassicObject { }
    public sealed class GraphicListComposition : ClassicComposition<GraphicList> { }
}
