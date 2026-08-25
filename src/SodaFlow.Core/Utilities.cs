using System.Threading.Tasks;

namespace SodaFlow
{
    internal class Utilities
    {
        internal static async Task Yield() => await Task.Yield();
    }
}