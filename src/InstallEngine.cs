using System;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// All installation mutations are confined to an explicitly selected application root.
// No network, registry, process launch or user-data deletion is performed by this class.
internal static class InstallEngine {
    internal const string AppName="BVE6互換ランチャー";
    internal static readonly string[] PayloadNames={"BVE6互換ランチャー.exe","導入・更新.exe","LauncherStarter.exe","Uninstall.exe","launcher.ico","LICENSE.txt","README.txt","THIRD_PARTY_NOTICES.txt","bridge/Bve5VehicleCompatibility.exe","bridge/Bve5VehicleCompatibility_.dll","bridge/LICENSE.txt"};
    internal sealed class Manifest {public string version{get;set;} public string displayVersion{get;set;} public string url{get;set;} public string sha256{get;set;} public string notes{get;set;}}
    internal sealed class Index {public string version{get;set;} public string displayVersion{get;set;} public Dictionary<string,string> files{get;set;}}
    internal static string Hash(string file){using(var sha=SHA256.Create())using(var input=File.OpenRead(file))return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");}
    internal static void RejectLinks(string path){var p=new DirectoryInfo(Path.GetFullPath(path));while(p!=null){if((Directory.Exists(p.FullName)||File.Exists(p.FullName))&&(File.GetAttributes(p.FullName)&FileAttributes.ReparsePoint)!=0)throw new IOException("リンクを含む場所は使用できません: "+p.FullName);p=p.Parent;}}
    internal static string Root(string path){if(String.IsNullOrWhiteSpace(path)||!Path.IsPathRooted(path)||path.StartsWith(@"\\",StringComparison.Ordinal))throw new InvalidDataException("ローカルのインストール先を選んでください。");string full=Path.GetFullPath(path).TrimEnd('\\','/');if(!Path.GetFileName(full).Equals(AppName,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("インストール先は専用の「"+AppName+"」フォルダーにしてください。");RejectLinks(full);return full;}
    internal static void ValidateManifest(Manifest m,Version minimum,string exact){
        Version version; if(m==null||!Version.TryParse(m.version,out version)||version.Revision<0||version<minimum||(!String.IsNullOrEmpty(exact)&&m.version!=exact))throw new InvalidDataException("取得する版が一致しません。公開中の旧版は導入せずに中止しました。");
        if(String.IsNullOrEmpty(m.displayVersion)||!System.Text.RegularExpressions.Regex.IsMatch(m.displayVersion,@"^[0-9]+\.[0-9]+[a-z][0-9]+$")||String.IsNullOrEmpty(m.sha256)||!System.Text.RegularExpressions.Regex.IsMatch(m.sha256,@"^[A-Fa-f0-9]{64}$"))throw new InvalidDataException("更新情報が不正です。");
        string expected=Release.BaseUrl+"downloads/"+m.displayVersion+"/BVE6Launcher-package.zip";
        if(m.url!=expected)throw new InvalidDataException("配布パッケージの取得先が許可された公開先と一致しません。");
    }
    internal static Manifest ParseManifest(string json,Version minimum,string exact){var m=new JavaScriptSerializer().Deserialize<Manifest>(json);ValidateManifest(m,minimum,exact);return m;}
    internal static string InstalledVersion(string root){string target=ActiveTarget(root);if(target==null)return null;return FileVersionInfo.GetVersionInfo(target).FileVersion;}
    internal static string ActiveTarget(string root){root=Root(root);string pointer=Path.Combine(root,"現在.txt");if(!File.Exists(pointer))return null;string target=Path.GetFullPath(File.ReadAllText(pointer).Trim());string versions=Path.Combine(root,"versions")+Path.DirectorySeparatorChar;if(!target.StartsWith(versions,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).Equals("BVE6互換ランチャー.exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(target))throw new InvalidDataException("既存の起動設定が不正です。変更を中止しました。");RejectLinks(target);return target;}
    internal static string DetectRoot(string directory){var p=new DirectoryInfo(directory.TrimEnd('\\'));if(p.Parent!=null&&p.Parent.Name=="versions"&&p.Parent.Parent!=null)return Root(p.Parent.Parent.FullName);return null;}
    internal static void Atomic(string path,byte[] bytes){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
    static byte[] Bytes(string path){return File.Exists(path)?File.ReadAllBytes(path):null;}
    static void Restore(string path,byte[] bytes){if(bytes==null){if(File.Exists(path))File.Delete(path);}else Atomic(path,bytes);}
    internal static string Install(string root,string zip,Manifest manifest,Action beforeCommit){
        ValidateManifest(manifest,new Version(Release.Number),null);
        if(!Hash(zip).Equals(manifest.sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SHA-256が一致しません。インストールを中止しました。");
        root=Root(root);string active=ActiveTarget(root);Version old;if(active!=null&&Version.TryParse(FileVersionInfo.GetVersionInfo(active).FileVersion,out old)&&old>new Version(manifest.version))throw new InvalidDataException("古い版への変更は行いません。");
        string owner=Path.Combine(root,"installer.owner");
        if(Directory.Exists(root)&&active==null&&Directory.GetFileSystemEntries(root).Length>0&&(!File.Exists(owner)||File.ReadAllText(owner)!=AppName))throw new IOException("インストール先に別のファイルがあります。別の場所を選んでください。");
        Directory.CreateDirectory(root);
        if(!File.Exists(owner))Atomic(owner,System.Text.Encoding.UTF8.GetBytes(AppName));
        using(var operation=new FileStream(Path.Combine(root,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
            string versions=Path.Combine(root,"versions");RejectLinks(versions);Directory.CreateDirectory(versions);
            string stage=Path.Combine(versions,".new-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            string pointer=Path.Combine(root,"現在.txt"),starter=Path.Combine(root,"LauncherStarter.exe"),starterHash=Path.Combine(root,"starter.sha256");
            byte[] oldPointer=Bytes(pointer),oldStarter=Bytes(starter),oldHash=Bytes(starterHash);bool rootTouched=false;
            try{
                var permitted=new HashSet<string>(PayloadNames,StringComparer.OrdinalIgnoreCase);permitted.Add("package-index.json");
                var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long size=0;
                using(var archive=ZipFile.OpenRead(zip)){
                    if(archive.Entries.Count!=permitted.Count)throw new InvalidDataException("配布パッケージの構成が一致しません。");
                    foreach(var entry in archive.Entries){string name=entry.FullName;
                        if(!permitted.Contains(name)||name.Contains("\\")||!seen.Add(name)||entry.Length<0||entry.Length>64L*1024*1024||(size+=entry.Length)>128L*1024*1024||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("許可されていないファイルがパッケージに含まれています。");
                        string dest=Path.Combine(stage,name.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(dest));entry.ExtractToFile(dest,false);
                    }
                }
                if(new FileInfo(Path.Combine(stage,"package-index.json")).Length>262144)throw new InvalidDataException("内部のファイル一覧が大きすぎます。");
                var index=new JavaScriptSerializer().Deserialize<Index>(File.ReadAllText(Path.Combine(stage,"package-index.json")));
                if(index==null||index.version!=manifest.version||index.displayVersion!=manifest.displayVersion||index.files==null||index.files.Count!=PayloadNames.Length)throw new InvalidDataException("パッケージ内部の版が一致しません。");
                foreach(string name in PayloadNames){string hash;if(!index.files.TryGetValue(name,out hash)||!Hash(Path.Combine(stage,name)).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("内部ファイルの確認値が一致しません: "+name);}
                foreach(string name in new[]{"BVE6互換ランチャー.exe","導入・更新.exe","LauncherStarter.exe","Uninstall.exe"})if(FileVersionInfo.GetVersionInfo(Path.Combine(stage,name)).FileVersion!=manifest.version||FileVersionInfo.GetVersionInfo(Path.Combine(stage,name)).ProductVersion!=manifest.displayVersion)throw new InvalidDataException("実行ファイルと更新情報の版が一致しません: "+name);
                if(oldStarter!=null&&(oldHash==null||!Hash(starter).Equals(System.Text.Encoding.UTF8.GetString(oldHash).Trim('\uFEFF',' ','\r','\n'),StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("既存の起動用ファイルを確認できません。変更を中止しました。");
                // Keep any earlier a4 candidate intact; activate the validated package by content identity.
                string final=Path.Combine(versions,manifest.version+"-"+manifest.sha256.Substring(0,12));RejectLinks(final);
                if(Directory.Exists(final)){foreach(string name in permitted){string a=Path.Combine(final,name),b=Path.Combine(stage,name);RejectLinks(a);if(!File.Exists(a)||!Hash(a).Equals(Hash(b),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("同じ版に異なるファイルがあります。既存のファイルは保持しました。");}foreach(string file in Directory.GetFiles(final,"*",SearchOption.AllDirectories)){string relative=file.Substring(final.Length+1).Replace('\\','/');if(!permitted.Contains(relative)&&relative!="installed.marker")throw new InvalidDataException("同じ版に記録外のファイルがあります。変更を中止しました。");}}
                else Directory.Move(stage,final);
                string exe=Path.Combine(final,"BVE6互換ランチャー.exe");
                // All payload files are validated before touching the active pointer.
                Atomic(Path.Combine(final,"installed.marker"),System.Text.Encoding.UTF8.GetBytes("installed"));
                rootTouched=true;Atomic(starter,File.ReadAllBytes(Path.Combine(final,"LauncherStarter.exe")));
                Atomic(starterHash,System.Text.Encoding.UTF8.GetBytes(Hash(starter)));
                if(beforeCommit!=null)beforeCommit();
                Atomic(pointer,System.Text.Encoding.UTF8.GetBytes(exe));return exe;
            }catch{if(rootTouched){Restore(starter,oldStarter);Restore(starterHash,oldHash);Restore(pointer,oldPointer);}throw;}
            finally{if(Directory.Exists(stage)){RejectLinks(stage);Directory.Delete(stage,true);}}
        }
    }
    internal static bool HasOlder(string root){
        root=Root(root);string active=ActiveTarget(root);if(active==null)return false;Version current;
        if(!Version.TryParse(FileVersionInfo.GetVersionInfo(active).FileVersion,out current))return false;
        string versions=Path.Combine(root,"versions");if(!Directory.Exists(versions))return false;RejectLinks(versions);
        foreach(string dir in Directory.GetDirectories(versions)){var match=System.Text.RegularExpressions.Regex.Match(Path.GetFileName(dir),@"^(\d+\.\d+\.\d+\.\d+)-[A-Fa-f0-9]{12}$");Version version;
            if(match.Success&&Version.TryParse(match.Groups[1].Value,out version)&&version<current&&File.Exists(Path.Combine(dir,"installed.marker"))&&File.Exists(Path.Combine(dir,"package-index.json")))return true;
        }return false;
    }
    // Delete only intact, indexed application files from older installed versions.
    // Unknown, modified, linked, newer, or currently locked files are retained.
    internal static bool CleanupOld(string root){
        root=Root(root);string active=ActiveTarget(root);if(active==null)throw new InvalidDataException("有効なランチャーが見つかりません。");
        string versions=Path.Combine(root,"versions");RejectLinks(versions);Version activeVersion;
        if(!Version.TryParse(FileVersionInfo.GetVersionInfo(active).FileVersion,out activeVersion))throw new InvalidDataException("現在の版を確認できません。");
        bool remaining=false;
        using(var operation=new FileStream(Path.Combine(root,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
            foreach(string dir in Directory.GetDirectories(versions)){
                try{
                    string name=Path.GetFileName(dir);var match=System.Text.RegularExpressions.Regex.Match(name,@"^(\d+\.\d+\.\d+\.\d+)-[A-Fa-f0-9]{12}$");Version version;
                    if(!match.Success||!Version.TryParse(match.Groups[1].Value,out version)||version>=activeVersion||String.Equals(Path.GetDirectoryName(active),dir,StringComparison.OrdinalIgnoreCase))continue;
                    RejectLinks(dir);string marker=Path.Combine(dir,"installed.marker"),indexFile=Path.Combine(dir,"package-index.json"),bridge=Path.Combine(dir,"bridge");
                    if(!File.Exists(marker)||File.ReadAllText(marker)!="installed"||!File.Exists(indexFile)||new FileInfo(indexFile).Length>262144)continue;
                    RejectLinks(marker);RejectLinks(indexFile);if(Directory.Exists(bridge))RejectLinks(bridge);
                    var index=new JavaScriptSerializer().Deserialize<Index>(File.ReadAllText(indexFile));
                    if(index==null||index.version!=version.ToString()||index.files==null||index.files.Count!=PayloadNames.Length||PayloadNames.Any(x=>!index.files.ContainsKey(x)))continue;
                    var allowed=new HashSet<string>(PayloadNames.Where(x=>!x.Contains("/")),StringComparer.OrdinalIgnoreCase);allowed.Add("installed.marker");allowed.Add("package-index.json");
                    if(Directory.GetFiles(dir).Any(x=>!allowed.Contains(Path.GetFileName(x)))||Directory.GetDirectories(dir).Any(x=>!String.Equals(Path.GetFileName(x),"bridge",StringComparison.OrdinalIgnoreCase)))continue;
                    var bridgeAllowed=new HashSet<string>(PayloadNames.Where(x=>x.StartsWith("bridge/")).Select(x=>x.Substring(7)),StringComparer.OrdinalIgnoreCase);
                    if(Directory.Exists(bridge)&&(Directory.GetDirectories(bridge).Length!=0||Directory.GetFiles(bridge).Any(x=>!bridgeAllowed.Contains(Path.GetFileName(x)))))continue;
                    bool valid=true;foreach(string item in PayloadNames){string file=Path.Combine(dir,item.Replace('/',Path.DirectorySeparatorChar));if(!File.Exists(file))continue;RejectLinks(file);string expected=index.files[item];if(String.IsNullOrEmpty(expected)||!Hash(file).Equals(expected,StringComparison.OrdinalIgnoreCase)){valid=false;break;}}
                    if(!valid)continue;
                    foreach(string item in PayloadNames){string file=Path.Combine(dir,item.Replace('/',Path.DirectorySeparatorChar));try{if(File.Exists(file))File.Delete(file);}catch(IOException){remaining=true;}catch(UnauthorizedAccessException){remaining=true;}}
                    if(PayloadNames.Any(item=>File.Exists(Path.Combine(dir,item.Replace('/',Path.DirectorySeparatorChar))))){remaining=true;continue;}
                    if(Directory.Exists(bridge)&&Directory.GetFileSystemEntries(bridge).Length==0)Directory.Delete(bridge);
                    File.Delete(indexFile);File.Delete(marker);if(Directory.GetFileSystemEntries(dir).Length==0)Directory.Delete(dir);
                }catch(IOException){remaining=true;}catch(UnauthorizedAccessException){remaining=true;}catch(InvalidDataException){continue;}catch(ArgumentException){continue;}
            }
        }return remaining;
    }
    internal static List<string> Uninstall(string root){
        root=Root(root);var retained=new List<string>();string active=ActiveTarget(root);if(active==null)throw new InvalidDataException("インストール済みの本体が見つかりません。");
        using(var operation=new FileStream(Path.Combine(root,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
            string versionDir=Path.GetDirectoryName(active);var index=new JavaScriptSerializer().Deserialize<Index>(File.ReadAllText(Path.Combine(versionDir,"package-index.json")));
            if(index==null||index.files==null||index.files.Count!=PayloadNames.Length||PayloadNames.Any(x=>!index.files.ContainsKey(x)))throw new InvalidDataException("削除対象の記録が不正です。削除しませんでした。");
            foreach(string name in PayloadNames){string file=Path.Combine(versionDir,name);RejectLinks(file);if(!File.Exists(file))continue;if(!Hash(file).Equals(index.files[name],StringComparison.OrdinalIgnoreCase)){retained.Add(file);continue;}try{File.Delete(file);}catch(IOException){retained.Add(file);}catch(UnauthorizedAccessException){retained.Add(file);}}
            string starter=Path.Combine(root,"LauncherStarter.exe"),hash=Path.Combine(root,"starter.sha256");if(File.Exists(starter)&&File.Exists(hash)&&Hash(starter).Equals(File.ReadAllText(hash).Trim(),StringComparison.OrdinalIgnoreCase)){try{File.Delete(starter);File.Delete(hash);}catch(IOException){retained.Add(starter);}}
            File.Delete(Path.Combine(root,"現在.txt"));
            foreach(string name in new[]{"installed.marker","package-index.json"}){string file=Path.Combine(versionDir,name);if(File.Exists(file))File.Delete(file);}
            foreach(string dir in new[]{Path.Combine(versionDir,"bridge"),versionDir})if(Directory.Exists(dir)&&Directory.GetFileSystemEntries(dir).Length==0)Directory.Delete(dir);
        }
        // Root, old versions and all user-created files/settings are intentionally retained.
        return retained;
    }
}
