using System.Reflection;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>
/// Audit N3 (not live-tested): <c>plc_read get_station_fingerprint_data</c>. Evidence: V21 <c>Siemens.Engineering.Base.xml</c>
/// (members.txt): <c>Siemens.Engineering.FingerprintData.FingerprintDataProvider</c> ("Service provides fingerprintdata functionality")
/// with <c>Configuration</c> ("Connection Configuration.") and <c>GetFingerprintData(ConfigurationAddress, OnlineConfigurationDelegate)</c>;
/// <c>FingerprintDataResult.FingerprintDataItems</c>; <c>FingerprintDataItem.FingerprintDataIdentifier</c> and <c>.FingerprintDataValue</c>.
/// No type of these members is in the compile stubs and the XML does not say which object offers the provider: it is looked up by exact
/// full name among the <c>GetServiceInfos()</c> of the TIA Portal, then of the bound project (never guessed further), and every member is
/// bound at runtime. The identifier and value types are not documented, so each is projected as text (<see cref="RuntimeValueInfo"/>).
/// Siemens-free and shared by worker and FakeWorker.
/// </summary>
public static class PlcFingerprintData
{
    public const string ProviderTypeName = "Siemens.Engineering.FingerprintData.FingerprintDataProvider";
    public const string GetMethodName = "GetFingerprintData";
    public const string Documented = "FingerprintDataProvider.GetFingerprintData(ConfigurationAddress, OnlineConfigurationDelegate)";
    public const int DefaultMaxItems = 200;
    public const int MaxItems = 2000;

    /// <summary>The provider's <c>Configuration</c> (exactly one public readable instance property of that name).</summary>
    public static object Configuration(object provider)
    {
        var property = LibraryRuntimeBinding.Property(provider.GetType(), "Configuration", out var error)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"{error} (FingerprintDataProvider.Configuration) Nothing was read.");
        return Read(provider, property) ?? throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed,
            "FingerprintDataProvider.Configuration returned null; nothing was read.");
    }

    /// <summary>Binds <c>GetFingerprintData(addressType, delegateType)</c> on the live provider (api_member_missing otherwise).</summary>
    public static MethodInfo Bind(object provider, Type addressType, Type delegateType)
        => LibraryRuntimeBinding.Find(provider.GetType(), GetMethodName, new[] { addressType, delegateType }, out var error)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"{error ?? "No " + Documented + "."} ({Documented}) Nothing was read.");

    /// <summary>Projects <c>FingerprintDataResult.FingerprintDataItems</c> into <paramref name="info"/> (at most <paramref name="max"/> items).</summary>
    public static void Project(object? result, int max, PlcStationFingerprintDataInfo info, Func<Exception, bool> recoverable)
    {
        if (result is null)
            throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed, $"{Documented} returned null.");
        var itemsProperty = LibraryRuntimeBinding.Property(result.GetType(), "FingerprintDataItems", out var error)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"{error} (FingerprintDataResult.FingerprintDataItems)");
        var items = RuntimeServiceValues.Elements(Read(result, itemsProperty), "FingerprintDataResult.FingerprintDataItems");
        info.ItemCount = items.Count;
        foreach (var item in items.Take(max))
        {
            if (item is null) continue;
            info.Items.Add(new PlcFingerprintDataItemInfo
            {
                Identifier = RuntimeServiceValues.ReadProperty(item, "FingerprintDataIdentifier", recoverable),
                Value = RuntimeServiceValues.ReadProperty(item, "FingerprintDataValue", recoverable),
            });
        }

        info.Truncated = items.Count > info.Items.Count;
    }

    private static object? Read(object target, PropertyInfo property)
    {
        try
        {
            return property.GetValue(target, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}

/// <summary>Audit N3: the fingerprint data of the station at one node address.</summary>
public class PlcStationFingerprintDataInfo
{
    public string Operation { get; set; } = PlcOnlineMethods.GetStationFingerprintData;

    /// <summary>Where the provider was found: <c>portal</c> or <c>project</c> (their <c>GetServiceInfos()</c>).</summary>
    public string ProviderOwner { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string PcInterfaceName { get; set; } = string.Empty;

    public int PcInterfaceNumber { get; set; }

    public string TargetAddress { get; set; } = string.Empty;

    /// <summary>Every item TIA Portal returned, counted before the cap.</summary>
    public int ItemCount { get; set; }

    public bool Truncated { get; set; }

    public int MaxItems { get; set; }

    public List<PlcFingerprintDataItemInfo> Items { get; set; } = new List<PlcFingerprintDataItemInfo>();

    /// <summary>The online legitimation configurations TIA Portal presented (passwords, user login, TLS), like compare_to_online.</summary>
    public List<PlcCallbackConfigurationInfo> ConfigurationsSeen { get; set; } = new List<PlcCallbackConfigurationInfo>();

    /// <summary>The refused configuration on failure evidence; null on success.</summary>
    public PlcCallbackAbortInfo? Abort { get; set; }
}

/// <summary>One <c>FingerprintDataItem</c>: identifier and value of undocumented type, as text.</summary>
public class PlcFingerprintDataItemInfo
{
    public RuntimeValueInfo Identifier { get; set; } = new RuntimeValueInfo();

    public RuntimeValueInfo Value { get; set; } = new RuntimeValueInfo();
}
