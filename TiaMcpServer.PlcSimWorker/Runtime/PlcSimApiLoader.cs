using System.Diagnostics;
using System.Reflection;
using TiaMcpServer.Contracts.PlcSim;

namespace TiaMcpServer.PlcSimWorker.Runtime;

/// <summary>The outcome of locating and loading the API: a type lookup when loaded, otherwise why not.</summary>
internal sealed class PlcSimLoadedApi
{
    public PlcSimApiLocationInfo Location { get; init; } = new();

    /// <summary>Resolves a full type name in the loaded API assembly; null when the API is not loaded.</summary>
    public Func<string, Type?>? Lookup { get; init; }

    public string? LoadError { get; init; }

    public string? AssemblyName { get; init; }

    public string? AssemblyVersion { get; init; }

    public string? FileVersion { get; init; }

    public string? ProductVersion { get; init; }

    public bool Loaded => Lookup is not null;
}

/// <summary>
/// Loads <c>Siemens.Simatic.Simulation.Runtime.Api.x64.dll</c> with <see cref="Assembly.LoadFile(string)"/>,
/// as the Siemens manual prescribes, and resolves the API's own dependencies from the same
/// directory only. The <see cref="AppDomain.AssemblyResolve"/> handler answers only binds whose
/// requesting assembly is the loaded API assembly, a dependency this handler loaded for it, or another
/// assembly whose file lies in the API folder, and only with a file of that folder whose assembly name
/// matches the requested name (simple name, and version, culture and public key token when the
/// request states them). Every other failed bind in the process is left to .NET. No Siemens assembly
/// is referenced at compile time.
/// </summary>
internal static class PlcSimApiLoader
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Assembly> LoadedByDirectory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Assembly> Siblings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every assembly this loader loaded (the API and its siblings), with the API folder it came from.</summary>
    private static readonly Dictionary<Assembly, string> LoadedFrom = new();
    private static bool _resolverRegistered;

    public static PlcSimLoadedApi Load(PlcSimApiLocationInfo location)
    {
        if (!location.DllExists || location.DllPath is null || location.ApiDirectory is null)
            return new PlcSimLoadedApi { Location = location, LoadError = location.Problem ?? "The S7-PLCSIM Advanced API DLL was not found." };

        try
        {
            Assembly assembly;
            lock (Gate)
            {
                if (!LoadedByDirectory.TryGetValue(location.ApiDirectory, out assembly!))
                {
                    RegisterResolver();
                    assembly = Assembly.LoadFile(location.DllPath);
                    LoadedByDirectory[location.ApiDirectory] = assembly;
                    LoadedFrom[assembly] = location.ApiDirectory;
                }
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(location.DllPath);
            return new PlcSimLoadedApi
            {
                Location = location,
                Lookup = name => assembly.GetType(name, throwOnError: false, ignoreCase: false),
                AssemblyName = assembly.FullName,
                AssemblyVersion = assembly.GetName().Version?.ToString(),
                FileVersion = versionInfo.FileVersion,
                ProductVersion = versionInfo.ProductVersion
            };
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or FileNotFoundException
            or IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return new PlcSimLoadedApi
            {
                Location = location,
                LoadError = $"Loading '{location.DllPath}' failed: {exception.GetType().Name}: {exception.Message}"
            };
        }
    }

    private static void RegisterResolver()
    {
        if (_resolverRegistered)
            return;
        _resolverRegistered = true;
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) => Resolve(args.Name, args.RequestingAssembly);
    }

    private static Assembly? Resolve(string requestedName, Assembly? requesting)
    {
        AssemblyName requested;
        try
        {
            requested = new AssemblyName(requestedName);
        }
        catch (Exception exception) when (exception is ArgumentException or FileLoadException)
        {
            return null;
        }

        lock (Gate)
        {
            // Binds requested by anything else (the worker, the runtime, an unknown requester) are not ours.
            var directory = RequestingApiDirectory(requesting, LoadedFrom, LoadedByDirectory.Keys);
            if (directory is null)
                return null;
            var api = LoadedByDirectory.TryGetValue(directory, out var loaded) ? loaded : null;
            if (api is not null && Matches(requested, api.GetName()))
                return api;
            var path = SelectDependency(requested, directory, File.Exists, AssemblyName.GetAssemblyName);
            if (path is null)
                return null;
            if (Siblings.TryGetValue(path, out var cached))
                return cached;
            Assembly sibling;
            try
            {
                sibling = Assembly.LoadFile(path);
            }
            catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or FileNotFoundException
                or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                // The bind then fails as .NET would report it without this handler.
                return null;
            }

            Siblings[path] = sibling;
            LoadedFrom[sibling] = directory;
            return sibling;
        }
    }

    /// <summary>
    /// The API folder a bind request belongs to: the folder this loader loaded
    /// <paramref name="requesting"/> from, otherwise the API folder that holds the requesting
    /// assembly's file. Null for an unknown, dynamic or location-less requester, or one elsewhere.
    /// </summary>
    internal static string? RequestingApiDirectory(Assembly? requesting, IReadOnlyDictionary<Assembly, string> loadedFrom, IEnumerable<string> apiDirectories)
    {
        if (requesting is null)
            return null;
        if (loadedFrom.TryGetValue(requesting, out var directory))
            return directory;
        if (requesting.IsDynamic)
            return null;
        try
        {
            var location = requesting.Location;
            if (string.IsNullOrEmpty(location))
                return null;
            var folder = Normalize(Path.GetDirectoryName(Path.GetFullPath(location)));
            return apiDirectories.FirstOrDefault(apiDirectory => string.Equals(Normalize(apiDirectory), folder, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or PathTooLongException or System.Security.SecurityException)
        {
            return null;
        }

        static string Normalize(string? path)
            => string.IsNullOrEmpty(path) ? string.Empty : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// The file in <paramref name="apiDirectory"/> that may satisfy <paramref name="requested"/>:
    /// <c>&lt;simple name&gt;.dll</c> whose own assembly name matches, or null.
    /// </summary>
    internal static string? SelectDependency(AssemblyName requested, string apiDirectory, Func<string, bool> fileExists, Func<string, AssemblyName> readName)
    {
        if (string.IsNullOrEmpty(requested.Name) || requested.Name!.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;
        var path = Path.Combine(apiDirectory, requested.Name + ".dll");
        if (!fileExists(path))
            return null;
        AssemblyName actual;
        try
        {
            actual = readName(path);
        }
        catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or FileNotFoundException
            or IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return null;
        }

        return Matches(requested, actual) ? path : null;
    }

    /// <summary>Simple name always; version, culture and public key token only when the request states them.</summary>
    internal static bool Matches(AssemblyName requested, AssemblyName actual)
    {
        if (!string.Equals(requested.Name, actual.Name, StringComparison.OrdinalIgnoreCase))
            return false;
        if (requested.Version is not null && requested.Version != actual.Version)
            return false;
        if (requested.CultureName is { Length: > 0 } culture && !string.Equals(culture, actual.CultureName, StringComparison.OrdinalIgnoreCase))
            return false;
        var token = requested.GetPublicKeyToken();
        return token is not { Length: > 0 } || (actual.GetPublicKeyToken() ?? Array.Empty<byte>()).SequenceEqual(token);
    }
}
