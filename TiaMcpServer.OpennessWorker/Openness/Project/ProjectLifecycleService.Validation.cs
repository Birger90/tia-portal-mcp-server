using Siemens.Engineering;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

public static partial class ProjectLifecycleService
{
    internal static void RequireAbsoluteFile(string path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{fieldName} is required.");
        }

        if (!Path.IsPathRooted(path))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{fieldName} must be an absolute path.");
        }

        RequireCallerPathShape(path, fieldName);
        if (!File.Exists(path))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "TIA Portal project file was not found.");
        }
    }

    internal static void RequireAbsoluteDirectory(string path, string fieldName, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{fieldName} is required.");
        }

        if (!Path.IsPathRooted(path))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{fieldName} must be an absolute path.");
        }

        RequireCallerPathShape(path, fieldName);
        if (mustExist && !Directory.Exists(path))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{fieldName} '{path}' was not found.");
        }
    }

    /// <summary>
    /// The shared caller-path rules of the file exports (security review F5): device paths (\\?\, \\.\, \??\), reserved
    /// device names and segments ending in a space or a dot are refused before TIA Portal sees the path.
    /// </summary>
    internal static void RequireCallerPathShape(string path, string fieldName)
    {
        if (TiaMcpServer.Contracts.Plc.PlcFileTargets.PathShapeError(fieldName, path) is { } error)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error);
        }
    }

    internal static void RequireName(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{fieldName} is required.");
        }
    }

    internal static void RequireDirectoryExists(string path, string fieldName)
    {
        if (!Directory.Exists(path))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{fieldName} '{path}' was not found.");
        }
    }

    internal static ProjectArchivationMode ParseArchiveMode(string archiveMode)
    {
        if (!Enum.TryParse<ProjectArchivationMode>(archiveMode, ignoreCase: true, out var mode))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Invalid archive mode '{archiveMode}'.");
        }

        return mode;
    }
}
