// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.SW.Alarm
{
    public abstract class AlarmClassDataProvider : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.IEngineeringService
    {
        public global::Siemens.Engineering.SW.Alarm.AlarmClassExportImportResult Export(global::System.IO.FileInfo path) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.SW.Alarm.AlarmClassExportImportResult Import(global::System.IO.FileInfo path) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class AlarmClassExportImportResult
    {
    }
    public abstract class PlcAlarmTextListProvider : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.IEngineeringService
    {
        public global::Siemens.Engineering.SW.Alarm.TextListXlsxResult ExportToXlsx(global::System.IO.FileInfo path) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.SW.Alarm.TextListXlsxResult ExportToXlsx(global::System.IO.FileInfo path, global::System.Collections.Generic.IEnumerable<string> textListNames, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Language> languages) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.SW.Alarm.TextListXlsxResult ImportFromXlsx(global::System.IO.FileInfo path, global::Siemens.Engineering.ImportOptions importOptions) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class PlcAlarmTextProvider : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.IEngineeringService
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public enum PlcAlarmTextXlsxExportOption : int
    {
    }
    public abstract class TextListXlsxResult
    {
    }
}

namespace Siemens.Engineering.SW.Alarm.TextLists
{
    public abstract class PlcAlarmSystemTextlist : global::Siemens.Engineering.SW.Alarm.TextLists.PlcAlarmTextlist
    {
    }
    public abstract class PlcAlarmSystemTextlistComposition : global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.SW.Alarm.TextLists.PlcAlarmSystemTextlist>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.SW.Alarm.TextLists.PlcAlarmSystemTextlist> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public abstract class PlcAlarmTextlist : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class PlcAlarmTextlistGroup : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public global::Siemens.Engineering.SW.Alarm.TextLists.PlcAlarmSystemTextlistComposition PlcAlarmSystemTextlists { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.SW.Alarm.TextLists.PlcAlarmUserTextlistComposition PlcAlarmUserTextlists { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class PlcAlarmUserTextlist : global::Siemens.Engineering.SW.Alarm.TextLists.PlcAlarmTextlist
    {
        public string Name { get => throw new global::System.NotSupportedException(); }
        public void Delete() => throw new global::System.NotSupportedException();
    }
    public abstract class PlcAlarmUserTextlistComposition : global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.SW.Alarm.TextLists.PlcAlarmUserTextlist>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.SW.Alarm.TextLists.PlcAlarmUserTextlist> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
}

namespace Siemens.Engineering.SW.Supervision
{
    public abstract class SupervisionProvider : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.IEngineeringService
    {
        public global::Siemens.Engineering.SW.Supervision.SupervisionXlsxResult ExportSupervisionsToXlsx(global::System.IO.FileInfo path) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.SW.Supervision.SupervisionXlsxResult ImportSupervisionSettingsFromXlsx(global::System.IO.FileInfo path, global::Siemens.Engineering.ImportOptions importOptions) => throw new global::System.NotSupportedException();
        public global::Siemens.Engineering.SW.Supervision.SupervisionXlsxResult ImportSupervisionsFromXlsx(global::System.IO.FileInfo path, global::Siemens.Engineering.ImportOptions importOptions) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class SupervisionXlsxResult
    {
    }
}
