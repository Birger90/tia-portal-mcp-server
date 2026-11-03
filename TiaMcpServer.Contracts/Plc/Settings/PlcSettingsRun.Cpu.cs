using System.Reflection;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using M = TiaMcpServer.Contracts.Plc.Settings.PlcCpuSettingsMethods;
using N = TiaMcpServer.Contracts.Plc.Settings.PlcCpuSettingsNames;

namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>Issue #15: further CPU settings (see <see cref="PlcCpuSettingsMethods"/> for the evidence).</summary>
public static partial class PlcSettingsRun
{
    private static List<PlcSettingsSectionInfo> CpuSections(PlcSettingsContext context)
    {
        var sections = new List<PlcSettingsSectionInfo>
        {
            Section(context, "accountLocking", context.CpuOwners(), N.AccountLocking, N.AccountLockingProperties,
                note: "The manual: the values are downloaded only when local user management is activated in Access Control (S7-1500 V4.1 and later)."),
            Section(context, "syslog", context.CpuOwners(), N.SyslogManager, N.SyslogProperties, service => new List<PlcSettingsCollectionInfo>
            {
                Collection(context, "SysLogServerConfiguration", () => Get(service, "SysLogServerConfiguration"), "SysLogServerAddress", new[] { "SysLogServerAddress", "SysLogServerPort" }),
            }),
            AttributeSection(context, "syslogAttributes", N.SyslogAttributes),
            Section(context, "systemWebPages", context.CpuOwners(), N.SystemWebPages, new[] { "EnableSystemWebPages" }),
            Section(context, "webApplications", context.CpuOwners(), N.DefaultWebPages, Array.Empty<string>(), service => new List<PlcSettingsCollectionInfo>
            {
                Collection(context, "WebApplicationConfigurations", () => Get(service, "WebApplicationConfigurations"), "Name", new[] { "Name", "IsDefault", "ApplicationType" }),
            }),
            TableAccessSection(context, "watchAndForceTableAccess", context.CpuOwners()),
            TableAccessSection(context, "displayWatchAndForceTableAccess", context.CpuItems),
            Section(context, "displayProtection", context.CpuAndItems(), N.DisplayProtection, Array.Empty<string>(),
                members: service => Bindable(service, ("SetPassword", p => p.Length == 1 && IsSecureString(p[0]), "SetPassword(SecureString)")),
                note: "The display password cannot be read; set it with set_display_password. Software controllers have a display without password protection (manual)."),
            Section(context, "frontPanelDisplay", context.CpuAndItems(), N.FrontPanelDisplay, N.FrontPanelProperties,
                members: service => Bindable(service, ("SetUserDefinedLogo", p => p.Length == 1 && p[0].ParameterType == typeof(FileInfo), "SetUserDefinedLogo(FileInfo)"))),
            Section(context, "userDefinedWebPages", context.CpuOwners(), N.UserDefinedPages, Array.Empty<string>(),
                members: service => Bindable(service,
                    ("GenerateBlocks", p => p.Length == 1 && p[0].ParameterType.IsEnum, "GenerateBlocks(WebDBGenerateOptions)"),
                    ("GenerateBlocks", p => p.Length == 4 && p[0].ParameterType == typeof(DirectoryInfo), "GenerateBlocks(DirectoryInfo, FileInfo, String, WebDBGenerateOptions)"))),
            MultilingualSection(context),
            AttributeSection(context, "ipAccessibility", new[] { N.IpAccessibility }, value => value is null ? null : context.NameOf(value) ?? value.ToString()),
        };
        sections.AddRange(MoreCpuSections(context));
        return sections;
    }

    private static List<PlcSettingsSectionInfo> ProjectSections(PlcSettingsContext context)
    {
        var owners = context.Project is null ? Array.Empty<object>() : new[] { context.Project };
        var sections = new List<PlcSettingsSectionInfo>
        {
            Section(context, "plcPasswordPolicy", owners, N.PasswordPolicy, new[] { "PasswordPolicyEnabled" }),
            Section(context, "legacyPlcPasswordPolicy", owners, N.LegacyPasswordPolicy, N.LegacyPolicyProperties),
            Section(context, "projectSyslogServers", owners, N.ProjectSyslog, Array.Empty<string>(), service => new List<PlcSettingsCollectionInfo>
            {
                Collection(context, "Servers", () => Get(service, "Servers"), "Name", new[] { "Name", "Address", "Port", "Tls", "Comment", "AssignedModules" },
                    special: (server, property) => property == "AssignedModules" ? Names(context, server, property) : null),
            }),
        };
        return sections;
    }

    /// <summary>The names of the objects a collection property holds, as one text.</summary>
    private static RuntimeValueInfo Names(PlcSettingsContext context, object owner, string property)
    {
        try
        {
            var names = Enumerate(Get(owner, property)).Select(e => context.NameOf(e) ?? e?.ToString() ?? "(null)").ToList();
            return RuntimeServiceValues.Project(string.Join(", ", names));
        }
        catch (RuntimeBindingException exception)
        {
            return new RuntimeValueInfo { State = RuntimeValueStates.Missing, Error = exception.Message };
        }
        catch (Exception exception) when (context.Recoverable(exception))
        {
            return new RuntimeValueInfo { State = RuntimeValueStates.Unreadable, Error = RuntimeServiceValues.Bound($"{exception.GetType().Name}: {exception.Message}") };
        }
    }

    private static PlcSettingsSectionInfo TableAccessSection(PlcSettingsContext context, string name, IEnumerable<object> owners)
        => Section(context, name, owners, N.WatchAndForceTables, Array.Empty<string>(), service => new List<PlcSettingsCollectionInfo>
        {
            Collection(context, "WatchtableAccessRules", () => Get(service, "WatchtableAccessRules"), "WatchTable", new[] { "WatchTable", "Access" },
                special: (rule, property) => property == "WatchTable" ? RuntimeServiceValues.Project(context.NameOf(Get(rule, "WatchTable"))) : null),
            Collection(context, "ForcetableAccessRules", () => Get(service, "ForcetableAccessRules"), "ForceTable", new[] { "ForceTable", "Access" },
                special: (rule, property) => property == "ForceTable" ? RuntimeServiceValues.Project(context.NameOf(Get(rule, "ForceTable"))) : null),
        });

    private static PlcSettingsSectionInfo AttributeSection(PlcSettingsContext context, string name, IReadOnlyList<string> attributes, Func<object?, object?>? project = null)
    {
        var section = new PlcSettingsSectionInfo { Name = name, Offered = context.Cpu is not null };
        if (context.Cpu is null) return section;
        section.Owner = context.Owner(context.Cpu);
        foreach (var attribute in attributes)
            section.Properties.Add(new PlcSettingPropertyInfo { Name = attribute, Value = ReadAttribute(context, context.Cpu, attribute, project) });
        section.Note = "Device item attributes (GetAttribute); a CPU that does not support one reports it as missing or unreadable.";
        return section;
    }

    private static PlcSettingsSectionInfo MultilingualSection(PlcSettingsContext context)
    {
        var section = new PlcSettingsSectionInfo { Name = "multilingualSupport", Offered = context.Cpu is not null };
        if (context.Cpu is null) return section;
        section.Owner = context.Owner(context.Cpu);
        var rows = new PlcSettingsCollectionInfo { Name = "Rows" };
        try
        {
            var table = GetAttribute(context.Cpu, N.MultilingualSupport);
            var index = 0;
            foreach (var row in Elements(table is null ? null : Get(table, "Rows"), "TableData.Rows"))
            {
                index++;
                if (row is null) continue;
                rows.Items.Add(new PlcSettingsItemInfo
                {
                    Key = Text(index),
                    Properties =
                    {
                        new PlcSettingPropertyInfo { Name = N.ProjectLanguage, Value = ReadAttribute(context, row, N.ProjectLanguage) },
                        new PlcSettingPropertyInfo { Name = N.DisplayLanguage, Value = ReadAttribute(context, row, N.DisplayLanguage) },
                    },
                });
            }
        }
        catch (RuntimeBindingException exception)
        {
            rows.Error = exception.Message;
        }
        catch (Exception exception) when (context.Recoverable(exception))
        {
            rows.Error = RuntimeServiceValues.Bound($"{exception.GetType().Name}: {exception.Message}");
        }

        section.Collections.Add(rows);
        section.Note = "The CPU attribute MultilingualSupport (TableData): one row per web server/display language; set a row's project language with set_plc_project_language (position = the row key).";
        return section;
    }

    private static bool WriteCpu(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info,
        PlcSettingsResultInfo result, List<string> warnings)
    {
        switch (method)
        {
            case M.SetAccountLocking:
            case M.SetSystemWebPages:
            {
                var (service, owner) = RequireService(context, context.CpuOwners(), definition.Service!, CpuWhere(context));
                Describe(context, result, owner, definition.Service);
                SetProperties(context, service, ShortName(definition.Service!), info.Settings, definition.Settings, result);
                if (method == M.SetAccountLocking)
                    warnings.Add("The account locking values are downloaded only when local user management is activated in Access Control (manual); they take effect after a download.");
                return true;
            }

            case M.SetPlcPasswordPolicy:
            case M.SetLegacyPlcPasswordPolicy:
            {
                var project = RequireProject(context);
                var (service, owner) = RequireService(context, new[] { project }, definition.Service!, "The project");
                Describe(context, result, owner, definition.Service);
                SetProperties(context, service, ShortName(definition.Service!), info.Settings, definition.Settings, result);
                return true;
            }

            case M.SetSyslog:
            {
                var properties = info.Settings.Where(s => N.SyslogProperties.Contains(s.Name)).ToList();
                var attributes = info.Settings.Where(s => N.SyslogAttributes.Contains(s.Name)).ToList();
                if (properties.Count > 0)
                {
                    var (service, owner) = RequireService(context, context.CpuOwners(), definition.Service!, CpuWhere(context));
                    Describe(context, result, owner, definition.Service);
                    SetProperties(context, service, ShortName(definition.Service!), properties, N.SyslogProperties, result);
                }
                else
                {
                    Describe(context, result, RequireCpu(context), null);
                }

                if (attributes.Count > 0) SetAttributes(context, RequireCpu(context), "CPU", attributes, N.SyslogAttributes, result);
                return true;
            }

            case M.AddSyslogServer:
            case M.RemoveSyslogServer:
            {
                var (service, owner) = RequireService(context, context.CpuOwners(), definition.Service!, CpuWhere(context));
                Describe(context, result, owner, definition.Service);
                var servers = Get(service, "SysLogServerConfiguration")
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, "SysLogConfigurationManager.SysLogServerConfiguration returned null. Nothing was changed.");
                var address = info.Address ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "address is required. Nothing was changed.");
                result.Target = address;
                const string FindDoc = "SysLogServerConfigurationComposition.Find(String)";
                if (method == M.AddSyslogServer)
                {
                    var port = info.Port ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "port is required. Nothing was changed.");
                    if (port is < 0 or > ushort.MaxValue)
                        throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "port must be 0 to 65535 (UInt16). Nothing was changed.");
                    var create = RequireMethod(servers, "Create", new[] { typeof(string), typeof(ushort) }, "SysLogServerConfigurationComposition.Create(String, UInt16)");
                    Call(create, servers, address, (ushort)port);
                    result.Calls.Add($"SysLogServerConfiguration.Create(\"{address}\", {Text(port)})");
                    var created = Find(servers, typeof(string), address, FindDoc);
                    Exists(result, "server", true, created is not null, $"Syslog server '{address}'", context.PlcName);
                    Compare(context, result, "SysLogServerPort", Text(port), null, ReadValue(context, created!, "SysLogServerPort"), "SysLogServerConfiguration");
                }
                else
                {
                    var server = Find(servers, typeof(string), address, FindDoc)
                        ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"PLC '{context.PlcName}' has no syslog server '{address}'. Nothing was changed.");
                    Delete(server, "SysLogServerConfiguration.Delete()", result);
                    Exists(result, "server", false, Find(servers, typeof(string), address, FindDoc) is not null, $"Syslog server '{address}'", context.PlcName);
                }

                return true;
            }

            case M.CreateProjectSyslogServer:
            case M.UpdateProjectSyslogServer:
            case M.DeleteProjectSyslogServer:
            {
                var project = RequireProject(context);
                var (service, owner) = RequireService(context, new[] { project }, definition.Service!, "The project");
                Describe(context, result, owner, definition.Service);
                var servers = Get(service, "Servers")
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, "SyslogServerProvider.Servers returned null. Nothing was changed.");
                var name = info.Name ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "name is required. Nothing was changed.");
                result.Target = name;
                const string FindDoc = "SyslogServerComposition.Find(String)";
                var existing = Find(servers, typeof(string), name, FindDoc);
                if (method == M.CreateProjectSyslogServer)
                {
                    var create = RequireMethod(servers, "Create", new[] { typeof(string) }, "SyslogServerComposition.Create(String)");
                    // Bind every setting on the created type's members only after Create; a refusal then leaves the new server as created.
                    var created = Call(create, servers, name)
                        ?? throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed, $"SyslogServerComposition.Create(\"{name}\") returned null.");
                    result.Calls.Add($"SyslogServerComposition.Create(\"{name}\")");
                    Exists(result, "server", true, Find(servers, typeof(string), name, FindDoc) is not null, $"Project syslog server '{name}'", null);
                    if (info.Settings.Count > 0) SetProperties(context, created, "SyslogServer", info.Settings, definition.Settings, result);
                }
                else if (existing is null)
                {
                    throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"The project has no syslog server '{name}'. Nothing was changed.");
                }
                else if (method == M.UpdateProjectSyslogServer)
                {
                    SetProperties(context, existing, "SyslogServer", info.Settings, definition.Settings, result);
                }
                else
                {
                    Delete(existing, "SyslogServer.Delete()", result);
                    Exists(result, "server", false, Find(servers, typeof(string), name, FindDoc) is not null, $"Project syslog server '{name}'", null);
                }

                return true;
            }

            case M.SetDefaultWebApplication:
            {
                var (service, owner) = RequireService(context, context.CpuOwners(), definition.Service!, CpuWhere(context));
                Describe(context, result, owner, definition.Service);
                var name = info.Name ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "name is required. Nothing was changed.");
                result.Target = name;
                var apps = Get(service, "WebApplicationConfigurations")
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, "DefaultWebPagesFeature.WebApplicationConfigurations returned null. Nothing was changed.");
                var app = Find(apps, typeof(string), name, "WebApplicationConfigurationComposition.Find(String)")
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"PLC '{context.PlcName}' has no web application '{name}'. Nothing was changed.");
                SetProperties(context, app, "WebApplicationConfiguration", new[] { new PlcSettingValueInfo { Name = "IsDefault", Value = "true" } }, new[] { "IsDefault" }, result);
                return true;
            }

            case M.SetWatchTableAccess:
            case M.RemoveWatchTableAccess:
            case M.SetForceTableAccess:
            case M.RemoveForceTableAccess:
                TableAccess(method, definition, context, info, result);
                return true;

            case M.SetDisplayPassword:
            {
                var (service, owner) = RequireService(context, context.CpuAndItems(), definition.Service!, CpuWhere(context) + " and its device items");
                Describe(context, result, owner, definition.Service);
                var setPassword = RequireMethodByShape(service, "SetPassword", p => p.Length == 1 && IsSecureString(p[0]), "SetPassword(SecureString)");
                WithPassword(context, info.PasswordVariable, "DisplayProtection", result, secure => Call(setPassword, service, secure));
                result.Calls.Add("DisplayProtection.SetPassword(SecureString)");
                warnings.Add("The display password cannot be read back; it takes effect on the CPU after a download. The hardware compile (compile_check scope hardware) no longer warns about a display without password protection when it is set.");
                return true;
            }

            case M.SetFrontPanelDisplay:
            {
                var (service, owner) = RequireService(context, context.CpuAndItems(), definition.Service!, CpuWhere(context) + " and its device items");
                Describe(context, result, owner, definition.Service);
                if (info.FilePath is not null)
                {
                    RequireExistingFile(info.FilePath, "filePath");
                    var setLogo = RequireMethodByShape(service, "SetUserDefinedLogo", p => p.Length == 1 && p[0].ParameterType == typeof(FileInfo), "SetUserDefinedLogo(FileInfo)");
                    Call(setLogo, service, new FileInfo(info.FilePath));
                    result.Calls.Add("FrontPanelDisplay.SetUserDefinedLogo(FileInfo)");
                    result.Items.Add(info.FilePath);
                }

                if (info.Settings.Count > 0) SetProperties(context, service, "FrontPanelDisplay", info.Settings, definition.Settings, result);
                return true;
            }

            case M.SetPlcProjectLanguage:
            {
                var cpu = RequireCpu(context);
                Describe(context, result, cpu, null);
                var position = info.Position ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "position is required. Nothing was changed.");
                var culture = info.Culture ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "culture is required. Nothing was changed.");
                var language = context.ActiveCulture(culture)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"'{culture}' is no active project language (LanguageSettings.ActiveLanguages). Nothing was changed.");
                var table = GetAttribute(cpu, N.MultilingualSupport)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability, $"The CPU of PLC '{context.PlcName}' returns no {N.MultilingualSupport} attribute. Nothing was changed.");
                var rows = Elements(Get(table, "Rows"), "TableData.Rows");
                if (position < 1 || position > rows.Count || rows[position - 1] is not { } row)
                    throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound,
                        $"The {N.MultilingualSupport} table of PLC '{context.PlcName}' has {Text(rows.Count)} row(s); position {Text(position)} is not one. Nothing was changed.");
                result.Target = $"row {Text(position)}";
                var before = ReadAttribute(context, row, N.ProjectLanguage);
                SetAttribute(row, N.ProjectLanguage, language);
                result.Calls.Add($"MultilingualSupport.Rows[{Text(position - 1)}].SetAttribute(\"ProjectLanguage\", Language.Culture)");
                Compare(context, result, N.ProjectLanguage, RuntimeServiceValues.Project(language).Value, before, ReadAttribute(context, row, N.ProjectLanguage), "MultilingualSupport row");
                return true;
            }

            case M.SetIpAccessibility:
            {
                var cpu = RequireCpu(context);
                Describe(context, result, cpu, null);
                var name = info.ModuleName ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "moduleName is required. Nothing was changed.");
                var module = context.DeviceItem(name)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"The device of PLC '{context.PlcName}' has no device item named '{name}'. Nothing was changed.");
                result.Target = name;
                var before = ReadAttribute(context, cpu, N.IpAccessibility, value => value is null ? null : context.NameOf(value));
                SetAttribute(cpu, N.IpAccessibility, module);
                result.Calls.Add("CPU.SetAttribute(\"PlcAccessCommunicationModule\", DeviceItem)");
                var after = GetAttribute(cpu, N.IpAccessibility);
                var same = after is not null && after.Equals(module);
                result.Changes.Add(new PlcSettingChangeInfo
                {
                    Name = N.IpAccessibility, Requested = name, Before = before, After = RuntimeServiceValues.Project(after is null ? null : context.NameOf(after)), Matched = same,
                });
                if (!same)
                    throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                        $"PlcAccessCommunicationModule of PLC '{context.PlcName}' does not read back as '{name}' after the call. Inspect it with plc_read get_cpu_settings.");
                return true;
            }

            case M.GenerateWebBlocks:
            {
                var (service, owner) = RequireService(context, context.CpuOwners(), definition.Service!, CpuWhere(context));
                Describe(context, result, owner, definition.Service);
                MethodInfo generate;
                object?[] arguments;
                if (info.DirectoryPath is not null)
                {
                    if (!Directory.Exists(info.DirectoryPath))
                        throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, "directoryPath must name an existing HTML directory. Nothing was changed.");
                    RequireExistingFile(info.FilePath, "filePath (the default HTML page)");
                    generate = RequireMethodByShape(service, "GenerateBlocks", p => p.Length == 4 && p[0].ParameterType == typeof(DirectoryInfo)
                        && p[1].ParameterType == typeof(FileInfo) && p[2].ParameterType == typeof(string) && p[3].ParameterType.IsEnum,
                        "GenerateBlocks(DirectoryInfo, FileInfo, String, WebDBGenerateOptions)");
                    arguments = new object?[] { new DirectoryInfo(info.DirectoryPath), new FileInfo(info.FilePath!), info.Name ?? string.Empty, null };
                }
                else
                {
                    generate = RequireMethodByShape(service, "GenerateBlocks", p => p.Length == 1 && p[0].ParameterType.IsEnum, "GenerateBlocks(WebDBGenerateOptions)");
                    arguments = new object?[] { null };
                }

                var optionType = generate.GetParameters().Last().ParameterType;
                var option = info.Overwrite ? "Override" : "None";
                arguments[arguments.Length - 1] = Library.LibraryRuntimeBinding.EnumValue(optionType, option)
                    ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                        $"{optionType.FullName} has no member '{option}' (members: {Library.LibraryRuntimeBinding.MemberList(optionType)}). Nothing was changed.");
                var blocks = Call(generate, service, arguments);
                result.Calls.Add($"WebserverUserDefinedPages.{Signature(generate)} [{option}]");
                foreach (var block in Enumerate(blocks)) result.Items.Add(context.NameOf(block) ?? "(unnamed)");
                warnings.Add($"GenerateBlocks returned {Text(result.Items.Count)} block(s); compile the PLC (compile_check) before a download.");
                return true;
            }

            default:
                return false;
        }
    }

    private static void TableAccess(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info, PlcSettingsResultInfo result)
    {
        var force = method is M.SetForceTableAccess or M.RemoveForceTableAccess;
        var owners = info.Display ? context.CpuItems : context.CpuOwners();
        var (service, owner) = RequireService(context, owners, definition.Service!, info.Display ? $"No device item below the CPU of PLC '{context.PlcName}'" : CpuWhere(context));
        Describe(context, result, owner, definition.Service);
        var tableName = info.TableName ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "tableName is required. Nothing was changed.");
        var table = context.Table(tableName, info.FolderPath, force);
        result.Target = $"{(force ? "force" : "watch")} table '{tableName}'";
        var rules = Get(service, force ? "ForcetableAccessRules" : "WatchtableAccessRules")
            ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, "The access rule composition is null. Nothing was changed.");
        var ruleType = force ? "ForceTableAccessRule" : "WatchTableAccessRule";
        var findDoc = force ? "ForceTableAccessRuleComposition.Find(PlcForceTable)" : "WatchTableAccessRuleComposition.Find(PlcWatchTable)";
        var find = RequireMethodByShape(rules, "Find", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(table), findDoc);
        var existing = Call(find, rules, table);
        if (method is M.RemoveWatchTableAccess or M.RemoveForceTableAccess)
        {
            if (existing is null)
                throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"{result.Target} has no access rule at {result.Owner}. Nothing was changed.");
            Delete(existing, $"{ruleType}.Delete()", result);
            Exists(result, "accessRule", false, Call(find, rules, table) is not null, $"The access rule of {result.Target}", context.PlcName);
            return;
        }

        var access = info.Access ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "access is required. Nothing was changed.");
        if (existing is not null)
        {
            SetProperties(context, existing, ruleType, new[] { new PlcSettingValueInfo { Name = "Access", Value = access } }, new[] { "Access" }, result);
            return;
        }

        var create = RequireMethodByShape(rules, "Create", p => p.Length == 2 && p[0].ParameterType.IsInstanceOfType(table) && p[1].ParameterType.IsEnum,
            force ? "Create(PlcForceTable, WatchAndForceTableAccess)" : "Create(PlcWatchTable, WatchAndForceTableAccess)");
        var accessValue = ConvertValue(create.GetParameters()[1].ParameterType, access, "access");
        var created = Call(create, rules, table, accessValue);
        result.Calls.Add($"{ruleType}Composition.Create({(force ? "PlcForceTable" : "PlcWatchTable")}, WatchAndForceTableAccess.{access})");
        var found = Call(find, rules, table);
        Exists(result, "accessRule", true, found is not null, $"The access rule of {result.Target}", context.PlcName);
        Compare(context, result, "Access", access, null, ReadValue(context, found ?? created!, "Access"), ruleType);
    }

    internal static object RequireCpu(PlcSettingsContext context)
        => context.Cpu ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed,
            $"The CPU device item of PLC '{context.PlcName}' is unknown. Nothing was changed.");

    internal static void Describe(PlcSettingsContext context, PlcSettingsResultInfo result, object owner, string? service)
    {
        result.Owner = context.Owner(owner);
        result.Service = service;
    }

    internal static string ShortName(string fullName) => fullName.Substring(fullName.LastIndexOf('.') + 1);
}
