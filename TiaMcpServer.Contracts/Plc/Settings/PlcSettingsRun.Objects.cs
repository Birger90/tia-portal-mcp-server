using System.Collections;
using System.Reflection;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using M = TiaMcpServer.Contracts.Plc.Settings.PlcObjectMethods;
using N = TiaMcpServer.Contracts.Plc.Settings.PlcObjectNames;

namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>Package G: operations on engineering objects named by object paths (see <see cref="PlcObjectMethods"/> for the evidence).</summary>
public static partial class PlcSettingsRun
{
    private const string MarkedByMe = "IsMarkedByMe";

    // ----- reads -----

    private static PlcSettingsReadInfo ReadObjects(string method, PlcSettingsContext? context, PlcSettingsReadInfo result, PlcSettingsRequestInfo info)
    {
        if (context is null) return result;
        var section = new PlcSettingsSectionInfo { Name = method, Service = PlcSettingsCatalog.Find(method)?.Service, Owner = PlcSettingsNames.ProjectOwner };
        result.Project.Add(section);
        try
        {
            switch (method)
            {
                case M.GetMarkings:
                {
                    var service = MarkingService(context);
                    section.Service = "Siemens.Engineering.Multiuser.MarkingService";
                    var markings = Call(RequireMethod(service, "GetMarkings", Type.EmptyTypes, "MarkingService.GetMarkings()"), service);
                    section.Offered = true;
                    foreach (var name in new[] { "AllMarkings", "ConflictedMarkings" })
                        section.Collections.Add(Collection(context, name, () => markings is null ? null : Get(markings, name), "MarkedObject", new[] { "MarkedObject", "MarkState" },
                            special: (marking, property) => property == "MarkedObject" ? RuntimeServiceValues.Project(context.DescribeObject(Get(marking, "MarkedObject"))) : null));
                    break;
                }
                case M.GetMarkState:
                {
                    var service = MarkingService(context);
                    section.Service = "Siemens.Engineering.Multiuser.MarkingService";
                    var target = context.ResolveObject(Required(info.ObjectPath, "objectPath"));
                    var (state, markable) = MarkState(service, target);
                    section.Offered = true;
                    section.Properties.Add(new PlcSettingPropertyInfo { Name = "Object", Value = RuntimeServiceValues.Project(context.DescribeObject(target)) });
                    section.Properties.Add(new PlcSettingPropertyInfo { Name = "MarkState", Value = RuntimeServiceValues.Project(state) });
                    section.Properties.Add(new PlcSettingPropertyInfo { Name = "IsMarkable", Value = RuntimeServiceValues.Project(markable) });
                    var isMarkable = RequireMethodByShape(service, "IsMarkable", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(target), "IsMarkable(IEngineeringObject)");
                    section.Properties.Add(new PlcSettingPropertyInfo { Name = "IsMarkableCheck", Value = RuntimeServiceValues.Project(Call(isMarkable, service, target)) });
                    break;
                }
                case M.GetObjectIdentifier:
                case M.FindObjectByIdentifier:
                {
                    var (provider, _) = RequireService(context, new[] { context.EngineeringRoot ?? RequireProject(context) }, N.ObjectIdentifierProvider, "The project");
                    section.Owner = PlcSettingsNames.ProjectOwner;
                    section.Offered = true;
                    if (method == M.GetObjectIdentifier)
                    {
                        var target = context.ResolveObject(Required(info.ObjectPath, "objectPath"));
                        var get = RequireMethodByShape(provider, "GetIdentifier", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(target), "GetIdentifier(IEngineeringObject)");
                        section.Properties.Add(new PlcSettingPropertyInfo { Name = "Object", Value = RuntimeServiceValues.Project(context.DescribeObject(target)) });
                        section.Properties.Add(new PlcSettingPropertyInfo { Name = "Identifier", Value = RuntimeServiceValues.Project(Call(get, provider, target)) });
                    }
                    else
                    {
                        var identifier = Required(info.Name, "name");
                        var found = Call(RequireMethod(provider, "Find", new[] { typeof(string) }, "ObjectIdentifierProvider.Find(String)"), provider, identifier);
                        section.Properties.Add(new PlcSettingPropertyInfo { Name = "Found", Value = RuntimeServiceValues.Project(found is not null) });
                        if (found is not null) section.Properties.Add(new PlcSettingPropertyInfo { Name = "Object", Value = RuntimeServiceValues.Project(context.DescribeObject(found)) });
                    }

                    break;
                }
                case M.GetCreationInfos:
                    ReadCreationInfos(context, context.ResolveObject(Required(info.ObjectPath, "objectPath")), info.Name, section);
                    break;
                case M.GetLinkedTags:
                {
                    var channel = context.ResolveObject(Required(info.ObjectPath, "objectPath"));
                    var (provider, owner) = RequireService(context, new[] { channel }, N.PlcTagProvider, "The channel");
                    section.Owner = context.DescribeObject(owner);
                    section.Offered = true;
                    var tags = Call(RequireMethod(provider, "GetLinkedTags", Type.EmptyTypes, "PlcTagProvider.GetLinkedTags()"), provider);
                    section.Collections.Add(Collection(context, "LinkedTags", () => tags, "Name", new[] { "Name", "LogicalAddress", "DataTypeName", "TagTable" },
                        special: (tag, property) => property == "TagTable" ? RuntimeServiceValues.Project(context.NameOf(Parent(tag))) : null));
                    break;
                }
                default:
                    throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"Unsupported read '{method}'.");
            }
        }
        catch (RuntimeBindingException exception) when (exception.Category is WorkerFailureCategories.UnsupportedCapability or WorkerFailureCategories.ApiMemberMissing)
        {
            section.Unavailable = exception.Message;
        }
        catch (Exception exception) when (context.Recoverable(exception))
        {
            section.Unavailable = RuntimeServiceValues.Bound($"{exception.GetType().Name}: {exception.Message}");
        }

        return result;
    }

    private static object? Parent(object? value)
    {
        if (value is null) return null;
        var property = value.GetType().GetProperty("Parent", BindingFlags.Public | BindingFlags.Instance)
            ?? value.GetType().GetInterfaces().Select(i => i.GetProperty("Parent")).FirstOrDefault(p => p is not null);
        try
        {
            return property?.GetValue(value, null);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The compositions of an object (<c>GetCompositionInfos()</c>) and what can be created in each (<c>GetCreationInfos(name)</c>),
    /// or, with <paramref name="composition"/>, only that one plus the composition's own <c>IEngineeringComposition.GetCreationInfos()</c>.
    /// </summary>
    private static void ReadCreationInfos(PlcSettingsContext context, object target, string? composition, PlcSettingsSectionInfo section)
    {
        section.Service = null;
        section.Owner = context.DescribeObject(target);
        section.Offered = true;
        var names = new List<string>();
        if (composition is not null) names.Add(composition);
        else
        {
            var infos = InvokeInterface(target, "IEngineeringObject", "GetCompositionInfos", Type.EmptyTypes, Array.Empty<object>());
            foreach (var info in Enumerate(infos))
            {
                if (names.Count >= N.MaxCompositions)
                {
                    section.Note = $"Only the first {N.MaxCompositions} compositions are listed.";
                    break;
                }

                if (info is not null && ReadText(info, "Name") is { } name) names.Add(name);
            }
        }

        var list = new PlcSettingsCollectionInfo { Name = "Compositions" };
        foreach (var name in names)
        {
            var item = new PlcSettingsItemInfo { Key = name };
            item.Collections.Add(CreationCollection(context, "CreationInfos",
                () => InvokeInterface(target, "IEngineeringObject", "GetCreationInfos", new[] { typeof(string) }, new object[] { name })));
            if (composition is not null)
            {
                var compositionObject = InvokeInterface(target, "IEngineeringObject", "GetComposition", new[] { typeof(string) }, new object[] { name });
                if (compositionObject is not null)
                    item.Collections.Add(CreationCollection(context, "CompositionCreationInfos",
                        () => InvokeInterface(compositionObject, "IEngineeringComposition", "GetCreationInfos", Type.EmptyTypes, Array.Empty<object>())));
            }

            list.Items.Add(item);
        }

        section.Collections.Add(list);
    }

    private static PlcSettingsCollectionInfo CreationCollection(PlcSettingsContext context, string name, Func<object?> read)
        => Collection(context, name, read, "Type", new[] { "Type", "Parameters" }, special: (info, property) => property switch
        {
            "Type" => RuntimeServiceValues.Project(Get(info, "Type") is Type type ? type.FullName : ReadText(info, "Type")),
            "Parameters" => RuntimeServiceValues.Project(string.Join("; ", Enumerate(Get(info, "ParameterInfos")).Where(p => p is not null).Select(p =>
                $"{ReadText(p!, "Name")}{(Get(p!, "IsMandatory") is true ? " (mandatory)" : string.Empty)}: "
                + string.Join("|", Enumerate(Get(p!, "SupportedTypes")).Select(t => t is Type type ? type.FullName : t?.ToString())))) ),
            _ => null,
        });

    private static string? ReadText(object target, string name)
    {
        var value = Get(target, name);
        return value is Type type ? type.FullName : value?.ToString();
    }

    /// <summary>Calls a method of a documented interface (explicitly implemented by Siemens types) by interface short name.</summary>
    private static object? InvokeInterface(object target, string interfaceName, string method, Type[] parameters, object[] arguments)
    {
        var declared = target.GetType().GetInterfaces().Where(i => i.Name == interfaceName && i.Namespace == "Siemens.Engineering")
            .Select(i => i.GetMethod(method, parameters)).FirstOrDefault(m => m is not null)
            ?? target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance, null, parameters, null)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                $"'{target.GetType().FullName}' implements no {interfaceName}.{method}({string.Join(", ", parameters.Select(p => p.Name))}) on this TIA Portal installation.");
        return Call(declared, target, arguments);
    }

    // ----- writes -----

    private static bool WriteObjects(string method, PlcSettingsDefinition definition, PlcSettingsContext context, PlcSettingsRequestInfo info,
        PlcSettingsResultInfo result, List<string> warnings)
    {
        if (definition.Issue != PlcSettingsCatalog.PackageG) return false;
        result.Owner = PlcSettingsNames.ProjectOwner;
        switch (method)
        {
            case M.MarkObjects:
            case M.UnmarkObjects:
            {
                var service = MarkingService(context);
                result.Service = "Siemens.Engineering.Multiuser.MarkingService";
                var paths = info.ObjectPaths is { Count: > 0 } list ? list : throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "objectPaths is required. Nothing was changed.");
                var targets = paths.Select(path => (Path: path, Object: context.ResolveObject(path))).ToList();
                var name = method == M.MarkObjects ? "MarkObjects" : "UnmarkObjects";
                var call = RequireMethodByShape(service, name, p => p.Length == 1 && EnumerableElement(p[0].ParameterType) is not null, $"{name}(IEnumerable<IEngineeringObject>)");
                var element = EnumerableElement(call.GetParameters()[0].ParameterType)!;
                foreach (var target in targets.Where(t => !element.IsInstanceOfType(t.Object)))
                    throw new RuntimeBindingException(WorkerFailureCategories.TargetKindUnsupported, $"'{target.Path}' is no {element.Name}. Nothing was changed.");
                var typed = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
                foreach (var target in targets) typed.Add(target.Object);
                var before = targets.Select(t => MarkState(service, t.Object).State).ToList();
                Call(call, service, typed);
                result.Calls.Add($"MarkingService.{name}({targets.Count} object(s))");
                for (var index = 0; index < targets.Count; index++)
                {
                    var (state, _) = MarkState(service, targets[index].Object);
                    var markedByMe = state == MarkedByMe;
                    result.Items.Add($"{targets[index].Path}: {state}");
                    var expected = method == M.MarkObjects;
                    result.Changes.Add(new PlcSettingChangeInfo
                    {
                        Name = targets[index].Path,
                        Requested = expected ? MarkedByMe : "not " + MarkedByMe,
                        Before = RuntimeServiceValues.Project(before[index]),
                        After = RuntimeServiceValues.Project(state),
                        Matched = markedByMe == expected,
                    });
                    if (markedByMe != expected)
                        throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                            $"'{targets[index].Path}' reads MarkState {state} after {name} (before: {before[index]}). Inspect it with plc_read get_mark_state.");
                }

                return true;
            }
            case M.SaveCamData:
            case M.LoadCamData:
            {
                var target = context.ResolveObject(Required(info.ObjectPath, "objectPath"));
                var (support, owner) = RequireService(context, new[] { target }, N.CamDataSupport, $"The object '{info.ObjectPath}'");
                result.Service = N.CamDataSupport;
                result.Owner = context.DescribeObject(owner);
                result.Target = info.ObjectPath;
                var format = info.CamFormat ?? (method == M.LoadCamData ? null : "MCD");
                if (method == M.SaveCamData)
                {
                    var documented = format switch
                    {
                        "Binary" => "CamDataSupport.SaveCamDataBinary(FileInfo)",
                        "PointList" => "CamDataSupport.SaveCamDataPointList(FileInfo, CamDataFormatSeparator, Int32)",
                        _ => "CamDataSupport.SaveCamData(FileInfo, CamDataFormat, CamDataFormatSeparator)",
                    };
                    var (bound, arguments) = CamCall(support, format!, info, write: true);
                    result.File = WriteFile(context, info.FilePath, info.Overwrite, documented, temporary =>
                    {
                        arguments[0] = new FileInfo(temporary);
                        Call(bound, support, arguments);
                    }, result.Messages);
                    result.Calls.Add(documented);
                    return true;
                }

                RequireExistingFile(info.FilePath, "filePath");
                var (load, loadArguments) = CamCall(support, format ?? "MCD", info, write: false);
                loadArguments[0] = new FileInfo(info.FilePath!);
                Call(load, support, loadArguments);
                result.Calls.Add(format == "Binary" ? "CamDataSupport.LoadCamDataBinary(FileInfo)" : "CamDataSupport.LoadCamData(FileInfo, CamDataFormatSeparator)");
                result.Messages.Add("The cam data were loaded into the technology object; no documented member compares them with the file. Export them again with save_cam_data to check.");
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>The CamDataSupport method of a format and its arguments (the FileInfo slot left for the caller).</summary>
    private static (MethodInfo Method, object?[] Arguments) CamCall(object support, string format, PlcSettingsRequestInfo info, bool write)
    {
        object Separator(Type type) => LibraryRuntimeBinding.EnumValue(type, info.CamSeparator ?? "Comma")
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"{type.FullName} has no member '{info.CamSeparator ?? "Comma"}' (members: {LibraryRuntimeBinding.MemberList(type)}). Nothing was changed.");
        bool IsEnum(Type type, string fullName) => type.IsEnum && type.FullName == fullName;
        if (format == "Binary")
            return (RequireMethod(support, write ? "SaveCamDataBinary" : "LoadCamDataBinary", new[] { typeof(FileInfo) }, write ? "SaveCamDataBinary(FileInfo)" : "LoadCamDataBinary(FileInfo)"), new object?[1]);
        if (!write)
        {
            var load = RequireMethodByShape(support, "LoadCamData", p => p.Length == 2 && p[0].ParameterType == typeof(FileInfo) && IsEnum(p[1].ParameterType, N.CamDataFormatSeparator), "LoadCamData(FileInfo, CamDataFormatSeparator)");
            return (load, new object?[] { null, Separator(load.GetParameters()[1].ParameterType) });
        }

        if (format == "PointList")
        {
            var points = RequireMethodByShape(support, "SaveCamDataPointList",
                p => p.Length == 3 && p[0].ParameterType == typeof(FileInfo) && IsEnum(p[1].ParameterType, N.CamDataFormatSeparator) && p[2].ParameterType == typeof(int),
                "SaveCamDataPointList(FileInfo, CamDataFormatSeparator, Int32)");
            var samples = info.SamplePoints ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, "save_cam_data PointList needs samplePoints. Nothing was written.");
            return (points, new object?[] { null, Separator(points.GetParameters()[1].ParameterType), samples });
        }

        var save = RequireMethodByShape(support, "SaveCamData",
            p => p.Length == 3 && p[0].ParameterType == typeof(FileInfo) && IsEnum(p[1].ParameterType, N.CamDataFormat) && IsEnum(p[2].ParameterType, N.CamDataFormatSeparator),
            "SaveCamData(FileInfo, CamDataFormat, CamDataFormatSeparator)");
        var formatType = save.GetParameters()[1].ParameterType;
        var formatValue = LibraryRuntimeBinding.EnumValue(formatType, format)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing, $"{formatType.FullName} has no member '{format}' (members: {LibraryRuntimeBinding.MemberList(formatType)}). Nothing was written.");
        return (save, new object?[] { null, formatValue, Separator(save.GetParameters()[2].ParameterType) });
    }

    // ----- shared -----

    private static string Required(string? value, string field)
        => value ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"{field} is required. Nothing was changed.");

    private static object MarkingService(PlcSettingsContext context)
    {
        var session = context.LocalSession ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
            "Markings exist only in a Multiuser local session (LocalSession.MarkingService); the bound project is no local session. Nothing was changed.");
        return Get(session, "MarkingService")
            ?? throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability, "LocalSession.MarkingService returned null. Nothing was changed.");
    }

    private static (string? State, object? Markable) MarkState(object service, object target)
    {
        var get = RequireMethodByShape(service, "GetMarkStateInfo", p => p.Length == 1 && p[0].ParameterType.IsInstanceOfType(target), "GetMarkStateInfo(IEngineeringObject)");
        var info = Call(get, service, target);
        return info is null ? (null, null) : (Get(info, "MarkState")?.ToString(), Get(info, "IsMarkable"));
    }

    private static Type? EnumerableElement(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? type.GetGenericArguments()[0] : null;
}
