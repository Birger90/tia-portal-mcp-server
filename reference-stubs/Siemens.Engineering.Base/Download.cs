// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.Download
{
    public delegate void DownloadConfigurationDelegate(global::Siemens.Engineering.Download.Configurations.DownloadConfiguration downloadConfiguration);
    public enum DownloadOptions : int
    {
    }
    public abstract class DownloadProvider : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.IEngineeringService
    {
        public global::Siemens.Engineering.Connection.ConnectionConfiguration Configuration { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Download.DownloadResult Download(global::Siemens.Engineering.Connection.IConfiguration configuration, global::Siemens.Engineering.Download.DownloadConfigurationDelegate preConfigurationDelegate, global::Siemens.Engineering.Download.DownloadConfigurationDelegate postConfigurationDelegate, global::Siemens.Engineering.Download.DownloadOptions downloadOptions) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class DownloadResult : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public int ErrorCount { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Download.DownloadResultMessageComposition Messages { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Download.DownloadResultState State { get => throw new global::System.NotSupportedException(); }
        public int WarningCount { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class DownloadResultMessage : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string Message { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Download.DownloadResultMessageComposition Messages { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Download.DownloadResultState State { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class DownloadResultMessageComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Download.DownloadResultMessage>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Download.DownloadResultMessage> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public enum DownloadResultState : int
    {
    }
}

namespace Siemens.Engineering.Download.Configurations
{
    public abstract class AlarmTextLibrariesDownload : global::Siemens.Engineering.Download.Configurations.DownloadConfiguration
    {
        public global::Siemens.Engineering.Download.Configurations.AlarmTextLibrariesDownloadSelections CurrentSelection { get => throw new global::System.NotSupportedException(); set => throw new global::System.NotSupportedException(); }
    }
    public enum AlarmTextLibrariesDownloadSelections : int
    {
    }
    public abstract class DownloadConfiguration : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string Message { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class OverwriteSystemData : global::Siemens.Engineering.Download.Configurations.DownloadConfiguration
    {
        public global::Siemens.Engineering.Download.Configurations.OverwriteSystemDataSelections CurrentSelection { get => throw new global::System.NotSupportedException(); set => throw new global::System.NotSupportedException(); }
    }
    public enum OverwriteSystemDataSelections : int
    {
    }
    public abstract class StartModules : global::Siemens.Engineering.Download.Configurations.DownloadConfiguration
    {
        public global::Siemens.Engineering.Download.Configurations.StartModulesSelections CurrentSelection { get => throw new global::System.NotSupportedException(); set => throw new global::System.NotSupportedException(); }
    }
    public enum StartModulesSelections : int
    {
    }
    public abstract class StopModules : global::Siemens.Engineering.Download.Configurations.DownloadConfiguration
    {
        public global::Siemens.Engineering.Download.Configurations.StopModulesSelections CurrentSelection { get => throw new global::System.NotSupportedException(); set => throw new global::System.NotSupportedException(); }
    }
    public enum StopModulesSelections : int
    {
    }
}
