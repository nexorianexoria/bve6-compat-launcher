using System;
using System.Text;
using System.Runtime.InteropServices;

// Read-only enumeration; never moves, activates, hides or closes a BVE window.
internal static class BveWindowsReadOnly {
    private delegate bool EnumProc(IntPtr hwnd,IntPtr param);
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumProc callback,IntPtr param);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    [DllImport("user32.dll")]private static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int capacity);
    [DllImport("user32.dll")]private static extern int GetClassName(IntPtr hwnd,StringBuilder text,int capacity);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
    private struct RECT{public int Left,Top,Right,Bottom;}
    private static int Main(string[] args){
        if(args.Length!=1){Console.Error.WriteLine("Usage: BveWindowsReadOnly.exe <BVE PID>");return 2;}
        uint wanted;if(!uint.TryParse(args[0],out wanted)){return 2;}
        EnumWindows((hwnd,param)=>{
            uint id;GetWindowThreadProcessId(hwnd,out id);
            if(id!=wanted)return true;
            var title=new StringBuilder(512);
            var cls=new StringBuilder(256);
            GetWindowText(hwnd,title,title.Capacity);
            GetClassName(hwnd,cls,cls.Capacity);
            RECT r;GetWindowRect(hwnd,out r);
            Console.WriteLine("HWND={0} CLASS={1} VISIBLE={2} RECT={3},{4},{5},{6} TITLE={7}",
                hwnd.ToInt64(),cls.ToString(),IsWindowVisible(hwnd),
                r.Left,r.Top,r.Right,r.Bottom,title.ToString());
            return true;
        },IntPtr.Zero);
        return 0;
    }
}
