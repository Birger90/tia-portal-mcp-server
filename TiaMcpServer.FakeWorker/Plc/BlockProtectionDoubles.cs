// Stand-ins with the members Siemens.Engineering.Step7.xml (V21) documents for block protection:
// PlcBlockProtectionProvider.Protect/Unprotect(SecureString), PlcBlockWriteProtectionProvider.IsDefined, IsProtected,
// Define/Protect/Unprotect(SecureString), Change(SecureString, SecureString), and ProtectionProviderBase.GetInvalidPasswordCharacters()
// (Siemens.Engineering.Base.xml). The behavior (wrong password rejected, write protection needs a defined password) is invented
// test behavior; member shapes only, no evidence of Siemens behavior.
namespace Siemens.Engineering.SW.Blocks
{
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Security;

    /// <summary>The protection state of one fake block, shared by its two providers and its block stand-in.</summary>
    public sealed class FakeBlockProtectionState
    {
        public string? KnowHowPassword { get; set; }

        public string? WritePassword { get; set; }

        public bool WriteEnabled { get; set; }

        public static string Plain(SecureString secure)
        {
            var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
            try { return Marshal.PtrToStringUni(pointer)!; }
            finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
        }
    }

    /// <summary>The block as the protection run reads it (IsKnowHowProtected by name).</summary>
    public sealed class FakeProtectableBlock(FakeBlockProtectionState state)
    {
        public bool IsKnowHowProtected => state.KnowHowPassword is not null;
    }

    public sealed class PlcBlockProtectionProvider(FakeBlockProtectionState state, System.Func<string, System.Exception> reject)
    {
        public IEnumerable<char> GetInvalidPasswordCharacters() => new[] { '"', '\'' };

        public void Protect(SecureString password)
        {
            if (state.KnowHowPassword is not null) throw reject("The block is already know-how protected.");
            state.KnowHowPassword = FakeBlockProtectionState.Plain(password);
        }

        public void Unprotect(SecureString password)
        {
            if (state.KnowHowPassword is null) throw reject("The block is not know-how protected.");
            if (state.KnowHowPassword != FakeBlockProtectionState.Plain(password)) throw reject("The password is wrong.");
            state.KnowHowPassword = null;
        }
    }

    public sealed class PlcBlockWriteProtectionProvider(FakeBlockProtectionState state, System.Func<string, System.Exception> reject)
    {
        public bool IsDefined => state.WritePassword is not null;

        public bool IsProtected => state.WriteEnabled;

        public IEnumerable<char> GetInvalidPasswordCharacters() => new[] { '"', '\'' };

        public void Define(SecureString password)
        {
            if (state.WritePassword is not null) throw reject("A write-protection password is already defined.");
            state.WritePassword = FakeBlockProtectionState.Plain(password);
        }

        public void Protect(SecureString password)
        {
            Check(password);
            state.WriteEnabled = true;
        }

        public void Unprotect(SecureString password)
        {
            Check(password);
            state.WriteEnabled = false;
        }

        public void Change(SecureString current, SecureString changed)
        {
            Check(current);
            state.WritePassword = FakeBlockProtectionState.Plain(changed);
        }

        private void Check(SecureString password)
        {
            if (state.WritePassword is null) throw reject("No write-protection password is defined.");
            if (state.WritePassword != FakeBlockProtectionState.Plain(password)) throw reject("The password is wrong.");
        }
    }
}
