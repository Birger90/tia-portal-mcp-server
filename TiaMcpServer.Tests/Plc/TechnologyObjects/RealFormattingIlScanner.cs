using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Tests.Plc.TechnologyObjects;

/// <summary>Which calls <see cref="RealFormattingIlScanner"/> reports.</summary>
internal enum RealIlRules
{
    /// <summary>
    /// Any call that parses, formats or converts a real with the runtime: every member of Double, Single and Decimal
    /// except bit access, comparison and classification (so Parse, TryParse, ToString, TryFormat, the conversion
    /// operators and Decimal(Double) are reported), Nullable&lt;real&gt;.ToString, every System.Convert member that mentions
    /// a real or is ChangeType, and every other call whose signature mentions a real (declaring type arguments,
    /// parameters, return type, generic method arguments; StringBuilder.Append(Double), TextWriter.Write(Double),
    /// XmlConvert.ToString(Double), String.Join&lt;Double&gt;, interpolation of a real) except BitConverter.
    /// </summary>
    Reals,

    /// <summary>
    /// <see cref="Reals"/> plus every call that could format a boxed real: String.Format, String.Concat/Join over objects,
    /// StringBuilder Append/Insert of an object and AppendFormat, every interpolation handler AppendFormatted,
    /// IFormattable.ToString, Object.ToString (also constrained) and every System.Convert member.
    /// </summary>
    Strict,
}

/// <summary>One reported call: the method that makes it (nested and compiler-generated types by their full nested name), the callee and the rule.</summary>
internal sealed record RealIlFinding(string Type, string Method, string Callee, string Reason)
{
    /// <summary>The source method a compiler-generated lambda, local function, iterator or async state machine belongs to.</summary>
    public string Origin
        => Regex.Match(Type + "/" + Method, "<([^>]+)>") is { Success: true } generated ? generated.Groups[1].Value : Method;

    public override string ToString() => $"{Type}.{Method} calls {Callee}: {Reason}";
}

/// <summary>What a scan covered: the reported calls, the scanned methods and calls, and the source documents of the scanned methods.</summary>
internal sealed record RealIlScan(IReadOnlyList<RealIlFinding> Findings, int Methods, int Calls, IReadOnlySet<string> Documents);

/// <summary>
/// Reads the compiled IL (System.Reflection.Metadata; no assembly is loaded, so the net48 worker and its Siemens
/// references are read like any other file) and reports the calls of <see cref="RealIlRules"/>. Every call, callvirt,
/// newobj, ldftn, ldvirtftn and jmp operand is resolved (method definitions, member references and generic method
/// instantiations; a <c>constrained.</c> prefix, or a <c>box</c> right before a call or <c>ldvirtftn</c> of an Object,
/// ValueType or IFormattable member, makes the call count as declared on that type); <c>box</c> of a real is reported when
/// asked, except inside the exempted (strictly scanned) types, which return typed values as objects. Methods are selected by their outermost type or by
/// their source document in the portable PDB, which attributes partial classes, lambdas, local functions, iterators
/// and async state machines to the file that declares them.
/// </summary>
internal static class RealFormattingIlScanner
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);

    private static readonly HashSet<string> RealTypes = new(StringComparer.Ordinal) { "System.Double", "System.Single", "System.Decimal" };

    /// <summary>Members of Double/Single/Decimal that only read bits, compare or classify.</summary>
    private static readonly HashSet<string> RealMembersAllowed = new(StringComparer.Ordinal)
    {
        "GetBits", "Equals", "CompareTo", "GetHashCode", "IsFinite", "IsNaN", "IsInfinity", "IsNegative", "IsPositiveInfinity", "IsNegativeInfinity",
        "op_Equality", "op_Inequality", "op_LessThan", "op_GreaterThan", "op_LessThanOrEqual", "op_GreaterThanOrEqual",
    };

    /// <summary>The project folders of the solution, to tell which project a PDB document belongs to.</summary>
    public static readonly IReadOnlyList<string> ProjectFolders = new[]
    {
        "TiaMcpServer", "TiaMcpServer.Contracts", "TiaMcpServer.OpennessWorker", "TiaMcpServer.FakeWorker", "TiaMcpServer.PlcSimWorker", "TiaMcpServer.Tests",
    };

    /// <summary>
    /// True for a technology object source file of <paramref name="project"/>: below the last project folder segment of the
    /// path (so a clone directory named like a project does not count), in a <c>TechnologyObjects</c> folder or named
    /// <c>*TechnologyObject*</c>.
    /// </summary>
    public static bool IsTechnologyObjectSource(string path, string project)
    {
        var segments = path.Replace('\\', '/').Split('/');
        var index = Array.FindLastIndex(segments, segment => ProjectFolders.Contains(segment, StringComparer.Ordinal));
        if (index < 0 || index == segments.Length - 1 || segments[index] != project) return false;
        var rest = segments.Skip(index + 1).ToList();
        return rest.Take(rest.Count - 1).Contains("TechnologyObjects", StringComparer.Ordinal)
            || rest[^1].Contains("TechnologyObject", StringComparison.Ordinal);
    }

    /// <summary>Scans every method whose outermost declaring type <paramref name="includeOutermostType"/> accepts.</summary>
    public static RealIlScan ScanTypes(string assemblyPath, Func<string, bool> includeOutermostType, RealIlRules rules, bool reportBoxedReals = false)
        => Scan(assemblyPath, (metadata, _, method) => includeOutermostType(OutermostName(metadata, metadata.GetTypeDefinition(method.GetDeclaringType()))),
            rules, reportBoxedReals, exemptType: null, usePdb: false);

    /// <summary>
    /// Scans every method with a sequence point in a source document <paramref name="includeDocument"/> accepts (the
    /// portable PDB next to the assembly, or embedded). Calls into the types <paramref name="exemptType"/> accepts
    /// (outermost full name) are not reported, nor boxing inside them: they are the sanctioned conversion, scanned with
    /// <see cref="RealIlRules.Strict"/>.
    /// </summary>
    public static RealIlScan ScanDocuments(string assemblyPath, Func<string, bool> includeDocument, RealIlRules rules, Func<string, bool>? exemptType, bool reportBoxedReals)
        => Scan(assemblyPath, (_, documents, _) => documents.Any(includeDocument), rules, reportBoxedReals, exemptType, usePdb: true);

    private static RealIlScan Scan(string assemblyPath, Func<MetadataReader, IReadOnlyList<string>, MethodDefinition, bool> select, RealIlRules rules,
        bool reportBoxedReals, Func<string, bool>? exemptType, bool usePdb)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        using var pdbProvider = usePdb ? OpenPdb(pe, assemblyPath) : null;
        var pdb = pdbProvider?.GetMetadataReader();
        var names = new NameProvider();

        // First pass: the selected methods (a call into one of them is scanned there, so it is not reported at the call).
        var selected = new HashSet<MethodDefinitionHandle>();
        var documents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            var methodDocuments = pdb is null ? Array.Empty<string>() : Documents(pdb, handle);
            if (!select(metadata, methodDocuments, method)) continue;
            selected.Add(handle);
            documents.UnionWith(methodDocuments);
        }

        var findings = new List<RealIlFinding>();
        var calls = 0;
        foreach (var handle in selected)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0) continue;
            var typeName = NestedName(metadata, metadata.GetTypeDefinition(method.GetDeclaringType()));
            var methodName = metadata.GetString(method.Name);
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() ?? Array.Empty<byte>();
            string? constrained = null;
            string? boxed = null;
            var boxingAllowed = exemptType is not null && exemptType(OutermostName(metadata, metadata.GetTypeDefinition(method.GetDeclaringType())));
            foreach (var (opCode, token) in Operands(il, $"{typeName}.{methodName}"))
            {
                if (opCode == OpCodes.Constrained)
                {
                    // "constrained. T callvirt Object::ToString" calls T's ToString: judged as declared on T.
                    constrained = TypeName(metadata, names, MetadataTokens.EntityHandle(token));
                    continue;
                }

                if (opCode == OpCodes.Box)
                {
                    // "box T [dup] callvirt|ldvirtftn Object::ToString" calls T's ToString as well.
                    boxed = TypeName(metadata, names, MetadataTokens.EntityHandle(token));
                    if (reportBoxedReals && !boxingAllowed && RealTypes.Contains(boxed))
                        findings.Add(new(typeName, methodName, "box " + boxed, "a boxed real can reach object-based formatting"));
                    continue;
                }

                if (opCode == OpCodes.Dup) continue;
                if (opCode.OperandType != OperandType.InlineMethod)
                {
                    boxed = null;
                    continue;
                }

                var (receiverConstraint, receiverBox) = (constrained, boxed);
                (constrained, boxed) = (null, null);
                var target = MetadataTokens.EntityHandle(token);
                if (target.Kind == HandleKind.MethodDefinition && selected.Contains((MethodDefinitionHandle)target)) continue;
                if (Resolve(metadata, names, target) is not { } resolved) continue;
                var receiver = receiverConstraint
                    ?? (receiverBox is not null && resolved.Declaring is "System.Object" or "System.ValueType" or "System.IFormattable" ? receiverBox : null);
                var call = receiver is null || !MentionsReal(receiver) ? resolved : resolved with { Declaring = receiver };
                calls++;
                if (exemptType is not null && call.Outermost is { } outer && exemptType(outer)) continue;
                if (Forbidden(call, rules) is { } reason) findings.Add(new(typeName, methodName, call.ToString(), reason));
            }
        }

        return new(findings, selected.Count, calls, documents);
    }

    private static MetadataReaderProvider OpenPdb(PEReader pe, string assemblyPath)
    {
        var embedded = pe.ReadDebugDirectory().FirstOrDefault(entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        if (embedded.DataSize > 0) return pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded);
        var path = Path.ChangeExtension(assemblyPath, ".pdb");
        if (!File.Exists(path)) throw new FileNotFoundException($"The IL scan selects methods by source file and needs the portable PDB '{path}'; build with debug symbols.", path);
        return MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(path));
    }

    private static string[] Documents(MetadataReader pdb, MethodDefinitionHandle handle)
    {
        var information = pdb.GetMethodDebugInformation(handle.ToDebugInformationHandle());
        if (!information.Document.IsNil) return new[] { pdb.GetString(pdb.GetDocument(information.Document).Name) };
        return information.GetSequencePoints().Select(point => point.Document).Where(document => !document.IsNil).Distinct()
            .Select(document => pdb.GetString(pdb.GetDocument(document).Name)).ToArray();
    }

    private sealed record Call(string Declaring, string? Outermost, string Name, IReadOnlyList<string> Parameters, string Return, IReadOnlyList<string> MethodArguments)
    {
        public IEnumerable<string> SignatureTypes => Parameters.Append(Return).Append(Declaring).Concat(MethodArguments);

        public override string ToString()
            => $"{Declaring}::{Name}{(MethodArguments.Count == 0 ? string.Empty : "<" + string.Join(", ", MethodArguments) + ">")}({string.Join(", ", Parameters)}) : {Return}";
    }

    private static string? Forbidden(Call call, RealIlRules rules)
    {
        var declaring = call.Declaring.Split('<')[0];
        var mentionsReal = call.SignatureTypes.Any(MentionsReal);
        var strict = rules == RealIlRules.Strict;

        if (RealTypes.Contains(declaring))
        {
            if (call.Name == ".ctor" ? call.Parameters.Any(MentionsReal) : !RealMembersAllowed.Contains(call.Name))
                return "runtime real parse/format/conversion";
            return null;
        }

        if (declaring == "System.Nullable`1")
            return mentionsReal && call.Name == "ToString" ? "runtime real formatting (Nullable<real>.ToString)" : null;
        if (declaring == "System.Convert")
            return strict ? "System.Convert" : mentionsReal || call.Name == "ChangeType" ? "System.Convert of a real" : null;
        if (declaring == "System.BitConverter") return null;
        if (mentionsReal) return "a call taking or returning a real (runtime real formatting or parsing)";
        if (!strict) return null;

        // Strict only: calls that format whatever object they get, a boxed real included.
        var objects = call.Parameters.Concat(call.MethodArguments).Any(type => type is "System.Object" or "System.Object[]" or "System.ReadOnlySpan`1<System.Object>");
        if (declaring == "System.String" && call.Name == "Format") return "String.Format";
        if (declaring == "System.String" && call.Name is "Concat" or "Join" && (objects || call.MethodArguments.Count > 0)) return $"String.{call.Name} over objects";
        if (declaring == "System.Text.StringBuilder" && (call.Name == "AppendFormat" || (call.Name is "Append" or "Insert" && objects)))
            return "StringBuilder formatting of an object";
        if (declaring.EndsWith("InterpolatedStringHandler", StringComparison.Ordinal) && call.Name.StartsWith("AppendFormatted", StringComparison.Ordinal))
            return "string interpolation of a value";
        if (declaring == "System.IFormattable" && call.Name == "ToString") return "IFormattable.ToString";
        if (declaring == "System.Object" && call.Name == "ToString") return "Object.ToString (a boxed real formats with the runtime)";
        return null;
    }

    private static bool MentionsReal(string type)
        => type.Split(new[] { '<', '>', ',', '[', ']', '&', '*', ' ' }, StringSplitOptions.RemoveEmptyEntries).Any(RealTypes.Contains);

    /// <summary>Every instruction's opcode, with the 32-bit token of a method, box or constrained. operand (0 otherwise).</summary>
    private static IEnumerable<(OpCode Code, int Token)> Operands(byte[] il, string method)
    {
        var offset = 0;
        while (offset < il.Length)
        {
            var start = offset;
            short value = il[offset++];
            if (value == 0xFE)
            {
                if (offset >= il.Length) throw new InvalidDataException($"{method}: truncated two-byte opcode at IL offset {start}.");
                value = unchecked((short)(0xFE00 | il[offset++]));
            }

            if (!OpCodesByValue.TryGetValue(value, out var code))
                throw new InvalidDataException($"{method}: unknown opcode 0x{value & 0xFFFF:X4} at IL offset {start}.");
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4,
            };
            if (offset + size > il.Length) throw new InvalidDataException($"{method}: operand of {code.Name} at IL offset {start} runs past the body.");
            yield return (code, code.OperandType is OperandType.InlineMethod || code == OpCodes.Box || code == OpCodes.Constrained ? BitConverter.ToInt32(il, offset) : 0);
            offset += size;
        }
    }

    private static Call? Resolve(MetadataReader metadata, NameProvider names, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
            {
                var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
                if (member.GetKind() != MemberReferenceKind.Method) return null;
                var signature = member.DecodeMethodSignature(names, null);
                return new(TypeName(metadata, names, member.Parent), Outermost(metadata, member.Parent), metadata.GetString(member.Name),
                    signature.ParameterTypes, signature.ReturnType, Array.Empty<string>());
            }

            case HandleKind.MethodDefinition:
            {
                var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                var signature = method.DecodeSignature(names, null);
                var declaring = method.GetDeclaringType();
                return new(TypeName(metadata, names, declaring), OutermostName(metadata, metadata.GetTypeDefinition(declaring)), metadata.GetString(method.Name),
                    signature.ParameterTypes, signature.ReturnType, Array.Empty<string>());
            }

            case HandleKind.MethodSpecification:
            {
                var specification = metadata.GetMethodSpecification((MethodSpecificationHandle)handle);
                if (Resolve(metadata, names, specification.Method) is not { } generic) return null;
                return generic with { MethodArguments = specification.DecodeSignature(names, null) };
            }

            default:
                return null;
        }
    }

    /// <summary>The outermost type of a method's parent when it is defined in this assembly (null for references).</summary>
    private static string? Outermost(MetadataReader metadata, EntityHandle parent) => parent.Kind switch
    {
        HandleKind.TypeDefinition => OutermostName(metadata, metadata.GetTypeDefinition((TypeDefinitionHandle)parent)),
        HandleKind.TypeReference => OutermostReference(metadata, (TypeReferenceHandle)parent),
        HandleKind.MethodDefinition => OutermostName(metadata, metadata.GetTypeDefinition(metadata.GetMethodDefinition((MethodDefinitionHandle)parent).GetDeclaringType())),
        _ => null,
    };

    private static string OutermostReference(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var type = metadata.GetTypeReference(handle);
        while (type.ResolutionScope.Kind == HandleKind.TypeReference) type = metadata.GetTypeReference((TypeReferenceHandle)type.ResolutionScope);
        var ns = metadata.GetString(type.Namespace);
        return ns.Length == 0 ? metadata.GetString(type.Name) : ns + "." + metadata.GetString(type.Name);
    }

    private static string TypeName(MetadataReader metadata, NameProvider names, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeReference => names.GetTypeFromReference(metadata, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeDefinition => names.GetTypeFromDefinition(metadata, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeSpecification => metadata.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(names, null),
        HandleKind.MethodDefinition => TypeName(metadata, names, metadata.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType()),
        _ => handle.Kind.ToString(),
    };

    private static string OutermostName(MetadataReader metadata, TypeDefinition type)
    {
        while (type.GetDeclaringType() is { IsNil: false } outer) type = metadata.GetTypeDefinition(outer);
        var ns = metadata.GetString(type.Namespace);
        return ns.Length == 0 ? metadata.GetString(type.Name) : ns + "." + metadata.GetString(type.Name);
    }

    private static string NestedName(MetadataReader metadata, TypeDefinition type)
    {
        var name = metadata.GetString(type.Name);
        while (type.GetDeclaringType() is { IsNil: false } outer)
        {
            type = metadata.GetTypeDefinition(outer);
            name = metadata.GetString(type.Name) + "+" + name;
        }

        var ns = metadata.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private sealed class NameProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[,]";

        public string GetByReferenceType(string elementType) => elementType + "&";

        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";

        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index.ToString(CultureInfo.InvariantCulture);

        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index.ToString(CultureInfo.InvariantCulture);

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => NestedName(reader, reader.GetTypeDefinition(handle));

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            var name = reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
                return GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + name;
            var ns = reader.GetString(type.Namespace);
            return (ns.Length == 0 ? string.Empty : ns + ".") + name;
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
