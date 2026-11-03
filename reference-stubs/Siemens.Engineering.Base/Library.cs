// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering
{
    public enum OpenMode : int
    {
    }
    public abstract partial class TiaPortal
    {
        public global::Siemens.Engineering.Library.GlobalLibraryComposition GlobalLibraries { get => throw new global::System.NotSupportedException(); }
    }
}

namespace Siemens.Engineering.Library
{
    public abstract class GlobalLibrary : global::Siemens.Engineering.Library.ILibrary, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        global::Siemens.Engineering.Library.Types.LibraryTypeSystemFolder global::Siemens.Engineering.Library.ILibrary.TypeFolder => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder global::Siemens.Engineering.Library.ILibrary.MasterCopyFolder => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.Library.Types.LibraryType global::Siemens.Engineering.Library.ILibrary.FindType(global::System.Guid guid) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.Library.Types.LibraryTypeVersion global::Siemens.Engineering.Library.ILibrary.FindVersion(global::System.Guid guid) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class GlobalLibraryComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance
    {
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public interface ILibrary
    {
        global::Siemens.Engineering.Library.Types.LibraryTypeSystemFolder TypeFolder { get; }
        global::Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder MasterCopyFolder { get; }
        global::Siemens.Engineering.Library.Types.LibraryType FindType(global::System.Guid guid);
        global::Siemens.Engineering.Library.Types.LibraryTypeVersion FindVersion(global::System.Guid guid);
    }
    public abstract class ProjectLibrary : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.Compare.ISoftwareCompareTarget, global::Siemens.Engineering.Library.ILibrary
    {
        public global::Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder MasterCopyFolder { get => throw new global::System.NotSupportedException(); }
        global::Siemens.Engineering.Library.Types.LibraryTypeSystemFolder global::Siemens.Engineering.Library.ILibrary.TypeFolder => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.Library.Types.LibraryType global::Siemens.Engineering.Library.ILibrary.FindType(global::System.Guid guid) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.Library.Types.LibraryTypeVersion global::Siemens.Engineering.Library.ILibrary.FindVersion(global::System.Guid guid) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class CorporateGlobalLibrary : global::Siemens.Engineering.Library.GlobalLibrary, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class SystemGlobalLibrary : global::Siemens.Engineering.Library.GlobalLibrary, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class UserGlobalLibrary : global::Siemens.Engineering.Library.GlobalLibrary, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
}

namespace Siemens.Engineering.Library.MasterCopies
{
    public interface IMasterCopySource
    {
    }
    public abstract class MasterCopy : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.Compare.ISoftwareCompareTarget
    {
        public string Name { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class MasterCopyComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Library.MasterCopies.MasterCopy>
    {
        public global::Siemens.Engineering.Library.MasterCopies.MasterCopy Create(global::Siemens.Engineering.Library.MasterCopies.IMasterCopySource source) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.Library.MasterCopies.MasterCopy CreateFrom(global::Siemens.Engineering.Library.MasterCopies.MasterCopy masterCopy) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.Library.MasterCopies.MasterCopy Find(string name) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Library.MasterCopies.MasterCopy> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class MasterCopyFolder : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.Library.MasterCopies.MasterCopyComposition MasterCopies { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public enum MasterCopyMode : int
    {
    }
    public abstract class MasterCopySystemFolder : global::Siemens.Engineering.Library.MasterCopies.MasterCopyFolder, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
}

namespace Siemens.Engineering.Library.Types
{
    public enum CreateOrReleaseDependenciesMode : int
    {
    }
    public interface IInstanceSearchScope
    {
    }
    public interface ILibraryTypeOrFolderSelection
    {
    }
    public interface IUpdateProjectScope
    {
    }
    public abstract class LibraryType : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::System.Guid Guid { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeFolder : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeInstanceInfo : global::Siemens.Engineering.IEngineeringService, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeSystemFolder : global::Siemens.Engineering.Library.Types.LibraryTypeFolder, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeUserFolder : global::Siemens.Engineering.Library.Types.LibraryTypeFolder, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class LibraryTypeVersion : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::System.Guid Guid { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.Types.LibraryTypeVersionState State { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Library.Types.LibraryTypeVersion Edit() => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public enum LibraryTypeVersionState : int
    {
    }
    public enum UpdateCheckMode : int
    {
    }
    public enum UpdatePathsMode : int
    {
    }
}
