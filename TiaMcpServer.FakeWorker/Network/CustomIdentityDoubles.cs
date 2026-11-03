// Stand-ins for the custom identity members of Siemens.Engineering.Base.xml (V21). Member shapes only; no evidence of Siemens behavior.
namespace Siemens.Engineering.CustomIdentity
{
    /// <summary>Stand-in with the members Siemens.Engineering.Base.xml (V21) documents; the exception type is matched by full name.</summary>
    public sealed class CustomIdentityNotFoundException(string message) : Exception(message);

    /// <summary>Stand-in of CustomIdentityProvider: Get throws CustomIdentityNotFoundException for a missing key, as Remove is documented to.</summary>
    public sealed class CustomIdentityProvider
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string Get(string key) => _values.TryGetValue(key, out var value) ? value : throw new CustomIdentityNotFoundException($"No custom identity '{key}'.");

        public void Set(string key, string value) => _values[key] = value;

        public void Remove(string key)
        {
            if (!_values.Remove(key)) throw new CustomIdentityNotFoundException($"No custom identity '{key}'.");
        }
    }
}
