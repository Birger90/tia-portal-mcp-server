// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.SW.WatchAndForceTables
{
    public abstract class PlcForceTable : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public bool IsConsistent { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public void Export(global::System.IO.FileInfo path, global::Siemens.Engineering.ExportOptions exportOptions) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class PlcForceTableComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.SW.WatchAndForceTables.PlcForceTable>
    {
        public global::System.Collections.Generic.IList<global::Siemens.Engineering.SW.WatchAndForceTables.PlcForceTable> Import(global::System.IO.FileInfo path, global::Siemens.Engineering.ImportOptions importOptions) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.SW.WatchAndForceTables.PlcForceTable> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class PlcWatchAndForceTableGroup : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.SW.WatchAndForceTables.PlcForceTableComposition ForceTables { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchAndForceTableUserGroupComposition Groups { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchTableComposition WatchTables { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class PlcWatchAndForceTableSystemGroup : global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchAndForceTableGroup, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class PlcWatchAndForceTableUserGroup : global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchAndForceTableGroup, global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public void Delete() => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class PlcWatchAndForceTableUserGroupComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchAndForceTableUserGroup>
    {
        public global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchAndForceTableUserGroup Create(string name) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchAndForceTableUserGroup> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class PlcWatchTable : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public bool IsConsistent { get => throw new global::System.NotSupportedException(); }
        public string Name { get => throw new global::System.NotSupportedException(); }
        public void Delete() => throw new global::System.NotSupportedException();
        public void Export(global::System.IO.FileInfo path, global::Siemens.Engineering.ExportOptions exportOptions) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class PlcWatchTableComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchTable>
    {
        public global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchTable Create(string name) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IList<global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchTable> Import(global::System.IO.FileInfo path, global::Siemens.Engineering.ImportOptions importOptions) => throw new global::System.NotSupportedException();
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.SW.WatchAndForceTables.PlcWatchTable> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}
