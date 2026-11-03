namespace TiaMcpServer.Plc;

/// <summary>
/// The shape of the hierarchical verification keys (<see cref="PlcVerificationPolicy.ObjectKeys"/>): newline-joined
/// segments, a container's key prefixing its members' keys (<c>TrustedWriteVerifier</c>: a later key k supersedes an
/// earlier key o when <c>o == k</c> or o starts with <c>k + "\n"</c>). Containers (block and type groups, tag table
/// folders, watch table groups, software units) are their path segments. A leaf (a block, a PLC data type, a tag
/// table, a watch or force table) puts <see cref="LeafMarker"/> before its name, so a leaf key never prefixes the key
/// of a container or object of the same name (block <c>Line</c> beside group <c>Line</c>, table <c>A</c> beside
/// folder <c>A</c>), while every container above it still prefixes it.
/// </summary>
internal static class PlcObjectKeys
{
    /// <summary>Starts the last segment of a leaf key; a control character no project name segment starts with.</summary>
    public const char LeafMarker = '\u0001';

    /// <summary><c>prefix</c> followed by every '/'-separated segment of <paramref name="path"/>, each after a newline.</summary>
    public static string Container(string prefix, string? path)
        => prefix + string.Concat(Segments(path).Select(segment => "\n" + segment));

    /// <summary>The leaf key of the object at <paramref name="path"/>: its container's segments, then the marked name.</summary>
    public static string Leaf(string prefix, string? path)
    {
        var segments = Segments(path);
        if (segments.Count == 0) return prefix + "\n" + LeafMarker;
        return Container(prefix, string.Join("/", segments.Take(segments.Count - 1))) + "\n" + LeafMarker + segments[^1];
    }

    /// <summary>The leaf key of a named object in a folder written <c>/</c> or <c>/A/B</c>.</summary>
    public static string LeafIn(string prefix, string? folder, string? name)
        => Container(prefix, folder) + "\n" + LeafMarker + name;

    /// <summary>
    /// The key a container operation (a group or software unit create, delete or rename) owns, in a namespace of its own:
    /// a later operation on the same or an enclosing container supersedes it (it offers this key too), while a later
    /// content item that covers the container's whole content namespace (generate_blocks_from_source offers the prefix
    /// of every block and type of its PLC) does not.
    /// </summary>
    public static string ContainerOwn(string scope, string kind, string? path)
        => Container($"{scope}container\n{kind}", path);

    private static IReadOnlyList<string> Segments(string? path)
        => (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
}
