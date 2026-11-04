using System;
using System.Text;
using System.Runtime.InteropServices;

// For the explicitly launched smoke-test PID only.
// Posts the same WM_CLOSE that the window's X button receives.
// This never kills processes or sends keyboard/controller input.
internal static class CloseBveTestDialogs {
    private delegate bool EnumProc(IntPtr handle,IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback,IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle,StringBuilder caption,int capacity);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool PostMessage(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam);
    private const uint WM_CLOSE=0x0010;
    private static int Main(string[] args) {
        uint pid;
        if(args.Length!=1 || !uint.TryParse(args[0],out pid) || pid==0)return 2;
        int count=0;
        EnumWindows((handle,state)=>{
            uint owner;GetWindowThreadProcessId(handle,out owner);
            if(owner!=pid)return true;
            var caption=new StringBuilder(128);GetWindowText(handle,caption,caption.Capacity);
            // BVE native scenario selection dialog.
            if(caption.ToString()=="\u30b7\u30ca\u30ea\u30aa\u306e\u9078\u629e") {
                if(PostMessage(handle,WM_CLOSE,IntPtr.Zero,IntPtr.Zero))count++;
            }
            return true;
        },IntPtr.Zero);
        Console.WriteLine("SCENARIO_DIALOG_CLOSE_REQUESTS="+count);
        return 0;
    }
}
