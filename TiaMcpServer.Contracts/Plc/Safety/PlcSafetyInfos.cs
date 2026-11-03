namespace TiaMcpServer.Contracts.Plc.Safety;

/// <summary>
/// Parameters of one fail-safe worker method (<c>WorkerRequest.PlcSafety</c>). The host validated the shape; a method
/// reads only the members it documents. A password never travels: <see cref="PasswordVariable"/> is the NAME of an
/// environment variable the worker reads at the call.
/// </summary>
public sealed class PlcSafetyRequestInfo
{
    /// <summary>PLC software or device name; null reads every PLC (reads) or addresses the only PLC (writes).</summary>
    public string? PlcName { get; set; }

    /// <summary>safety_login, set_safety_password, revoke_safety_password: the environment variable NAME holding the password.</summary>
    public string? PasswordVariable { get; set; }

    /// <summary>create_safety_runtime_group, set_safety_runtime_group: the F-runtime group name.</summary>
    public string? RuntimeGroupName { get; set; }

    /// <summary>create_safety_runtime_group (optional), set_safety_runtime_group (required): the main safety block (an F-FB or F-FC) as a block path.</summary>
    public string? MainSafetyBlockPath { get; set; }

    /// <summary>create_safety_runtime_group, set_safety_runtime_group: the instance DB of an F-FB main safety block.</summary>
    public string? MainSafetyInstanceDbPath { get; set; }

    public int? Offset { get; set; }

    public int? Limit { get; set; }
}

/// <summary>
/// One Siemens member read at runtime. <see cref="State"/> is <c>read</c>, <c>api_member_missing</c> (the installed
/// API has no member of that exact name and kind) or <c>unreadable</c> (the member threw; <see cref="Error"/> says why).
/// </summary>
public sealed class PlcSafetyValueInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The value as invariant text (booleans true/false, enums by member name), or null.</summary>
    public string? Value { get; set; }

    /// <summary>The runtime type of the value, or null.</summary>
    public string? ValueType { get; set; }

    public string State { get; set; } = PlcSafetyValueStates.Read;

    public string? Error { get; set; }
}

public static class PlcSafetyValueStates
{
    public const string Read = "read";
    public const string ApiMemberMissing = "api_member_missing";
    public const string Unreadable = "unreadable";

    public static bool IsKnown(string? state) => state is Read or ApiMemberMissing or Unreadable;
}

/// <summary>One <c>SafetySignature</c>: its <c>Type</c> member name and its <c>Value</c> as decimal and hexadecimal text.</summary>
public sealed class PlcSafetySignatureInfo
{
    public string? Type { get; set; }

    public string? Value { get; set; }

    /// <summary><c>0x</c> plus upper-case hexadecimal digits of an integral value; null when the value is not integral.</summary>
    public string? Hex { get; set; }
}

/// <summary>One F-runtime group (<c>RuntimeGroup</c>) with every documented attribute read individually.</summary>
public sealed class PlcSafetyRuntimeGroupInfo
{
    public string? Name { get; set; }

    public List<PlcSafetyValueInfo> Attributes { get; set; } = new();

    /// <summary>
    /// <c>GetAttributeInfos()</c> of the group: every attribute TIA Portal lists, with its access mode, so a live read shows
    /// what is writable (set_safety_runtime_group binds only what is listed writable; live V21 2026-11-02 listed no
    /// MainSafetyBlock). Null when the group offers no GetAttributeInfos() or the call failed (the PLC's problems say why).
    /// </summary>
    public List<PlcSafetyAttributeAccessInfo>? AttributeInfos { get; set; }
}

/// <summary>One entry of <c>GetAttributeInfos()</c>: the attribute name and its <c>EngineeringAttributeAccessMode</c> as text.</summary>
public sealed class PlcSafetyAttributeAccessInfo
{
    public string Name { get; set; } = string.Empty;

    public string AccessMode { get; set; } = string.Empty;
}

/// <summary>The safety administration of one PLC (an F-CPU when <see cref="Available"/> is true).</summary>
public sealed class PlcSafetyCpuInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>True when the CPU device item offers the <c>Siemens.Engineering.Safety.SafetyAdministration</c> service.</summary>
    public bool Available { get; set; }

    /// <summary>Why the administration is not available (not an F-CPU, Safety option not installed, or the service could not be listed).</summary>
    public string? Reason { get; set; }

    /// <summary><c>SafetyAdministration.IsSafetyOfflineProgramPasswordSet</c>; null when not readable.</summary>
    public bool? PasswordSet { get; set; }

    /// <summary><c>SafetyAdministration.IsLoggedOnToSafetyOfflineProgram</c>; null when not readable.</summary>
    public bool? LoggedOn { get; set; }

    /// <summary>The access members as read, including missing or unreadable ones.</summary>
    public List<PlcSafetyValueInfo> Access { get; set; } = new();

    /// <summary><c>SafetySettings</c> members (SafetySystemVersion, ActivationOfFChangeHistory, ...); empty when not available.</summary>
    public List<PlcSafetyValueInfo> Settings { get; set; } = new();

    /// <summary><c>SafetySettings.GetApplicableSafetySystemVersions()</c> as text; null when not readable.</summary>
    public List<string>? ApplicableSafetySystemVersions { get; set; }

    /// <summary><c>SafetySettings.AssignmentOfBlockNumbers</c> members (ManagementMode, From/To DB, FB, FC).</summary>
    public List<PlcSafetyValueInfo> BlockNumbers { get; set; } = new();

    public List<PlcSafetyRuntimeGroupInfo> RuntimeGroups { get; set; } = new();

    /// <summary>The collective program signatures (<c>SafetyAdministration.ProgramSignatures.Signatures</c>); null when not readable.</summary>
    public List<PlcSafetySignatureInfo>? ProgramSignatures { get; set; }

    /// <summary>The Siemens.Engineering.Safety services the CPU device item lists (<c>GetServiceInfos()</c>), by full type name.</summary>
    public List<string> SafetyServices { get; set; } = new();

    /// <summary>The names in <c>PlcUnitProvider.UnitGroup.SafetyUnits</c>; null when the PLC has no unit provider or the member is missing.</summary>
    public List<string>? SafetyUnits { get; set; }

    /// <summary>Every member that was missing or unreadable, as <c>Type.Member: reason</c> (one line each).</summary>
    public List<string> Problems { get; set; } = new();
}

/// <summary>The TIA Portal-wide <c>GlobalSettings</c> service (its getters are methods).</summary>
public sealed class PlcSafetyGlobalSettingsInfo
{
    public bool Available { get; set; }

    public string? Reason { get; set; }

    public List<PlcSafetyValueInfo> Values { get; set; } = new();
}

/// <summary>The payload of <c>get_safety_administration</c>.</summary>
public sealed class PlcSafetyAdministrationInfo
{
    public List<PlcSafetyCpuInfo> Plcs { get; set; } = new();

    public PlcSafetyGlobalSettingsInfo GlobalSettings { get; set; } = new();

    /// <summary>The PLC discovery messages (device items that could not be read); empty when complete.</summary>
    public List<string> Messages { get; set; } = new();
}

/// <summary>One block that offers the <c>SafetySignatureProvider</c> service (an F-block) or whose language starts with F_.</summary>
public sealed class PlcSafetyBlockInfo
{
    public string BlockPath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int? Number { get; set; }

    public string? BlockType { get; set; }

    public string? ProgrammingLanguage { get; set; }

    public bool? IsKnowHowProtected { get; set; }

    /// <summary>True when the block lists the SafetySignatureProvider service.</summary>
    public bool SignatureProvider { get; set; }

    public List<PlcSafetySignatureInfo>? Signatures { get; set; }

    /// <summary>
    /// <see cref="PlcSafetyValueStates.Read"/> when <see cref="Signatures"/> was read; <see cref="PlcSafetyValueStates.Unreadable"/>
    /// when the provider is listed but GetService returned null for it (live, V21, 2026-11-02, defect D13) or reading threw
    /// (<see cref="Problem"/> says why); null when the block lists no SafetySignatureProvider. Never fails the listing.
    /// </summary>
    public string? SignatureState { get; set; }

    public string? Problem { get; set; }
}

public sealed class PlcSafetyBlockPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public bool Available { get; set; }

    public string? Reason { get; set; }

    public int TotalBlocks { get; set; }

    public int Offset { get; set; }

    public int? NextOffset { get; set; }

    public List<PlcSafetyBlockInfo> Blocks { get; set; } = new();
}

/// <summary>The payload of <c>list_safety_blocks</c>.</summary>
public sealed class PlcSafetyBlockListInfo
{
    public List<PlcSafetyBlockPlcInfo> Plcs { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// The payload of every fail-safe write: the access state before and after,
/// read back from the live administration. Never carries a password; <see cref="PasswordVariable"/> is the name only.
/// </summary>
public sealed class PlcSafetyWriteResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string? PasswordVariable { get; set; }

    public bool? PasswordSetBefore { get; set; }

    public bool? LoggedOnBefore { get; set; }

    public bool? PasswordSetAfter { get; set; }

    public bool? LoggedOnAfter { get; set; }

    /// <summary>create_safety_runtime_group: the group as read back after Create.</summary>
    public PlcSafetyRuntimeGroupInfo? RuntimeGroup { get; set; }

    /// <summary>create_safety_runtime_group: the documented Create overload used.</summary>
    public string? Overload { get; set; }

    /// <summary>set_safety_runtime_group: each assignment with the route that bound and its read-back; null for every other write.</summary>
    public List<PlcSafetyRuntimeGroupAssignmentInfo>? Assignments { get; set; }

    /// <summary>The acceptance notice every safety write carries (<see cref="PlcSafetyPolicy.AcceptanceNotice"/>).</summary>
    public string Notice { get; set; } = PlcSafetyPolicy.AcceptanceNotice;
}

/// <summary>One assignment of set_safety_runtime_group (<see cref="PlcSafetyRuntimeGroupBinding"/>).</summary>
public sealed class PlcSafetyRuntimeGroupAssignmentInfo
{
    /// <summary><c>MainSafetyBlock</c> or <c>MainSafetyBlockIDB</c>.</summary>
    public string Member { get; set; } = string.Empty;

    public string BlockPath { get; set; } = string.Empty;

    public string BlockName { get; set; } = string.Empty;

    /// <summary><see cref="PlcSafetyRuntimeGroupBinding.Routes"/>: property, setAttribute or setAttributeName.</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>The bound member as a signature.</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>The value before the assignment of the read-back attribute (MainSafetyBlockName / MainSafetyBlockIDbName); null when unset or unreadable.</summary>
    public string? Before { get; set; }

    /// <summary>The read-back attribute's value after the assignment; equal to <see cref="BlockName"/>, or the item failed postcondition_failed.</summary>
    public string? After { get; set; }
}
