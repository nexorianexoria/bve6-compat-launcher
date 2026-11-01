using System;
using System.Runtime.InteropServices;

// Keep the identity stable even though the actual launcher executable lives
// in a versioned folder. This is a documented Windows shell API.
internal static class TaskbarIdentity {
    internal const string AppId="Nexorianexoria.BVE6CompatibilityLauncher";
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);
    internal static void IdentifyProcess(){
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
    }
}
