using System.Globalization;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using Xunit;
using Reals = TiaMcpServer.Contracts.Plc.TechnologyObjects.TechnologyObjectReals;

namespace TiaMcpServer.Tests.Plc.TechnologyObjects;

/// <summary>
/// The runtime-independent real conversion. .NET 10 parses correctly rounded (IEEE 754) and formats "R" as the
/// shortest round-trip text except for a few doubles where its R text does not round-trip (pinned below), so it is the
/// oracle here; the conversion itself uses no runtime real parser or formatter (pinned by an IL scan), so the .NET
/// Framework worker computes exactly the same bits and texts.
/// </summary>
public sealed class TechnologyObjectRealsTests
{
    private static double ParseDouble(string text)
    {
        Assert.True(Reals.TryParseText(text, out var negative, out var digits, out var exponent), text);
        Assert.True(Reals.TryToDouble(negative, digits, exponent, out var value), text);
        return value;
    }

    private static float ParseSingle(string text)
    {
        Assert.True(Reals.TryParseText(text, out var negative, out var digits, out var exponent), text);
        Assert.True(Reals.TryToSingle(negative, digits, exponent, out var value), text);
        return value;
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    [Fact]
    public void ShortestTextAndParsingMatchTheIeeeOracleForRandomDoubles()
    {
        var random = new Random(20261101);
        var buffer = new byte[8];
        for (var i = 0; i < 2500; i++)
        {
            random.NextBytes(buffer);
            var value = BitConverter.ToDouble(buffer, 0);
            if (!double.IsFinite(value)) continue;
            var oracle = value.ToString("R", CultureInfo.InvariantCulture);
            // .NET 10's R does not round-trip a few doubles (see TheShortestTextRoundTripsWhereNetRDoesNot): only the round-trip is compared there.
            if (Bits(double.Parse(oracle, CultureInfo.InvariantCulture)) != Bits(value))
            {
                Assert.Equal(Bits(value), Bits(ParseDouble(Reals.Shortest(value))));
                continue;
            }

            Assert.Equal(oracle, Reals.Shortest(value));
            Assert.Equal(Bits(value), Bits(ParseDouble(oracle)));
            Assert.Equal(Bits(value), Bits(ParseDouble(Reals.Exact(value))));
        }
    }

    [Fact]
    public void ShortestTextAndParsingMatchTheIeeeOracleForRandomSingles()
    {
        var random = new Random(1101);
        var buffer = new byte[4];
        for (var i = 0; i < 2500; i++)
        {
            random.NextBytes(buffer);
            var value = BitConverter.ToSingle(buffer, 0);
            if (!float.IsFinite(value)) continue;
            var oracle = value.ToString("R", CultureInfo.InvariantCulture);
            if (Bits(float.Parse(oracle, CultureInfo.InvariantCulture)) != Bits(value))
            {
                Assert.Equal(Bits(value), Bits(ParseSingle(Reals.Shortest(value))));
                continue;
            }

            Assert.Equal(oracle, Reals.Shortest(value));
            Assert.Equal(Bits(value), Bits(ParseSingle(oracle)));
            Assert.Equal(Bits(value), Bits(ParseSingle(Reals.Exact(value))));
        }
    }

    [Fact]
    public void ParsingIsCorrectlyRoundedForRandomDecimalTexts()
    {
        var random = new Random(4711);
        for (var i = 0; i < 3000; i++)
        {
            var digits = new string(Enumerable.Range(0, random.Next(1, 30)).Select(_ => (char)('0' + random.Next(10))).ToArray());
            var exponent = random.Next(-360, 330);
            var text = (random.Next(2) == 0 ? "-" : string.Empty) + digits + "e" + exponent.ToString(CultureInfo.InvariantCulture);
            Assert.True(Reals.TryParseText(text, out var negative, out var mantissa, out var power));
            var oracleDouble = double.Parse(text, CultureInfo.InvariantCulture);
            if (double.IsInfinity(oracleDouble)) Assert.False(Reals.TryToDouble(negative, mantissa, power, out _), text);
            else Assert.Equal(Bits(oracleDouble), Bits(ParseDouble(text)));
            var oracleSingle = float.Parse(text, CultureInfo.InvariantCulture);
            if (float.IsInfinity(oracleSingle)) Assert.False(Reals.TryToSingle(negative, mantissa, power, out _), text);
            else Assert.Equal(Bits(oracleSingle), Bits(ParseSingle(text)));
        }
    }

    public static TheoryData<double> DoubleExtremes => new()
    {
        double.MaxValue, -double.MaxValue, double.Epsilon, -double.Epsilon, 2.2250738585072014E-308, 2.2250738585072009E-308,
        4.9406564584124654E-322, 1.0, 0.1, 0.30000000000000004, 9007199254740992.0, 1e15, 1e16, 123456789012345680.0, 1e-5, 1e-4, 1e23,
        8.563919745513608, Math.Pow(2, 90), Math.Pow(2, -1022), Math.Pow(2, 1023),
    };

    [Theory]
    [MemberData(nameof(DoubleExtremes))]
    public void DoubleExtremesRoundTripLikeTheOracle(double value)
    {
        Assert.Equal(value.ToString("R", CultureInfo.InvariantCulture), Reals.Shortest(value));
        Assert.Equal(Bits(value), Bits(ParseDouble(Reals.Shortest(value))));
        Assert.Equal(Bits(value), Bits(ParseDouble(Reals.Exact(value))));
    }

    public static TheoryData<float> SingleExtremes => new()
    {
        float.MaxValue, -float.MaxValue, float.Epsilon, -float.Epsilon, 1.17549435E-38f, 1.1754942E-38f, 16777216f, 0.1f, 1e7f, 1e-5f,
        (float)Math.Pow(2, 90), (float)Math.Pow(2, -126), 3.4028233E+38f,
    };

    [Theory]
    [MemberData(nameof(SingleExtremes))]
    public void SingleExtremesRoundTripLikeTheOracle(float value)
    {
        Assert.Equal(value.ToString("R", CultureInfo.InvariantCulture), Reals.Shortest(value));
        Assert.Equal(Bits(value), Bits(ParseSingle(Reals.Shortest(value))));
        Assert.Equal(Bits(value), Bits(ParseSingle(Reals.Exact(value))));
    }

    [Fact]
    public void OverflowStartsAtTheLargestValuePlusHalfAnUlp()
    {
        // float.MaxValue + half an ulp ties to the even neighbour 2^128, which overflows; anything below rounds to MaxValue.
        Assert.Equal(float.MaxValue, ParseSingle("340282356779733661637539395458142568447"));
        Assert.True(Reals.TryParseText("340282356779733661637539395458142568448", out var n, out var d, out var e));
        Assert.False(Reals.TryToSingle(n, d, e, out _));
        Assert.Equal(double.MaxValue, ParseDouble("1.7976931348623158079e308"));
        Assert.True(Reals.TryParseText("1.7976931348623158080e308", out n, out d, out e));
        Assert.False(Reals.TryToDouble(n, d, e, out _));
        // Half the smallest subnormal ties to even (zero); anything above it rounds to the smallest subnormal.
        const string HalfEpsilon = "7.00649232162408535461864791644958065640130970938257885878534141944895541342930300743319094181060791015625e-46";
        Assert.Equal(Bits(0f), Bits(ParseSingle(HalfEpsilon)));
        Assert.Equal(float.Epsilon, ParseSingle("7.0064923216240854e-46"));
        Assert.Equal(Bits(double.Epsilon), Bits(ParseDouble("2.4703282292062328e-324")));
        Assert.Equal(Bits(0.0), Bits(ParseDouble("2.4703282292062327e-324")));
    }

    [Fact]
    public void TheSignOfZeroIsKept()
    {
        Assert.Equal(Bits(-0.0), Bits(ParseDouble("-0")));
        Assert.Equal(Bits(-0.0), Bits(ParseDouble("-0.000e5")));
        Assert.Equal(Bits(0.0), Bits(ParseDouble("0")));
        Assert.Equal(Bits(-0f), Bits(ParseSingle("-0.0")));
        Assert.Equal(Bits(-0.0), Bits(ParseDouble("-1e-500")));
        Assert.Equal("-0", Reals.Shortest(-0.0));
        Assert.Equal("-0", Reals.Shortest(-0f));
        Assert.False(Reals.SameBits(-0.0, 0.0));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("-1.5")]
    [InlineData("+.5")]
    [InlineData("5.")]
    [InlineData("1E-3")]
    [InlineData("1e+0003")]
    [InlineData("0000.000")]
    public void InvariantNumberSyntaxIsAccepted(string text) => Assert.True(Reals.TryParseText(text, out _, out _, out _));

    [Theory]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1,5")]
    [InlineData("1e")]
    [InlineData("e5")]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0x10")]
    [InlineData("1_000")]
    public void OtherTextIsRefused(string text) => Assert.False(Reals.TryParseText(text, out _, out _, out _));

    [Fact]
    public void DecimalsAreHeldExactlyOrRefusedWithTheNearest()
    {
        Assert.True(Reals.TryParseText("79228162514264337593543950335", out var n, out var d, out var e));
        Assert.True(Reals.TryToDecimalExactly(n, d, e, out var max, out _));
        Assert.Equal(decimal.MaxValue, max);
        Assert.True(Reals.TryParseText("79228162514264337593543950336", out n, out d, out e));
        Assert.False(Reals.TryToDecimalExactly(n, d, e, out _, out var none));
        Assert.Null(none);
        Assert.True(Reals.TryParseText("1e-28", out n, out d, out e));
        Assert.True(Reals.TryToDecimalExactly(n, d, e, out var tiny, out _));
        Assert.Equal(0.0000000000000000000000000001m, tiny);
        Assert.True(Reals.TryParseText("1.5e-28", out n, out d, out e));
        Assert.False(Reals.TryToDecimalExactly(n, d, e, out _, out var nearest));
        Assert.Equal("0.0000000000000000000000000002", Reals.Render(nearest!.Value));
        Assert.Equal("1.50", Reals.Render(1.50m));
        Assert.Equal("-12.5", Reals.Render(-12.5m));
        Assert.Equal("0", Reals.Render(0m));
    }

    private const string RealsType = "TiaMcpServer.Contracts.Plc.TechnologyObjects.TechnologyObjectReals";
    private const string ValuesType = "TiaMcpServer.Contracts.Plc.TechnologyObjects.TechnologyObjectValues";

    /// <summary>
    /// The conversion must not depend on the runtime's real parsing or formatting (which differs between .NET Framework
    /// and .NET 10): the compiled IL of TechnologyObjectReals and TechnologyObjectValues (nested and compiler-generated
    /// types included) makes none of the calls of <see cref="RealIlRules.Strict"/>.
    /// </summary>
    [Fact]
    public void TheConversionCoreIlCallsNoRuntimeRealParserOrFormatter()
    {
        var scan = RealFormattingIlScanner.ScanTypes(typeof(Reals).Assembly.Location, type => type is RealsType or ValuesType, RealIlRules.Strict);
        Assert.True(scan.Findings.Count == 0, string.Join(Environment.NewLine, scan.Findings));
        Assert.True(scan.Methods > 40 && scan.Calls > 200, $"only {scan.Methods} methods and {scan.Calls} calls were scanned");
    }

    public static TheoryData<string> TechnologyObjectProjects => new() { "TiaMcpServer.Contracts", "TiaMcpServer.OpennessWorker", "TiaMcpServer.FakeWorker", "TiaMcpServer" };

    /// <summary>
    /// No technology object code of the four assemblies (Contracts, the net48 worker, the FakeWorker, the host) parses,
    /// formats or converts a real with the runtime (<see cref="RealIlRules.Reals"/>; calls into the strictly scanned core
    /// are the sanctioned conversion), and outside the FakeWorker's scripted values none boxes a real. Methods are chosen
    /// by their source file in the PDB, and every technology object source file of the project must have contributed IL,
    /// so a file the selection misses fails the test instead of being skipped.
    /// </summary>
    [Theory]
    [MemberData(nameof(TechnologyObjectProjects))]
    public void NoTechnologyObjectCodeParsesOrFormatsRealsWithTheRuntime(string project)
    {
        var root = SolutionRoot();
        var sources = Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal))
            .Where(path => RealFormattingIlScanner.IsTechnologyObjectSource(path, project))
            .Select(path => path.Substring(project.Length + 1)).OrderBy(path => path, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(sources);
        var assembly = BuiltAssembly(root, project, sources);

        var scan = RealFormattingIlScanner.ScanDocuments(assembly, document => RealFormattingIlScanner.IsTechnologyObjectSource(document, project), RealIlRules.Reals,
            exemptType: type => type is RealsType or ValuesType, reportBoxedReals: project != "TiaMcpServer.FakeWorker");
        Assert.True(scan.Findings.Count == 0, string.Join(Environment.NewLine, scan.Findings));
        var scanned = scan.Documents.Select(document => ProjectRelative(document, project)).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(sources, sources.Where(scanned.Contains).ToList());
        Assert.True(scan.Calls > 50, $"only {scan.Calls} calls were scanned in {assembly}");
    }

    private static HashSet<string> Origins(RealIlScan scan) => scan.Findings.Select(finding => finding.Origin).ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> Samples(Type type)
        => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(method => method.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The scanner's self-test: each forbidden sample is reported by the rule sets meant to report it, compiler-generated
    /// bodies (lambda, local function, iterator, async state machine) included, and no clean sample by either.
    /// </summary>
    [Fact]
    public void TheIlScannerReportsEveryForbiddenShapeAndNoCleanOne()
    {
        var path = typeof(RealFormattingSamples).Assembly.Location;
        var reals = Samples(typeof(RealFormattingSamples.Reals));
        var objects = Samples(typeof(RealFormattingSamples.Objects));
        Assert.Equal(23, reals.Count);
        Assert.Equal(9, objects.Count);
        Assert.Equal(7, Samples(typeof(RealFormattingSamples.Clean)).Count);

        var strict = RealFormattingIlScanner.ScanTypes(path, type => type == typeof(RealFormattingSamples).FullName, RealIlRules.Strict);
        Assert.Equal(reals.Concat(objects).OrderBy(n => n), Origins(strict).OrderBy(n => n));
        var realRules = RealFormattingIlScanner.ScanTypes(path, type => type == typeof(RealFormattingSamples).FullName, RealIlRules.Reals);
        Assert.Equal(reals.OrderBy(n => n), Origins(realRules).OrderBy(n => n));

        // The bodies themselves are read, not only the methods that create them.
        Assert.Contains(realRules.Findings, f => f.Method.StartsWith("<InLambda>b__", StringComparison.Ordinal) && f.Callee.StartsWith("System.Double::ToString", StringComparison.Ordinal));
        Assert.Contains(realRules.Findings, f => f.Method.StartsWith("<InLocalFunction>g__", StringComparison.Ordinal));
        Assert.Contains(realRules.Findings, f => f.Type.Contains("<InAsync>d__", StringComparison.Ordinal) && f.Callee.StartsWith("System.Double::Parse", StringComparison.Ordinal));
        Assert.Contains(realRules.Findings, f => f.Type.Contains("<InIterator>d__", StringComparison.Ordinal) && f.Callee.StartsWith("System.Single::ToString", StringComparison.Ordinal));
        // constrained. Nullable<double> callvirt Object::ToString counts as Nullable<double>.ToString.
        Assert.Contains(realRules.Findings, f => f.Method == "NullableToString" && f.Callee.StartsWith("System.Nullable`1<System.Double>::ToString", StringComparison.Ordinal));
    }

    /// <summary>
    /// Selecting by source document finds the same methods as selecting by type, compiler-generated ones included; boxing
    /// a real is reported when asked, and calls into an exempted type are not.
    /// </summary>
    [Fact]
    public void TheDocumentSelectionAttributesCompilerGeneratedCodeToItsSourceFile()
    {
        var path = typeof(RealFormattingSamples).Assembly.Location;
        static bool SamplesFile(string document) => document.Replace('\\', '/').EndsWith("/RealFormattingSamples.cs", StringComparison.Ordinal);
        var scan = RealFormattingIlScanner.ScanDocuments(path, SamplesFile, RealIlRules.Reals, exemptType: null, reportBoxedReals: false);
        Assert.Single(scan.Documents);
        Assert.Equal(Samples(typeof(RealFormattingSamples.Reals)).OrderBy(n => n), Origins(scan).OrderBy(n => n));
        Assert.Contains(scan.Findings, f => f.Type.Contains("<InAsync>d__", StringComparison.Ordinal));
        Assert.Contains(scan.Findings, f => f.Method.StartsWith("<InLambda>b__", StringComparison.Ordinal));

        var boxed = RealFormattingIlScanner.ScanDocuments(path, SamplesFile, RealIlRules.Reals, exemptType: null, reportBoxedReals: true);
        Assert.Contains(boxed.Findings, f => f.Origin == "DelegateToString" && f.Callee == "box System.Double");
        var exempted = RealFormattingIlScanner.ScanDocuments(path, SamplesFile, RealIlRules.Reals, exemptType: type => type == "System.Double", reportBoxedReals: false);
        Assert.DoesNotContain(exempted.Findings, f => f.Origin == "ParseDouble");

        Assert.True(RealFormattingIlScanner.IsTechnologyObjectSource(@"D:\a\TiaMcpServer\TiaMcpServer\Plc\TechnologyObjects\PlcTechnologyObjectPlanning.cs", "TiaMcpServer"));
        Assert.True(RealFormattingIlScanner.IsTechnologyObjectSource("/_/TiaMcpServer.OpennessWorker/Program.TechnologyObjects.cs", "TiaMcpServer.OpennessWorker"));
        // A clone directory named like the host project does not make the test files host sources.
        Assert.False(RealFormattingIlScanner.IsTechnologyObjectSource("/src/TiaMcpServer/TiaMcpServer.Tests/Plc/TechnologyObjects/TechnologyObjectRealsTests.cs", "TiaMcpServer"));
        Assert.False(RealFormattingIlScanner.IsTechnologyObjectSource("/src/TiaMcpServer/TiaMcpServer/Plc/PlcOperationCatalog.cs", "TiaMcpServer"));
    }

    private static string SolutionRoot() => TiaMcpServer.Tests.TestSupport.RepositoryFiles.Root;

    /// <summary>The path below the project folder of a PDB document (separators normalized).</summary>
    private static string ProjectRelative(string document, string project)
    {
        var segments = document.Replace('\\', '/').Split('/');
        var index = Array.FindLastIndex(segments, segment => RealFormattingIlScanner.ProjectFolders.Contains(segment, StringComparer.Ordinal));
        return index >= 0 && segments[index] == project ? string.Join("/", segments.Skip(index + 1)) : document;
    }

    /// <summary>
    /// The product assembly of <paramref name="project"/> in the configuration of this test run. Contracts is the copy the
    /// tests load; the worker, FakeWorker and host are their projects' build outputs, and must be newer than every
    /// technology object source file scanned (the test project does not build the worker or the host, so a stale build is
    /// refused instead of scanned).
    /// </summary>
    private static string BuiltAssembly(string root, string project, IReadOnlyList<string> sources)
    {
        if (project == "TiaMcpServer.Contracts") return typeof(Reals).Assembly.Location;
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var path = project switch
        {
            "TiaMcpServer.OpennessWorker" => Path.Combine(root, project, "bin", configuration, "net48", "TiaMcpServer.OpennessWorker.exe"),
            _ => Path.Combine(root, project, "bin", configuration, "net10.0", project + ".dll"),
        };
        Assert.True(File.Exists(path), $"{path} is missing; build the solution (dotnet build TiaMcpServer.slnx) before running the tests.");
        var built = File.GetLastWriteTimeUtc(path);
        var newer = sources.Where(source => File.GetLastWriteTimeUtc(Path.Combine(root, project, source)) > built).ToList();
        Assert.True(newer.Count == 0, $"{path} is older than {string.Join(", ", newer)}; build the solution before running the tests.");
        return path;
    }

    /// <summary>
    /// .NET 10's R text of these doubles (2^-25, 2^-958 and their negatives) does not round-trip; the shortest text here
    /// does, and it is the text Python's repr gives (David Gay's shortest round-trip algorithm, an independent oracle).
    /// R's text is instead the shortest text of the double below, so a value shown with R would be written back as that
    /// neighbour; the description used here converts back to exactly the described bits.
    /// </summary>
    [Theory]
    [InlineData(0x3E60000000000000L, "2.9802322387695312E-08")]
    [InlineData(0x0410000000000000L, "4.1045368012983762E-289")]
    [InlineData(unchecked((long)0xBE60000000000000UL), "-2.9802322387695312E-08")]
    [InlineData(unchecked((long)0x8410000000000000UL), "-4.1045368012983762E-289")]
    public void TheShortestTextRoundTripsWhereNetRDoesNot(long bits, string shortest)
    {
        var value = BitConverter.Int64BitsToDouble(bits);
        Assert.Equal(shortest, Reals.Shortest(value));
        Assert.Equal(bits, Bits(double.Parse(shortest, CultureInfo.InvariantCulture)));
        Assert.Equal(bits, Bits(ParseDouble(shortest)));

        var r = value.ToString("R", CultureInfo.InvariantCulture);
        var neighbour = double.Parse(r, CultureInfo.InvariantCulture);
        Assert.Equal(bits - 1, Bits(neighbour));
        Assert.Equal(r, Reals.Shortest(neighbour));

        var described = TechnologyObjectValues.Describe(value).Text;
        Assert.Equal(shortest, described);
        Assert.True(TechnologyObjectValues.TryConvert(described, typeof(double), out var back, out var error), error);
        Assert.Equal(bits, Bits((double)back!));
        // One digit fewer than the shortest text does not round-trip (it is the neighbour's text).
        Assert.True(TechnologyObjectValues.TryConvert(r, typeof(double), out var other, out error), error);
        Assert.Equal(bits - 1, Bits((double)other!));
    }

    /// <summary>The reviewed float case 2^-25 (and its negative), pinned to the text of an exact rational shortest search.</summary>
    [Theory]
    [InlineData(0x33000000, "2.9802322E-08")]
    [InlineData(unchecked((int)0xB3000000), "-2.9802322E-08")]
    public void TheShortestSingleTextOfTwoToTheMinus25IsPinned(int bits, string shortest)
    {
        var value = BitConverter.Int32BitsToSingle(bits);
        Assert.Equal(shortest, Reals.Shortest(value));
        Assert.Equal(bits, Bits(ParseSingle(shortest)));
        Assert.Equal(bits, Bits(float.Parse(shortest, CultureInfo.InvariantCulture)));
        Assert.True(TechnologyObjectValues.TryConvert(TechnologyObjectValues.Describe(value).Text, typeof(float), out var back, out var error), error);
        Assert.Equal(bits, Bits((float)back!));
        // Seven digits are too few: neither neighbour at seven digits converts back to these bits.
        foreach (var seven in new[] { "2.980232E-08", "2.980233E-08" })
            Assert.NotEqual(Math.Abs(bits), Math.Abs(Bits(ParseSingle(seven))));
    }

    /// <summary>
    /// Every power of two (normal and subnormal, float and double) is where the rounding interval is asymmetric: the
    /// shortest text round-trips through this parser and .NET 10's, and equals .NET 10's R wherever R round-trips.
    /// </summary>
    [Fact]
    public void EveryPowerOfTwoHasARoundTrippingShortestText()
    {
        for (var bit = 0; bit < 2046 + 52; bit++)
        {
            var bits = bit < 52 ? 1L << bit : (long)(bit - 51) << 52;
            var value = BitConverter.Int64BitsToDouble(bits);
            var shortest = Reals.Shortest(value);
            Assert.Equal(bits, Bits(ParseDouble(shortest)));
            Assert.Equal(bits, Bits(double.Parse(shortest, CultureInfo.InvariantCulture)));
            var r = value.ToString("R", CultureInfo.InvariantCulture);
            if (Bits(double.Parse(r, CultureInfo.InvariantCulture)) == bits) Assert.Equal(r, shortest);
        }

        for (var bit = 0; bit < 254 + 23; bit++)
        {
            var bits = bit < 23 ? 1 << bit : (bit - 22) << 23;
            var value = BitConverter.Int32BitsToSingle(bits);
            var shortest = Reals.Shortest(value);
            Assert.Equal(bits, Bits(ParseSingle(shortest)));
            Assert.Equal(bits, Bits(float.Parse(shortest, CultureInfo.InvariantCulture)));
            var r = value.ToString("R", CultureInfo.InvariantCulture);
            if (Bits(float.Parse(r, CultureInfo.InvariantCulture)) == bits) Assert.Equal(r, shortest);
        }
    }

    /// <summary>Extreme exponents end in bounded time and never throw: far below resolution is the nearest 0, far above out of range.</summary>
    [Theory]
    [InlineData("1e-100000000")]
    [InlineData("1e-999999999999")]
    [InlineData("1e-2000000000")]
    [InlineData("-7e-99999999999999999999")]
    [InlineData("1e100000000")]
    [InlineData("1e999999999999")]
    public void ExtremeExponentsAreBoundedForEveryTarget(string text)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(Reals.TryParseText(text, out var negative, out var digits, out var exponent));
        var huge = exponent > 0;
        Assert.Equal(!huge, Reals.TryToDouble(negative, digits, exponent, out var asDouble));
        Assert.Equal(!huge, Reals.TryToSingle(negative, digits, exponent, out _));
        if (!huge) Assert.Equal(0.0, asDouble);
        Assert.False(Reals.TryToDecimalExactly(negative, digits, exponent, out _, out var nearest));
        if (huge) Assert.Null(nearest);
        else Assert.Equal(0m, nearest);
        foreach (var target in new[] { typeof(double), typeof(float), typeof(decimal) })
        {
            // Far below the resolution the nearest value is 0 (named in the refusal); far above it is out of range.
            Assert.False(TechnologyObjectValues.TryConvert(text, target, out _, out var error));
            var zero = negative && target != typeof(decimal) ? "-0" : "0";
            Assert.Contains(huge ? "outside the range of " + target.FullName : $"the nearest representable value is {zero}", error);
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"{text} took {watch.Elapsed}");
    }

    /// <summary>
    /// The conversions take any <see cref="long"/> exponent, not only the saturated ones TryParseText produces: the range
    /// is decided without adding to the exponent, so long.MaxValue cannot wrap around into a tiny value (or long.MinValue
    /// into a huge one), and no power of ten beyond the bound is ever formed.
    /// </summary>
    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MaxValue - 1)]
    [InlineData((long)int.MaxValue + 5)]
    [InlineData(int.MaxValue)]
    [InlineData(1000)]
    [InlineData(-1000)]
    [InlineData(int.MinValue)]
    [InlineData((long)int.MinValue - 5)]
    [InlineData(long.MinValue + 1)]
    [InlineData(long.MinValue)]
    public void AnyExponentIsDecidedWithoutOverflow(long exponent)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var huge = exponent > 0;
        foreach (var digits in new[] { System.Numerics.BigInteger.One, new System.Numerics.BigInteger(123456789), System.Numerics.BigInteger.Pow(10, 40) + 7 })
        {
            Assert.Equal(!huge, Reals.TryToDouble(true, digits, exponent, out var asDouble));
            if (!huge) Assert.Equal(Bits(-0.0), Bits(asDouble));
            Assert.Equal(!huge, Reals.TryToSingle(false, digits, exponent, out var asSingle));
            if (!huge) Assert.Equal(Bits(0f), Bits(asSingle));
            Assert.False(Reals.TryToDecimalExactly(false, digits, exponent, out _, out var nearest));
            Assert.Equal(huge ? (decimal?)null : 0m, nearest);
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"{exponent} took {watch.Elapsed}");
    }

    [Fact]
    public void DigitsWithTrailingZerosFromADirectCallAreStillExact()
    {
        // TryParseText strips trailing zeros; a direct caller may not: 10^30 × 10^-2 is 10^28, which decimal holds exactly.
        Assert.True(Reals.TryToDecimalExactly(false, System.Numerics.BigInteger.Pow(10, 30), -2, out var value, out var nearest));
        Assert.Null(nearest);
        Assert.Equal(10000000000000000000000000000m, value);
    }

    [Theory]
    [InlineData("7922816251426433759354395033.56", "7922816251426433759354395033.5")]
    [InlineData("-7922816251426433759354395033.56", "-7922816251426433759354395033.5")]
    [InlineData("79228162514264337593543950335.4", "79228162514264337593543950335")]
    [InlineData("1.5e-28", "0.0000000000000000000000000002")]
    [InlineData("2.5e-28", "0.0000000000000000000000000002")]
    [InlineData("0.12345678901234567890123456789", "0.1234567890123456789012345679")]
    [InlineData("1e-40", "0")]
    public void TheNearestDecimalIsTrulyTheNearest(string text, string nearest)
    {
        Assert.True(Reals.TryParseText(text, out var negative, out var digits, out var exponent));
        Assert.False(Reals.TryToDecimalExactly(negative, digits, exponent, out _, out var held));
        Assert.Equal(nearest, Reals.Render(held!.Value));
    }

    [Fact]
    public void ValuesLongerThanTheTextBoundAreRefusedWithTheBoundNamed()
    {
        var tooLong = new string('1', TechnologyObjectPolicy.MaxTextChars + 1);
        foreach (var target in new[] { typeof(double), typeof(decimal), typeof(string), typeof(int) })
        {
            Assert.False(TechnologyObjectValues.TryConvert(tooLong, target, out var value, out var error));
            Assert.Null(value);
            Assert.Equal("'value' has 4001 characters; at most 4000 are accepted", error);
            Assert.Equal(TechnologyObjectPolicy.ValueLengthRefusal(tooLong), error);
        }

        Assert.Null(TechnologyObjectPolicy.ValueLengthRefusal(new string('1', TechnologyObjectPolicy.MaxTextChars)));
        Assert.True(TechnologyObjectValues.TryConvert(new string('1', TechnologyObjectPolicy.MaxTextChars), typeof(string), out _, out _));
    }
}
