// Stand-ins for the web server and OPC UA user management (issue #13) with the member names of Siemens.Engineering.Step7.xml (V21).
// Member shapes and a simple state model only; no evidence of Siemens behavior (whether WebserverUserPermissions is a flags enum
// is not documented; the stand-in is one so combined permissions can be modeled).
using System.Collections;
using System.Security;

namespace Siemens.Engineering.HW
{
    [Flags]
    public enum WebserverUserPermissions
    {
        None = 0, ReadTag = 1, ModifyTag = 2, ReadTagStatus = 4, ModifyTagStatus = 8, ChangeOperatingMode = 16, FlashLed = 32, ReadFiles = 64,
        ModifyFiles = 128, DoDiagnosis = 256, AcknowledgeMessages = 512, OpenUserDefinedWebPages = 1024, WriteUserDefinedWebPages = 2048,
        ManageUserDefinedWebPages = 4096, ChangeSystemParameter = 8192, ChangeApplicationParameter = 16384, Backup = 32768, Restore = 65536,
        WriteFirmware = 131072, FAdmin = 262144,
    }

    public enum SimpleWebserverUserPermissions { None, ReadOnly, ReadWrite }

    public sealed class WebserverUser(WebserverUserComposition owner, string name, WebserverUserPermissions permissions)
    {
        public string UserName { get; } = name;

        public WebserverUserPermissions Permissions { get; set; } = permissions;

        public int PasswordsSet { get; private set; } = 1;

        public void SetPassword(SecureString password)
        {
            if (password.Length < 8) throw new InvalidOperationException("user password violates the security setting password policy");
            PasswordsSet++;
        }

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class WebserverUserComposition : IEnumerable<WebserverUser>
    {
        public List<WebserverUser> Items { get; } = new();

        public WebserverUser Create(string name, WebserverUserPermissions permissions, SecureString password)
        {
            if (password.Length < 8) throw new InvalidOperationException("user password violates the security setting password policy");
            var user = new WebserverUser(this, name, permissions);
            Items.Add(user);
            return user;
        }

        public WebserverUser? Find(string name) => Items.FirstOrDefault(u => u.UserName == name);

        public IEnumerator<WebserverUser> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SimpleWebserverUser(string name)
    {
        public string UserName { get; } = name;

        public bool Active { get; set; }

        public SimpleWebserverUserPermissions Permissions { get; set; } = SimpleWebserverUserPermissions.ReadOnly;

        public void SetPassword(SecureString password)
        {
        }
    }

    public sealed class SimpleWebserverUserComposition : IEnumerable<SimpleWebserverUser>
    {
        public List<SimpleWebserverUser> Items { get; } = new() { new("Everybody") };

        public SimpleWebserverUser? Find(string name) => Items.FirstOrDefault(u => u.UserName == name);

        public IEnumerator<SimpleWebserverUser> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class OpcUaUser(OpcUaUserComposition owner, string name)
    {
        public string UserName { get; } = name;

        public void SetPassword(SecureString password)
        {
        }

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class OpcUaUserComposition : IEnumerable<OpcUaUser>
    {
        public List<OpcUaUser> Items { get; } = new();

        public OpcUaUser Create(string name, SecureString password)
        {
            var user = new OpcUaUser(this, name);
            Items.Add(user);
            return user;
        }

        public OpcUaUser? Find(string name) => Items.FirstOrDefault(u => u.UserName == name);

        public IEnumerator<OpcUaUser> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace Siemens.Engineering.HW.Features
{
    public sealed class WebserverUserManagement
    {
        public Siemens.Engineering.HW.WebserverUserComposition WebserverUsers { get; } = new();
    }

    public sealed class SimpleWebserverUserManagement
    {
        public Siemens.Engineering.HW.SimpleWebserverUserComposition WebserverUsers { get; } = new();
    }

    public sealed class OpcUaUserManagement
    {
        public Siemens.Engineering.HW.OpcUaUserComposition OpcUaUsers { get; } = new();
    }
}
