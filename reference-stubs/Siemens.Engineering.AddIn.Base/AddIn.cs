// Compile-only stub of the TIA Portal V21 Add-In API surface used by TiaMcpServer.AddIn.
// Names, namespaces and parameter types: Siemens.Engineering.AddIn.Base.xml (V21 IntelliSense, /home/claude/siemens-api-v21).
// Return types and delegate signatures the XML does not show come from the Siemens V21 online help pages
// "Programmazione del View Provider", "Programmazione dei menu di scelta rapida", "API Messagebox", "API di feedback"
// (docs.tia.siemens.cloud, v21) and are marked "assumed" in the provenance table of docs/SupportedOperations/CLAUDE_IN_TIA.md.
// Members whose return types neither source shows (MenuSelectionProvider.GetSelection, MessageBoxProvider.ShowNotification,
// FeedbackProvider.Log) are NOT declared: the Add-In binds them at runtime by name.

namespace Siemens.Engineering.AddIn.Menu
{
    public abstract class MenuItem
    {
    }

    public abstract class ContextMenuAddIn
    {
        protected ContextMenuAddIn(string displayName) => throw new global::System.NotSupportedException();
        protected abstract void BuildContextMenuItems(global::Siemens.Engineering.AddIn.Menu.ContextMenuAddInRoot addInRootSubmenu);
    }

    public sealed class ContextMenuAddInRoot
    {
        private ContextMenuAddInRoot() { }
        public global::Siemens.Engineering.AddIn.Menu.ChildItemFactory Items { get => throw new global::System.NotSupportedException(); }
    }

    public sealed class ChildItemFactory
    {
        private ChildItemFactory() { }
        public void AddActionItem<TSelection>(string displayName, global::Siemens.Engineering.AddIn.Menu.ActionItem<TSelection>.OnClickDelegate onClick) => throw new global::System.NotSupportedException();
    }

    public abstract class ActionItem : global::Siemens.Engineering.AddIn.Menu.MenuItem
    {
    }

    public sealed class ActionItem<TSelection> : global::Siemens.Engineering.AddIn.Menu.ActionItem
    {
        private ActionItem() { }
        public delegate void OnClickDelegate(global::Siemens.Engineering.AddIn.Menu.MenuSelectionProvider<TSelection> menuSelectionProvider);
    }

    public abstract class MenuSelectionProvider
    {
    }

    public abstract class MenuSelectionProvider<TSelection> : global::Siemens.Engineering.AddIn.Menu.MenuSelectionProvider
    {
    }
}

namespace Siemens.Engineering.AddIn
{
    public abstract class ProjectTreeAddInProvider : global::System.IDisposable
    {
        protected ProjectTreeAddInProvider() => throw new global::System.NotSupportedException();
        protected abstract global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.AddIn.Menu.ContextMenuAddIn> GetContextMenuAddIns();
        void global::System.IDisposable.Dispose() => throw new global::System.NotSupportedException();
        protected virtual void Dispose(bool disposing) => throw new global::System.NotSupportedException();
    }

    public abstract class DevicesAndNetworksAddInProvider : global::System.IDisposable
    {
        protected DevicesAndNetworksAddInProvider() => throw new global::System.NotSupportedException();
        protected abstract global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.AddIn.Menu.ContextMenuAddIn> GetContextMenuAddIns();
        void global::System.IDisposable.Dispose() => throw new global::System.NotSupportedException();
        protected virtual void Dispose(bool disposing) => throw new global::System.NotSupportedException();
    }

    public abstract class ProjectLibraryTreeAddInProvider : global::System.IDisposable
    {
        protected ProjectLibraryTreeAddInProvider() => throw new global::System.NotSupportedException();
        protected abstract global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.AddIn.Menu.ContextMenuAddIn> GetContextMenuAddIns();
        void global::System.IDisposable.Dispose() => throw new global::System.NotSupportedException();
        protected virtual void Dispose(bool disposing) => throw new global::System.NotSupportedException();
    }

    public abstract class GlobalLibraryTreeAddInProvider : global::System.IDisposable
    {
        protected GlobalLibraryTreeAddInProvider() => throw new global::System.NotSupportedException();
        protected abstract global::System.Collections.Generic.IEnumerable<global::Siemens.Engineering.AddIn.Menu.ContextMenuAddIn> GetContextMenuAddIns();
        void global::System.IDisposable.Dispose() => throw new global::System.NotSupportedException();
        protected virtual void Dispose(bool disposing) => throw new global::System.NotSupportedException();
    }

    public sealed class MessageBoxProvider : global::Siemens.Engineering.IEngineeringService, global::System.IDisposable
    {
        private MessageBoxProvider() { }
        public void Dispose() => throw new global::System.NotSupportedException();
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }

    public sealed class FeedbackProvider : global::Siemens.Engineering.IEngineeringService
    {
        private FeedbackProvider() { }
        object global::Siemens.Engineering.IEngineeringObject.GetAttribute(string name) => throw new global::System.NotSupportedException();
        void global::Siemens.Engineering.IEngineeringObject.SetAttribute(string name, object value) => throw new global::System.NotSupportedException();
        global::System.Collections.Generic.IList<global::Siemens.Engineering.EngineeringAttributeInfo> global::Siemens.Engineering.IEngineeringObject.GetAttributeInfos() => throw new global::System.NotSupportedException();
        global::Siemens.Engineering.IEngineeringObject global::Siemens.Engineering.IEngineeringInstance.Parent => throw new global::System.NotSupportedException();
    }
}
