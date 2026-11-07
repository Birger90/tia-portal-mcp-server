// Stand-ins for UMAC (issue #12) with the member names of Siemens.Engineering.Base.xml (V21), namespace Siemens.Engineering.Umac.
// Member shapes and a simple state model only; no evidence of Siemens behavior.
using System.Collections;
using System.Security;

namespace Siemens.Engineering.Umac
{
    public class Role(string name)
    {
        public string Name { get; protected set; } = name;

        public string Identifier { get; } = "id-" + name;
    }

    public sealed class EngineeringFunctionRight(string name)
    {
        public string Name { get; } = name;

        public string Identifier { get; } = "efr-" + name;
    }

    public sealed class DeviceFunctionRight(string name, string group)
    {
        public string Name { get; } = name;

        public string Group { get; } = group;

        public string Identifier { get; } = "dfr-" + name;
    }

    public sealed class UmacDevice
    {
        public List<DeviceFunctionRight> AvailableDeviceFunctionRights { get; } = new() { new("Read access", "Access levels"), new("Full access", "Access levels") };
    }

    public sealed class EngineeringFunctionRightAssociation : IEnumerable<EngineeringFunctionRight>
    {
        private readonly List<EngineeringFunctionRight> _items = new();

        public void Add(EngineeringFunctionRight right) => _items.Add(right);

        public bool Remove(EngineeringFunctionRight right) => _items.Remove(right);

        public IEnumerator<EngineeringFunctionRight> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class CustomRole(CustomRoleComposition owner, string name, string comment) : Role(name)
    {
        private readonly Dictionary<UmacDevice, List<DeviceFunctionRight>> _rights = new();

        public string Comment { get; set; } = comment;

        public uint SessionTimeOut { get; set; } = 30;

        public EngineeringFunctionRightAssociation AssignedEngineeringRights { get; } = new();

        public void AssignDeviceFunctionRight(UmacDevice device, DeviceFunctionRight right)
        {
            if (!_rights.TryGetValue(device, out var list)) _rights[device] = list = new List<DeviceFunctionRight>();
            list.Add(right);
        }

        public void UnAssignDeviceFunctionRight(UmacDevice device, DeviceFunctionRight right)
        {
            if (_rights.TryGetValue(device, out var list)) list.Remove(right);
        }

        public IList<DeviceFunctionRight> GetAssignedDeviceFunctionRights(UmacDevice device) => _rights.TryGetValue(device, out var list) ? list : new List<DeviceFunctionRight>();

        public void SetName(string name) => Name = name;

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class CustomRoleComposition : IEnumerable<CustomRole>
    {
        public List<CustomRole> Items { get; } = new();

        public CustomRole Create(string name, string comment)
        {
            var role = new CustomRole(this, name, comment);
            Items.Add(role);
            return role;
        }

        public CustomRole? Find(string name) => Items.FirstOrDefault(r => r.Name == name);

        public IEnumerator<CustomRole> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SystemRole(string name) : Role(name)
    {
        public EngineeringFunctionRightAssociation AssignedEngineeringRights { get; } = new();

        public IList<DeviceFunctionRight> GetAssignedSystemDeviceFunctionRights(UmacDevice device) => device.AvailableDeviceFunctionRights;
    }

    public sealed class SystemRoleComposition : IEnumerable<SystemRole>
    {
        public List<SystemRole> Items { get; } = new() { new("Engineering administrator") };

        public SystemRole? Find(string name) => Items.FirstOrDefault(r => r.Name == name);

        public IEnumerator<SystemRole> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class RoleAssociation : IEnumerable<Role>
    {
        private readonly List<Role> _items = new();

        public void Add(Role role) => _items.Add(role);

        public bool Remove(Role role) => _items.Remove(role);

        public IEnumerator<Role> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ProjectUser(ProjectUserComposition owner, string name)
    {
        public string Name { get; private set; } = name;

        public string AliasName { get; private set; } = string.Empty;

        public string Comment { get; set; } = string.Empty;

        public bool IsActive { get; private set; } = true;

        public bool IsRuntimeSessionTimeoutActive { get; private set; }

        public uint SessionTimeOut { get; set; } = 30;

        public RoleAssociation Roles { get; } = new();

        public void SetPassword(SecureString password)
        {
            if (password.Length < 8) throw new InvalidOperationException("The password violates the password policy.");
        }

        public void SetName(string name) => Name = name;

        public void SetAliasName(string alias) => AliasName = alias;

        public void Activate() => IsActive = true;

        public void Deactivate() => IsActive = false;

        public void ActivateRuntimeSessionTimeout() => IsRuntimeSessionTimeoutActive = true;

        public void DeactivateRuntimeSessionTimeout() => IsRuntimeSessionTimeoutActive = false;

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class ProjectUserComposition : IEnumerable<ProjectUser>
    {
        public List<ProjectUser> Items { get; } = new();

        public ProjectUser Create(string name, SecureString password)
        {
            if (password.Length < 8) throw new InvalidOperationException("The password violates the password policy.");
            var user = new ProjectUser(this, name);
            Items.Add(user);
            return user;
        }

        public ProjectUser? Find(string name) => Items.FirstOrDefault(u => u.Name == name);

        public IEnumerator<ProjectUser> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class AnonymousUserStandIn
    {
        public string Name => "Anonymous";

        public bool IsActive { get; set; }

        public RoleAssociation Roles { get; } = new();
    }

    public sealed class CustomDeviceFunctionRight(CustomDeviceFunctionRightComposition owner, string name, string group, string comment)
    {
        public string Name { get; private set; } = name;

        public string Group { get; } = group;

        public string Identifier { get; } = "cdfr-" + name;

        public string Comment { get; set; } = comment;

        public void SetName(string name) => Name = name;

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class CustomDeviceFunctionRightComposition : IEnumerable<CustomDeviceFunctionRight>
    {
        public List<CustomDeviceFunctionRight> Items { get; } = new();

        public CustomDeviceFunctionRight Create(string name, string group, string comment)
        {
            var right = new CustomDeviceFunctionRight(this, name, group, comment);
            Items.Add(right);
            return right;
        }

        public CustomDeviceFunctionRight? Find(string name) => Items.FirstOrDefault(r => r.Name == name);

        public IEnumerator<CustomDeviceFunctionRight> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class EngineeringFunctionRightComposition : IEnumerable<EngineeringFunctionRight>
    {
        public List<EngineeringFunctionRight> Items { get; } = new() { new("Modify project via Openness API"), new("Open project read-only") };

        public EngineeringFunctionRight? Find(string name) => Items.FirstOrDefault(r => r.Name == name);

        public IEnumerator<EngineeringFunctionRight> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class UmcUser(UmcUserComposition owner, string name)
    {
        public string Name { get; private set; } = name;

        public string DomainId { get; } = "DOMAIN";

        public bool IsActive { get; private set; } = true;

        public RoleAssociation Roles { get; } = new();

        public void Activate() => IsActive = true;

        public void Deactivate() => IsActive = false;

        public void SetName(string name) => Name = name;

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class UmcUserComposition : IEnumerable<UmcUser>
    {
        public List<UmcUser> Items { get; } = new();

        public UmcUser? Find(string name) => Items.FirstOrDefault(u => u.Name == name);

        // Package G.
        public UmcUser CreateOfflineUmcUser(string name)
        {
            var user = new UmcUser(this, name);
            Items.Add(user);
            return user;
        }

        public UmcUser Create(UmcUserInfo info)
        {
            var user = new UmcUser(this, info.Name);
            Items.Add(user);
            return user;
        }

        public IEnumerator<UmcUser> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class UmcUserGroupComposition : IEnumerable<UmcUser>
    {
        private readonly UmcUserComposition _owner = new();

        public List<UmcUser> Items { get; } = new();

        public UmcUser? Find(string name) => Items.FirstOrDefault(u => u.Name == name);

        // Package G: the fake keeps groups as UmcUser stand-ins (Name, SetName, Delete on this list).
        public UmcUser CreateOfflineUmcUserGroup()
        {
            // K96 (live 2026-11-07): TIA names the new group Group_N; the caller's name is set afterwards with SetName.
            var group = new UmcUser(_owner, "Group_" + (Items.Count + 1));
            Items.Add(group);
            return group;
        }

        public UmcUser Create(UmcUserGroupInfo info)
        {
            var group = new UmcUser(_owner, info.UserGroupName);
            Items.Add(group);
            return group;
        }

        public IEnumerator<UmcUser> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class UmacConfigurator
    {
        public UmacConfigurator()
        {
            UmcUsers.Items.Add(new UmcUser(UmcUsers, "DOMAIN\\engineer"));
        }

        public CustomRoleComposition CustomRoles { get; } = new();

        public SystemRoleComposition SystemRoles { get; } = new();

        public ProjectUserComposition ProjectUsers { get; } = new();

        public AnonymousUserStandIn AnonymousUser { get; } = new();

        public void ActivateAnonymousUser() => AnonymousUser.IsActive = true;

        public void DeactivateAnonymousUser() => AnonymousUser.IsActive = false;

        public EngineeringFunctionRightComposition EngineeringFunctionRights { get; } = new();

        public CustomDeviceFunctionRightComposition CustomDeviceFunctionRights { get; } = new();

        public UmcUserComposition UmcUsers { get; } = new();

        public UmcUserGroupComposition UmcUserGroups { get; } = new();
    }

    // Package G: the UMC server. The fake server knows "DOMAIN\\operator" and the group "DOMAIN\\Operators" and accepts only the user
    // "umcadmin" with a password of at least 8 characters, given through the Authentication event.
    public sealed class UmcUserInfo(string name)
    {
        public string Name { get; } = name;
    }

    public sealed class UmcUserGroupInfo(string name, string description)
    {
        public string UserGroupName { get; } = name;

        public string Description { get; } = description;
    }

    public sealed class UmcAuthenticationEventArgs : EventArgs
    {
        public Siemens.Engineering.UmacCredentials UmcCredentials { get; } = new();
    }

    public sealed class UmcServer
    {
        public event EventHandler<UmcAuthenticationEventArgs>? Authentication;

        public int Authentications { get; private set; }

        private void Authenticate()
        {
            var args = new UmcAuthenticationEventArgs();
            Authentication?.Invoke(this, args);
            Authentications++;
            if (args.UmcCredentials.Name != "umcadmin" || args.UmcCredentials.PasswordLength < 8 || args.UmcCredentials.Type != Siemens.Engineering.UmacUserType.Global)
                throw new InvalidOperationException("The UMC server refused the credentials.");
        }

        public UmcUserInfo? GetUserByName(string userName)
        {
            Authenticate();
            return userName == "DOMAIN\\operator" ? new UmcUserInfo(userName) : null;
        }

        public UmcUserGroupInfo? GetUserGroupByName(string userGroupName)
        {
            Authenticate();
            return userGroupName == "DOMAIN\\Operators" ? new UmcUserGroupInfo(userGroupName, "Operators of line 1") : null;
        }
    }

    public sealed class UmcServerConfigurator
    {
        public UmcServer UmcServer { get; } = new();

        public bool CheckConsistency() => true;
    }

    public sealed class PasswordPolicyConfigurator
    {
        public int MinimumLength { get; set; } = 8;

        public int MinimumNumericCharacterLength { get; set; } = 1;

        public int MinimumSpecialCharacterLength { get; set; } = 1;

        public bool IncludesLowerCaseAndUpperCaseCharacters { get; set; } = true;

        public bool EnablePasswordAging { get; set; }

        public int PasswordValidity { get; set; } = 90;

        public int PasswordValidityPrewarningTime { get; set; } = 7;

        public int MinimumUserPasswordsBlockedForReuse { get; set; } = 3;
    }
}

namespace Siemens.Engineering
{
    public enum UmacUserType
    {
        Global,
        Project,
    }

    public sealed class UmacCredentials
    {
        public string? Name { get; set; }

        public UmacUserType Type { get; set; } = UmacUserType.Project;

        public int PasswordLength { get; private set; }

        public void SetPassword(System.Security.SecureString password) => PasswordLength = password.Length;
    }
}
