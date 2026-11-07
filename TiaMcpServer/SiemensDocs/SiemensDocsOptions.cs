// Ported from Czarnak/siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki), siemens_docs_mcp/server.py
// load_settings; reviewed and rewritten in C#. See THIRD_PARTY_NOTICES.md.
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using TiaMcpServer.Cli;

namespace TiaMcpServer.SiemensDocs;

/// <summary>
/// Startup settings of <c>siemens_docs</c>: which documentation hosts may be contacted, the default
/// content locale and the minimum interval between two requests to one host. Resolved once at
/// startup (CLI &gt; environment &gt; default); an invalid value refuses startup. The agent never
/// chooses a host outside <see cref="Hosts"/>.
/// </summary>
public sealed partial class SiemensDocsOptions
{
    public const string HostsFlag = "--docs-hosts";
    public const string HostsEnvVar = "TIA_MCP_DOCS_HOSTS";
    public const string LocaleFlag = "--docs-locale";
    public const string LocaleEnvVar = "TIA_MCP_DOCS_LOCALE";
    public const string MinIntervalFlag = "--docs-min-interval-ms";
    public const string MinIntervalEnvVar = "TIA_MCP_DOCS_MIN_INTERVAL_MS";

    public const string TiaHost = "docs.tia.siemens.cloud";
    public const string IoxHost = "docs.industrial-operations-x.siemens.cloud";
    public const string DefaultLocale = "en-US";
    public const int DefaultMinIntervalMs = 300;
    public const int MaxMinIntervalMs = 60_000;
    public const int MaxHosts = 8;

    /// <summary>Stable short prefixes of the two Siemens hosts; other hosts use their full name as prefix.</summary>
    public static IReadOnlyDictionary<string, string> Aliases { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["tia"] = TiaHost,
        ["iox"] = IoxHost,
    };

    public SiemensDocsOptions(IReadOnlyList<string> hosts, string locale, TimeSpan minInterval)
    {
        if (hosts is null || hosts.Count == 0) throw new ArgumentException("At least one host is required.", nameof(hosts));
        Hosts = hosts.ToArray();
        Locale = locale ?? throw new ArgumentNullException(nameof(locale));
        MinInterval = minInterval;
    }

    public static SiemensDocsOptions Default { get; } = new(new[] { TiaHost, IoxHost }, DefaultLocale, TimeSpan.FromMilliseconds(DefaultMinIntervalMs));

    /// <summary>Allowed hosts, lower case, in configured order; the first is the default host.</summary>
    public IReadOnlyList<string> Hosts { get; }

    public string DefaultHost => Hosts[0];

    public string Locale { get; }

    public TimeSpan MinInterval { get; }

    public bool IsAllowed(string host) => Hosts.Contains(host, StringComparer.Ordinal);

    /// <summary>The compact reference prefix of an allowed host (<c>tia</c>, <c>iox</c> or the host name).</summary>
    public static string PrefixFor(string host)
        => Aliases.FirstOrDefault(pair => pair.Value == host).Key ?? host;

    public string Describe()
        => $"hosts {string.Join(", ", Hosts)}; default locale {Locale}; minimum interval {(int)MinInterval.TotalMilliseconds} ms per host";

    /// <summary>Reads the three settings from the command line and the environment.</summary>
    public static (SiemensDocsOptions? Options, string? Error) Resolve(string[] args, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        var (hostsText, hostsError) = Read(args, HostsFlag, HostsEnvVar, environment);
        if (hostsError is not null) return (null, hostsError);
        var (localeText, localeError) = Read(args, LocaleFlag, LocaleEnvVar, environment);
        if (localeError is not null) return (null, localeError);
        var (intervalText, intervalError) = Read(args, MinIntervalFlag, MinIntervalEnvVar, environment);
        if (intervalError is not null) return (null, intervalError);

        var hosts = Default.Hosts;
        if (hostsText is not null)
        {
            var parsed = ParseHosts(hostsText.Value.Text, out var error);
            if (parsed is null) return (null, $"Invalid {hostsText.Value.Source} value '{hostsText.Value.Text}': {error}");
            hosts = parsed;
        }

        var locale = DefaultLocale;
        if (localeText is not null)
        {
            if (NormalizeLocale(localeText.Value.Text.Trim()) is not { } normalized)
                return (null, $"Invalid {localeText.Value.Source} value '{localeText.Value.Text}': expected a culture name such as en-US or de-DE.");
            locale = normalized;
        }

        var interval = DefaultMinIntervalMs;
        if (intervalText is not null
            && !(int.TryParse(intervalText.Value.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out interval)
                 && interval <= MaxMinIntervalMs))
            return (null, $"Invalid {intervalText.Value.Source} value '{intervalText.Value.Text}': expected whole milliseconds from 0 to {MaxMinIntervalMs}.");

        return (new SiemensDocsOptions(hosts, locale, TimeSpan.FromMilliseconds(interval)), null);
    }

    /// <summary>
    /// Parses a comma-separated host list. Each entry must be a plain DNS host name (no scheme, port,
    /// path, user info or IP address, at least two labels, not localhost); requests always go to
    /// https://&lt;host&gt;/ on port 443. The list replaces the default set, so it can also narrow it.
    /// </summary>
    public static IReadOnlyList<string>? ParseHosts(string text, out string? error)
    {
        error = null;
        var hosts = new List<string>();
        foreach (var raw in text.Split(','))
        {
            var host = raw.Trim().ToLowerInvariant();
            if (host.Length == 0) continue;
            if (!IsPlainHostName(host))
            {
                error = $"'{raw.Trim()}' is not a plain DNS host name (no scheme, port, path or IP address; for example {TiaHost}).";
                return null;
            }

            if (!hosts.Contains(host, StringComparer.Ordinal)) hosts.Add(host);
        }

        if (hosts.Count == 0)
        {
            error = "the list names no host.";
            return null;
        }

        if (hosts.Count > MaxHosts)
        {
            error = $"at most {MaxHosts} hosts are allowed.";
            return null;
        }

        return hosts;
    }

    public static bool IsPlainHostName(string host)
        => host.Length <= 253 && HostName().IsMatch(host) && !IPAddress.TryParse(host, out _)
           && host != "localhost" && !host.EndsWith(".localhost", StringComparison.Ordinal)
           && !host.Split('.').All(label => label.All(char.IsAsciiDigit));

    /// <summary>Normalizes a culture name to the documentation hosts' spelling (en-us → en-US); null when malformed.</summary>
    public static string? NormalizeLocale(string? value)
    {
        if (value is null) return null;
        var match = LocaleShape().Match(value);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() + "-" + match.Groups[2].Value.ToUpperInvariant() : null;
    }

    private static ((string Text, string Source)? Value, string? Error) Read(string[] args, string flag, string env, Func<string, string?> environment)
    {
        var option = StartupOptionReader.ReadSingle(args, flag);
        if (option.Error is not null) return (null, option.Error);
        if (option.Value is not null) return ((option.Value, flag), null);
        var text = environment(env);
        return string.IsNullOrWhiteSpace(text) ? (null, null) : ((text, env), null);
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex HostName();

    [GeneratedRegex(@"^([A-Za-z]{2,3})-([A-Za-z]{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex LocaleShape();
}
