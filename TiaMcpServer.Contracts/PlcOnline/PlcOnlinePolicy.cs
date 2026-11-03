namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>
/// Siemens-free rules of the compare and online operations, shared by host, worker and FakeWorker:
/// caps, defaults, state names, the GoOnline polling loop and the bounded compare-tree projection.
/// Enum values cross the boundary by name only (the stubs declare no enum members).
/// </summary>
public static class PlcOnlinePolicy
{
    public const string DefaultMode = "PN/IE";

    public const int MaxNameLength = 256;

    /// <summary>Upper bound of a station-upload address (an IP address or a bus address as text).</summary>
    public const int MaxAddressLength = 64;

    public const int DefaultCompareDepth = 16;

    public const int MaxCompareDepth = 64;

    public const int DefaultCompareElements = 300;

    public const int MaxCompareElements = 5000;

    public const int MaxTextChars = 1000;

    public const int DefaultTimeoutSeconds = 30;

    public const int MaxTimeoutSeconds = 120;

    /// <summary>Siemens' online-and-download guidance polls every 200 ms.</summary>
    public const int PollIntervalMilliseconds = 200;

    public const int MaxObservedStates = 50;

    /// <summary>compare_software targets. A global library is not offered: the sources show no way to obtain an opened one.</summary>
    public static class CompareTargets
    {
        public const string Plc = "plc";
        public const string ProjectLibrary = "project_library";
        public const string MasterCopy = "master_copy";

        /// <summary>A PLC of an open secondary (read-only reference) project (issue #20): targetName names it, targetProject the project.</summary>
        public const string SecondaryPlc = "secondary_plc";

        public static IReadOnlyList<string> All { get; } = new[] { Plc, ProjectLibrary, MasterCopy, SecondaryPlc };

        public static bool NeedsName(string? target) => target is Plc or MasterCopy or SecondaryPlc;
    }

    /// <summary>compare_hardware targets: another PLC's hardware or a root master copy of the project library (no library itself).</summary>
    public static IReadOnlyList<string> HardwareCompareTargets { get; } = new[] { CompareTargets.Plc, CompareTargets.MasterCopy };

    /// <summary>Which hardware object of the PLC the hardware compares use: its station (<c>Device</c>) or its CPU device item.</summary>
    public static class HardwareScopes
    {
        public const string Device = "device";
        public const string Cpu = "cpu";

        public static IReadOnlyList<string> All { get; } = new[] { Device, Cpu };
    }

    /// <summary>The documented interface a hardware compare target must implement (checked on the live object).</summary>
    public const string HardwareCompareTargetType = "Siemens.Engineering.HW.IHardwareCompareTarget";

    /// <summary>Host and worker validation of a hardware compare request; null when valid.</summary>
    public static string? ValidateHardwareCompare(string? compareTarget, string? targetName, string? hardwareScope, int maxDepth, int maxElements, bool online)
    {
        if (hardwareScope is not null && !HardwareScopes.All.Contains(hardwareScope))
            return $"hardwareScope must be one of: {string.Join(", ", HardwareScopes.All)}.";
        if (maxDepth < 1 || maxDepth > MaxCompareDepth) return $"maxDepth must be between 1 and {MaxCompareDepth}.";
        if (maxElements < 1 || maxElements > MaxCompareElements) return $"maxElements must be between 1 and {MaxCompareElements}.";
        if (online) return compareTarget is null && targetName is null ? null : "compare_hardware_to_online takes no compareTarget or targetName.";
        if (compareTarget is null || !HardwareCompareTargets.Contains(compareTarget))
            return $"compareTarget must be one of: {string.Join(", ", HardwareCompareTargets)}.";
        return string.IsNullOrWhiteSpace(targetName) || targetName!.Length > MaxNameLength
            ? $"compareTarget '{compareTarget}' requires a nonblank targetName of at most {MaxNameLength} characters."
            : null;
    }

    /// <summary>The documented V21 <c>OnlineState</c> member names.</summary>
    public static class States
    {
        public const string Offline = "Offline";
        public const string Connecting = "Connecting";
        public const string Online = "Online";
        public const string Disconnecting = "Disconnecting";
        public const string Incompatible = "Incompatible";
        public const string NotReachable = "NotReachable";
        public const string Protected = "Protected";

        public static IReadOnlyList<string> All { get; } = new[] { Offline, Connecting, Online, Disconnecting, Incompatible, NotReachable, Protected };

        /// <summary>States after which polling for <see cref="Online"/> stops without success.</summary>
        public static bool IsTerminalFailure(string? state) => state is Incompatible or NotReachable or Protected;
    }

    /// <summary>The documented <c>CompareResultState</c> names that mean "no difference".</summary>
    /// <summary>What a go-online/go-offline call did (<see cref="PlcOnlineActionResultInfo.Outcome"/>).</summary>
    public static class ActionOutcomes
    {
        /// <summary>go_online: the requested path was applied and Online was reached.</summary>
        public const string Connected = "connected";

        /// <summary>go_online: the PLC was already online; nothing was applied and the existing path is unverified.</summary>
        public const string AlreadyOnline = "already_online";

        /// <summary>go_online: ApplyConfiguration returned false, so GoOnline was not called.</summary>
        public const string ConfigurationNotApplied = "configuration_not_applied";

        /// <summary>The goal state was not reached (timeout or a terminal state).</summary>
        public const string NotReached = "not_reached";

        /// <summary>go_offline: Offline was reached after GoOffline.</summary>
        public const string Disconnected = "disconnected";

        /// <summary>go_offline: the PLC was already offline; GoOffline was not called.</summary>
        public const string AlreadyOffline = "already_offline";

        public static IReadOnlyList<string> ForGoOnline { get; } = new[] { Connected, AlreadyOnline, ConfigurationNotApplied, NotReached };

        public static IReadOnlyList<string> ForGoOffline { get; } = new[] { Disconnected, AlreadyOffline, NotReached };

        /// <summary>The goal state was reached for exactly these outcomes.</summary>
        public static bool ReachesGoal(string? outcome) => outcome is Connected or AlreadyOnline or Disconnected or AlreadyOffline;

        /// <summary>A path was applied (and must be the planned one) for exactly these outcomes.</summary>
        public static bool AppliesPath(string? outcome) => outcome is Connected or ConfigurationNotApplied or NotReached;
    }

    public static bool IsIdenticalState(string? state) => state is "ObjectsIdentical" or "FolderContentsIdentical";

    public static string? BoundText(string? text)
        => text is null ? null : text.Length <= MaxTextChars ? text : text.Substring(0, MaxTextChars);

    /// <summary>Host and worker validation of a compare request; null when valid.</summary>
    public static string? ValidateCompare(string? compareTarget, string? targetName, int maxDepth, int maxElements, bool online)
    {
        if (maxDepth < 1 || maxDepth > MaxCompareDepth) return $"maxDepth must be between 1 and {MaxCompareDepth}.";
        if (maxElements < 1 || maxElements > MaxCompareElements) return $"maxElements must be between 1 and {MaxCompareElements}.";
        if (online) return compareTarget is null && targetName is null ? null : "compare_to_online takes no compareTarget or targetName.";
        if (compareTarget is null || !CompareTargets.All.Contains(compareTarget))
            return $"compareTarget must be one of: {string.Join(", ", CompareTargets.All)}.";
        if (CompareTargets.NeedsName(compareTarget))
            return string.IsNullOrWhiteSpace(targetName) || targetName!.Length > MaxNameLength
                ? $"compareTarget '{compareTarget}' requires a nonblank targetName of at most {MaxNameLength} characters."
                : null;
        return targetName is null ? null : $"compareTarget '{compareTarget}' takes no targetName.";
    }

    /// <summary>Worker validation of a go-online request's path members; null when valid.</summary>
    public static string? ValidatePath(string? mode, string? pcInterfaceName, int pcInterfaceNumber, string? targetInterface, bool requireTarget)
    {
        if (Bad(mode)) return "mode must be nonblank and at most 256 characters.";
        if (Bad(pcInterfaceName)) return "pcInterface.name must be nonblank and at most 256 characters.";
        if (pcInterfaceNumber < 0) return "pcInterface.number must not be negative.";
        if (requireTarget && Bad(targetInterface)) return "targetInterface must be nonblank and at most 256 characters.";
        return null;
    }

    public static string? ValidateTimeout(int timeoutSeconds)
        => timeoutSeconds < 1 || timeoutSeconds > MaxTimeoutSeconds ? $"timeoutSeconds must be between 1 and {MaxTimeoutSeconds}." : null;

    /// <summary>Nonblank, bounded, no control characters; the format is TIA Portal's to judge.</summary>
    public static string? ValidateAddress(string? address)
        => string.IsNullOrWhiteSpace(address) || address!.Length > MaxAddressLength || address.Any(char.IsControl) || address.Trim() != address
            ? $"address must be nonblank, without surrounding spaces or control characters, and at most {MaxAddressLength} characters."
            : null;

    private static bool Bad(string? value) => string.IsNullOrWhiteSpace(value) || value!.Length > MaxNameLength;

    /// <summary>
    /// Polls <paramref name="readState"/> until it reports <paramref name="goal"/>, a terminal failure state
    /// (go online only), or the timeout. GoOnline returns before the connection is established (Siemens
    /// online-and-download guidance), so the poll is the only evidence of the connection.
    /// </summary>
    public static PlcOnlineWait Wait(Func<string?> readState, string goal, int timeoutMilliseconds,
        Func<long> elapsedMilliseconds, Action<int> sleep)
    {
        if (readState is null) throw new ArgumentNullException(nameof(readState));
        var observed = new List<string>();
        string? state = null;
        while (true)
        {
            state = readState();
            if (state is not null && (observed.Count == 0 || observed[observed.Count - 1] != state) && observed.Count < MaxObservedStates)
                observed.Add(state);
            if (state == goal) return new PlcOnlineWait(state, true, false, observed, (int)elapsedMilliseconds());
            if (goal == States.Online && States.IsTerminalFailure(state)) return new PlcOnlineWait(state, false, false, observed, (int)elapsedMilliseconds());
            if (elapsedMilliseconds() >= timeoutMilliseconds) return new PlcOnlineWait(state, false, true, observed, (int)elapsedMilliseconds());
            sleep(PollIntervalMilliseconds);
        }
    }

    /// <summary>
    /// Projects a compare tree depth-first in document order under the depth and element caps.
    /// With <paramref name="differencesOnly"/> an identical element below the root is skipped and
    /// counted; the root is always listed.
    /// </summary>
    public static PlcCompareProjection ProjectTree<T>(T? root, Func<T, string?> leftName, Func<T, string?> rightName,
        Func<T, string?> state, Func<T, string?> detail, Func<T, IEnumerable<T>?> children,
        int maxDepth, int maxElements, bool differencesOnly) where T : class
    {
        if (root is null) return new PlcCompareProjection(null, 0, 0, true, null);
        var walk = new TreeWalk<T>(leftName, rightName, state, detail, children, maxDepth, maxElements, differencesOnly);
        var projected = walk.Element(root, 1);
        return new PlcCompareProjection(projected, walk.Listed, walk.Skipped, walk.Omission is null, walk.Omission);
    }

    private sealed class TreeWalk<T>(Func<T, string?> leftName, Func<T, string?> rightName, Func<T, string?> state,
        Func<T, string?> detail, Func<T, IEnumerable<T>?> children, int maxDepth, int maxElements, bool differencesOnly) where T : class
    {
        public int Listed { get; private set; }

        public int Skipped { get; private set; }

        public string? Omission { get; private set; }

        public PlcCompareElementInfo Element(T node, int depth)
        {
            Listed++;
            var info = new PlcCompareElementInfo
            {
                LeftName = BoundText(leftName(node)),
                RightName = BoundText(rightName(node)),
                ComparisonResult = state(node),
                DetailedInformation = BoundText(detail(node)),
            };
            var items = children(node);
            if (items is null) return info;
            foreach (var child in items)
            {
                if (child is null) continue;
                if (differencesOnly && IsIdenticalState(state(child)))
                {
                    Skipped++;
                    continue;
                }

                if (depth >= maxDepth)
                {
                    info.ElementsTruncated = true;
                    Omission ??= $"The depth cap of {maxDepth} levels was reached; raise maxDepth to see deeper elements.";
                    break;
                }

                if (Listed >= maxElements)
                {
                    info.ElementsTruncated = true;
                    Omission = $"The element cap of {maxElements} was reached; raise maxElements (up to {MaxCompareElements}) or compare a smaller target.";
                    break;
                }

                info.Elements.Add(Element(child, depth + 1));
            }

            return info;
        }
    }
}

/// <summary>The outcome of <see cref="PlcOnlinePolicy.Wait"/>.</summary>
public sealed class PlcOnlineWait
{
    public PlcOnlineWait(string? finalState, bool goalReached, bool timedOut, IReadOnlyList<string> observed, int elapsedMilliseconds)
    {
        FinalState = finalState;
        GoalReached = goalReached;
        TimedOut = timedOut;
        Observed = observed;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public string? FinalState { get; }

    public bool GoalReached { get; }

    public bool TimedOut { get; }

    public IReadOnlyList<string> Observed { get; }

    public int ElapsedMilliseconds { get; }
}

/// <summary>The outcome of <see cref="PlcOnlinePolicy.ProjectTree{T}"/>.</summary>
public sealed class PlcCompareProjection
{
    public PlcCompareProjection(PlcCompareElementInfo? root, int listed, int skipped, bool complete, string? omission)
    {
        Root = root;
        Listed = listed;
        Skipped = skipped;
        Complete = complete;
        Omission = omission;
    }

    public PlcCompareElementInfo? Root { get; }

    public int Listed { get; }

    public int Skipped { get; }

    public bool Complete { get; }

    public string? Omission { get; }

    /// <summary>Copies the projection into <paramref name="result"/>.</summary>
    public PlcCompareResultInfo ApplyTo(PlcCompareResultInfo result)
    {
        result.Root = Root;
        result.RootState = Root?.ComparisonResult;
        result.Identical = PlcOnlinePolicy.IsIdenticalState(Root?.ComparisonResult);
        result.ElementsListed = Listed;
        result.IdenticalElementsSkipped = Skipped;
        result.Complete = Complete;
        result.Omission = Omission;
        return result;
    }
}
