using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// What the settings operations see of one PLC (or of the project for a project-wide operation), supplied by the worker (live
/// Openness objects) and the FakeWorker (stand-ins with the documented member names). Nothing here is a Siemens type: the
/// objects are bound by exact member names at runtime.
/// </summary>
public sealed class PlcSettingsContext
{
    public string? PlcName { get; set; }

    public string? DeviceName { get; set; }

    /// <summary>The standalone project (project-wide services); null when the bound container is not one.</summary>
    public object? Project { get; set; }

    /// <summary>The CPU device item hosting the PLC software; null for a project-wide operation.</summary>
    public object? Cpu { get; set; }

    /// <summary>The PLC software (some services are documented on it).</summary>
    public object? Software { get; set; }

    /// <summary>The device that holds the CPU (UMAC: <c>device.GetService&lt;UmacDevice&gt;()</c>); null when unknown.</summary>
    public object? Device { get; set; }

    /// <summary>The device items below the CPU, depth first (the display, the OPC UA submodule, ...).</summary>
    public IReadOnlyList<object> CpuItems { get; set; } = Array.Empty<object>();

    /// <summary><c>GetServiceInfos()</c> of an object; an empty list when the object lists none or is no service provider.</summary>
    public Func<object, IReadOnlyList<Type>> ServiceTypes { get; set; } = _ => Array.Empty<Type>();

    /// <summary><c>IServiceProvider.GetService(Type)</c> of a listed service; null when it is not returned.</summary>
    public Func<object, Type, object?> Service { get; set; } = (_, _) => null;

    /// <summary>The <c>Name</c> of an engineering object, null when unreadable.</summary>
    public Func<object?, string?> NameOf { get; set; } = DefaultName;

    /// <summary>The watch (force: true) table of the PLC by name and group path; throws <see cref="RuntimeBindingException"/> when absent.</summary>
    public Func<string, string?, bool, object> Table { get; set; } = (name, _, _) =>
        throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"Table '{name}' was not found.");

    /// <summary>A device item of the CPU's device by exact name; null when there is none, a refusal when the name is ambiguous.</summary>
    public Func<string, object?> DeviceItem { get; set; } = _ => null;

    /// <summary>The culture object of an active project language (the manual passes <c>Language.Culture</c>); null when not active.</summary>
    public Func<string, object?> ActiveCulture { get; set; } = _ => null;

    /// <summary>Resolves a password-source token into a read-only SecureString (worker: environment and credentials).</summary>
    public Func<string, SecureString> Resolve { get; set; } = PlcSettingsRun.Resolver(Environment.GetEnvironmentVariable, null);

    public Func<Exception, bool> Recoverable { get; set; } = _ => false;

    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public static string? DefaultName(object? value)
    {
        if (value is null) return null;
        var property = value.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        try
        {
            return property?.GetValue(value, null) as string;
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    /// <summary>The owner list of a CPU-side service: the CPU device item, then the PLC software.</summary>
    internal IEnumerable<object> CpuOwners()
    {
        if (Cpu is not null) yield return Cpu;
        if (Software is not null) yield return Software;
    }

    /// <summary>The owner list of a CPU-or-submodule service: the CPU, then its device items depth first.</summary>
    internal IEnumerable<object> CpuAndItems()
    {
        if (Cpu is not null) yield return Cpu;
        foreach (var item in CpuItems) yield return item;
    }

    internal string Owner(object owner)
        => ReferenceEquals(owner, Project) ? PlcSettingsNames.ProjectOwner
            : ReferenceEquals(owner, Software) ? $"PLC software '{NameOf(owner) ?? PlcName}'"
            : PlcSettingsNames.DeviceItemOwner(NameOf(owner) ?? "(unnamed)");
}

/// <summary>
/// The Siemens-free engine of the settings family, shared by the worker and the FakeWorker: services are found among the
/// owner's <c>GetServiceInfos()</c> by exact full name, every property and method is bound by exact name (and parameter types)
/// on the live object, values are converted to the live property type from invariant text and read back. Failures are
/// <see cref="RuntimeBindingException"/> with their category; the caller turns them into its own failure type. Passwords are
/// resolved from password-source tokens right before the Siemens call and disposed after it; none is generated, returned or logged.
/// </summary>
public static partial class PlcSettingsRun
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>The resolver the worker and the FakeWorker pass (<see cref="PasswordSources.Resolve"/>); the reason never holds a secret.</summary>
    public static Func<string, SecureString> Resolver(Func<string, string?> readEnvironment, ICredentialReader? credentials)
        => token =>
        {
            var secure = PasswordSources.Resolve(token, readEnvironment, credentials, out var reason);
            if (secure is not null) return secure;
            throw new RuntimeBindingException(
                reason == WindowsCredentialReader.UnsupportedReason ? WorkerFailureCategories.UnsupportedCapability : WorkerFailureCategories.ValidationError,
                (reason ?? "The password source could not be read.") + " Nothing was changed.");
        };

    public static PlcSettingsReadInfo Read(string method, IReadOnlyList<PlcSettingsContext> plcs, PlcSettingsContext? project, PlcSettingsRequestInfo? info = null)
    {
        var result = new PlcSettingsReadInfo { Operation = method };
        switch (method)
        {
            case PlcCpuSettingsMethods.GetCpuSettings:
                foreach (var plc in plcs) result.Plcs.Add(new PlcSettingsPlcInfo { PlcName = plc.PlcName ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty, Sections = CpuSections(plc) });
                if (project is not null) result.Project = ProjectSections(project);
                return result;
            default:
                return ReadMore(method, plcs, project, result, info ?? new PlcSettingsRequestInfo());
        }
    }

    public static (PlcSettingsResultInfo Result, List<string> Warnings) Write(string method, PlcSettingsContext context, PlcSettingsRequestInfo info)
    {
        var definition = PlcSettingsCatalog.Find(method);
        if (definition is not { IsWrite: true })
            throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"Unsupported settings method '{method}'.");
        foreach (var setting in info.Settings)
            if (!definition.Settings.Contains(setting.Name, StringComparer.Ordinal))
                throw new RuntimeBindingException(WorkerFailureCategories.ValidationError,
                    $"{method} does not take the setting '{setting.Name}' (allowed: {string.Join(", ", definition.Settings)}). Nothing was changed.");
        var result = new PlcSettingsResultInfo { Operation = method, PlcName = context.PlcName, DeviceName = context.DeviceName };
        var warnings = new List<string>();
        WriteCore(method, definition, context, info, result, warnings);
        result.ReadBack = ReadBackOf(result.Changes);
        return (result, warnings);
    }

    private static void WriteCore(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info,
        PlcSettingsResultInfo result, List<string> warnings)
    {
        if (WriteCpu(method, definition, context, info, result, warnings)) return;
        if (WriteMore(method, definition, context, info, result, warnings)) return;
        throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"Unsupported settings method '{method}'.");
    }

    /// <summary>matched when every compared change matched, unreadable when an after value could not be read, otherwise notAvailable.</summary>
    internal static string ReadBackOf(IReadOnlyList<PlcSettingChangeInfo> changes)
    {
        if (changes.Any(c => c.After is { State: RuntimeValueStates.Unreadable })) return PlcSettingsNames.ReadBacks.Unreadable;
        var compared = changes.Where(c => c.Matched is not null).ToList();
        return compared.Count > 0 && compared.Count == changes.Count && compared.All(c => c.Matched == true)
            ? PlcSettingsNames.ReadBacks.Matched
            : PlcSettingsNames.ReadBacks.NotAvailable;
    }

    // ----- services -----

    /// <summary>The first owner that lists the service: (service, owner, null), (null, owner, reason) when listed but not returned, or (null, null, null).</summary>
    internal static (object? Service, object? Owner, string? Unavailable) FindService(PlcSettingsContext context, IEnumerable<object> owners, string fullName)
    {
        // K76: a listed service that is not returned by one owner does not stop the search; a later owner (e.g. a device item
        // below the CPU) that returns it wins, and only when none does is the first "listed but unavailable" reason reported.
        (object? Owner, string Reason)? firstUnavailable = null;
        foreach (var owner in owners)
        {
            var (service, unavailable) = RuntimeServiceValues.Find(context.ServiceTypes(owner), type => context.Service(owner, type), fullName);
            if (service is not null) return (service, owner, null);
            if (unavailable is not null) firstUnavailable ??= (owner, unavailable);
        }

        return firstUnavailable is { } listed ? (null, listed.Owner, listed.Reason) : (null, null, null);
    }

    /// <summary>The service a write needs; absent or not returned is unsupported_capability, nothing changed.</summary>
    internal static (object Service, object Owner) RequireService(PlcSettingsContext context, IEnumerable<object> owners, string fullName, string where)
    {
        var (service, owner, unavailable) = FindService(context, owners, fullName);
        if (service is not null) return (service, owner!);
        throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
            (unavailable ?? $"{where} does not list '{fullName}' among its GetServiceInfos(); this TIA Portal installation offers no such service there.") + " Nothing was changed.");
    }

    internal static string CpuWhere(PlcSettingsContext context) => $"The CPU of PLC '{context.PlcName}'";

    internal static object RequireProject(PlcSettingsContext context)
        => context.Project ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
            "The project-wide settings are reached through the Project's services; the bound container is not a standalone project. Nothing was changed.");

    // ----- members -----

    internal static PropertyInfo RequireProperty(object target, string name, bool writable)
    {
        var property = LibraryRuntimeBinding.Property(target.GetType(), name, out var error)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"{error} Nothing was changed.");
        if (writable && property.GetSetMethod() is null)
            throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                $"{name} of '{target.GetType().FullName}' has no public setter on this TIA Portal installation. Nothing was changed.");
        return property;
    }

    internal static MethodInfo RequireMethod(object target, string name, Type[] argumentTypes, string documented)
        => LibraryRuntimeBinding.Find(target.GetType(), name, argumentTypes, out var error)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"{error ?? $"No {name} on '{target.GetType().FullName}'."} ({documented}) Nothing was changed.");

    /// <summary>A method found by name and parameter count only, whose parameter types the caller checks (enum parameters of undocumented assemblies).</summary>
    internal static MethodInfo RequireMethodByShape(object target, string name, Func<ParameterInfo[], bool> accepts, string documented)
    {
        var candidates = target.GetType().GetMethods(PublicInstance).Where(m => m.Name == name && !m.IsGenericMethodDefinition && accepts(m.GetParameters()))
            .GroupBy(m => m.ToString()).Select(g => g.First()).ToList();
        return candidates.Count == 1 ? candidates[0]
            : throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                $"'{target.GetType().FullName}' exposes {(candidates.Count == 0 ? "no" : candidates.Count.ToString(CultureInfo.InvariantCulture))} {documented}; none is chosen. Nothing was changed.");
    }

    internal static object? Get(object target, string name)
    {
        var property = RequireProperty(target, name, writable: false);
        return Invoke(() => property.GetValue(target, null));
    }

    internal static object? Call(MethodInfo method, object target, params object?[] arguments) => RuntimeServiceValues.Call(method, target, arguments);

    private static T Invoke<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    internal static string Signature(MethodInfo method)
        => $"{method.ReturnType.Name} {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    internal static RuntimeValueInfo ReadValue(PlcSettingsContext context, object target, string name)
        => RuntimeServiceValues.ReadProperty(target, name, context.Recoverable);

    internal static bool IsWritable(object target, string name)
        => target.GetType().GetProperty(name, PublicInstance)?.GetSetMethod() is not null;

    // ----- attributes (IEngineeringObject.GetAttribute / SetAttribute, bound by name) -----

    internal static object? GetAttribute(object target, string name)
        => RuntimeServiceValues.Invoke(target, "GetAttribute", new[] { typeof(string) }, new object?[] { name }, "IEngineeringObject.GetAttribute(String)");

    internal static void SetAttribute(object target, string name, object? value)
    {
        var method = RequireMethod(target, "SetAttribute", new[] { typeof(string), typeof(object) }, "IEngineeringObject.SetAttribute(String, Object)");
        Call(method, target, name, value);
    }

    internal static RuntimeValueInfo ReadAttribute(PlcSettingsContext context, object target, string name, Func<object?, object?>? project = null)
    {
        try
        {
            var value = GetAttribute(target, name);
            return RuntimeServiceValues.Project(project is null ? value : project(value));
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

    // ----- values -----

    /// <summary>Converts invariant text to the live type: string, bool, integer types (range-checked), enum members (flags: "A, B"), DateTime (ISO 8601), Guid.</summary>
    internal static object ConvertValue(Type type, string text, string name)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        RuntimeBindingException Invalid(string expected)
            => new(WorkerFailureCategories.ValidationError, $"The setting '{name}' needs {expected} (the live type is {target.FullName}). Nothing was changed.");
        if (target == typeof(string)) return text;
        if (target == typeof(bool))
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? true
                : string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ? false : throw Invalid("true or false");
        if (target.IsEnum)
        {
            var parts = text.Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length > 1 && target.GetCustomAttributes(typeof(FlagsAttribute), false).Length == 0) throw Invalid("one member of " + LibraryRuntimeBinding.MemberList(target));
            ulong bits = 0;
            foreach (var part in parts)
            {
                var member = LibraryRuntimeBinding.EnumValue(target, part) ?? throw Invalid("a member of " + LibraryRuntimeBinding.MemberList(target));
                bits |= System.Convert.ToUInt64(member, CultureInfo.InvariantCulture);
            }

            return Enum.ToObject(target, bits);
        }

        if (target == typeof(DateTime))
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date : throw Invalid("an ISO 8601 date and time");
        if (target == typeof(Guid)) return Guid.TryParse(text, out var guid) ? guid : throw Invalid("a GUID");
        if (target == typeof(byte) || target == typeof(sbyte) || target == typeof(short) || target == typeof(ushort) || target == typeof(int)
            || target == typeof(uint) || target == typeof(long) || target == typeof(ulong))
        {
            if (!decimal.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) throw Invalid("an integer");
            try
            {
                return System.Convert.ChangeType(number, target, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                throw Invalid("an integer in the range of the type");
            }
        }

        if (target == typeof(double) || target == typeof(float) || target == typeof(decimal))
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
                ? System.Convert.ChangeType(real, target, CultureInfo.InvariantCulture) : throw Invalid("a number");
        throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
            $"The setting '{name}' has the live type {target.FullName}, which this server does not convert from text. Nothing was changed.");
    }

    /// <summary>
    /// Sets the given properties of one object in the order of <paramref name="order"/> (the documented order), each read
    /// before and after; every property is bound and every value converted before the first change, so a refusal changes nothing.
    /// A readable after value different from the request fails postcondition_failed.
    /// </summary>
    internal static void SetProperties(PlcSettingsContext context, object target, string what, IReadOnlyList<PlcSettingValueInfo> settings,
        IReadOnlyList<string> order, PlcSettingsResultInfo result)
    {
        var planned = settings.OrderBy(s => IndexOf(order, s.Name))
            .Select(s =>
            {
                var property = RequireProperty(target, s.Name, writable: true);
                return (Setting: s, Property: property, Value: ConvertValue(property.PropertyType, s.Value, s.Name));
            }).ToList();
        foreach (var (setting, property, value) in planned)
        {
            var before = ReadValue(context, target, setting.Name);
            Invoke(() =>
            {
                property.SetValue(target, value, null);
                return true;
            });
            result.Calls.Add($"{what}.{setting.Name} {{ set; }}");
            Compare(context, result, setting.Name, RuntimeServiceValues.Project(value).Value, before, ReadValue(context, target, setting.Name), what);
        }
    }

    /// <summary>Sets device item attributes (<c>SetAttribute(name, value)</c>) with the value converted to the current value's type when it has one.</summary>
    internal static void SetAttributes(PlcSettingsContext context, object target, string what, IReadOnlyList<PlcSettingValueInfo> settings,
        IReadOnlyList<string> order, PlcSettingsResultInfo result)
    {
        var planned = settings.OrderBy(s => IndexOf(order, s.Name)).Select(s =>
        {
            var current = GetAttribute(target, s.Name);
            var value = current is null ? (object)s.Value : ConvertValue(current.GetType(), s.Value, s.Name);
            return (Setting: s, Value: value, Before: RuntimeServiceValues.Project(current));
        }).ToList();
        foreach (var (setting, value, before) in planned)
        {
            SetAttribute(target, setting.Name, value);
            result.Calls.Add($"{what}.SetAttribute(\"{setting.Name}\", {value.GetType().Name})");
            Compare(context, result, setting.Name, RuntimeServiceValues.Project(value).Value, before, ReadAttribute(context, target, setting.Name), what);
        }
    }

    internal static void Compare(PlcSettingsContext context, PlcSettingsResultInfo result, string name, string? requested, RuntimeValueInfo? before,
        RuntimeValueInfo after, string what)
    {
        var matched = after.State is RuntimeValueStates.Value or RuntimeValueStates.Empty or RuntimeValueStates.Null
            ? string.Equals(after.Value ?? string.Empty, requested ?? string.Empty, StringComparison.Ordinal) : (bool?)null;
        result.Changes.Add(new PlcSettingChangeInfo { Name = name, Requested = requested, Before = before, After = after, Matched = matched });
        if (matched == false)
            throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                $"{what}.{name} of PLC '{context.PlcName ?? "(project)"}' reads '{after.Value}' after setting '{requested}' (before: {before?.Value ?? "unread"}). "
                + "Earlier changes of this item stay; inspect it with plc_read.");
    }

    /// <summary>A change that is an existence check after a create or delete (read back by Find).</summary>
    internal static void Exists(PlcSettingsResultInfo result, string name, bool expected, bool actual, string what, string? plc)
    {
        result.Changes.Add(new PlcSettingChangeInfo
        {
            Name = name,
            Requested = expected ? "exists" : "absent",
            After = RuntimeServiceValues.Project(actual ? "exists" : "absent"),
            Matched = expected == actual,
        });
        if (expected != actual)
            throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                $"{what} {(expected ? "is not found" : "is still found")} after the call (PLC '{plc ?? "(project)"}'). Inspect it with plc_read.");
    }

    private static int IndexOf(IReadOnlyList<string> order, string name)
    {
        for (var index = 0; index < order.Count; index++)
            if (order[index] == name) return index;
        return order.Count;
    }

    // ----- compositions -----

    internal static List<object?> Elements(object? collection, string documented) => RuntimeServiceValues.Elements(collection, documented);

    /// <summary>The composition's <c>Find(key)</c> result (null when absent).</summary>
    internal static object? Find(object composition, Type keyType, object key, string documented)
        => RuntimeServiceValues.Invoke(composition, "Find", new[] { keyType }, new[] { key }, documented);

    /// <summary>Calls <c>Delete()</c> on an object.</summary>
    internal static void Delete(object target, string documented, PlcSettingsResultInfo result)
    {
        var method = RequireMethod(target, "Delete", Type.EmptyTypes, documented);
        Call(method, target);
        result.Calls.Add(documented);
    }

    /// <summary>A collection of objects as items, with the given properties and a key property.</summary>
    internal static PlcSettingsCollectionInfo Collection(PlcSettingsContext context, string name, Func<object?> read, string key,
        IReadOnlyList<string> properties, Func<object, List<PlcSettingsCollectionInfo>>? nested = null, Func<object, string, RuntimeValueInfo?>? special = null)
    {
        var info = new PlcSettingsCollectionInfo { Name = name };
        try
        {
            foreach (var element in Elements(read(), name))
            {
                if (element is null) continue;
                if (info.Items.Count >= PlcSettingsNames.MaxItems)
                {
                    info.Truncated = true;
                    break;
                }

                var item = new PlcSettingsItemInfo();
                foreach (var property in properties)
                {
                    var value = special?.Invoke(element, property) ?? ReadValue(context, element, property);
                    item.Properties.Add(new PlcSettingPropertyInfo { Name = property, Value = value, Writable = IsWritable(element, property) });
                }

                var keyValue = special?.Invoke(element, key) ?? ReadValue(context, element, key);
                item.Key = keyValue.State == RuntimeValueStates.Value ? keyValue.Value : null;
                if (nested is not null) item.Collections = nested(element);
                info.Items.Add(item);
            }
        }
        catch (RuntimeBindingException exception)
        {
            info.Error = exception.Message;
        }
        catch (Exception exception) when (context.Recoverable(exception))
        {
            info.Error = RuntimeServiceValues.Bound($"{exception.GetType().Name}: {exception.Message}");
        }

        return info;
    }

    /// <summary>A section of one service: offered where, its properties, collections and bindable members.</summary>
    internal static PlcSettingsSectionInfo Section(PlcSettingsContext context, string name, IEnumerable<object> owners, string service,
        IReadOnlyList<string> properties, Func<object, List<PlcSettingsCollectionInfo>>? collections = null, Func<object, List<string>>? members = null, string? note = null)
    {
        var section = new PlcSettingsSectionInfo { Name = name, Service = service, Note = note };
        try
        {
            var (found, owner, unavailable) = FindService(context, owners, service);
            section.Owner = owner is null ? null : context.Owner(owner);
            section.Offered = owner is not null;
            section.Unavailable = unavailable;
            if (found is null) return section;
            foreach (var property in properties)
                section.Properties.Add(new PlcSettingPropertyInfo { Name = property, Value = ReadValue(context, found, property), Writable = IsWritable(found, property) });
            if (collections is not null) section.Collections = collections(found);
            if (members is not null) section.Members = members(found);
        }
        catch (RuntimeBindingException exception)
        {
            section.Unavailable = exception.Message;
        }
        catch (Exception exception) when (context.Recoverable(exception))
        {
            section.Unavailable = RuntimeServiceValues.Bound($"{exception.GetType().Name}: {exception.Message}");
        }

        return section;
    }

    /// <summary>The documented methods of <paramref name="service"/> that bind, as text.</summary>
    internal static List<string> Bindable(object service, params (string Name, Func<ParameterInfo[], bool> Accepts, string Text)[] methods)
        => methods.Where(m => service.GetType().GetMethods(PublicInstance).Any(candidate => candidate.Name == m.Name && m.Accepts(candidate.GetParameters())))
            .Select(m => m.Text).ToList();

    internal static bool IsSecureString(ParameterInfo parameter) => parameter.ParameterType == typeof(SecureString);

    /// <summary>Uses a resolved password and disposes it afterwards.</summary>
    internal static void WithPassword(PlcSettingsContext context, string? token, string purpose, PlcSettingsResultInfo result, Action<SecureString> run)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new RuntimeBindingException(WorkerFailureCategories.ValidationError,
                $"{result.Operation} needs passwordVariable: where the password is (an environment variable NAME or a credential target), never the password. Nothing was changed.");
        using var secure = context.Resolve(token!);
        run(secure);
        result.PasswordSources.Add(new PlcSettingsPasswordUseInfo { Purpose = purpose, Source = token!, Kind = PasswordSources.KindName(token) ?? string.Empty });
    }

    /// <summary>
    /// Writes a caller file under the alarm exports' rules (<see cref="PlcAlarmFiles"/>): prechecks, a temporary file next to the
    /// target that <paramref name="write"/> fills, a check that a file was written, the move into place and the re-hash.
    /// </summary>
    internal static PlcSettingsFileInfo WriteFile(PlcSettingsContext context, string? path, bool overwrite, string documented, Action<string> write, List<string> messages)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "filePath is required. Nothing was written.");
        Files(() => PlcAlarmFiles.PrecheckExport(path, overwrite, null));
        messages.AddRange(PlcAlarmFiles.Notes(PlcAlarmFiles.Inspect(path!)));
        var swept = PlcAlarmFiles.SweepStaleTemporaries(path!, context.UtcNow());
        if (swept > 0) messages.Add($"Removed {swept} temporary file(s) next to '{path}' that an interrupted earlier call left behind.");
        var temporary = PlcAlarmFiles.TemporaryPath(path!);
        try
        {
            write(temporary);
            if (!File.Exists(temporary))
                throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                    $"{documented} returned, but no file was written at the path it was given ('{temporary}'). Nothing was placed at the target.");
            var (overwritten, bytes, sha256) = Files(() => PlcAlarmFiles.Commit(temporary, path!, overwrite));
            return new PlcSettingsFileInfo { Path = path!, Bytes = bytes, Sha256 = sha256, Overwritten = overwritten };
        }
        finally
        {
            PlcAlarmFiles.Discard(temporary);
        }
    }

    internal static void RequireExistingFile(string? path, string field)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path))
            throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"{field} must name an existing file by its absolute path. Nothing was changed.");
    }

    private static T Files<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (PlcAlarmException exception)
        {
            throw new RuntimeBindingException(exception.Category, exception.Message);
        }
    }

    private static void Files(Action run) => Files<object?>(() =>
    {
        run();
        return null;
    });

    internal static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    internal static IEnumerable<object?> Enumerate(object? collection) => collection is IEnumerable enumerable && collection is not string ? enumerable.Cast<object?>() : Array.Empty<object?>();
}
