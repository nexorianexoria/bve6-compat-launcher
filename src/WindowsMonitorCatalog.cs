using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Management;
using System.Windows.Forms;

// Windows display properties, not BVE frames per second. EnumDisplaySettings
// returns the currently configured resolution and refresh rate in hertz.
internal sealed class WindowsMonitorInfo {
    internal string Device, Name;
    internal bool Primary;
    internal int Width, Height, RefreshHz;
    internal string Label {
        get {
            string size = Width>0 && Height>0?Width+" × "+Height:"解像度を取得できません";
            string rate = RefreshHz>0?RefreshHz+" Hz":"Hz不明";
            return Name+"  |  "+size+"  |  "+rate+
                (Primary?"  （メイン）":"");
        }
    }
}
internal static class WindowsMonitorCatalog {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct DisplayDevice {
        internal int cb;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]
        internal string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]
        internal string DeviceString;
        internal int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]
        internal string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]
        internal string DeviceKey;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct DevMode {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]
        internal string dmDeviceName;
        internal ushort dmSpecVersion,dmDriverVersion,dmSize,dmDriverExtra;
        internal uint dmFields;
        internal int dmPositionX,dmPositionY;
        internal uint dmDisplayOrientation,dmDisplayFixedOutput;
        internal short dmColor,dmDuplex,dmYResolution,dmTTOption,dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]
        internal string dmFormName;
        internal ushort dmLogPixels;
        internal uint dmBitsPerPel,dmPelsWidth,dmPelsHeight,dmDisplayFlags,dmDisplayFrequency,
            dmICMMethod,dmICMIntent,dmMediaType,dmDitherType,dmReserved1,dmReserved2,
            dmPanningWidth,dmPanningHeight;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="EnumDisplayDevicesW")]
    private static extern bool EnumDisplayDevices(string device,uint index,ref DisplayDevice result,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="EnumDisplaySettingsW")]
    private static extern bool EnumDisplaySettings(string device,int mode,ref DevMode result);

    private sealed class MonitorFriendlyName {
        internal string DeviceInstance;
        internal string Name;
    }
    private static List<MonitorFriendlyName> ReadWindowsRegisteredNames(){
        var found=new List<MonitorFriendlyName>();
        try {
            using(var searcher=new ManagementObjectSearcher(
                "root\\WMI","SELECT InstanceName,Active,UserFriendlyName FROM WmiMonitorID"))
            using(var matches=searcher.Get()){
                foreach(ManagementObject monitor in matches){
                    using(monitor){
                        if(monitor["Active"] is bool && !(bool)monitor["Active"])continue;
                        string instance=monitor["InstanceName"] as string;
                        var codes=monitor["UserFriendlyName"] as ushort[];
                        if(String.IsNullOrWhiteSpace(instance)||codes==null)continue;
                        var letters=codes.TakeWhile(c=>c!=0).Select(c=>(char)c).ToArray();
                        var name=new String(letters).Trim();
                        if(name.Length>0)found.Add(new MonitorFriendlyName{
                            DeviceInstance=instance,Name=name});
                    }
                }
            }
        }catch(Exception ex){Diagnostics.Write("DISPLAY_WMI_NAME",ex.Message);}
        return found;
    }
    private static string MatchWindowsName(string deviceId,List<MonitorFriendlyName> known){
        if(String.IsNullOrWhiteSpace(deviceId))return null;
        // EnumDisplayDevices gives DISPLAY#GSM5BF8#instance#class-guid.
        // WmiMonitorID gives DISPLAY\GSM5BF8\instance_0. Match both
        // manufacturer/model ID and the specific instance to avoid assuming
        // Windows returns monitor info in the same order as Screen.AllScreens.
        string[] parts=deviceId.Replace('/','\\').Replace('#','\\').Split('\\');
        for(int i=0;i+2<parts.Length;i++){
            if(!parts[i].Equals("DISPLAY",StringComparison.OrdinalIgnoreCase))continue;
            string prefix="DISPLAY\\"+parts[i+1]+"\\"+parts[i+2];
            var match=known.FirstOrDefault(x=>
                x.DeviceInstance.Equals(prefix,StringComparison.OrdinalIgnoreCase) ||
                x.DeviceInstance.StartsWith(prefix+"_",StringComparison.OrdinalIgnoreCase));
            if(match!=null)return match.Name;
        }
        return null;
    }

    internal static List<WindowsMonitorInfo> GetCurrent(){
        var results=new List<WindowsMonitorInfo>();
        var windowsNames=ReadWindowsRegisteredNames();
        foreach(Screen screen in Screen.AllScreens){
            var name=screen.DeviceName;
            var monitor=new WindowsMonitorInfo{
                Device=name,Name=name,Primary=screen.Primary,
                Width=screen.Bounds.Width,Height=screen.Bounds.Height
            };
            // Device enumeration is tied to the same display identifier
            // Screen.AllScreens uses, avoiding WMI instance ordering guesses.
            try {
                var dd=new DisplayDevice();
                dd.cb=Marshal.SizeOf(typeof(DisplayDevice));
                for(uint index=0;index<12;index++){
                    dd.cb=Marshal.SizeOf(typeof(DisplayDevice));
                    if(!EnumDisplayDevices(name,index,ref dd,1))break;
                    if((dd.StateFlags&1)==0)continue;
                    string windowsName=MatchWindowsName(dd.DeviceID,windowsNames);
                    if(!String.IsNullOrWhiteSpace(windowsName))
                        monitor.Name=windowsName;
                    if(!String.IsNullOrWhiteSpace(dd.DeviceString) &&
                        !dd.DeviceString.Equals("Generic PnP Monitor",StringComparison.OrdinalIgnoreCase) &&
                        !dd.DeviceString.Equals("汎用 PnP モニター",StringComparison.OrdinalIgnoreCase) &&
                        String.IsNullOrWhiteSpace(windowsName)){
                        monitor.Name=dd.DeviceString.Trim();
                    }
                    break;
                }
            }catch(Exception ex){
                Diagnostics.Write("DISPLAY_NAME_LOOKUP",ex.Message);
            }
            try {
                var mode=new DevMode();
                mode.dmSize=(ushort)Marshal.SizeOf(typeof(DevMode));
                if(EnumDisplaySettings(name,-1,ref mode)){
                    if(mode.dmPelsWidth>0 && mode.dmPelsWidth<=16000)
                        monitor.Width=(int)mode.dmPelsWidth;
                    if(mode.dmPelsHeight>0 && mode.dmPelsHeight<=16000)
                        monitor.Height=(int)mode.dmPelsHeight;
                    if(mode.dmDisplayFrequency>1 && mode.dmDisplayFrequency<2000)
                        monitor.RefreshHz=(int)mode.dmDisplayFrequency;
                }
            }catch(Exception ex){
                Diagnostics.Write("DISPLAY_MODE_LOOKUP",ex.Message);
            }
            results.Add(monitor);
        }
        return results;
    }
}
