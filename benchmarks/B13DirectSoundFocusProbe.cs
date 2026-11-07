// A synthetic, SILENT DirectSound buffer test. This is NOT BVE or a BVE patch.
// Compile this file against the user's own installed SlimDX, then run it as a
// disposable process. The test never opens a scenario or plays audible sound.
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using SlimDX.DirectSound;
using SlimDX.Multimedia;

internal static class B13DirectSoundFocusProbe {
    private static int Main(string[] args) {
        if(args.Length!=1 || !File.Exists(args[0])) {
            Console.Error.WriteLine("Usage: B13DirectSoundFocusProbe.exe <installed SlimDX.dll>");
            return 2;
        }
        string slimdx=Path.GetFullPath(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>
            new AssemblyName(e.Name).Name=="SlimDX" ? Assembly.LoadFrom(slimdx) : null;
        try{return Probe();}
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }

    private static int Probe() {
        using(var window=new Form())
        using(var device=new DirectSound()) {
            var status=device.SetCooperativeLevel(window.Handle,CooperativeLevel.Priority);
            Console.WriteLine("CooperativeLevel=Priority Status="+status);
            bool allValid=true;
            foreach(bool global in new []{false,true}) {
                var flags=BufferFlags.ControlFrequency|BufferFlags.ControlPan|BufferFlags.ControlVolume;
                if(global)flags|=BufferFlags.GlobalFocus;
                var format=new WaveFormat {
                    FormatTag=WaveFormatTag.Pcm,
                    Channels=2,
                    SamplesPerSecond=22050,
                    BitsPerSample=16,
                    BlockAlignment=4,
                    AverageBytesPerSecond=88200
                };
                var description=new SoundBufferDescription {
                    Flags=flags,Format=format,SizeInBytes=8820
                };
                using(var buffer=new SecondarySoundBuffer(device,description)) {
                    bool actual=buffer.Capabilities.GlobalFocus;
                    Console.WriteLine("RequestedFlags=0x"+((int)flags).ToString("X4")+
                        " RequestedGlobalFocus="+global+" ReportedGlobalFocus="+actual);
                    if(actual!=global)allValid=false;
                }
            }
            Console.WriteLine("BUFFER_FLAG_ROUNDTRIP="+(allValid?"PASS":"FAIL"));
            Console.WriteLine("AUDIBLE_BACKGROUND_PLAYBACK=NOT_TESTED");
            return allValid?0:3;
        }
    }
}
