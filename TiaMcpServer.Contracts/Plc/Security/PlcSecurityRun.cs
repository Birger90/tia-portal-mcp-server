using TiaMcpServer.Contracts.Worker;
using Policy = TiaMcpServer.Contracts.Plc.Security.PlcSecurityPolicy;
using Reflection = TiaMcpServer.Contracts.Plc.Security.PlcSecurityReflection;

namespace TiaMcpServer.Contracts.Plc.Security;

/// <summary>
/// One CPU as the protection operations see it: its PLC, the service types its <c>GetServiceInfos()</c> lists and a way to
/// retrieve a listed service (<c>IServiceProvider.GetService(Type)</c> in the worker, stand-ins in the FakeWorker). The
/// retrieval returns null for a listed service that <c>GetService</c> does not return (seen live on V21, 2026-11-02, defect
/// D14: <c>PlcAccessControlConfigurationProvider</c> of an S7-1500 CPU): the read reports that part as unavailable, a write
/// that needs it fails <c>unsupported_capability</c> before any change.
/// </summary>
public sealed class PlcSecurityCpu(string plcName, string deviceName, IReadOnlyList<Type> offered, Func<Type, object?> service)
{
    public string PlcName { get; } = plcName;

    public string DeviceName { get; } = deviceName;

    public IReadOnlyList<Type> Offered { get; } = offered;

    public Func<Type, object?> Service { get; } = service;
}

/// <summary>
/// The Siemens-free orchestration of get_plc_security and the CPU protection writes, shared by the worker and the
/// FakeWorker so both produce the same evidence. Failures are <see cref="PlcSecurityBindingException"/> with their
/// category; the caller turns them into its own failure type. Passwords are resolved from password-source tokens into
/// read-only SecureStrings right before the Siemens call and disposed after it; none is generated, returned or logged.
/// </summary>
public static class PlcSecurityRun
{
    public static PlcSecurityPlcInfo Read(PlcSecurityCpu cpu, Func<Exception, bool> recoverable)
    {
        var names = cpu.Offered.Select(type => type.FullName ?? type.Name).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var entry = new PlcSecurityPlcInfo
        {
            PlcName = cpu.PlcName,
            DeviceName = cpu.DeviceName,
            Services = names.Take(Policy.MaxServices).ToList(),
            ServicesTruncated = names.Count > Policy.MaxServices,
        };

        var levelType = Reflection.FindByFullName(cpu.Offered, Policy.AccessLevelProviderType);
        entry.AccessLevel = Reflection.ReadSetting(Policy.AccessLevelProviderType, Policy.AccessLevelProperty, Policy.AccessLevelEnumName,
            levelType, levelType is null ? null : cpu.Service(levelType), recoverable, Policy.NoPasswordReadNote);

        var controlType = Reflection.FindByFullName(cpu.Offered, Policy.AccessControlProviderType);
        entry.AccessControl = Reflection.ReadSetting(Policy.AccessControlProviderType, Policy.AccessControlProperty, Policy.AccessControlEnumName,
            controlType, controlType is null ? null : cpu.Service(controlType), recoverable, null);

        var secretType = Reflection.FindByShortName(cpu.Offered, Policy.MasterSecretConfiguratorName);
        var confidential = new PlcConfidentialDataInfo { ServiceOffered = secretType is not null, ServiceType = secretType?.FullName };
        if (secretType is null)
        {
            confidential.Note = $"The CPU's GetServiceInfos() does not list a {Policy.MasterSecretConfiguratorName}; this CPU does not offer confidential-data protection through Openness.";
        }
        else
        {
            confidential.ProtectBindable = Reflection.HasProtect(secretType);
            confidential.UnprotectBindable = Reflection.FindUnprotect(secretType, out var candidates) is not null;
            confidential.UnprotectCandidates = candidates;
            var configurator = cpu.Service(secretType);
            confidential.ObservedMembers = configurator is null ? new List<PlcSecurityMemberInfo>() : Reflection.Observe(secretType, configurator, recoverable);
            confidential.Note = configurator is null
                ? Shared.ListedServices.UnavailableReason(secretType.FullName ?? secretType.Name) + " No member was observed; the confidential-data writes would fail unsupported_capability."
                : $"{Policy.MasterSecretConfigurationProperty} (documented: the PLC protection type; values undocumented) and the other readable properties are listed in observedMembers, uninterpreted."
                    + (confidential.UnprotectBindable ? string.Empty : " void Unprotect() is not present, so disable_plc_confidential_data_protection without a password would fail api_member_missing.");
        }

        entry.ConfidentialDataProtection = confidential;
        return entry;
    }

    /// <summary>
    /// The resolver the worker and the FakeWorker pass: <see cref="Shared.PasswordSources.Resolve"/> over the process
    /// environment and the credential reader; an unusable source fails before any Siemens call (validation_error, or
    /// unsupported_capability for a credential off Windows). The reason never contains a secret.
    /// </summary>
    public static Func<string, System.Security.SecureString> Resolver(Func<string, string?> readEnvironment, Shared.ICredentialReader? credentials)
        => token =>
        {
            var secure = Shared.PasswordSources.Resolve(token, readEnvironment, credentials, out var reason);
            if (secure is not null) return secure;
            throw new PlcSecurityBindingException(
                reason == Shared.WindowsCredentialReader.UnsupportedReason ? WorkerFailureCategories.UnsupportedCapability : WorkerFailureCategories.ValidationError,
                (reason ?? "The password source could not be read.") + " Nothing was changed.");
        };

    public static (PlcSecurityWriteResultInfo Result, List<string> Warnings) Write(string method, PlcSecurityCpu cpu, PlcSecurityRequestInfo info,
        Func<Exception, bool> recoverable, Func<string, System.Security.SecureString>? resolvePassword = null)
    {
        var resolve = resolvePassword ?? Resolver(Environment.GetEnvironmentVariable, null);
        return method switch
        {
            PlcSecurityMethods.SetAccessLevel => SetAccessLevel(cpu, info, recoverable, resolve),
            PlcSecurityMethods.DisableConfidentialDataProtection or PlcSecurityMethods.ProtectConfidentialData or PlcSecurityMethods.ChangeConfidentialDataPassword
                => ConfidentialData(method, cpu, info, recoverable, resolve),
            _ => throw new PlcSecurityBindingException(WorkerFailureCategories.ValidationError, $"Unsupported CPU protection method '{method}'."),
        };
    }

    /// <summary>The listed service a write needs; a listed one GetService does not return fails before any change (D14).</summary>
    private static object Retrieve(PlcSecurityCpu cpu, Type type)
        => cpu.Service(type) ?? throw new PlcSecurityBindingException(WorkerFailureCategories.UnsupportedCapability,
            $"PLC '{cpu.PlcName}': {Shared.ListedServices.UnavailableReason(type.FullName ?? type.Name)} Nothing was changed.");

    private static PlcSecurityPasswordUseInfo Use(string purpose, string token)
        => new() { Purpose = purpose, Source = token, Kind = Shared.PasswordSources.KindName(token) ?? string.Empty };

    private static (PlcSecurityWriteResultInfo, List<string>) SetAccessLevel(PlcSecurityCpu cpu, PlcSecurityRequestInfo info, Func<Exception, bool> recoverable,
        Func<string, System.Security.SecureString> resolve)
    {
        // The host validated it; this refuses again so no other level is ever written.
        if (Policy.AccessLevelError(info.AccessLevel) is { } refusal)
            throw new PlcSecurityBindingException(WorkerFailureCategories.ValidationError, refusal + " Nothing was changed.");
        foreach (var level in info.AccessLevelPasswords.Select(p => p.Level).Concat(info.ResetAccessLevelPasswords))
            if (Policy.PasswordLevelError("accessLevelPasswords", level) is { } levelError)
                throw new PlcSecurityBindingException(WorkerFailureCategories.ValidationError, levelError + " Nothing was changed.");
        var type = Reflection.FindByFullName(cpu.Offered, Policy.AccessLevelProviderType)
            ?? throw new PlcSecurityBindingException(WorkerFailureCategories.UnsupportedCapability,
                $"The CPU of PLC '{cpu.PlcName}' does not list {Policy.AccessLevelProviderType} among its services. Nothing was changed.");
        var property = Reflection.RequireWritableEnum(type, Policy.AccessLevelProperty, Policy.AccessLevelEnumName);
        var value = Reflection.ParseExact(property.PropertyType, info.AccessLevel!);
        // Every member is bound and every password resolved before the first call, so a missing one changes nothing.
        var setPassword = info.AccessLevelPasswords.Count == 0 ? null
            : Reflection.RequireMethod(type, Policy.SetPasswordMethod, "void SetPassword(PlcProtectionAccessLevel, SecureString)", Reflection.IsAccessLevelEnum, Reflection.IsSecureString);
        var resetPassword = info.ResetAccessLevelPasswords.Count == 0 ? null
            : Reflection.RequireMethod(type, Policy.ResetPasswordMethod, "void ResetPassword(PlcProtectionAccessLevel)", Reflection.IsAccessLevelEnum);
        var ordered = info.AccessLevelPasswords.OrderBy(p => Policy.PasswordLevels.ToList().IndexOf(p.Level)).ToList();
        var levels = ordered.Select(p => Reflection.ParseExact(setPassword!.GetParameters()[0].ParameterType, p.Level)).ToList();
        var resets = info.ResetAccessLevelPasswords.Select(level => Reflection.ParseExact(resetPassword!.GetParameters()[0].ParameterType, level)).ToList();
        var provider = Retrieve(cpu, type);
        var secrets = new List<System.Security.SecureString>();
        var calls = new List<string>();
        try
        {
            foreach (var entry in ordered) secrets.Add(resolve(entry.Source));
            var before = Reflection.ReadValue(property, provider, recoverable, Policy.NoPasswordReadNote);
            // K48 (live V21 2026-11-02, F-CPU PLC_F1): TIA Portal accepts SetPassword/ResetPassword of a level only while the
            // current level is less open than that level ("Cannot set password for the access level": SetPassword before
            // the level at FullAccessIncludingFailsafe, and ResetPassword(FullAccessIncludingFailsafe) after setting that
            // level). So: ResetPassword first (the old, more protective level still holds), then the level, then
            // SetPassword. The order is not documented by Siemens. A failed password call after the level changed restores
            // the level read before (when it was readable); a reset already made cannot be undone (no password value).
            for (var index = 0; index < resets.Count; index++)
            {
                Reflection.Invoke(resetPassword!, provider, resets[index]);
                calls.Add($"{Reflection.Signature(resetPassword!)} [{info.ResetAccessLevelPasswords[index]}]");
            }

            Reflection.SetValue(property, provider, value);
            calls.Add($"{Policy.AccessLevelProperty} {{ set; }} [{info.AccessLevel}]");
            try
            {
                for (var index = 0; index < ordered.Count; index++)
                {
                    Reflection.Invoke(setPassword!, provider, levels[index], secrets[index]);
                    calls.Add($"{Reflection.Signature(setPassword!)} [{ordered[index].Level}]");
                }
            }
            catch (Exception failure) when (recoverable(failure))
            {
                RestoreLevel(cpu, property, provider, before, recoverable);
                throw;
            }

            var after = Reflection.ReadValue(property, provider, recoverable, Policy.NoPasswordReadNote);
            if (after.Readable && !string.Equals(after.Value, info.AccessLevel, StringComparison.Ordinal))
                throw new PlcSecurityBindingException(WorkerFailureCategories.PostconditionFailed,
                    $"PlcProtectionAccessLevel of PLC '{cpu.PlcName}' reads '{after.Value}' after setting '{info.AccessLevel}' (before: {before.Value ?? "unreadable"}). Inspect it with plc_read get_plc_security.");

            var warnings = new List<string>();
            if (!after.Readable)
                warnings.Add($"PlcProtectionAccessLevel of PLC '{cpu.PlcName}' was set to {info.AccessLevel} but could not be read back ({after.Note}) Confirm with compile_check scope hardware.");
            var missing = Policy.LevelsAbove(info.AccessLevel!).Where(level => level == "FullAccess" && ordered.All(p => p.Level != level)).ToList();
            if (missing.Count > 0)
                warnings.Add($"PLC '{cpu.PlcName}' is now at {info.AccessLevel}; TIA Portal needs a FullAccess password for it, and none was set in this call (an earlier one may exist). The hardware compile (compile_check scope hardware) reports a missing one.");
            return (new PlcSecurityWriteResultInfo
            {
                Operation = PlcSecurityMethods.SetAccessLevel,
                PlcName = cpu.PlcName,
                DeviceName = cpu.DeviceName,
                Setting = Policy.Settings.AccessLevel,
                ServiceType = type.FullName ?? type.Name,
                Member = $"{Policy.AccessLevelProperty} {{ set; }}",
                Requested = info.AccessLevel,
                Before = before.Value,
                BeforeReadable = before.Readable,
                After = after.Value,
                AfterReadable = after.Readable,
                ReadBack = after.Readable ? Policy.ReadBacks.Matched : Policy.ReadBacks.Unreadable,
                Calls = calls,
                PasswordSources = ordered.Select(p => Use(p.Level, p.Source)).ToList(),
                Note = before.Readable ? null : before.Note,
            }, warnings);
        }
        finally
        {
            foreach (var secret in secrets) secret.Dispose();
        }
    }

    private static void RestoreLevel(PlcSecurityCpu cpu, System.Reflection.PropertyInfo property, object provider,
        (bool Readable, string? Value, string? Note) before, Func<Exception, bool> recoverable)
    {
        if (!before.Readable || before.Value is null)
        {
            Console.Error.WriteLine($"set_plc_access_level: a password call failed after PlcProtectionAccessLevel of PLC '{cpu.PlcName}' was set; the level before was unreadable and is not restored. Inspect it with plc_read get_plc_security.");
            return;
        }

        try
        {
            Reflection.SetValue(property, provider, Reflection.ParseExact(property.PropertyType, before.Value));
            Console.Error.WriteLine($"set_plc_access_level: a password call failed after PlcProtectionAccessLevel of PLC '{cpu.PlcName}' was set; the level was restored to {before.Value}.");
        }
        catch (Exception restore) when (recoverable(restore))
        {
            Console.Error.WriteLine($"set_plc_access_level: a password call failed after PlcProtectionAccessLevel of PLC '{cpu.PlcName}' was set, and restoring {before.Value} failed ({restore.Message}). Inspect it with plc_read get_plc_security.");
        }
    }

    private static (PlcSecurityWriteResultInfo, List<string>) ConfidentialData(string method, PlcSecurityCpu cpu, PlcSecurityRequestInfo info,
        Func<Exception, bool> recoverable, Func<string, System.Security.SecureString> resolve)
    {
        var type = Reflection.FindByShortName(cpu.Offered, Policy.MasterSecretConfiguratorName)
            ?? throw new PlcSecurityBindingException(WorkerFailureCategories.UnsupportedCapability,
                $"The CPU of PLC '{cpu.PlcName}' does not list a {Policy.MasterSecretConfiguratorName} among its services. Nothing was changed.");
        var current = info.PasswordVariable;
        var (member, arguments) = method switch
        {
            PlcSecurityMethods.ProtectConfidentialData when info.AllConfiguration && current is not null
                => (Reflection.RequireMethod(type, Policy.ProtectAllWithPasswordMethod, "void ProtectAllPlcConfigurationWithPassword(SecureString)", Reflection.IsSecureString), new[] { current! }),
            PlcSecurityMethods.ProtectConfidentialData when info.AllConfiguration
                => (Reflection.RequireMethod(type, Policy.ProtectAllMethod, "void ProtectAllPlcConfiguration()"), Array.Empty<string>()),
            PlcSecurityMethods.ProtectConfidentialData when current is not null
                => (Reflection.RequireMethod(type, Policy.ProtectMethod, "void Protect(SecureString)", Reflection.IsSecureString), new[] { current! }),
            PlcSecurityMethods.ProtectConfidentialData
                => throw new PlcSecurityBindingException(WorkerFailureCategories.ValidationError,
                    "protect_plc_confidential_data needs passwordVariable (or the server password map, purpose PlcMasterSecretPassword) unless allConfiguration is true. Nothing was changed."),
            PlcSecurityMethods.ChangeConfidentialDataPassword when current is not null && info.NewPasswordVariable is not null
                => (Reflection.RequireMethod(type, Policy.ChangePasswordMethod, "void ChangePassword(SecureString, SecureString)", Reflection.IsSecureString, Reflection.IsSecureString),
                    new[] { current!, info.NewPasswordVariable! }),
            PlcSecurityMethods.ChangeConfidentialDataPassword
                => throw new PlcSecurityBindingException(WorkerFailureCategories.ValidationError,
                    "change_plc_confidential_data_password needs passwordVariable (the current password) and newPasswordVariable. Nothing was changed."),
            _ when info.AllConfiguration
                => (Reflection.RequireMethod(type, Policy.UnprotectAllMethod, "void UnprotectAllPlcConfiguration()"), Array.Empty<string>()),
            _ when current is not null
                => (Reflection.RequireMethod(type, Policy.UnprotectMethod, "void Unprotect(SecureString)", Reflection.IsSecureString), new[] { current! }),
            _ => (Reflection.RequireMethod(type, Policy.UnprotectMethod, "void Unprotect()"), Array.Empty<string>()),
        };
        var configurator = Retrieve(cpu, type);
        var secrets = new List<System.Security.SecureString>();
        try
        {
            foreach (var token in arguments) secrets.Add(resolve(token));
            var before = Reflection.Observe(type, configurator, recoverable);
            Reflection.Invoke(member, configurator, secrets.Cast<object?>().ToArray());
            var after = Reflection.Observe(type, configurator, recoverable);
            var signature = Reflection.Signature(member);
            var uses = new List<PlcSecurityPasswordUseInfo>();
            if (arguments.Length > 0) uses.Add(Use(Policy.MasterSecretPurpose, arguments[0]));
            if (arguments.Length > 1) uses.Add(Use(Policy.MasterSecretPurpose + ":new", arguments[1]));
            return (new PlcSecurityWriteResultInfo
            {
                Operation = method,
                PlcName = cpu.PlcName,
                DeviceName = cpu.DeviceName,
                Setting = Policy.Settings.ConfidentialDataProtection,
                ServiceType = type.FullName ?? type.Name,
                Member = signature,
                ReadBack = Policy.ReadBacks.NotAvailable,
                ObservedBefore = before,
                ObservedAfter = after,
                Calls = new List<string> { signature },
                PasswordSources = uses,
                Note = $"{signature} returned without an error. {Policy.MasterSecretConfigurationProperty} (documented as the PLC protection type; its values are not documented) is reported in observedBefore/observedAfter when readable; confirm with compile_check scope hardware.",
            }, new List<string>());
        }
        finally
        {
            foreach (var secret in secrets) secret.Dispose();
        }
    }
}
