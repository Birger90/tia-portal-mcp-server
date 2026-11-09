using System.Reflection;
using Siemens.Engineering.Connection;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcOnline;

/// <summary>
/// Audit N4 (not live-tested): a caller node address for plc_download and go_online. V21 <c>Siemens.Engineering.Base.xml</c> documents
/// <c>ConfigurationTargetInterface.Addresses</c> and <c>ConfigurationPcInterface.Addresses</c> ("Composition of configurationAddress"),
/// <c>ConfigurationAddressComposition.Create(String)</c> ("Create an address"),
/// <c>DownloadProvider.Download(IConfiguration, ConfigurationAddress, DownloadConfigurationDelegate, DownloadConfigurationDelegate,
/// DownloadOptions)</c> ("Download Hardware and Software to a target with specific IP-Address"), the matching
/// <c>RHDownloadProvider.DownloadToPrimary/DownloadToBackup</c> overloads and <c>OnlineProvider.GoOnline(ConfigurationAddress)</c>
/// ("Command to go online with custom Ip address"). The XML does not say which composition the address must come from; the download
/// takes the selected target interface's, go_online the PC interface's (as station upload already does), following the Siemens
/// online-and-download guidance (third-party skill, not verified). Members that are not in the compile stubs are bound at runtime by
/// exact name and types before anything is sent (<c>api_member_missing</c>).
/// </summary>
internal static class PlcTargetAddresses
{
    /// <summary><c>ConfigurationTargetInterface.Addresses.Create(address)</c>; <c>Addresses</c> is not in the stubs and is read by reflection.</summary>
    public static ConfigurationAddress OnTargetInterface(ConfigurationTargetInterface target, string address, string nothing)
    {
        var property = target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(p => p.Name == "Addresses" && p.GetIndexParameters().Length == 0 && typeof(ConfigurationAddressComposition).IsAssignableFrom(p.PropertyType))
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                $"ConfigurationTargetInterface.Addresses is not available in this TIA Portal, so targetAddress cannot be used. {nothing}");
        var addresses = Unwrap(() => property.GetValue(target, null)) as ConfigurationAddressComposition
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"ConfigurationTargetInterface.Addresses returned nothing. {nothing}");
        return Create(addresses, address, nothing);
    }

    /// <summary><c>ConfigurationPcInterface.Addresses.Create(address)</c> (compile stubs).</summary>
    public static ConfigurationAddress OnPcInterface(ConfigurationPcInterface pcInterface, string address, string nothing)
        => Create(pcInterface.Addresses, address, nothing);

    /// <summary>The single public instance method <paramref name="name"/>(<paramref name="types"/>) of <paramref name="owner"/>, or api_member_missing.</summary>
    public static MethodInfo Bind(object owner, string name, Type[] types, string documented, string nothing)
        => LibraryRuntimeBinding.Find(owner.GetType(), name, types, out var error)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                $"{error ?? $"{documented} is not available in this TIA Portal."} ({documented}) {nothing}");

    private static ConfigurationAddress Create(ConfigurationAddressComposition addresses, string address, string nothing)
        => addresses.Create(address) ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
            $"TIA Portal created no configuration address for '{address}'. {nothing}");

    private static object? Unwrap(Func<object?> read)
    {
        try
        {
            return read();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
