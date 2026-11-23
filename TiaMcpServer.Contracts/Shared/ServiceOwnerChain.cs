// Idea adopted from asckye/TIA_Portal_Openness_MCP (MIT, Copyright (c) 2026 bulaofen0036-coder), commit 93eeb016
// ("PLC service lookup": resolve the loadable, fingerprint and monitor services from the PLC software, the CPU ancestors and
// the device). Re-implemented here; no code was copied.

namespace TiaMcpServer.Contracts.Shared;

/// <summary>
/// Searches an ordered chain of engineering objects for one service (Siemens-free; the worker supplies the live objects and the
/// lookup). V21 documents several services without naming the object that hosts them (e.g. <c>LoadableProvider</c>), so the
/// first object in the chain that returns the service decides: the caller lists the most specific owner first (the PLC
/// software), then the CPU device item, its parents and the device. Every object searched is named in the refusal.
/// </summary>
public static class ServiceOwnerChain
{
    /// <summary>At most this many objects are searched.</summary>
    public const int MaxObjects = 10;

    /// <summary>One object of the chain: what it is called in messages (<c>PLC software 'PLC_1'</c>) and the live object.</summary>
    public sealed class Owner
    {
        public Owner(string description, object instance)
        {
            Description = description;
            Instance = instance;
        }

        public string Description { get; }

        public object Instance { get; }
    }

    /// <summary>The outcome: the service and its owner, or null with every object searched and every unavailable reason.</summary>
    public sealed class Result
    {
        public object? Service { get; internal set; }

        public string? FoundOn { get; internal set; }

        public List<string> Searched { get; } = new();

        public List<string> Unavailable { get; } = new();

        /// <summary>The refusal text when no object returned the service.</summary>
        public string Reason(string fullName)
            => $"None of the objects searched offers '{fullName}' (searched: {string.Join(", ", Searched)})."
               + (Unavailable.Count > 0 ? " " + string.Join(" ", Unavailable) : string.Empty);
    }

    /// <summary>
    /// Asks each owner in order with <paramref name="lookup"/> (service, or null with an optional unavailable reason; null
    /// reason means the owner does not list the service) and stops at the first that returns one. A lookup that throws is not
    /// caught: a lost Portal must stay a lost Portal.
    /// </summary>
    public static Result Find(IEnumerable<Owner> owners, Func<object, (object? Service, string? Unavailable)> lookup)
    {
        if (owners is null) throw new ArgumentNullException(nameof(owners));
        if (lookup is null) throw new ArgumentNullException(nameof(lookup));
        var result = new Result();
        foreach (var owner in owners.Take(MaxObjects))
        {
            if (owner?.Instance is null) continue;
            result.Searched.Add(owner.Description);
            var (service, unavailable) = lookup(owner.Instance);
            if (service is not null)
            {
                result.Service = service;
                result.FoundOn = owner.Description;
                return result;
            }

            if (unavailable is not null) result.Unavailable.Add($"{owner.Description}: {unavailable}");
        }

        return result;
    }
}
