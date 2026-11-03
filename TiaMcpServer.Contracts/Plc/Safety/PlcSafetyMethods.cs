namespace TiaMcpServer.Contracts.Plc.Safety;

/// <summary>
/// Worker methods of the fail-safe (F-) PLC family. Each name is also the public <c>plc_read</c> / <c>plc_write</c>
/// operation name. The lists feed <c>OperationPolicyCatalog</c>, <c>ProjectCapabilityCatalog.OperationFor</c> and the
/// worker dispatch from this one place. Parameters travel in <c>WorkerRequest.PlcSafety</c>. Every Siemens member these
/// methods use is bound at runtime by exact name and signature (<see cref="PlcSafetyApiCatalog"/>); none is compiled.
/// </summary>
public static class PlcSafetyMethods
{
    // plc_read, Observe: SafetyAdministration state of each F-CPU and the F-blocks with their signatures.
    public const string GetAdministration = "get_safety_administration";
    public const string ListBlocks = "list_safety_blocks";

    // plc_write, ProjectMutation: access to the safety offline program and its administration.
    public const string Login = "safety_login";
    public const string Logoff = "safety_logoff";
    public const string SetPassword = "set_safety_password";
    public const string RevokePassword = "revoke_safety_password";
    public const string CreateRuntimeGroup = "create_safety_runtime_group";

    /// <summary>Assigns the main safety block (and its instance DB) of an existing F-runtime group (repair of E7, live 2026-11-02).</summary>
    public const string SetRuntimeGroup = "set_safety_runtime_group";

    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(new[] { GetAdministration, ListBlocks });

    public static IReadOnlyList<string> Write { get; } = Array.AsReadOnly(new[] { Login, Logoff, SetPassword, RevokePassword, CreateRuntimeGroup, SetRuntimeGroup });

    /// <summary>The writes that take a password from an environment variable named by <c>passwordVariable</c>.</summary>
    public static IReadOnlyList<string> PasswordWrites { get; } = Array.AsReadOnly(new[] { Login, SetPassword, RevokePassword });

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Read.Concat(Write).ToArray());

    public static bool IsRead(string? method) => method is not null && Read.Contains(method, StringComparer.Ordinal);

    public static bool IsWrite(string? method) => method is not null && Write.Contains(method, StringComparer.Ordinal);

    public static bool IsWorkerMethod(string? method) => method is not null && All.Contains(method, StringComparer.Ordinal);

    public static bool TakesPassword(string? method) => method is not null && PasswordWrites.Contains(method, StringComparer.Ordinal);

    /// <summary>The writes that address one F-runtime group by <c>runtimeGroupName</c>.</summary>
    public static bool IsRuntimeGroupWrite(string? method) => method is CreateRuntimeGroup or SetRuntimeGroup;
}
