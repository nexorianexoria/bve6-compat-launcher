// x86/x64 process module inventory, read-only; no injection or game modification.
using System;
using System.Diagnostics;
using System.IO;

internal static class B13BveModulesReadOnly {
    private static int Main(string[] args) {
        int pid;
        if(args.Length!=1 || !int.TryParse(args[0],out pid) || pid<=0) {
            Console.Error.WriteLine("Usage: B13BveModulesReadOnly.exe PID");
            return 2;
        }
        try {
            using(var process=Process.GetProcessById(pid)) {
                Console.WriteLine("PID="+pid);
                Console.WriteLine("EXE="+process.MainModule.FileName);
                foreach(ProcessModule module in process.Modules) {
                    string name=module.ModuleName;
                    string path=module.FileName;
                    if(name.IndexOf("dsound",StringComparison.OrdinalIgnoreCase)>=0 ||
                       name.IndexOf("dsoal",StringComparison.OrdinalIgnoreCase)>=0 ||
                       name.IndexOf("panel",StringComparison.OrdinalIgnoreCase)>=0 ||
                       name.IndexOf("ats",StringComparison.OrdinalIgnoreCase)>=0 ||
                       name.IndexOf("bveex",StringComparison.OrdinalIgnoreCase)>=0 ||
                       name.IndexOf("tshud",StringComparison.OrdinalIgnoreCase)>=0 ||
                       name.IndexOf("slimdx",StringComparison.OrdinalIgnoreCase)>=0)
                        Console.WriteLine("MODULE="+name+" PATH="+path);
                }
            }
            return 0;
        }catch(Exception error) {
            Console.Error.WriteLine(error.GetType().Name+": "+error.Message);
            return 1;
        }
    }
}
