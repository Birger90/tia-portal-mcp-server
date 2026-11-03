// Stand-ins for the services of the settings family with the member names of the V21 IntelliSense files (Siemens.Engineering.Base.xml,
// Siemens.Engineering.Step7.xml) and the V21 manual sections named in PlcCpuSettingsMethods. Member shapes and a simple state model only;
// no evidence of Siemens behavior.
using System.Collections;
using System.Security;

namespace Siemens.Engineering.HW
{
    public enum SysLogTransportProtocolType { None, TLSServerAndClientAuthentication, TLSOnlyServerAuthentication, UDP }

    public enum WebApplicationType { None, System, System_Inbuilt, UserDefined }

    public enum WatchAndForceTableAccess { None, Read, Write }

    /// <summary>A device item with a name, children and the IEngineeringObject attribute pair (GetAttribute/SetAttribute).</summary>
    public sealed class FakeDeviceItem(string name)
    {
        public string Name { get; } = name;

        public Dictionary<string, object?> Attributes { get; } = new(StringComparer.Ordinal);

        public object? GetAttribute(string name)
            => Attributes.TryGetValue(name, out var value) ? value : throw new InvalidOperationException($"Attribute '{name}' is not supported.");

        public void SetAttribute(string name, object value)
        {
            if (!Attributes.ContainsKey(name)) throw new InvalidOperationException($"Attribute '{name}' is not supported.");
            Attributes[name] = value;
        }
    }

    public sealed class WebApplicationConfiguration(string name, WebApplicationType type, bool isDefault)
    {
        public string Name { get; } = name;

        public bool IsDefault { get; set; } = isDefault;

        public WebApplicationType ApplicationType { get; } = type;
    }

    public sealed class WebApplicationConfigurationComposition : IEnumerable<WebApplicationConfiguration>
    {
        public List<WebApplicationConfiguration> Items { get; } = new();

        public WebApplicationConfiguration? Find(string name) => Items.FirstOrDefault(i => i.Name == name);

        public IEnumerator<WebApplicationConfiguration> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class WatchTableAccessRule(WatchTableAccessRuleComposition owner, SW.WatchAndForceTables.PlcWatchTable table, WatchAndForceTableAccess access)
    {
        public SW.WatchAndForceTables.PlcWatchTable WatchTable { get; } = table;

        public WatchAndForceTableAccess Access { get; set; } = access;

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class WatchTableAccessRuleComposition : IEnumerable<WatchTableAccessRule>
    {
        public List<WatchTableAccessRule> Items { get; } = new();

        public WatchTableAccessRule? Find(SW.WatchAndForceTables.PlcWatchTable table) => Items.FirstOrDefault(r => ReferenceEquals(r.WatchTable, table));

        public WatchTableAccessRule Create(SW.WatchAndForceTables.PlcWatchTable table, WatchAndForceTableAccess access)
        {
            if (access == WatchAndForceTableAccess.None) throw new InvalidOperationException("WatchAndForceTableAccess.None is not allowed as a access level");
            if (Find(table) is not null) throw new InvalidOperationException("The watchtable already exists.");
            var rule = new WatchTableAccessRule(this, table, access);
            Items.Add(rule);
            return rule;
        }

        public IEnumerator<WatchTableAccessRule> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ForceTableAccessRule(ForceTableAccessRuleComposition owner, SW.WatchAndForceTables.PlcForceTable table, WatchAndForceTableAccess access)
    {
        public SW.WatchAndForceTables.PlcForceTable ForceTable { get; } = table;

        public WatchAndForceTableAccess Access { get; set; } = access;

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class ForceTableAccessRuleComposition : IEnumerable<ForceTableAccessRule>
    {
        public List<ForceTableAccessRule> Items { get; } = new();

        public ForceTableAccessRule? Find(SW.WatchAndForceTables.PlcForceTable table) => Items.FirstOrDefault(r => ReferenceEquals(r.ForceTable, table));

        public ForceTableAccessRule Create(SW.WatchAndForceTables.PlcForceTable table, WatchAndForceTableAccess access)
        {
            if (access == WatchAndForceTableAccess.None) throw new InvalidOperationException("WatchAndForceTableAccess.None is not allowed as a access level");
            var rule = new ForceTableAccessRule(this, table, access);
            Items.Add(rule);
            return rule;
        }

        public IEnumerator<ForceTableAccessRule> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    namespace CustomDataTypes
    {
        public sealed class StructuredData
        {
            public Dictionary<string, object?> Attributes { get; } = new(StringComparer.Ordinal);

            public object? GetAttribute(string name) => Attributes.TryGetValue(name, out var value) ? value : throw new InvalidOperationException($"No attribute '{name}'.");

            public void SetAttribute(string name, object value)
            {
                if (!Attributes.ContainsKey(name)) throw new InvalidOperationException($"No attribute '{name}'.");
                Attributes[name] = value;
            }
        }

        public sealed class TableData
        {
            public List<StructuredData> Rows { get; } = new();
        }
    }
}

namespace Siemens.Engineering.SW.WatchAndForceTables
{
    public sealed class PlcWatchTable(string name)
    {
        public string Name { get; } = name;
    }

    public sealed class PlcForceTable(string name)
    {
        public string Name { get; } = name;
    }
}

namespace Siemens.Engineering.SW.Blocks
{
    public sealed class FakeWebDb(string name)
    {
        public string Name { get; } = name;
    }
}

namespace Siemens.Engineering.HW.Features
{
    public enum WebDBGenerateOptions { None, Override }

    public sealed class PlcAccountLockingAtRuntimeFeature
    {
        public bool EnableLockUserAccountAtRunTime { get; set; } = true;

        public uint NumberOfFailedLoginAttempts { get; set; } = 5;

        public uint LoginRetryInterval { get; set; } = 2;

        public uint LockAccountForDuration { get; set; } = 2;
    }

    public sealed class SysLogServerConfiguration(SysLogServerConfigurationComposition owner, string address, ushort port)
    {
        public string SysLogServerAddress { get; } = address;

        public ushort SysLogServerPort { get; set; } = port;

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class SysLogServerConfigurationComposition : IEnumerable<SysLogServerConfiguration>
    {
        public List<SysLogServerConfiguration> Items { get; } = new();

        public SysLogServerConfiguration Create(string address, ushort port)
        {
            var server = new SysLogServerConfiguration(this, address, port);
            Items.Add(server);
            return server;
        }

        public SysLogServerConfiguration? Find(string address) => Items.FirstOrDefault(s => s.SysLogServerAddress == address);

        public IEnumerator<SysLogServerConfiguration> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SysLogConfigurationManager
    {
        public bool EnableSystemLogging { get; set; }

        public Siemens.Engineering.HW.SysLogTransportProtocolType TransportProtocol { get; set; } = Siemens.Engineering.HW.SysLogTransportProtocolType.None;

        public SysLogServerConfigurationComposition SysLogServerConfiguration { get; } = new();
    }

    public sealed class SystemWebPagesFeature
    {
        public bool EnableSystemWebPages { get; set; } = true;
    }

    public sealed class DefaultWebPagesFeature
    {
        public Siemens.Engineering.HW.WebApplicationConfigurationComposition WebApplicationConfigurations { get; } = new()
        {
            Items =
            {
                new("System web pages", Siemens.Engineering.HW.WebApplicationType.System_Inbuilt, true),
                new("MyApp", Siemens.Engineering.HW.WebApplicationType.UserDefined, false),
            },
        };
    }

    public sealed class WatchAndForceTableAccessManager
    {
        public Siemens.Engineering.HW.WatchTableAccessRuleComposition WatchtableAccessRules { get; } = new();

        public Siemens.Engineering.HW.ForceTableAccessRuleComposition ForcetableAccessRules { get; } = new();
    }

    public sealed class DisplayProtection
    {
        public int PasswordsSet { get; private set; }

        public void SetPassword(SecureString password)
        {
            if (password.Length == 0) throw new ArgumentException("The password is empty.");
            PasswordsSet++;
        }
    }

    public sealed class FrontPanelDisplay
    {
        public bool UserDefinedLogoActivated { get; set; }

        public bool AdaptLogoActivated { get; set; }

        public string? Logo { get; private set; }

        public void SetUserDefinedLogo(FileInfo logo) => Logo = logo.FullName;
    }

    public sealed class WebserverUserDefinedPages
    {
        public IList<Siemens.Engineering.SW.Blocks.FakeWebDb> GenerateBlocks(WebDBGenerateOptions options)
            => new List<Siemens.Engineering.SW.Blocks.FakeWebDb> { new("WWW_Fragment_1") };

        public IList<Siemens.Engineering.SW.Blocks.FakeWebDb> GenerateBlocks(DirectoryInfo html, FileInfo defaultPage, string application, WebDBGenerateOptions options)
            => new List<Siemens.Engineering.SW.Blocks.FakeWebDb> { new("WWW_" + application), new("WWW_Fragment_" + application) };
    }
}

namespace Siemens.Engineering.Security
{
    public sealed class PlcPasswordPolicyService
    {
        public bool PasswordPolicyEnabled { get; set; } = true;
    }

    public sealed class LegacyPlcPasswordPolicyService
    {
        public bool PasswordPolicyEnabled { get; set; }

        public short MinimumLength { get; set; } = 5;

        public short MinimumNumericCharacterLength { get; set; }

        public short MinimumSpecialCharacterLength { get; set; }

        public bool IncludesLowerCaseAndUpperCaseCharacters { get; set; }
    }

    public sealed class SyslogServer(SyslogServerComposition owner, string name)
    {
        public string Name { get; } = name;

        public string Address { get; set; } = string.Empty;

        public ushort Port { get; set; } = 514;

        public bool Tls { get; set; }

        public string Comment { get; set; } = string.Empty;

        public List<Siemens.Engineering.HW.FakeDeviceItem> AssignedModules { get; } = new();

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class SyslogServerComposition : IEnumerable<SyslogServer>
    {
        public List<SyslogServer> Items { get; } = new();

        public SyslogServer Create(string name)
        {
            var server = new SyslogServer(this, name);
            Items.Add(server);
            return server;
        }

        public SyslogServer? Find(string name) => Items.FirstOrDefault(s => s.Name == name);

        public IEnumerator<SyslogServer> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class SyslogServerProvider
    {
        public SyslogServerComposition Servers { get; } = new();
    }
}
