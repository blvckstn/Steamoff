using System.Runtime.InteropServices;

namespace Steamoff.Infrastructure.UserContext;

/// <summary>Thin P/Invoke wrapper around GetTokenInformation(TokenElevation) — the authoritative way to know if the current process token is actually elevated (vs merely "in the Administrators group").</summary>
internal static class TokenElevation
{
    private const int TokenElevationType = 18;
    private const uint TokenQuery = 0x0008;

    public static bool IsProcessElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var tokenHandle))
        {
            return false;
        }

        try
        {
            var elevationTypePtr = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                if (!GetTokenInformation(tokenHandle, TokenElevationType, elevationTypePtr, sizeof(int), out _))
                {
                    return false;
                }

                var elevationType = (TokenElevationTypeValue)Marshal.ReadInt32(elevationTypePtr);

                // Full = split-token admin running elevated; Default = single-token
                // account (built-in Administrator, UAC disabled) — already "as elevated as it gets".
                return elevationType is TokenElevationTypeValue.Full or TokenElevationTypeValue.Default;
            }
            finally
            {
                Marshal.FreeHGlobal(elevationTypePtr);
            }
        }
        finally
        {
            CloseHandle(tokenHandle);
        }
    }

    private enum TokenElevationTypeValue
    {
        Default = 1,
        Full = 2,
        Limited = 3
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
