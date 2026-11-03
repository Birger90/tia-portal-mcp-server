// Stand-ins of the documented block protection members (Siemens.Engineering.Step7.xml and Siemens.Engineering.Base.xml, V21),
// named exactly as PlcBlockProtectionRun binds them. Behavior is invented test behavior; shapes only.
namespace Siemens.Engineering.SW.Blocks
{
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Security;

    public sealed class TestProtectionState
    {
        public string? KnowHow { get; set; }

        public string? Write { get; set; }

        public bool Enabled { get; set; }

        /// <summary>When true the providers return without changing anything (a silently ignored call).</summary>
        public bool Silent { get; set; }

        public List<string> Calls { get; } = new();

        public static string Plain(SecureString secure)
        {
            var pointer = Marshal.SecureStringToGlobalAllocUnicode(secure);
            try { return Marshal.PtrToStringUni(pointer)!; }
            finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
        }
    }

    public sealed class TestProtectableBlock(TestProtectionState state)
    {
        public bool IsKnowHowProtected => state.KnowHow is not null;
    }

    public sealed class PlcBlockProtectionProvider(TestProtectionState state)
    {
        public IEnumerable<char> GetInvalidPasswordCharacters() => new[] { '"', ' ' };

        public void Protect(SecureString password)
        {
            state.Calls.Add("Protect");
            if (!state.Silent) state.KnowHow = TestProtectionState.Plain(password);
        }

        public void Unprotect(SecureString password)
        {
            state.Calls.Add("Unprotect");
            if (state.KnowHow != TestProtectionState.Plain(password)) throw new System.InvalidOperationException("wrong password");
            if (!state.Silent) state.KnowHow = null;
        }
    }

    public sealed class PlcBlockWriteProtectionProvider(TestProtectionState state)
    {
        public bool IsDefined => state.Write is not null;

        public bool IsProtected => state.Enabled;

        public void Define(SecureString password)
        {
            state.Calls.Add("Define");
            state.Write = TestProtectionState.Plain(password);
        }

        public void Protect(SecureString password)
        {
            state.Calls.Add("Protect");
            state.Enabled = true;
        }

        public void Unprotect(SecureString password)
        {
            state.Calls.Add("Unprotect");
            state.Enabled = false;
        }

        public void Change(SecureString current, SecureString changed)
        {
            state.Calls.Add("Change");
            if (state.Write != TestProtectionState.Plain(current)) throw new System.InvalidOperationException("wrong password");
            state.Write = TestProtectionState.Plain(changed);
        }
    }
}
