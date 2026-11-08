// Research-only Windows Core Audio *session peak* sampler.
// No microphone, audio loopback recording, audio settings changes, or BVE writes.
// Build as a standalone executable; do NOT embed this COM code into the launcher.
// Interfaces based on Microsoft's IMMDevice/IAudioSessionManager2/IAudioMeterInformation
// documentation. Only aggregate numerical levels are printed.
using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Collections.Generic;

internal static class B13BveAudioFocusMeter {
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow, int stateMask,
            out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id,
            out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice {
        [PreserveSig] int Activate(ref Guid iid, int clsctx, IntPtr param,
            [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2 {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid,int flags,out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid,int flags,out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
        [PreserveSig] int RegisterSessionNotification(IntPtr notify);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notify);
        [PreserveSig] int RegisterDuckNotification(IntPtr id,IntPtr notify);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notify);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 control);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2 {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name,IntPtr guid);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string icon);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string icon,IntPtr guid);
        [PreserveSig] int GetGroupingParam(out Guid guid);
        [PreserveSig] int SetGroupingParam(ref Guid guid,IntPtr eventGuid);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out int id);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out int count);
        [PreserveSig] int GetChannelsPeakValues(int count,[Out] float[] peaks);
        [PreserveSig] int QueryHardwareSupport(out int flags);
    }
    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid,IntPtr outer,int context,
        ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle,out uint pid);

    private sealed class Stats {
        public int Samples,AudioDetected,Missing;
        public double Sum,Maximum;
        public void Add(double p,bool found) {
            Samples++; if(!found)Missing++;
            if(p>0.0008)AudioDetected++;
            Sum+=p; Maximum=Math.Max(Maximum,p);
        }
        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture,
                "samples={0} audibleSamples={1} missingSessionSamples={2} maxPeak={3:F6} meanPeak={4:F6}",
                Samples,AudioDetected,Missing,Maximum,Samples==0?0:Sum/Samples);
        }
    }
    private static IMMDeviceEnumerator CreateDevices() {
        Guid clsid=new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        Guid iid=new Guid("A95664D2-9614-4F35-A746-DE8DB63617E6");
        object obj; int result=CoCreateInstance(ref clsid,IntPtr.Zero,23,ref iid,out obj);
        if(result!=0)Marshal.ThrowExceptionForHR(result);
        return (IMMDeviceEnumerator)obj;
    }
    private static double SamplePeak(IMMDeviceEnumerator devices,int targetPid,out bool found) {
        found=false;
        IMMDeviceCollection list;
        if(devices.EnumAudioEndpoints(0,1,out list)!=0)return 0;
        int devicesCount;
        if(list.GetCount(out devicesCount)!=0)return 0;
        float maxPeak=0;
        for(int d=0;d<devicesCount;d++){
            IMMDevice device;
            if(list.Item(d,out device)!=0)continue;
            object obj;
            Guid iid=new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
            if(device.Activate(ref iid,23,IntPtr.Zero,out obj)!=0)continue;
            var manager=(IAudioSessionManager2)obj;
            IAudioSessionEnumerator sessions;
            if(manager.GetSessionEnumerator(out sessions)!=0)continue;
            int count;
            if(sessions.GetCount(out count)!=0)continue;
            for(int s=0;s<count;s++){
                IAudioSessionControl2 session;
                if(sessions.GetSession(s,out session)!=0)continue;
                int pid;
                if(session.GetProcessId(out pid)!=0 || pid!=targetPid)continue;
                found=true;
                var meter=session as IAudioMeterInformation;
                if(meter==null)continue;
                float peak;
                if(meter.GetPeakValue(out peak)==0)maxPeak=Math.Max(maxPeak,peak);
            }
        }
        return maxPeak;
    }
    private static int Main(string[] args) {
        int pid,seconds=25,intervalMs=100;
        if(args.Length<1 || !int.TryParse(args[0],out pid) || pid<=0 ||
            (args.Length>1 && !int.TryParse(args[1],out seconds)) ||
            seconds<1 || seconds>180){
            Console.Error.WriteLine("Usage: B13BveAudioFocusMeter.exe <BVE_PID> [seconds, 1..180]");
            return 2;
        }
        using(var target=Process.GetProcessById(pid)){
            var devices=CreateDevices();
            var focused=new Stats();
            var unfocused=new Stats();
            Console.WriteLine("BVE_PID="+pid);
            Console.WriteLine("AUDIO_RAW_RECORDING=NONE");
            Console.WriteLine("WINDOW_FOCUS_IS_READ_ONLY=YES");
            Console.WriteLine("FORMAT=elapsed_seconds,foreground_is_BVE,audio_session_exists,peak");
            var clock=Stopwatch.StartNew();
            while(clock.Elapsed.TotalSeconds<seconds && !target.HasExited) {
                uint foregroundPid;
                GetWindowThreadProcessId(GetForegroundWindow(),out foregroundPid);
                bool isFocused=foregroundPid==(uint)pid;
                bool found;
                double value=SamplePeak(devices,pid,out found);
                (isFocused?focused:unfocused).Add(value,found);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F2},{1},{2},{3:F6}",clock.Elapsed.TotalSeconds,
                    isFocused?"1":"0",found?"1":"0",value));
                Thread.Sleep(intervalMs);
            }
            Console.WriteLine("FOCUSED="+focused);
            Console.WriteLine("UNFOCUSED="+unfocused);
            Console.WriteLine("FOCUS_COMPARISON=OBSERVATION_ONLY_NO_AUTOMATIC_CAUSAL_CLAIM");
            return 0;
        }
    }
}
