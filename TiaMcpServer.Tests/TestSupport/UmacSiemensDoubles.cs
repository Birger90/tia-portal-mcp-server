using System.Security;

// Stand-ins with the full names of the documented V21 UMAC members (Siemens Openness V21 "Functions for Projects and
// Project Data"; not in the compile stubs), so UmacProjectLoginBinding binds them exactly as on the live types.
namespace Siemens.Engineering;

public enum UmacUserType
{
    Project,
    Global,
}

public sealed class UmacCredentials
{
    public string? Name { get; set; }
    public UmacUserType Type { get; set; }
    public int PasswordLength { get; private set; }
    public void SetPassword(SecureString password) => PasswordLength = password.Length;
}

public delegate void UmacDelegate(UmacCredentials credentials);

/// <summary>A stand-in for ProjectComposition: Open(FileInfo, UmacDelegate) asks once, or twice like a refused login.</summary>
public sealed class UmacFakeComposition
{
    public bool AskTwice { get; init; }
    public List<UmacCredentials> Asked { get; } = new();

    public object Open(FileInfo file) => "opened without login";

    public object Open(FileInfo file, UmacDelegate umacDelegate)
    {
        var first = new UmacCredentials();
        Asked.Add(first);
        umacDelegate(first);
        if (AskTwice)
        {
            var second = new UmacCredentials();
            Asked.Add(second);
            umacDelegate(second);
        }

        return "opened";
    }
}
