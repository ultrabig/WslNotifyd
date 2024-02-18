using System.Runtime.InteropServices;

namespace WslNotifyd.Libc
{
    internal static class Libc
    {
        [DllImport("libc")]
        internal static extern uint getuid();
    }
}
