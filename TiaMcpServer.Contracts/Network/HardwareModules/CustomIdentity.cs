using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Network;

/// <summary>
/// Names of the custom identity members (GitHub issue #18). Evidence: the V21 IntelliSense file Siemens.Engineering.Base.xml,
/// <c>M:Siemens.Engineering.CustomIdentity.CustomIdentityProvider.Set(System.String,System.String)</c> ("The user defined key
/// and value to set"), <c>.Get(System.String)</c> (returns <c>System.String</c>), <c>.Remove(System.String)</c> ("Throws
/// <c>CustomIdentityNotFoundException</c> if CustomIdentity for a given key is not found") and
/// <c>T:Siemens.Engineering.CustomIdentity.CustomIdentityNotFoundException</c> ("Throws if CustomIdentity is not found for a
/// given key"). The XML does not say which objects offer the provider; it is taken from the object's <c>GetServiceInfos()</c>.
/// </summary>
public static class CustomIdentityNames
{
    public const string Provider = "Siemens.Engineering.CustomIdentity.CustomIdentityProvider";
    public const string NotFoundException = "Siemens.Engineering.CustomIdentity.CustomIdentityNotFoundException";

    public static class States
    {
        /// <summary>Get returned a string.</summary>
        public const string Found = "found";

        /// <summary>Get threw <c>CustomIdentityNotFoundException</c>.</summary>
        public const string NotFound = "notFound";

        /// <summary>Get returned null (not documented as a possibility; reported as observed).</summary>
        public const string Null = "null";

        public static IReadOnlyList<string> All { get; } = new[] { Found, NotFound, Null };
    }
}

/// <summary>What <c>CustomIdentityProvider.Get(key)</c> observed: <see cref="CustomIdentityNames.States"/> and the value when found.</summary>
public sealed class CustomIdentityObservationInfo
{
    public string State { get; set; } = CustomIdentityNames.States.NotFound;

    public string? Value { get; set; }
}

/// <summary>Result of get_custom_identity.</summary>
public sealed class HardwareCustomIdentityReadInfo
{
    public HardwareObjectIdentityInfo Target { get; set; } = new();

    public string Key { get; set; } = string.Empty;

    public CustomIdentityObservationInfo Observed { get; set; } = new();

    /// <summary>The runtime type of the provider the object returned.</summary>
    public string ProviderType { get; set; } = string.Empty;
}

/// <summary>Result of set_custom_identity and remove_custom_identity: before and the read-back after.</summary>
public sealed class HardwareCustomIdentityWriteResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public HardwareObjectIdentityInfo Target { get; set; } = new();

    public string Key { get; set; } = string.Empty;

    /// <summary>The requested value of set_custom_identity; null for remove_custom_identity.</summary>
    public string? RequestedValue { get; set; }

    public CustomIdentityObservationInfo Before { get; set; } = new();

    public CustomIdentityObservationInfo After { get; set; } = new();

    public string ProviderType { get; set; } = string.Empty;
}

/// <summary>
/// The Siemens-free calls on a live <c>CustomIdentityProvider</c>, shared by the worker and the FakeWorker. Each member is bound
/// by exact name and the documented <see cref="string"/> parameters (<see cref="RuntimeServiceValues.Invoke"/>; missing is
/// api_member_missing); <c>CustomIdentityNotFoundException</c> is recognised by its exact full name on the thrown exception
/// or one of its base types. Writes are read back with <c>Get</c>: a set must read back the value, a remove must no longer find
/// the key; anything else is postcondition_failed. Other exceptions pass through unchanged for the shared classification.
/// </summary>
public static class CustomIdentityRun
{
    public static bool IsNotFound(Exception exception)
    {
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
            if (string.Equals(type.FullName, CustomIdentityNames.NotFoundException, StringComparison.Ordinal)) return true;
        return false;
    }

    public static CustomIdentityObservationInfo Get(object provider, string key)
    {
        object? value;
        try
        {
            value = RuntimeServiceValues.Invoke(provider, "Get", new[] { typeof(string) }, new object?[] { key }, "CustomIdentityProvider.Get(String)");
        }
        catch (Exception exception) when (IsNotFound(exception))
        {
            return new CustomIdentityObservationInfo { State = CustomIdentityNames.States.NotFound };
        }

        return value is null
            ? new CustomIdentityObservationInfo { State = CustomIdentityNames.States.Null }
            : new CustomIdentityObservationInfo { State = CustomIdentityNames.States.Found, Value = value as string ?? value.ToString() };
    }

    public static (CustomIdentityObservationInfo Before, CustomIdentityObservationInfo After) Set(object provider, string key, string value, string description)
    {
        var before = Get(provider, key);
        RuntimeServiceValues.Invoke(provider, "Set", new[] { typeof(string), typeof(string) }, new object?[] { key, value }, "CustomIdentityProvider.Set(String, String)");
        var after = Get(provider, key);
        if (after.State != CustomIdentityNames.States.Found || !string.Equals(after.Value, value, StringComparison.Ordinal))
            throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                $"CustomIdentityProvider.Set('{key}', ...) on {description} returned, but Get('{key}') reads back {Describe(after)} instead of the requested value.");
        return (before, after);
    }

    public static (CustomIdentityObservationInfo Before, CustomIdentityObservationInfo After) Remove(object provider, string key, string description)
    {
        var before = Get(provider, key);
        try
        {
            RuntimeServiceValues.Invoke(provider, "Remove", new[] { typeof(string) }, new object?[] { key }, "CustomIdentityProvider.Remove(String)");
        }
        catch (Exception exception) when (IsNotFound(exception))
        {
            throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound,
                $"{description} has no custom identity with the key '{key}' (TIA Portal threw CustomIdentityNotFoundException); nothing was removed.");
        }

        var after = Get(provider, key);
        if (after.State == CustomIdentityNames.States.Found)
            throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                $"CustomIdentityProvider.Remove('{key}') on {description} returned, but Get('{key}') still reads {Describe(after)}.");
        return (before, after);
    }

    private static string Describe(CustomIdentityObservationInfo observed) => observed.State switch
    {
        CustomIdentityNames.States.Found => $"'{observed.Value}'",
        CustomIdentityNames.States.Null => "null",
        _ => "no entry (CustomIdentityNotFoundException)",
    };
}
