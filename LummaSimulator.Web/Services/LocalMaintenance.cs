using System.Runtime.InteropServices;
using System.Security.Principal;

namespace LummaSimulator.Web.Services;

internal static class LocalMaintenance
{
    [DllImport("libc")] private static extern uint geteuid();
    public static void RequireAdministrator()
    {
        var allowed = OperatingSystem.IsWindows()
            ? new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)
            : OperatingSystem.IsLinux() && geteuid() == 0;
        if (!allowed) throw new UnauthorizedAccessException("Run local bootstrap from an elevated administrator terminal (root on Linux).");
    }
}
