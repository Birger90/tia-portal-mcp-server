using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// The password policies and the runtime account locking (security review 2026-11-03, F1): every write of
/// <c>set_account_locking</c>, <c>set_plc_password_policy</c>, <c>set_legacy_plc_password_policy</c> and
/// <c>set_umac_password_policy</c> is planned against the current values, read once with the family's read
/// (<c>get_cpu_settings</c> or <c>get_umac</c>, Observe). A requested value weaker than the current one (a lower minimum
/// length, character count, reuse block or lock duration, a higher number of failed logins allowed, a longer password
/// validity, a protection switched off), a value that cannot be compared, or a current value that cannot be read fires
/// <see cref="PlcGuardDefinitions.LowersCpuProtection"/>; every other write of these operations fires the info guard
/// <see cref="PlcGuardDefinitions.ChangesPasswordPolicy"/>. Nothing is refused: TIA Portal allows each of these values.
/// </summary>
internal static partial class PlcSettings
{
    /// <summary>The operations planned against their current values.</summary>
    public static IReadOnlyList<string> PolicyOperations { get; } = new[]
    {
        PlcCpuSettingsMethods.SetAccountLocking, PlcCpuSettingsMethods.SetPlcPasswordPolicy,
        PlcCpuSettingsMethods.SetLegacyPlcPasswordPolicy, PlcUmacMethods.SetUmacPasswordPolicy,
    };

    public static bool IsPolicyOperation(string? operation) => operation is not null && PolicyOperations.Contains(operation, StringComparer.Ordinal);

    /// <summary>How a numeric or boolean setting relates to protection.</summary>
    internal enum Strength
    {
        /// <summary>true is stronger (a protection switched on).</summary>
        SwitchOn,

        /// <summary>A higher number is stronger (minimum lengths and counts, reuse block, lock duration, retry interval).</summary>
        HigherIsStronger,

        /// <summary>A lower number is stronger (failed logins allowed before locking, password validity).</summary>
        LowerIsStronger,

        /// <summary>No protection direction (the pre-warning time before a password expires).</summary>
        Neutral,
    }

    /// <summary>The direction of every setting of the four operations (names as in the catalog).</summary>
    internal static Strength DirectionOf(string setting) => setting switch
    {
        "EnableLockUserAccountAtRunTime" or "PasswordPolicyEnabled" or "IncludesLowerCaseAndUpperCaseCharacters" or "EnablePasswordAging" => Strength.SwitchOn,
        "MinimumLength" or "MinimumNumericCharacterLength" or "MinimumSpecialCharacterLength" or "MinimumUserPasswordsBlockedForReuse"
            or "LockAccountForDuration" or "LoginRetryInterval" => Strength.HigherIsStronger,
        "NumberOfFailedLoginAttempts" or "PasswordValidity" => Strength.LowerIsStronger,
        "PasswordValidityPrewarningTime" => Strength.Neutral,
        // A setting without a known direction: any change may weaken it (fail safe).
        _ => Strength.HigherIsStronger,
    };

    /// <summary>
    /// Whether <paramref name="requested"/> is weaker than <paramref name="current"/> for <paramref name="setting"/>: true when
    /// weaker, when either value cannot be read or compared (fail safe), false when equal or stronger.
    /// </summary>
    internal static bool IsWeaker(string setting, string? current, string requested)
    {
        var direction = DirectionOf(setting);
        if (direction == Strength.Neutral) return false;
        if (current is null) return true;
        if (direction == Strength.SwitchOn)
        {
            if (!bool.TryParse(current.Trim(), out var before) || !bool.TryParse(requested.Trim(), out var after)) return true;
            return before && !after;
        }

        if (!Number(current, out var was) || !Number(requested, out var will)) return true;
        return direction == Strength.HigherIsStronger ? will < was : will > was;
    }

    private static bool Number(string text, out decimal value)
    {
        if (decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value)) return true;
        if (TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var span))
        {
            value = (decimal)span.TotalSeconds;
            return true;
        }

        return false;
    }

    /// <summary>The planning read, its section and whether the read is PLC-scoped.</summary>
    private static (string Read, string Section, bool PlcScoped) PolicySource(string operation) => operation switch
    {
        PlcCpuSettingsMethods.SetAccountLocking => (PlcCpuSettingsMethods.GetCpuSettings, "accountLocking", true),
        PlcCpuSettingsMethods.SetPlcPasswordPolicy => (PlcCpuSettingsMethods.GetCpuSettings, "plcPasswordPolicy", false),
        PlcCpuSettingsMethods.SetLegacyPlcPasswordPolicy => (PlcCpuSettingsMethods.GetCpuSettings, "legacyPlcPasswordPolicy", false),
        _ => (PlcUmacMethods.GetUmac, "umacPasswordPolicy", false),
    };

    /// <summary>The settings planning: <see cref="Resolve"/> plus, for the policy operations, the comparison with the current values.</summary>
    public static async Task<(PlcWriteEffect Effect, IReadOnlyList<FiredGuard> Guards)> ResolveAsync(OpennessWorkerClient client, PlcOperationRequest item, PlcTagInventoryPlcInfo? plc)
    {
        if (!IsPolicyOperation(item.Operation)) return Resolve(item, plc);
        var (current, unreadable) = await CurrentPolicyAsync(client, item, plc).ConfigureAwait(false);
        return ResolvePolicy(item, plc, current, unreadable);
    }

    /// <summary>
    /// The current values of the item's section, by setting name (a value that is not readable is absent), and why the
    /// section could not be read (null when it was).
    /// </summary>
    internal static async Task<(IReadOnlyDictionary<string, string> Current, string? Unreadable)> CurrentPolicyAsync(OpennessWorkerClient client, PlcOperationRequest item, PlcTagInventoryPlcInfo? plc)
    {
        var (readOperation, sectionName, plcScoped) = PolicySource(item.Operation);
        var read = new PlcOperationRequest { OperationId = item.OperationId, Operation = readOperation, PlcName = plcScoped ? plc?.PlcName : null };
        WorkerCallResult call;
        try
        {
            call = await client.PlcSettingsAsync(readOperation, ToRequest(read)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (new Dictionary<string, string>(), $"the planning read {readOperation} failed ({exception.Message})");
        }

        if (!call.Success) return (new Dictionary<string, string>(), $"the planning read {readOperation} failed ({call.FailureCategory}: {call.Error})");
        PlcSettingsReadInfo info;
        try
        {
            info = CanonicalJson.Deserialize<PlcSettingsReadInfo>(DecodeRead(read, call.Payload ?? string.Empty).GetRawText());
        }
        catch (JsonException)
        {
            return (new Dictionary<string, string>(), $"the planning read {readOperation} did not decode");
        }

        return CurrentFrom(info, sectionName, plcScoped, plc);
    }

    /// <summary>The section's readable values from a decoded read (testable without a worker).</summary>
    internal static (IReadOnlyDictionary<string, string> Current, string? Unreadable) CurrentFrom(PlcSettingsReadInfo info, string sectionName, bool plcScoped, PlcTagInventoryPlcInfo? plc)
    {
        var sections = plcScoped
            ? info.Plcs.Where(p => plc is null || PlcNameRules.Comparer.Equals(p.PlcName, plc.PlcName)).Select(p => p.Sections).ToList()
            : new List<List<PlcSettingsSectionInfo>> { info.Project };
        if (sections.Count != 1) return (new Dictionary<string, string>(), $"the planning read describes {sections.Count} PLCs, not exactly one");
        var section = sections[0].FirstOrDefault(s => s.Name == sectionName);
        if (section is null) return (new Dictionary<string, string>(), $"the planning read has no section '{sectionName}'");
        if (!section.Offered) return (new Dictionary<string, string>(), $"section '{sectionName}' is not offered ({section.Unavailable ?? "no reason given"})");
        var values = section.Properties
            .Where(p => p.Value.State == RuntimeValueStates.Value && p.Value.Value is not null)
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value.Value!, StringComparer.Ordinal);
        return (values, null);
    }

    /// <summary>The policy planning from the current values (testable without a worker).</summary>
    internal static (PlcWriteEffect Effect, IReadOnlyList<FiredGuard> Guards) ResolvePolicy(PlcOperationRequest item, PlcTagInventoryPlcInfo? plc,
        IReadOnlyDictionary<string, string> current, string? unreadable)
    {
        var (effect, baseGuards) = Resolve(item, plc);
        var where = plc is null ? "the project" : $"the CPU of PLC '{plc.PlcName}'";
        var entries = item.Settings?.Entries ?? Array.Empty<KeyValuePair<string, string>>();
        // The comparison replaces the fixed lowering values of the definition for these operations.
        var guards = baseGuards.Where(g => g.Id != PlcGuardDefinitions.LowersCpuProtection).ToList();
        var changes = entries.Select(e => new PlcFieldChange(e.Key, current.TryGetValue(e.Key, out var before) ? before : null, e.Value)).ToList();
        changes.AddRange(effect.Changes.Where(c => entries.All(e => e.Key != c.Field)));
        var weaker = new List<string>();
        foreach (var entry in entries)
        {
            var before = current.TryGetValue(entry.Key, out var value) ? value : null;
            if (IsWeaker(entry.Key, before, entry.Value))
                weaker.Add(before is null ? $"{entry.Key}={entry.Value} (current value unreadable)" : $"{entry.Key} {before} -> {entry.Value}");
        }

        if (unreadable is not null)
            guards.Insert(0, new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                $"{item.Operation}: the current values of {where} could not be read for planning ({unreadable}), so the change may lower a protection "
                + $"({string.Join(", ", entries.Select(e => $"{e.Key}={e.Value}"))}); it takes effect on the CPU after a download."));
        else if (weaker.Count > 0)
            guards.Insert(0, new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                $"{item.Operation} lowers a protection of {where} in the offline project ({string.Join(", ", weaker)}); it takes effect on the CPU after a download."));
        else
            guards.Insert(0, new FiredGuard(PlcGuardDefinitions.ChangesPasswordPolicy, item.OperationId,
                $"{item.Operation} changes the password or account locking policy of {where} ({string.Join(", ", entries.Select(e => $"{e.Key}={e.Value}"))}); "
                + "no requested value is weaker than the current one read in planning."));
        return (effect with { Changes = changes }, guards);
    }
}
