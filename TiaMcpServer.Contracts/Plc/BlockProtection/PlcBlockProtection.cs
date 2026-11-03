using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.BlockProtection;

/// <summary>
/// Know-how protection and write protection of PLC blocks (GitHub issue #5). Evidence: the V21 IntelliSense file
/// Siemens.Engineering.Step7.xml (<c>T:Siemens.Engineering.SW.Blocks.PlcBlockProtectionProvider</c> with <c>Protect(SecureString)</c>
/// "Sets protection for the underlying object" and <c>Unprotect(SecureString)</c> "Removes protection";
/// <c>T:Siemens.Engineering.SW.Blocks.PlcBlockWriteProtectionProvider</c> with <c>IsDefined</c> "Is password defined",
/// <c>IsProtected</c> "Is write protection enabled", <c>Define(SecureString)</c>, <c>Protect(SecureString)</c>,
/// <c>Unprotect(SecureString)</c>, <c>Change(SecureString, SecureString)</c>; <c>P:Siemens.Engineering.SW.Blocks.PlcBlock.IsKnowHowProtected</c>),
/// Siemens.Engineering.Base.xml (<c>ProtectionProviderBase.GetInvalidPasswordCharacters()</c>, an <c>IEnumerable&lt;Char&gt;</c>) and the
/// Siemens blocks skill ("Protect Block with Password": <c>block.GetService&lt;PlcBlockProtectionProvider&gt;()</c>, check the invalid
/// characters before <c>Protect</c>). The XML does not say which object offers the write-protection provider; both are looked up among
/// the block's <c>GetServiceInfos()</c> by exact full name. Each method name is also the public plc_read / plc_write operation name;
/// parameters travel in <c>WorkerRequest.PlcBlockProtection</c>; passwords only as password-source tokens.
/// </summary>
public static class PlcBlockProtectionMethods
{
    /// <summary>plc_read, Observe: know-how and write protection state of one block.</summary>
    public const string GetBlockProtection = "get_block_protection";

    /// <summary>plc_write: <c>PlcBlockProtectionProvider.Protect(SecureString)</c>.</summary>
    public const string SetKnowHowProtection = "set_block_know_how_protection";

    /// <summary>plc_write: <c>PlcBlockProtectionProvider.Unprotect(SecureString)</c>.</summary>
    public const string RemoveKnowHowProtection = "remove_block_know_how_protection";

    /// <summary>plc_write: <c>PlcBlockWriteProtectionProvider.Define(SecureString)</c> (defines the write-protection password).</summary>
    public const string DefineWriteProtection = "define_block_write_protection";

    /// <summary>plc_write: <c>PlcBlockWriteProtectionProvider.Protect(SecureString)</c> (enables write protection).</summary>
    public const string SetWriteProtection = "set_block_write_protection";

    /// <summary>plc_write: <c>PlcBlockWriteProtectionProvider.Unprotect(SecureString)</c> (disables write protection).</summary>
    public const string RemoveWriteProtection = "remove_block_write_protection";

    /// <summary>plc_write: <c>PlcBlockWriteProtectionProvider.Change(SecureString, SecureString)</c> (changes the password).</summary>
    public const string ChangeWriteProtectionPassword = "change_block_write_protection_password";

    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(new[] { GetBlockProtection });

    public static IReadOnlyList<string> Write { get; } = Array.AsReadOnly(new[]
    {
        SetKnowHowProtection, RemoveKnowHowProtection, DefineWriteProtection, SetWriteProtection, RemoveWriteProtection, ChangeWriteProtectionPassword,
    });

    public static bool IsRead(string? method) => method is not null && Read.Contains(method, StringComparer.Ordinal);

    public static bool IsWrite(string? method) => method is not null && Write.Contains(method, StringComparer.Ordinal);

    public static bool IsWorkerMethod(string? method) => IsRead(method) || IsWrite(method);

    /// <summary>True for the know-how protection writes; the other writes act on the write protection.</summary>
    public static bool IsKnowHow(string? method) => method is SetKnowHowProtection or RemoveKnowHowProtection;

    /// <summary>The password map purpose of the item's password: <see cref="PlcBlockProtectionNames.KnowHowPurpose"/> or <see cref="PlcBlockProtectionNames.WriteProtectionPurpose"/>.</summary>
    public static string Purpose(string? method) => IsKnowHow(method) ? PlcBlockProtectionNames.KnowHowPurpose : PlcBlockProtectionNames.WriteProtectionPurpose;

    /// <summary>The writes that remove a protection (info guard plc_lowers_block_protection); the others set or change one.</summary>
    public static bool Lowers(string? method) => method is RemoveKnowHowProtection or RemoveWriteProtection;
}

/// <summary>The documented names the block protection operations bind at runtime.</summary>
public static class PlcBlockProtectionNames
{
    public const string KnowHowProvider = "Siemens.Engineering.SW.Blocks.PlcBlockProtectionProvider";
    public const string WriteProvider = "Siemens.Engineering.SW.Blocks.PlcBlockWriteProtectionProvider";

    public const string Protect = "Protect";
    public const string Unprotect = "Unprotect";
    public const string Define = "Define";
    public const string Change = "Change";
    public const string InvalidCharacters = "GetInvalidPasswordCharacters";
    public const string IsKnowHowProtected = "IsKnowHowProtected";
    public const string IsDefined = "IsDefined";
    public const string IsProtected = "IsProtected";

    /// <summary>The password map purpose of the know-how protection password (the TiaMcp password manager offers it).</summary>
    public const string KnowHowPurpose = "KnowHowProtection";

    /// <summary>The password map purpose of the block write-protection password.</summary>
    public const string WriteProtectionPurpose = "BlockWriteProtection";

    /// <summary>The <c>readBack</c> values of a write result.</summary>
    public const string ReadBackMatched = "matched";
    public const string ReadBackUnreadable = "unreadable";

    /// <summary>The <c>role</c> values of a password use.</summary>
    public const string RolePassword = "password";
    public const string RoleNewPassword = "newPassword";

    public const int MaxInvalidCharacters = 256;
}

/// <summary>The worker parameters: the block and the password-source tokens (never values).</summary>
public sealed class PlcBlockProtectionRequestInfo
{
    public string BlockPath { get; set; } = string.Empty;

    /// <summary>The password source (environment variable NAME or <c>credential:</c> target) of the call's password; for change, the current one.</summary>
    public string? PasswordSource { get; set; }

    /// <summary>change_block_write_protection_password: the source of the new password.</summary>
    public string? NewPasswordSource { get; set; }
}

/// <summary>The protection state of one block as read by its documented members; null where unreadable or not offered.</summary>
public sealed class PlcBlockProtectionStateInfo
{
    /// <summary><c>PlcBlock.IsKnowHowProtected</c>.</summary>
    public bool? KnowHowProtected { get; set; }

    /// <summary><c>PlcBlockWriteProtectionProvider.IsDefined</c>.</summary>
    public bool? WriteProtectionDefined { get; set; }

    /// <summary><c>PlcBlockWriteProtectionProvider.IsProtected</c>.</summary>
    public bool? WriteProtectionEnabled { get; set; }
}

/// <summary>One protection provider of the block: whether the block lists and returns it and which documented members bind.</summary>
public sealed class PlcBlockProtectionProviderInfo
{
    public string Service { get; set; } = string.Empty;

    /// <summary>The block's GetServiceInfos() lists the type.</summary>
    public bool ServiceOffered { get; set; }

    /// <summary>GetService returned the provider (false also when not offered).</summary>
    public bool Available { get; set; }

    /// <summary>The documented methods found on the live provider, as signatures.</summary>
    public List<string> Members { get; set; } = new();

    /// <summary>The characters <c>GetInvalidPasswordCharacters()</c> returns, each as a one-character string; null when not readable or not offered.</summary>
    public List<string>? InvalidPasswordCharacters { get; set; }

    public string? Note { get; set; }
}

/// <summary>get_block_protection: the block and both providers.</summary>
public sealed class PlcBlockProtectionInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string BlockPath { get; set; } = string.Empty;

    public string BlockName { get; set; } = string.Empty;

    public string? ProgrammingLanguage { get; set; }

    /// <summary>The block's ProgrammingLanguage starts with F_ (a fail-safe block: TIA Portal's safety login rule applies to its changes).</summary>
    public bool IsFailSafe { get; set; }

    public PlcBlockProtectionStateInfo State { get; set; } = new();

    public PlcBlockProtectionProviderInfo KnowHow { get; set; } = new();

    public PlcBlockProtectionProviderInfo WriteProtection { get; set; } = new();

    public List<string> Notes { get; set; } = new();
}

/// <summary>A password source the write used: its role, map purpose, kind and token (never the value).</summary>
public sealed class PlcBlockProtectionPasswordUseInfo
{
    public string Role { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;
}

/// <summary>The result of a block protection write.</summary>
public sealed class PlcBlockProtectionWriteResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public string BlockPath { get; set; } = string.Empty;

    public string BlockName { get; set; } = string.Empty;

    public string Service { get; set; } = string.Empty;

    /// <summary>The bound member as signature.</summary>
    public string Member { get; set; } = string.Empty;

    public PlcBlockProtectionStateInfo Before { get; set; } = new();

    public PlcBlockProtectionStateInfo After { get; set; } = new();

    /// <summary>The state property the write is verified by (<c>IsKnowHowProtected</c>, <c>IsDefined</c> or <c>IsProtected</c>).</summary>
    public string ReadBackProperty { get; set; } = string.Empty;

    public bool Expected { get; set; }

    /// <summary><see cref="PlcBlockProtectionNames.ReadBackMatched"/> or <see cref="PlcBlockProtectionNames.ReadBackUnreadable"/> (a readable mismatch fails the item).</summary>
    public string ReadBack { get; set; } = string.Empty;

    public List<PlcBlockProtectionPasswordUseInfo> PasswordSources { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// One block as the protection operations see it: identity, the live block object (IsKnowHowProtected is read by name on it),
/// the service types its GetServiceInfos() lists and a retrieval of a listed service (IServiceProvider.GetService(Type) in the
/// worker, stand-ins in the FakeWorker).
/// </summary>
public sealed class PlcBlockProtectionTarget(string plcName, string blockPath, string blockName, string? programmingLanguage, object block,
    IReadOnlyList<Type> offered, Func<Type, object?> service)
{
    public string PlcName { get; } = plcName;

    public string BlockPath { get; } = blockPath;

    public string BlockName { get; } = blockName;

    public string? ProgrammingLanguage { get; } = programmingLanguage;

    public object Block { get; } = block;

    public IReadOnlyList<Type> Offered { get; } = offered;

    public Func<Type, object?> Service { get; } = service;
}

/// <summary>
/// The Siemens-free part of the block protection operations, shared by the worker and the FakeWorker. Providers are found by exact
/// full name among the listed services, every member is bound by exact name and parameter types (<c>SecureString</c>) on the live
/// object, and a missing one fails <c>api_member_missing</c> before any change. Passwords are resolved from source tokens into
/// read-only SecureStrings right before the call and disposed after it; none is returned or logged. A new password (the know-how
/// password, the defined and the changed write-protection password) is checked against the provider's own
/// <c>GetInvalidPasswordCharacters()</c> first, as the Siemens skill recommends; the check names neither the password nor which
/// character it holds. Failures are <see cref="RuntimeBindingException"/> with their category.
/// </summary>
public static class PlcBlockProtectionRun
{
    private static readonly Type SecureStringType = typeof(SecureString);

    public static PlcBlockProtectionInfo Read(PlcBlockProtectionTarget target, Func<Exception, bool> recoverable)
    {
        var info = new PlcBlockProtectionInfo
        {
            PlcName = target.PlcName,
            BlockPath = target.BlockPath,
            BlockName = target.BlockName,
            ProgrammingLanguage = target.ProgrammingLanguage,
            IsFailSafe = Safety.PlcSafetyPolicy.IsFailSafeLanguage(target.ProgrammingLanguage),
        };
        var knowHow = Provider(target, PlcBlockProtectionNames.KnowHowProvider);
        var write = Provider(target, PlcBlockProtectionNames.WriteProvider);
        info.State = State(target, write.Provider, recoverable, info.Notes);
        info.KnowHow = Describe(PlcBlockProtectionNames.KnowHowProvider, knowHow, new[] { (PlcBlockProtectionNames.Protect, 1), (PlcBlockProtectionNames.Unprotect, 1) }, recoverable);
        info.WriteProtection = Describe(PlcBlockProtectionNames.WriteProvider, write, new[]
        {
            (PlcBlockProtectionNames.Define, 1), (PlcBlockProtectionNames.Protect, 1), (PlcBlockProtectionNames.Unprotect, 1), (PlcBlockProtectionNames.Change, 2),
        }, recoverable);
        return info;
    }

    /// <summary>The resolver the worker and the FakeWorker pass (<see cref="PasswordSources.Resolve"/>); an unusable source fails before any Siemens call.</summary>
    public static Func<string, SecureString> Resolver(Func<string, string?> readEnvironment, ICredentialReader? credentials)
        => token =>
        {
            var secure = PasswordSources.Resolve(token, readEnvironment, credentials, out var reason);
            if (secure is not null) return secure;
            throw new RuntimeBindingException(
                reason == WindowsCredentialReader.UnsupportedReason ? WorkerFailureCategories.UnsupportedCapability : WorkerFailureCategories.ValidationError,
                (reason ?? "The password source could not be read.") + " Nothing was changed.");
        };

    public static PlcBlockProtectionWriteResultInfo Write(string method, PlcBlockProtectionTarget target, PlcBlockProtectionRequestInfo request,
        Func<Exception, bool> recoverable, Func<string, SecureString> resolve)
    {
        var (service, member, parameters, property, expected) = method switch
        {
            PlcBlockProtectionMethods.SetKnowHowProtection => (PlcBlockProtectionNames.KnowHowProvider, PlcBlockProtectionNames.Protect, 1, PlcBlockProtectionNames.IsKnowHowProtected, true),
            PlcBlockProtectionMethods.RemoveKnowHowProtection => (PlcBlockProtectionNames.KnowHowProvider, PlcBlockProtectionNames.Unprotect, 1, PlcBlockProtectionNames.IsKnowHowProtected, false),
            PlcBlockProtectionMethods.DefineWriteProtection => (PlcBlockProtectionNames.WriteProvider, PlcBlockProtectionNames.Define, 1, PlcBlockProtectionNames.IsDefined, true),
            PlcBlockProtectionMethods.SetWriteProtection => (PlcBlockProtectionNames.WriteProvider, PlcBlockProtectionNames.Protect, 1, PlcBlockProtectionNames.IsProtected, true),
            PlcBlockProtectionMethods.RemoveWriteProtection => (PlcBlockProtectionNames.WriteProvider, PlcBlockProtectionNames.Unprotect, 1, PlcBlockProtectionNames.IsProtected, false),
            PlcBlockProtectionMethods.ChangeWriteProtectionPassword => (PlcBlockProtectionNames.WriteProvider, PlcBlockProtectionNames.Change, 2, PlcBlockProtectionNames.IsDefined, true),
            _ => throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"Unsupported block protection method '{method}'."),
        };
        var tokens = parameters == 2 ? new[] { request.PasswordSource, request.NewPasswordSource } : new[] { request.PasswordSource };
        if (tokens.Any(token => !PasswordSources.IsSource(token)))
            throw new RuntimeBindingException(WorkerFailureCategories.ValidationError,
                $"{method} needs {(parameters == 2 ? "passwordVariable (the current password) and newPasswordVariable" : "passwordVariable")}: an environment variable NAME or a credential target, never the password. Nothing was changed.");

        var found = Provider(target, service);
        if (found.Provider is null)
            throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
                (found.Unavailable ?? RuntimeServiceValues.NotListed($"The block '{target.BlockPath}'", service)) + " Nothing was changed.");
        var provider = found.Provider;
        var bound = LibraryRuntimeBinding.Find(provider.GetType(), member, Enumerable.Repeat(SecureStringType, parameters).ToArray(), out var error)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                $"{error ?? $"No {member} on '{provider.GetType().FullName}'."} (documented: {Documented(service, member, parameters)}) Nothing was changed.");
        var writeProvider = service == PlcBlockProtectionNames.WriteProvider ? provider : Provider(target, PlcBlockProtectionNames.WriteProvider).Provider;
        var result = new PlcBlockProtectionWriteResultInfo
        {
            Operation = method,
            PlcName = target.PlcName,
            BlockPath = target.BlockPath,
            BlockName = target.BlockName,
            Service = service,
            Member = Signature(bound),
            ReadBackProperty = property,
            Expected = expected,
        };
        result.Before = State(target, writeProvider, recoverable, result.Messages);

        var purpose = PlcBlockProtectionMethods.Purpose(method);
        var secrets = new List<SecureString>();
        try
        {
            foreach (var token in tokens) secrets.Add(resolve(token!));
            // A password the call sets anew is checked against TIA Portal's own list first (Siemens blocks skill).
            var fresh = method switch
            {
                PlcBlockProtectionMethods.SetKnowHowProtection or PlcBlockProtectionMethods.DefineWriteProtection => secrets[0],
                PlcBlockProtectionMethods.ChangeWriteProtectionPassword => secrets[1],
                _ => null,
            };
            if (fresh is not null) CheckInvalidCharacters(provider, fresh, method, result.Messages);
            RuntimeServiceValues.Call(bound, provider, secrets.Cast<object?>().ToArray());
        }
        finally
        {
            foreach (var secret in secrets) secret.Dispose();
        }

        result.PasswordSources.Add(Use(PlcBlockProtectionNames.RolePassword, purpose, tokens[0]!));
        if (parameters == 2) result.PasswordSources.Add(Use(PlcBlockProtectionNames.RoleNewPassword, purpose, tokens[1]!));
        result.After = State(target, writeProvider, recoverable, result.Messages);
        var observed = property switch
        {
            PlcBlockProtectionNames.IsKnowHowProtected => result.After.KnowHowProtected,
            PlcBlockProtectionNames.IsDefined => result.After.WriteProtectionDefined,
            _ => result.After.WriteProtectionEnabled,
        };
        if (observed is { } value && value != expected)
            throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                $"{result.Member} on '{target.BlockPath}' returned without an error, but {property} reads {Text(value)} afterwards (expected {Text(expected)}). Inspect the block with plc_read get_block_protection.");
        result.ReadBack = observed is null ? PlcBlockProtectionNames.ReadBackUnreadable : PlcBlockProtectionNames.ReadBackMatched;
        if (observed is null)
            result.Messages.Add($"{property} could not be read after {result.Member}; the write is not verified by a read-back.");
        if (method == PlcBlockProtectionMethods.ChangeWriteProtectionPassword)
            result.Messages.Add("No documented member reads a password back; IsDefined is the read-back, the new password itself is not verified.");
        return result;
    }

    /// <summary>
    /// Refuses a password holding a character the provider's <c>GetInvalidPasswordCharacters()</c> lists (validation_error, nothing
    /// changed). The password is read from the SecureString into unmanaged memory only for the comparison and zeroed right after;
    /// the message never says which character matched. Without a bindable method the check is skipped with a note (TIA Portal decides).
    /// </summary>
    private static void CheckInvalidCharacters(object provider, SecureString password, string method, List<string> messages)
    {
        var invalid = InvalidCharacters(provider, out var reason);
        if (invalid is null)
        {
            messages.Add($"The password was not checked against GetInvalidPasswordCharacters() ({reason}); TIA Portal decides.");
            return;
        }

        if (invalid.Count == 0) return;
        var set = new HashSet<char>(invalid);
        var pointer = IntPtr.Zero;
        var found = false;
        try
        {
            pointer = Marshal.SecureStringToGlobalAllocUnicode(password);
            for (var index = 0; index < password.Length && !found; index++)
                found = set.Contains((char)Marshal.ReadInt16(pointer, index * 2));
        }
        finally
        {
            if (pointer != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(pointer);
        }

        if (found)
            throw new RuntimeBindingException(WorkerFailureCategories.ValidationError,
                $"{method}: the password from its source contains a character that TIA Portal does not allow (GetInvalidPasswordCharacters() lists {invalid.Count}: {Show(invalid)}). Nothing was changed.");
    }

    private static List<char>? InvalidCharacters(object provider, out string? reason)
    {
        var method = LibraryRuntimeBinding.Find(provider.GetType(), PlcBlockProtectionNames.InvalidCharacters, Type.EmptyTypes, out var error);
        if (method is null)
        {
            reason = error ?? $"no {PlcBlockProtectionNames.InvalidCharacters}() on '{provider.GetType().FullName}'";
            return null;
        }

        if (RuntimeServiceValues.Call(method, provider, Array.Empty<object?>()) is not IEnumerable<char> characters)
        {
            reason = $"{PlcBlockProtectionNames.InvalidCharacters}() did not return characters";
            return null;
        }

        reason = null;
        return characters.Distinct().Take(PlcBlockProtectionNames.MaxInvalidCharacters).ToList();
    }

    private static string Show(IEnumerable<char> characters)
        => string.Join(" ", characters.Select(c => char.IsControl(c) || char.IsWhiteSpace(c) ? $"U+{(int)c:X4}" : c.ToString()));

    private static (object? Provider, string? Unavailable, bool Offered) Provider(PlcBlockProtectionTarget target, string fullName)
    {
        var (provider, unavailable) = RuntimeServiceValues.Find(target.Offered, target.Service, fullName);
        return (provider, unavailable, target.Offered.Any(type => type.FullName == fullName));
    }

    private static PlcBlockProtectionProviderInfo Describe(string service, (object? Provider, string? Unavailable, bool Offered) found,
        (string Name, int Passwords)[] members, Func<Exception, bool> recoverable)
    {
        var info = new PlcBlockProtectionProviderInfo { Service = service, ServiceOffered = found.Offered, Available = found.Provider is not null };
        if (found.Provider is null)
        {
            info.Note = found.Unavailable ?? RuntimeServiceValues.NotListed("The block", service);
            return info;
        }

        var type = found.Provider.GetType();
        foreach (var (name, passwords) in members)
            if (LibraryRuntimeBinding.Find(type, name, Enumerable.Repeat(SecureStringType, passwords).ToArray(), out _) is { } method)
                info.Members.Add(Signature(method));
        try
        {
            info.InvalidPasswordCharacters = InvalidCharacters(found.Provider, out var reason)?.Select(c => c.ToString()).ToList();
            if (info.InvalidPasswordCharacters is null) info.Note = $"The invalid password characters are not readable: {reason}.";
        }
        catch (Exception exception) when (recoverable(exception))
        {
            info.Note = $"{PlcBlockProtectionNames.InvalidCharacters}() threw {exception.GetType().Name}: {exception.Message}";
        }

        var missing = members.Where(m => !info.Members.Any(s => s.Contains(" " + m.Name + "("))).Select(m => m.Name).ToList();
        if (missing.Count > 0)
            info.Note = (info.Note is null ? string.Empty : info.Note + " ") + $"Not bindable on this installation: {string.Join(", ", missing)} (their writes fail api_member_missing).";
        return info;
    }

    private static PlcBlockProtectionStateInfo State(PlcBlockProtectionTarget target, object? writeProvider, Func<Exception, bool> recoverable, List<string> notes)
        => new()
        {
            KnowHowProtected = Bool(target.Block, PlcBlockProtectionNames.IsKnowHowProtected, recoverable, notes),
            WriteProtectionDefined = writeProvider is null ? null : Bool(writeProvider, PlcBlockProtectionNames.IsDefined, recoverable, notes),
            WriteProtectionEnabled = writeProvider is null ? null : Bool(writeProvider, PlcBlockProtectionNames.IsProtected, recoverable, notes),
        };

    private static bool? Bool(object target, string property, Func<Exception, bool> recoverable, List<string> notes)
    {
        var read = RuntimeServiceValues.ReadProperty(target, property, recoverable);
        if (read.State == RuntimeValueStates.Value && bool.TryParse(read.Value, out var value)) return value;
        var note = $"{property} is not readable ({read.State}{(read.Error is null ? string.Empty : ": " + read.Error)}).";
        if (!notes.Contains(note)) notes.Add(note);
        return null;
    }

    private static PlcBlockProtectionPasswordUseInfo Use(string role, string purpose, string token)
        => new() { Role = role, Purpose = purpose, Source = token, Kind = PasswordSources.KindName(token) ?? string.Empty };

    private static string Documented(string service, string member, int parameters)
        => $"{service}.{member}({string.Join(", ", Enumerable.Repeat("SecureString", parameters))})";

    private static string Signature(MethodInfo method)
        => $"{(method.ReturnType == typeof(void) ? "void" : method.ReturnType.Name)} {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    private static string Text(bool value) => value ? "true" : "false";
}
