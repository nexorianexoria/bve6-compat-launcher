using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

// EXPERIMENTAL. BVE6 with DSOAL was audibly verified in a single scenario.
// This does not patch, hook, or edit the installed BVE executable. When the
// user opts in, launch a temporary identical local copy with DSOAL beside it.
// BVE5 and compatibility with all ATS / BveEX combinations remain unverified.
internal static class BveBackgroundAudio {
    private const string SandboxMarkerFile="background-audio-sandbox.marker";
    private const string SandboxMarkerContent="BVE_BACKGROUND_AUDIO_TEST_COPY_V1";
    private static readonly Dictionary<string,string> TrustedDlls=
        new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            {"Win32/dsound.dll","105BC75B63B12E7CB30BE51EE5CA450D98C3E89014F47372889F8C8D1BC5DF2C"},
            {"Win32/dsoal-aldrv.dll","9B97D02D372D63A1253271A6BF45765398D9B737B7A1DA152C22FF1E3782E97E"},
            {"Win64/dsound.dll","83E2413478FD55B8676F80B2B78A3A0A40A6DFAAC9B0E497B2B4FCCF475CCC96"},
            {"Win64/dsoal-aldrv.dll","D3C64A8CCF531E987132148930DB544FB3F40233218372CF808CB201E7741FEF"}
        };

    private static string Architecture(int version) {
        if(version==5)return "Win32";
        if(version==6)return "Win64";
        throw new ArgumentOutOfRangeException("version");
    }

    private static string Hash(string path) {
        using(var sha=SHA256.Create())
        using(var file=File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","");
    }
    private static string EmbeddedName(string arch,string file) {
        return "BVEAudio."+arch+"."+file;
    }
    private static bool EmbeddedVerified(int version) {
        string arch=Architecture(version);
        foreach(string name in new[]{"dsound.dll","dsoal-aldrv.dll"}) {
            using(Stream stream=typeof(BveBackgroundAudio).Assembly.
                GetManifestResourceStream(EmbeddedName(arch,name))) {
                if(stream==null)return false;
                using(var sha=SHA256.Create()) {
                    string actual=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
                    if(!actual.Equals(TrustedDlls[arch+"/"+name],
                        StringComparison.OrdinalIgnoreCase))return false;
                }
            }
        }
        return true;
    }
    private static void CopyEmbedded(string arch,string name,string target) {
        using(Stream stream=typeof(BveBackgroundAudio).Assembly.
            GetManifestResourceStream(EmbeddedName(arch,name))) {
            if(stream==null)throw new FileNotFoundException(
                "ランチャーに音声ライブラリが含まれていません。");
            using(var output=File.Create(target)){stream.CopyTo(output);}
        }
    }

    // Do not follow junctions / symlinks from a vendor installation. In
    // particular, SearchOption.AllDirectories could cross a directory link.
    private static IEnumerable<FileInfo> SafeFiles(DirectoryInfo root) {
        var folders=new Stack<DirectoryInfo>();
        folders.Push(root);
        while(folders.Count>0) {
            var current=folders.Pop();
            if((current.Attributes & FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("リンクされたフォルダーは複製しません。");
            foreach(var subdirectory in current.GetDirectories()){
                if((subdirectory.Attributes & FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException(
                        "BVE本体内のリンクされたフォルダーは複製できません。");
                folders.Push(subdirectory);
            }
            foreach(var file in current.GetFiles()){
                if((file.Attributes & FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("リンクされたファイルは複製できません。");
                yield return file;
            }
        }
    }

    internal static bool HasTrustedRuntime(int version) {
        return HasTrustedRuntime(version,null);
    }
    internal static bool HasTrustedRuntime(int version,string audioRoot) {
        if(audioRoot==null)return EmbeddedVerified(version);
        string arch=Architecture(version);
        foreach(string name in new[]{"dsound.dll","dsoal-aldrv.dll"}) {
            string dll=Path.Combine(audioRoot,arch,name);
            string expected=TrustedDlls[arch+"/"+name];
            try {
                if(!File.Exists(dll) || !Hash(dll).Equals(expected,
                    StringComparison.OrdinalIgnoreCase))return false;
            }catch(IOException){return false;}
            catch(UnauthorizedAccessException){return false;}
        }
        return true;
    }

    // Isolated, testable staging primitive. outputRoot is never an installed
    // vendor directory. The return path points to a copy, NOT the BVE original.
    internal static string Stage(int version,string originalExecutable,
        string audioRoot,string outputRoot) {
        string arch=Architecture(version);
        if(!HasTrustedRuntime(version,audioRoot))
            throw new InvalidOperationException(
                "検証済みのDSOAL音声ファイルがありません。バックグラウンド音声をオフにして起動してください。");
        if(!File.Exists(originalExecutable) ||
           !Path.GetFileName(originalExecutable).Equals("BveTs.exe",
               StringComparison.OrdinalIgnoreCase) ||
           FileVersionInfo.GetVersionInfo(originalExecutable).FileMajorPart!=version)
            throw new InvalidDataException("BVE本体のファイルを確認できません。");

        string source=Path.GetFullPath(Path.GetDirectoryName(originalExecutable));
        string output=Path.GetFullPath(outputRoot);
        string prefix=source.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(output.Equals(source,StringComparison.OrdinalIgnoreCase) ||
           output.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "元のBVEフォルダー内には音声テスト環境を作成できません。");
        if(File.Exists(Path.Combine(source,"dsound.dll")) ||
           File.Exists(Path.Combine(source,"dsoal-aldrv.dll")))
            throw new InvalidOperationException(
                "BVE本体のフォルダーに既存の音声DLLがあります。上書きしないため起動を中止しました。");

        Directory.CreateDirectory(output);
        string sandbox=Path.Combine(output,"BVE"+version+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try {
            var root=new DirectoryInfo(source);
            foreach(var file in SafeFiles(root)) {
                string relative=file.FullName.Substring(prefix.Length);
                string dest=Path.Combine(sandbox,relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                file.CopyTo(dest);
            }
            string originalHash=Hash(originalExecutable);
            string copy=Path.Combine(sandbox,"BveTs.exe");
            if(!File.Exists(copy) || Hash(copy)!=originalHash)
                throw new IOException("BVE本体のコピーが一致しません。");
            foreach(string name in new[]{"dsound.dll","dsoal-aldrv.dll"}) {
                string dest=Path.Combine(sandbox,name);
                if(audioRoot==null)CopyEmbedded(arch,name,dest);
                else File.Copy(Path.Combine(audioRoot,arch,name),dest,false);
                if(!Hash(dest).Equals(TrustedDlls[arch+"/"+name],
                    StringComparison.OrdinalIgnoreCase))
                    throw new IOException("音声DLLのコピーが一致しません。");
            }
            File.WriteAllText(Path.Combine(sandbox,SandboxMarkerFile),
                SandboxMarkerContent);
            return copy;
        }catch{
            try{Directory.Delete(sandbox,true);}catch(IOException){}
            throw;
        }
    }

    // The environment and command line constructed by the launcher are kept.
    // Only the game executable and its working directory change on opt-in.
    internal static Process Start(ProcessStartInfo start,int version) {
        bool enabled=BveWindowOptions.Read(version).BackgroundAudio;
        return StartCore(start,version,enabled,null,
            Path.Combine(Program.Data,"background-audio-test"));
    }

    // Explicit arguments permit a live integration test without modifying
    // the user's saved launcher preferences or the official launcher build.
    internal static Process StartCore(ProcessStartInfo start,int version,
        bool enabled,string audioRoot,string runRoot) {
        if(start==null)throw new ArgumentNullException("start");
        if(!enabled)
            return Process.Start(start);
        if(Process.GetProcessesByName("BveTs").Any())
            throw new InvalidOperationException(
                "BVEが起動中です。音声試験用BVEを複数起動できません。");
        CleanupPreviousTestRuns(runRoot);
        string copy=Stage(version,start.FileName,audioRoot,runRoot);
        string runDir=Path.GetDirectoryName(copy);
        start.FileName=copy;
        start.WorkingDirectory=runDir;
        start.EnvironmentVariables["DSOAL_LOGLEVEL"]="2";
        start.EnvironmentVariables["DSOAL_LOGFILE"]=Path.Combine(runDir,"dsoal-test.log");
        try {
            var process=Process.Start(start);
            if(process==null)
                throw new IOException("音声試験用BVEの起動に失敗しました。");
            // Do not tear down the copied files while any process is running.
            // Keep the isolated folder for log inspection after exit.
            Diagnostics.Write("BVE_AUDIO_TEST",
                "version="+version+" source=copy directory="+runDir);
            return process;
        }catch{
            try{Directory.Delete(runDir,true);}catch(IOException){}
            throw;
        }
    }

    // Called only after confirming that no BVE is running. Never delete
    // arbitrary directories: match our own GUID-shaped staging names only.
    internal static void CleanupPreviousTestRuns(string runRoot) {
        if(!Directory.Exists(runRoot))return;
        foreach(var folder in new DirectoryInfo(runRoot).GetDirectories()){
            if(!Regex.IsMatch(folder.Name,@"^BVE[56]-[a-f0-9]{32}$",
                    RegexOptions.CultureInvariant))continue;
            if((folder.Attributes & FileAttributes.ReparsePoint)!=0)continue;
            try{
                string marker=Path.Combine(folder.FullName,SandboxMarkerFile);
                if(!File.Exists(marker) ||
                   File.ReadAllText(marker)!=SandboxMarkerContent)continue;
                // A renamed or tampered folder may contain links; never
                // traverse through those during recursive deletion.
                foreach(var file in SafeFiles(folder)){ /* validate only */ }
                folder.Delete(true);
            }
            catch(IOException ex){Diagnostics.Write("BVE_AUDIO_TEST_CLEANUP",ex.Message);}
            catch(UnauthorizedAccessException ex){
                Diagnostics.Write("BVE_AUDIO_TEST_CLEANUP",ex.Message);
            }
            catch(InvalidDataException ex){
                Diagnostics.Write("BVE_AUDIO_TEST_CLEANUP",ex.Message);
            }
        }
    }
}
