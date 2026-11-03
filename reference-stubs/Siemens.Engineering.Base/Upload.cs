// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.Upload
{
    public abstract class StationUploadProvider : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.IEngineeringService
    {
        public global::Siemens.Engineering.Connection.ConnectionConfiguration Configuration { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Upload.UploadResult StationUpload(global::Siemens.Engineering.Connection.ConfigurationAddress configurationAddress, global::Siemens.Engineering.Upload.UploadConfigurationDelegate uploadConfigurationDelegate) => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public delegate void UploadConfigurationDelegate(global::Siemens.Engineering.Upload.Configurations.UploadConfiguration uploadConfiguration);
    public abstract class UploadResult : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public int ErrorCount { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Upload.UploadResultMessageComposition Messages { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Upload.UploadResultState State { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.HW.Device UploadedStation { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class UploadResultMessage : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public abstract class UploadResultMessageComposition : global::Siemens.Engineering.IEngineeringComposition, global::Siemens.Engineering.IEngineeringInstance, global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.Upload.UploadResultMessage>
    {
        public global::System.Collections.Generic.IEnumerator<global::Siemens.Engineering.Upload.UploadResultMessage> GetEnumerator() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringComposition.Create(global::System.Type type, global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>> parameters) => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => throw new global::System.NotSupportedException();
    }
    public enum UploadResultState : int
    {
    }
}

namespace Siemens.Engineering.Upload.Configurations
{
    public abstract class UploadConfiguration : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        public string Message { get => throw new global::System.NotSupportedException(); }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
}
