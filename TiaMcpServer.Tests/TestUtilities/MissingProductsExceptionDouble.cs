// Executable boundary double only: a type named like the documented V21 exception, for name-based matching tests.
namespace Siemens.Engineering
{
    /// <summary>Test double named like the documented V21 type (members.txt: T:Siemens.Engineering.MissingProductsException).</summary>
    public class MissingProductsException : Exception
    {
        public MissingProductsException(string message) : base(message)
        {
        }
    }
}
