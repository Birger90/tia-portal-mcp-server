// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.Online
{
    public delegate void OnlineConfigurationDelegate(global::Siemens.Engineering.Online.Configurations.OnlineConfiguration onlineConfiguration);
    public abstract class OnlineProvider : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance, global::Siemens.Engineering.IEngineeringService
    {
        public global::Siemens.Engineering.Connection.ConnectionConfiguration Configuration { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Online.OnlineState State { get => throw new global::System.NotSupportedException(); }
        public global::Siemens.Engineering.Online.OnlineState GoOnline() => throw new global::System.NotSupportedException();
        public void GoOffline() => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
    public enum OnlineState : int
    {
    }
}

namespace Siemens.Engineering.Online.Configurations
{
    public abstract class OnlineConfiguration : global::Siemens.Engineering.IEngineeringObject, global::Siemens.Engineering.IEngineeringInstance
    {
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
}
