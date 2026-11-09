using System.Reflection;
using System.Runtime.ExceptionServices;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Block;

/// <summary>
/// Audit N8/N7: the caller-selectable import options of the SimaticML and SIMATIC SD block/type imports, Siemens-free and shared
/// by host, worker and FakeWorker. Evidence: V21 <c>Siemens.Engineering.Step7.xml</c>:
/// <c>T:Siemens.Engineering.SW.SWImportOptions</c> with <c>None</c>, <c>IgnoreStructuralChanges</c> ("Import is not aborted in case
/// of referenced object changed structurally."), <c>IgnoreMissingReferencedObjects</c> ("... referenced object is missing.") and
/// <c>IgnoreUnitAttributes</c> ("... unit related attributes are present."), taken by
/// <c>PlcBlockComposition.Import(FileInfo, ImportOptions, SWImportOptions)</c> and <c>PlcTypeComposition.Import(FileInfo, ImportOptions,
/// SWImportOptions)</c>; <c>T:Siemens.Engineering.SW.ImportDocumentOptions</c> with <c>None</c>, <c>Override</c>,
/// <c>SkipInactiveCultures</c> and <c>ActivateInactiveCultures</c>, taken by <c>PlcBlockComposition.ImportFromDocuments(DirectoryInfo,
/// String, ImportDocumentOptions)</c>. The XML does not say whether either enum is a flags enum, so more than one member is combined
/// only when the live enum type carries <see cref="FlagsAttribute"/>; otherwise the import is refused before it starts
/// (<c>unsupported_capability</c>, nothing imported). The <c>SWImportOptions</c> type is not in the compile stubs: the overload is bound
/// at runtime by exact parameter type names. Not live-tested.
/// </summary>
public static class PlcImportOptionNames
{
    public const string SwImportOptionsType = "Siemens.Engineering.SW.SWImportOptions";

    public const string ImportOptionsType = "Siemens.Engineering.ImportOptions";

    public const string ImportDocumentOptionsType = "Siemens.Engineering.SW.ImportDocumentOptions";

    /// <summary>The ImportOptions member every update import already uses.</summary>
    public const string Override = "Override";

    public const string IgnoreStructuralChanges = "IgnoreStructuralChanges";
    public const string IgnoreMissingReferencedObjects = "IgnoreMissingReferencedObjects";
    public const string IgnoreUnitAttributes = "IgnoreUnitAttributes";

    public const string SkipInactiveCultures = "SkipInactiveCultures";
    public const string ActivateInactiveCultures = "ActivateInactiveCultures";

    /// <summary>The documented <c>SWImportOptions</c> members a caller may name (<c>None</c> is the omitted list).</summary>
    public static IReadOnlyList<string> SwImportOptions { get; } = new[] { IgnoreStructuralChanges, IgnoreMissingReferencedObjects, IgnoreUnitAttributes };

    /// <summary>The documented <c>ImportDocumentOptions</c> culture members a caller may add to <c>Override</c>.</summary>
    public static IReadOnlyList<string> DocumentCultureOptions { get; } = new[] { SkipInactiveCultures, ActivateInactiveCultures };

    /// <summary>Null when valid: 1 to 3 distinct documented members.</summary>
    public static string? ValidateSwImportOptions(IReadOnlyList<string>? names)
    {
        if (names is null) return null;
        if (names.Count == 0 || names.Count > SwImportOptions.Count)
            return $"swImportOptions must name 1 to {SwImportOptions.Count} of {string.Join(", ", SwImportOptions)} (omit it for SWImportOptions.None).";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (name is null || !SwImportOptions.Contains(name, StringComparer.Ordinal))
                return $"swImportOptions: '{name}' is not one of {string.Join(", ", SwImportOptions)} (exact spelling).";
            if (!seen.Add(name)) return $"swImportOptions: '{name}' is listed more than once.";
        }

        return null;
    }

    /// <summary>Null when valid: one documented culture member.</summary>
    public static string? ValidateDocumentCultureOption(string? name)
        => name is null || DocumentCultureOptions.Contains(name, StringComparer.Ordinal)
            ? null
            : $"documentImportOption must be {string.Join(" or ", DocumentCultureOptions)} (exact spelling); omitted imports with ImportDocumentOptions.Override alone.";

    /// <summary>
    /// The value of <paramref name="enumType"/> for <paramref name="names"/>: each must be an exact member name of the live enum
    /// (<c>api_member_missing</c> otherwise); several are OR-combined only when the live enum is a flags enum
    /// (<c>unsupported_capability</c> otherwise). Nothing is called on Siemens objects.
    /// </summary>
    public static object Combine(Type enumType, IReadOnlyList<string> names, string what)
    {
        if (enumType is null || !enumType.IsEnum) throw new ArgumentException("An enum type is required.", nameof(enumType));
        if (names is null || names.Count == 0) throw new ArgumentException("At least one member is required.", nameof(names));
        foreach (var name in names)
            if (!Enum.GetNames(enumType).Contains(name, StringComparer.Ordinal))
                throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                    $"{enumType.FullName} of this TIA Portal has no member '{name}' (members: {string.Join(", ", Enum.GetNames(enumType))}); nothing was imported.");
        if (names.Count > 1 && !enumType.IsDefined(typeof(FlagsAttribute), inherit: false))
            throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
                $"{what} needs {string.Join(" | ", names)} combined, but {enumType.FullName} of this TIA Portal is not a flags enum, so the members cannot be "
                + "combined safely; nothing was imported.");
        ulong bits = 0;
        foreach (var name in names)
            bits |= Convert.ToUInt64(Enum.Parse(enumType, name, ignoreCase: false), System.Globalization.CultureInfo.InvariantCulture);
        return Enum.ToObject(enumType, bits);
    }

    /// <summary>
    /// Binds <c>Import(FileInfo, ImportOptions, SWImportOptions)</c> of a live block or type composition by exact parameter type
    /// names (the compile stubs do not declare the overload) with <c>ImportOptions.Override</c>, as the existing update path uses, and
    /// the caller's <c>SWImportOptions</c> members. Binding happens before anything is imported: a composition without exactly one
    /// such overload is <c>api_member_missing</c>, uncombinable members <c>unsupported_capability</c>. The returned call rethrows the
    /// member's own exception unwrapped so the worker classifies it as before.
    /// </summary>
    public static Func<FileInfo, object?> BindSwImport(object composition, IReadOnlyList<string> swImportOptions)
    {
        if (composition is null) throw new ArgumentNullException(nameof(composition));
        var candidates = composition.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Import" && method.GetParameters() is { Length: 3 } p
                && p[0].ParameterType == typeof(FileInfo)
                && p[1].ParameterType.IsEnum && p[1].ParameterType.FullName == ImportOptionsType
                && p[2].ParameterType.IsEnum && p[2].ParameterType.FullName == SwImportOptionsType)
            .ToArray();
        if (candidates.Length != 1)
            throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                $"{composition.GetType().Name}.Import(FileInfo, ImportOptions, SWImportOptions) is not available in this TIA Portal; nothing was imported.");
        var method = candidates[0];
        var parameters = method.GetParameters();
        var importOptions = Combine(parameters[1].ParameterType, new[] { Override }, "ImportOptions");
        var swOptions = Combine(parameters[2].ParameterType, swImportOptions, "swImportOptions");
        return file =>
        {
            try
            {
                return method.Invoke(composition, new[] { file, importOptions, swOptions });
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        };
    }

    /// <summary>The <c>ImportDocumentOptions</c> value: <c>Override</c>, combined with the caller's culture member when given.</summary>
    public static object DocumentOptions(Type importDocumentOptionsType, string? cultureOption)
        => Combine(importDocumentOptionsType, cultureOption is null ? new[] { Override } : new[] { Override, cultureOption }, "documentImportOption");
}
