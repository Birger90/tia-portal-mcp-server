using System.Reflection;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.Loadable;

/// <summary>
/// plc_write <c>generate_loadable</c> (GitHub issue #26). Evidence: the V21 IntelliSense file Siemens.Engineering.Step7.xml,
/// <c>M:Siemens.Engineering.SW.Loader.LoadableProvider.GenerateLoadable(System.IO.FileInfo,System.Collections.Generic.IEnumerable{Siemens.Engineering.SW.Blocks.PlcBlock},Siemens.Engineering.SW.Loader.TargetOption)</c>
/// ("Generates a loadable file for blocks in the non unit program"), the overload with
/// <c>IEnumerable{Siemens.Engineering.SW.Units.PlcUnit}</c> ("Generates a loadable file for software units") and the fields of
/// <c>T:Siemens.Engineering.SW.Loader.TargetOption</c>: <c>None</c> ("Throws an exception"), <c>Plc</c>, <c>PlcSim</c>. The XML
/// documents no return value and does not say which object offers <c>LoadableProvider</c>; it is found among the
/// PlcSoftware's <c>GetServiceInfos()</c>. The worker method carries the public name.
/// </summary>
public static class PlcLoadableMethods
{
    public const string GenerateLoadable = "generate_loadable";

    public static bool IsWorkerMethod(string? method) => method == GenerateLoadable;
}

public static class PlcLoadableNames
{
    public const string Provider = "Siemens.Engineering.SW.Loader.LoadableProvider";
    public const string TargetOptionEnum = "Siemens.Engineering.SW.Loader.TargetOption";
    public const string BlockType = "Siemens.Engineering.SW.Blocks.PlcBlock";
    public const string UnitType = "Siemens.Engineering.SW.Units.PlcUnit";
    public const string Method = "GenerateLoadable";

    /// <summary>The <c>TargetOption</c> members a caller may name; <c>None</c> is documented to throw and is not offered.</summary>
    public static IReadOnlyList<string> Targets { get; } = new[] { "Plc", "PlcSim" };

    public const string OverloadBlocks = "blocks";
    public const string OverloadUnits = "units";

    public const int MaxObjects = 200;
}

/// <summary>The worker parameters of generate_loadable: exactly one of <c>BlockPaths</c> or <c>UnitNames</c>.</summary>
public sealed class PlcLoadableRequestInfo
{
    public string? PlcName { get; set; }

    public List<string>? BlockPaths { get; set; }

    public List<string>? UnitNames { get; set; }

    public string Target { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public bool Overwrite { get; set; }
}

/// <summary>The result of generate_loadable.</summary>
public sealed class PlcLoadableResultInfo
{
    public string Operation { get; set; } = PlcLoadableMethods.GenerateLoadable;

    public string PlcName { get; set; } = string.Empty;

    /// <summary><see cref="PlcLoadableNames.OverloadBlocks"/> or <see cref="PlcLoadableNames.OverloadUnits"/>.</summary>
    public string Overload { get; set; } = string.Empty;

    /// <summary>The blocks or software units passed, by name, in request order.</summary>
    public List<string> Objects { get; set; } = new();

    /// <summary>The <c>TargetOption</c> member passed.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>The bound overload as text, with its runtime return type.</summary>
    public string Signature { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    public bool Overwritten { get; set; }

    public long Bytes { get; set; }

    /// <summary>SHA-256 of the file re-read at its place.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// The Siemens-free part of generate_loadable, shared by the worker and the FakeWorker: binds the overload for the element type
/// on the live provider (three parameters: <c>FileInfo</c>, an <c>IEnumerable&lt;T&gt;</c> of exactly the element full name, and the
/// <c>TargetOption</c> enum of exactly its full name), parses the target by member name, and writes the file under the caller-file
/// rules of the alarm exports (<see cref="PlcAlarmFiles"/>: no overwrite without <c>overwrite</c>, a temporary file next to the
/// target, moved into place and re-hashed). A missing overload or enum member is api_member_missing, nothing written.
/// </summary>
public static class PlcLoadableRun
{
    public sealed class Bound(MethodInfo method, Type elementType, object target, string signature)
    {
        public MethodInfo Method { get; } = method;

        public Type ElementType { get; } = elementType;

        public object Target { get; } = target;

        public string Signature { get; } = signature;
    }

    /// <exception cref="RuntimeBindingException">No single overload, or the target is not a member of the live enum.</exception>
    public static Bound Bind(object provider, string elementFullName, string target)
    {
        var candidates = provider.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == PlcLoadableNames.Method && !method.IsGenericMethodDefinition)
            .Select(method => (Method: method, Parameters: method.GetParameters()))
            .Where(entry => entry.Parameters.Length == 3 && entry.Parameters[0].ParameterType == typeof(FileInfo)
                && ElementOf(entry.Parameters[1].ParameterType) is { } element && element.FullName == elementFullName
                && entry.Parameters[2].ParameterType.IsEnum && entry.Parameters[2].ParameterType.FullName == PlcLoadableNames.TargetOptionEnum)
            .GroupBy(entry => entry.Method.ToString()).Select(group => group.First()).ToList();
        if (candidates.Count != 1)
            throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                $"'{provider.GetType().FullName}' exposes {(candidates.Count == 0 ? "no" : candidates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))} "
                + $"{PlcLoadableNames.Method}(FileInfo, IEnumerable<{elementFullName}>, {PlcLoadableNames.TargetOptionEnum}); none is chosen. Nothing was written.");
        var (method, parameters) = candidates[0];
        var enumType = parameters[2].ParameterType;
        var value = LibraryRuntimeBinding.EnumValue(enumType, target)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                $"{PlcLoadableNames.TargetOptionEnum} has no member '{target}' on this TIA Portal installation (members: {LibraryRuntimeBinding.MemberList(enumType)}). Nothing was written.");
        return new Bound(method, ElementOf(parameters[1].ParameterType)!, value,
            $"{method.ReturnType.Name} {PlcLoadableNames.Method}(FileInfo, {parameters[1].ParameterType.Name}<{ElementOf(parameters[1].ParameterType)!.Name}>, {enumType.Name}.{target})");
    }

    /// <summary>
    /// Generates the file: the caller-file prechecks, a temporary file next to <paramref name="path"/>, the call, a check that the
    /// call wrote a file there (else postcondition_failed), then the move into place and the re-hash.
    /// </summary>
    public static PlcLoadableResultInfo Generate(object provider, Bound bound, IReadOnlyList<object> objects, string path, bool overwrite, DateTime utcNow,
        PlcLoadableResultInfo result)
    {
        Files(() => PlcAlarmFiles.PrecheckExport(path, overwrite, null));
        result.Messages.AddRange(PlcAlarmFiles.Notes(PlcAlarmFiles.Inspect(path)));
        var swept = PlcAlarmFiles.SweepStaleTemporaries(path, utcNow);
        if (swept > 0) result.Messages.Add($"Removed {swept} temporary file(s) next to '{path}' that an interrupted earlier call left behind.");
        var collection = LibraryRuntimeBinding.BuildCollection(bound.Method.GetParameters()[1].ParameterType, bound.ElementType, objects)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                $"A collection of {bound.ElementType.FullName} for {bound.Signature} could not be built from the resolved objects. Nothing was written.");
        var temporary = PlcAlarmFiles.TemporaryPath(path);
        try
        {
            RuntimeServiceValues.Call(bound.Method, provider, new[] { new FileInfo(temporary), collection, bound.Target });
            if (!File.Exists(temporary))
                throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                    $"{PlcLoadableNames.Method} returned, but no file was written at the path it was given ('{temporary}'"
                    + (Directory.Exists(temporary) ? ", a directory appeared there instead" : string.Empty) + "). Nothing was placed at the target.");
            var (overwritten, bytes, sha256) = Files(() => PlcAlarmFiles.Commit(temporary, path, overwrite));
            (result.FilePath, result.Overwritten, result.Bytes, result.Sha256) = (path, overwritten, bytes, sha256);
            result.Signature = bound.Signature;
            return result;
        }
        finally
        {
            PlcAlarmFiles.Discard(temporary);
        }
    }

    private static Type? ElementOf(Type parameter)
        => parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? parameter.GetGenericArguments()[0] : null;

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

    private static void Files(Action run) => Files<object?>(() => { run(); return null; });
}
