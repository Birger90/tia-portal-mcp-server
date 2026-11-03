namespace TiaMcpServer.Contracts.Plc.Safety;

/// <summary>
/// Every Siemens member the fail-safe operations touch, with its evidence. No member is compiled: the worker has no
/// reference to <c>Siemens.Engineering.Safety.dll</c> (it is part of the separately installed STEP 7 Safety option and
/// may be absent), so every type is found by its exact full name among the services an object lists
/// (<c>IEngineeringServiceProvider.GetServiceInfos()</c>) and every member is bound by exact name and argument types
/// (<c>LibraryRuntimeBinding</c>); a member that is absent is reported as <c>api_member_missing</c>, never guessed.
/// <para>
/// Evidence kinds: <see cref="SiemensSkill"/> is the official Siemens repository <c>tia-portal-ai-extensions</c>
/// (openness_development/skills); <see cref="V21Baseline"/> is the type and member list extracted from an installed
/// V21 <c>Siemens.Engineering.Safety.dll</c> and its XML documentation by the third-party repository
/// Czarnak/totally-integrated-claude (api-baselines/v21/safety.json, skills/tia-plc-operations/references), which is
/// not a Siemens publication; <see cref="ThirdPartyUse"/> is a third-party MIT project compiled against V21
/// (asckye/TIA_Portal_Openness_MCP, SafetyManagementAdapter.cs, DownloadPromptPolicy.cs), which shows return kinds and
/// enum members only. A member backed only by the last two is unverified against a Siemens primary source.
/// </para>
/// </summary>
public static class PlcSafetyApiCatalog
{
    public const string SiemensSkill = "siemens-skill";
    public const string V21Baseline = "v21-installed-api-baseline (third-party extraction)";
    public const string ThirdPartyUse = "third-party-use (MIT, compiled against V21)";

    public const string SafetyNamespace = "Siemens.Engineering.Safety";
    public const string SafetyAdministrationType = SafetyNamespace + ".SafetyAdministration";
    public const string GlobalSettingsType = SafetyNamespace + ".GlobalSettings";
    public const string SafetySignatureProviderType = SafetyNamespace + ".SafetySignatureProvider";
    public const string SafetyPrintoutType = SafetyNamespace + ".SafetyPrintout";
    public const string SafetyBaseIdProviderType = SafetyNamespace + ".SafetyBaseIdProvider";

    /// <summary>The F-program download configuration (V21 baseline); answered only by an explicit caller decision.</summary>
    public const string SafetyProgramConfigurationType = SafetyNamespace + ".Download.Configurations.SafetyProgram";

    /// <summary>Its decision key in plc_download <c>decisions</c> (the type's short name, like every download configuration).</summary>
    public const string SafetyProgramDecisionKey = "SafetyProgram";

    // SafetyAdministration members.
    public const string Settings = "Settings";
    public const string RuntimeGroups = "RuntimeGroups";
    public const string ProgramSignatures = "ProgramSignatures";
    public const string IsPasswordSet = "IsSafetyOfflineProgramPasswordSet";
    public const string IsLoggedOn = "IsLoggedOnToSafetyOfflineProgram";
    public const string Login = "LoginToSafetyOfflineProgram";
    public const string Logoff = "LogoffFromSafetyOfflineProgram";
    public const string SetPassword = "SetSafetyOfflineProgramPassword";
    public const string RevokePassword = "RevokeSafetyOfflineProgramPassword";

    // SafetySettings members.
    public const string SafetySystemVersion = "SafetySystemVersion";
    public const string GetApplicableSafetySystemVersions = "GetApplicableSafetySystemVersions";
    public const string AssignmentOfBlockNumbers = "AssignmentOfBlockNumbers";

    public static IReadOnlyList<string> SettingsValues { get; } = Array.AsReadOnly(new[]
    {
        SafetySystemVersion, "ActivationOfFChangeHistory", "CreateDriverInstanceDataBlocksWithoutPrefix", "SafetyModeCanBeDisabled",
    });

    public static IReadOnlyList<string> BlockNumberValues { get; } = Array.AsReadOnly(new[]
    {
        "ManagementMode", "FromDB", "ToDB", "FromFB", "ToFB", "FromFC", "ToFC",
    });

    /// <summary>RuntimeGroup members read as properties; the first two are also documented as GetAttribute names.</summary>
    public static IReadOnlyList<string> RuntimeGroupValues { get; } = Array.AsReadOnly(new[]
    {
        "MainSafetyBlockName", "MainSafetyBlockIDbName", "FOBName", "FOBEventClass", "InfoDbName",
        "MaximumCycleTime", "WarnCycleTime", "PreProcessingName", "PostProcessingName",
    });

    /// <summary>GlobalSettings getters: parameterless methods (the setters are one-argument overloads, never called here).</summary>
    public static IReadOnlyList<string> GlobalSettingsGetters { get; } = Array.AsReadOnly(new[]
    {
        "SafetyModificationsPossible", "UsernameForFChangeHistory", "GenerationOfDefaultFailsafeProgram", "ManagementOfFailsafeInSoftwareUnitsEnvironment",
    });

    public sealed class Member
    {
        public Member(string declaringType, string name, string kind, string signature, string evidence, string source)
        {
            DeclaringType = declaringType;
            Name = name;
            Kind = kind;
            Signature = signature;
            Evidence = evidence;
            Source = source;
        }

        public string DeclaringType { get; }

        public string Name { get; }

        /// <summary><c>service</c>, <c>property</c>, <c>method</c>, <c>attribute</c> or <c>type</c>.</summary>
        public string Kind { get; }

        public string Signature { get; }

        public string Evidence { get; }

        public string Source { get; }

        public bool SiemensPrimary => Evidence == SiemensSkill;

        public string Display => $"{DeclaringType}.{Name}";
    }

    private const string Skill = "tia-portal-ai-extensions/openness_development/skills/";
    private const string Baseline = "Czarnak/totally-integrated-claude api-baselines/v21/safety.json, skills/tia-plc-operations/references/safety-administration.md";
    private const string Asckye = "asckye/TIA_Portal_Openness_MCP src/Adapters/Native/Safety/SafetyManagementAdapter.cs";

    /// <summary>The members, in the order the docs list them. Every one is reflection-bound.</summary>
    public static IReadOnlyList<Member> Members { get; } = Array.AsReadOnly(new[]
    {
        new Member("Siemens.Engineering.HW.DeviceItem", "GetService<SafetyAdministration>()", "service", "SafetyAdministration (service of the CPU device item that carries SoftwareContainer)", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §2"),
        new Member(SafetyAdministrationType, Settings, "property", "SafetySettings Settings { get; }", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §1, §3"),
        new Member(SafetyAdministrationType, RuntimeGroups, "property", "RuntimeGroupComposition RuntimeGroups { get; }", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §5; libraries-and-alarms/SKILL.md"),
        new Member(SafetyAdministrationType, ProgramSignatures, "property", "SafetySignatureProvider ProgramSignatures { get; }", V21Baseline, Baseline),
        new Member(SafetyAdministrationType, IsPasswordSet, "property", "bool IsSafetyOfflineProgramPasswordSet { get; }", V21Baseline, Baseline),
        new Member(SafetyAdministrationType, IsLoggedOn, "property", "bool IsLoggedOnToSafetyOfflineProgram { get; }", V21Baseline, Baseline),
        new Member(SafetyAdministrationType, Login, "method", "void LoginToSafetyOfflineProgram(System.Security.SecureString)", V21Baseline, Baseline + "; " + Asckye),
        new Member(SafetyAdministrationType, Logoff, "method", "void LogoffFromSafetyOfflineProgram()", V21Baseline, Baseline + "; " + Asckye),
        new Member(SafetyAdministrationType, SetPassword, "method", "void SetSafetyOfflineProgramPassword(System.Security.SecureString)", V21Baseline, Baseline + "; " + Asckye),
        new Member(SafetyAdministrationType, RevokePassword, "method", "void RevokeSafetyOfflineProgramPassword(System.Security.SecureString)", V21Baseline, Baseline + "; " + Asckye),
        new Member(SafetyNamespace + ".SafetySettings", SafetySystemVersion, "property", "SafetySystemVersion SafetySystemVersion { get; }", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §1, §3"),
        new Member(SafetyNamespace + ".SafetySettings", GetApplicableSafetySystemVersions, "method", "IList<SafetySystemVersion> GetApplicableSafetySystemVersions()", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §3"),
        new Member(SafetyNamespace + ".SafetySettings", AssignmentOfBlockNumbers, "property", "AssignmentOfBlockNumbers AssignmentOfBlockNumbers { get; }", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §4"),
        new Member(SafetyNamespace + ".SafetySettings", "ActivationOfFChangeHistory", "property", "bool ActivationOfFChangeHistory { get; }", V21Baseline, Baseline),
        new Member(SafetyNamespace + ".SafetySettings", "CreateDriverInstanceDataBlocksWithoutPrefix", "property", "bool CreateDriverInstanceDataBlocksWithoutPrefix { get; }", V21Baseline, Baseline),
        new Member(SafetyNamespace + ".SafetySettings", "SafetyModeCanBeDisabled", "property", "bool SafetyModeCanBeDisabled { get; }", V21Baseline, Baseline),
        new Member(SafetyNamespace + ".SafetySystemVersion", "ToString()", "method", "string ToString() (e.g. \"V2.4\")", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §3"),
        new Member(SafetyNamespace + ".AssignmentOfBlockNumbers", "ManagementMode, FromDB, ToDB, FromFB, ToFB, FromFC, ToFC", "property", "BlockNumbersManagementMode ManagementMode; ushort From*/To*", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §1, §4"),
        new Member(SafetyNamespace + ".RuntimeGroupComposition", "GetEnumerator()", "method", "IEnumerator<RuntimeGroup>", SiemensSkill, Skill + "libraries-and-alarms/SKILL.md (RuntimeGroups.First())"),
        new Member(SafetyNamespace + ".RuntimeGroupComposition", "Find", "method", "RuntimeGroup Find(string)", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §5"),
        new Member(SafetyNamespace + ".RuntimeGroupComposition", "Create", "method", "RuntimeGroup Create(string, PlcBlock, PlcBlock)", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §5"),
        new Member(SafetyNamespace + ".RuntimeGroupComposition", "Create", "method", "RuntimeGroup Create(string, PlcBlock)", V21Baseline, Baseline),
        new Member(SafetyNamespace + ".RuntimeGroupComposition", "Create", "method", "RuntimeGroup Create(string)", V21Baseline, Baseline),
        new Member(SafetyNamespace + ".RuntimeGroup", "Name", "property", "string Name { get; }", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §1"),
        new Member(SafetyNamespace + ".RuntimeGroup", "MainSafetyBlockName, MainSafetyBlockIDbName", "attribute", "IEngineeringObject.GetAttribute(name)", SiemensSkill, Skill + "libraries-and-alarms/SKILL.md; engineering-objects/SKILL.md"),
        new Member(SafetyNamespace + ".RuntimeGroup", "FOBName, FOBEventClass, InfoDbName, MaximumCycleTime, WarnCycleTime, PreProcessingName, PostProcessingName", "property", "read-only properties", V21Baseline, Baseline),
        new Member(SafetyNamespace + ".RuntimeGroup", "MainSafetyBlock, MainSafetyBlockIDB", "property", "PlcBlock MainSafetyBlock; PlcBlock MainSafetyBlockIDB (object model; writability not documented: bound at runtime, a writable property first)", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §1"),
        new Member(SafetyNamespace + ".RuntimeGroup", "SetAttribute", "attribute", "IEngineeringObject.SetAttribute(\"MainSafetyBlock\" | \"MainSafetyBlockIDB\", PlcBlock) when GetAttributeInfos() lists it writable (the attribute path is not documented)", SiemensSkill, Skill + "plc-safety-administration/SKILL.md §5 (\"Call SetAttribute directly on the RuntimeGroup object\"); engineering-objects/SKILL.md"),
        new Member("Siemens.Engineering.SW.Blocks.PlcBlock", "GetService<SafetySignatureProvider>()", "service", "SafetySignatureProvider (F-signatures of a safety program block)", SiemensSkill, Skill + "change-detection/SKILL.md"),
        new Member(SafetySignatureProviderType, "Signatures", "property", "SafetySignatureComposition Signatures { get; }", V21Baseline, Baseline),
        new Member(SafetyNamespace + ".SafetySignature", "Type, Value", "property", "SafetySignatureType Type; integral Value (0 = no valid signature)", V21Baseline, Baseline + "; " + Asckye),
        new Member("Siemens.Engineering.TiaPortal", "GetService<GlobalSettings>()", "service", "GlobalSettings (TIA Portal-wide)", V21Baseline, Baseline + "; " + Asckye),
        new Member(GlobalSettingsType, "SafetyModificationsPossible(), UsernameForFChangeHistory(), GenerationOfDefaultFailsafeProgram(), ManagementOfFailsafeInSoftwareUnitsEnvironment()", "method", "parameterless getters", V21Baseline, Baseline + "; " + Asckye),
        new Member("Siemens.Engineering.SW.Units.PlcUnitGroup", "SafetyUnits", "property", "PlcSafetyUnitComposition SafetyUnits { get; } (names only)", SiemensSkill, Skill + "libraries-and-alarms/SKILL.md"),
        new Member(SafetyProgramConfigurationType, "CurrentSelection", "property", "SafetyProgramSelections CurrentSelection { get; set; } (members read live)", V21Baseline, Baseline + "; asckye/TIA_Portal_Openness_MCP src/Logic/Siemens/DownloadPromptPolicy.cs (ConsistentDownload)"),
    });
}
