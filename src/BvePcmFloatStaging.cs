using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

// Converts unsupported integer PCM 24/32-bit WAVs to IEEE float32 only inside
// the launcher's private, disposable run directory. Original WAV files are
// opened read-only and are NEVER modified. This is a per-launch opt-in.
internal static class BvePcmFloatStaging {
    const long MaxSourceBytes=256L*1024*1024;
    internal sealed class Result {
        internal int ChangedReferences,ConvertedFiles;
        internal string SoundFile,VehicleFile,ScenarioFile;
        internal readonly List<string> ConvertedNames=new List<string>();
    }
    sealed class WavInfo {
        internal ushort Bits,Channels,BlockAlign;
        internal uint SampleRate,DataLength;
        internal long DataOffset;
        internal uint Frames;
        internal long OutputLength;
        internal long SaurChunkOffset=-1;
        internal string SourceSha256;
    }
    static string FourCC(BinaryReader r){
        byte[] code=r.ReadBytes(4);
        if(code.Length!=4)throw new InvalidDataException("WAVチャンクの読み込みに失敗しました。");
        return Encoding.ASCII.GetString(code);
    }
    static string Sha256(Stream stream,CancellationToken cancellation){
        long position=stream.Position;
        try {
            stream.Position=0;
            using(var sha=SHA256.Create()){
                var buffer=new byte[65536];
                int size;
                while((size=stream.Read(buffer,0,buffer.Length))>0){
                    cancellation.ThrowIfCancellationRequested();
                    sha.TransformBlock(buffer,0,size,buffer,0);
                }
                cancellation.ThrowIfCancellationRequested();
                sha.TransformFinalBlock(new byte[0],0,0);
                return BitConverter.ToString(sha.Hash).Replace("-","");
            }
        }finally{stream.Position=position;}
    }
    static WavInfo InspectWav(string file,CancellationToken cancellation){
        cancellation.ThrowIfCancellationRequested();
        using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
        using(var input=new BinaryReader(stream)){
            if(stream.Length<44 || stream.Length>MaxSourceBytes || FourCC(input)!="RIFF")
                throw new InvalidDataException("対応するRIFF WAV形式でないか、256MBを超えています: "+file);
            uint riffSize=input.ReadUInt32();
            if(FourCC(input)!="WAVE" || (long)riffSize+8!=stream.Length)
                throw new InvalidDataException("WAVのサイズ/形式が不正です: "+file);
            var info=new WavInfo();
            bool fmt=false,data=false;
            while(stream.Position+8<=stream.Length){
                cancellation.ThrowIfCancellationRequested();
                string id=FourCC(input);
                uint size=input.ReadUInt32();
                long start=stream.Position;
                long next=start+(long)size+(size&1);
                if(next>stream.Length)throw new InvalidDataException("WAVチャンクがファイル外を参照しています: "+file);
                if(id=="fmt "){
                    if(fmt || size<16)throw new InvalidDataException("WAV形式情報が不正です: "+file);
                    fmt=true;
                    ushort tag=input.ReadUInt16();
                    info.Channels=input.ReadUInt16();
                    info.SampleRate=input.ReadUInt32();
                    uint byteRate=input.ReadUInt32();
                    info.BlockAlign=input.ReadUInt16();
                    info.Bits=input.ReadUInt16();
                    if(info.Channels<1 || info.Channels>2 || info.SampleRate<1 ||
                       (info.Bits!=24 && info.Bits!=32) ||
                       info.BlockAlign!=info.Channels*(info.Bits/8) ||
                       (long)byteRate!=(long)info.SampleRate*info.BlockAlign)
                        throw new NotSupportedException("未検証の整数PCM音声形式です: "+file);
                    if(tag==0xfffe){
                        // Preserve a strict, verified subset of extensible
                        // PCM rather than silently dropping channel layout.
                        if(size!=40)throw new NotSupportedException("未検証の拡張WAV形式情報です: "+file);
                        ushort cbSize=input.ReadUInt16();
                        ushort validBits=input.ReadUInt16();
                        uint channelMask=input.ReadUInt32();
                        byte[] guidBytes=input.ReadBytes(16);
                        uint ordinaryMask=info.Channels==1?4u:3u;
                        if(guidBytes.Length!=16 || cbSize!=22 ||
                           validBits!=info.Bits ||
                           channelMask!=ordinaryMask ||
                           new Guid(guidBytes)!=new Guid("00000001-0000-0010-8000-00aa00389b71"))
                            throw new NotSupportedException("パディング/別形式の拡張WAVには対応しません: "+file);
                    }else if(tag!=1 || size!=16){
                        throw new NotSupportedException("非整数PCM WAVは変換対象外です: "+file);
                    }
                }else if(id=="data"){
                    if(data)throw new InvalidDataException("複数のdataチャンクを含むWAVには対応しません: "+file);
                    data=true;
                    info.DataOffset=start;
                    info.DataLength=size;
                }else if(id=="SAUR"){
                    // The user's Wavosaur 1.8.0.0 files carry a 32-byte
                    // version/zero-padded metadata chunk after the samples.
                    // Preserve this *verbatim* in the staged float WAV;
                    // never interpret metadata as additional PCM samples.
                    if(!data || info.SaurChunkOffset>=0 || size!=32)
                        throw new NotSupportedException("未確認のSAURメタデータです: "+file);
                    byte[] metadata=input.ReadBytes(32);
                    byte[] version=Encoding.ASCII.GetBytes("1.8.0.0\0");
                    if(metadata.Length!=32 ||
                       !version.SequenceEqual(metadata.Take(version.Length)) ||
                       metadata.Skip(version.Length).Any(b=>b!=0))
                        throw new NotSupportedException("未確認のSAURバージョン情報です: "+file);
                    info.SaurChunkOffset=start-8;
                }else {
                    // Dropping unknown chunks (including loop metadata) can
                    // change playback semantics. Reject rather than guess.
                    throw new NotSupportedException("追加WAVチャンクを安全に変換できません: "+id+" "+file);
                }
                stream.Position=next;
            }
            if(stream.Position!=stream.Length || !fmt || !data || info.DataLength==0 ||
               info.DataLength%info.BlockAlign!=0)
                throw new InvalidDataException("WAV形式/データ境界が不正です: "+file);
            info.Frames=info.DataLength/info.BlockAlign;
            // 12 RIFF + 24 fmt + 12 fact + 8 data + N float bytes.
            info.OutputLength=56L+(long)info.Frames*info.Channels*4;
            // Eight chunk-header bytes plus 32 Wavosaur metadata bytes.
            if(info.SaurChunkOffset>=0)info.OutputLength+=40;
            if(info.OutputLength>uint.MaxValue || info.OutputLength>MaxSourceBytes)
                throw new InvalidDataException("変換後のWAVがサイズ上限を超えます: "+file);
            info.SourceSha256=Sha256(stream,cancellation);
            return info;
        }
    }
    static void ConvertFile(string source,string destination,WavInfo info,CancellationToken cancel){
        // FileMode.CreateNew avoids overwriting an existing conversion file.
        using(var inputStream=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
        using(var reader=new BinaryReader(inputStream))
        using(var outputStream=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        using(var writer=new BinaryWriter(outputStream)){
            if(inputStream.Length<info.DataOffset+info.DataLength)
                throw new InvalidDataException("WAVが変換開始前に変更されました: "+source);
            // Reread format while holding the source read-only: fail closed
            // if content changed between the plan and the conversion.
            inputStream.Position=0;
            if(FourCC(reader)!="RIFF")throw new InvalidDataException("変換元WAVが変化しました。");
            uint originalRiffSize=reader.ReadUInt32();
            if((long)originalRiffSize+8!=inputStream.Length || FourCC(reader)!="WAVE")
                throw new InvalidDataException("変換元WAVのヘッダーが変化しました。");
            if(!String.Equals(Sha256(inputStream,cancel),info.SourceSha256,StringComparison.Ordinal))
                throw new InvalidDataException("変換元WAVが検査後に変更されました: "+source);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(checked((uint)(info.OutputLength-8)));
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write((uint)16);
            writer.Write((ushort)3); // WAVE_FORMAT_IEEE_FLOAT
            writer.Write(info.Channels);
            writer.Write(info.SampleRate);
            writer.Write(checked((uint)((long)info.SampleRate*info.Channels*4)));
            writer.Write(checked((ushort)(info.Channels*4)));
            writer.Write((ushort)32);
            writer.Write(Encoding.ASCII.GetBytes("fact"));
            writer.Write((uint)4);
            writer.Write(info.Frames);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(checked((uint)((long)info.Frames*info.Channels*4)));
            inputStream.Position=info.DataOffset;
            long samples=(long)info.Frames*info.Channels;
            for(long i=0;i<samples;i++){
                if((i&4095)==0)cancel.ThrowIfCancellationRequested();
                float value;
                if(info.Bits==24){
                    int b0=reader.ReadByte(),b1=reader.ReadByte(),b2=reader.ReadByte();
                    int integer=b0|(b1<<8)|(b2<<16);
                    if((integer&0x800000)!=0)integer|=unchecked((int)0xff000000);
                    // Every normalized signed 24-bit value is exactly
                    // representable in IEEE float32 (24 significant bits).
                    value=integer/8388608f;
                }else{
                    // Integer 32 -> float32 is not sample-exact for all inputs.
                    // Originals remain intact; the UI documents the rounding.
                    value=(float)((double)reader.ReadInt32()/2147483648.0);
                }
                writer.Write(value);
            }
            if(info.SaurChunkOffset>=0){
                inputStream.Position=info.SaurChunkOffset;
                byte[] saur=reader.ReadBytes(40);
                if(saur.Length!=40 ||
                   Encoding.ASCII.GetString(saur,0,4)!="SAUR" ||
                   BitConverter.ToUInt32(saur,4)!=32)
                    throw new InvalidDataException("SAURメタデータが検査後に変更されました: "+source);
                writer.Write(saur);
            }
            writer.Flush();
            if(outputStream.Length!=info.OutputLength)
                throw new InvalidDataException("変換後のWAVサイズが一致しません: "+destination);
            if(!String.Equals(Sha256(inputStream,cancel),info.SourceSha256,StringComparison.Ordinal))
                throw new InvalidDataException("変換中に元WAVが変更されました: "+source);
        }
    }
    static string Full(string directory,string name){return Path.GetFullPath(Path.Combine(directory,name.Trim()));}
    static bool Inside(string path,string folder){
        return path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
    static void EnsureNoReparse(string path){
        var item=File.GetAttributes(path);
        if((item&FileAttributes.ReparsePoint)!=0)
            throw new InvalidOperationException("リンクされたパスには変換用ファイルを作成しません: "+path);
    }
    static void ValidatePrivateRun(string run,string root){
        if(!String.Equals(Path.GetDirectoryName(run),root,StringComparison.OrdinalIgnoreCase) ||
           !Regex.IsMatch(Path.GetFileName(run),@"^run-\d{8}-\d{6}-[0-9a-fA-F]{6}$"))
            throw new InvalidOperationException("ランチャーが新規作成した作業フォルダのみ使用できます。");
        // Reject junctions/symlinks in the entire private storage ancestry.
        // In particular, a run-* junction must not point to original assets.
        for(string current=run;!String.IsNullOrWhiteSpace(current);current=Path.GetDirectoryName(current)){
            EnsureNoReparse(current);
            if(String.Equals(Path.GetPathRoot(current),current,StringComparison.OrdinalIgnoreCase))break;
        }
    }
    static void WriteNewLines(string path,IEnumerable<string> lines){
        using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        using(var writer=new StreamWriter(file,Encoding.GetEncoding(932))){
            foreach(string line in lines)writer.WriteLine(line);
        }
    }
    static string[] ReplaceOne(string[] lines,string key,string target){
        int matched=0;
        var result=new string[lines.Length];
        for(int i=0;i<lines.Length;i++){
            var p=Program.Pair(lines[i]);
            if(p.Key.Equals(key,StringComparison.OrdinalIgnoreCase)){
                matched++;
                result[i]=key+" = "+target;
            }else result[i]=lines[i];
        }
        if(matched!=1)throw new InvalidDataException("起動用ファイルの "+key+" 指定が一意ではありません。");
        return result;
    }
    internal static bool IsPrivatePreparedScenario(string scenario){
        if(String.IsNullOrWhiteSpace(scenario))return false;
        string full=Path.GetFullPath(scenario);
        string run=Path.GetDirectoryName(full);
        string root=Path.GetFullPath(Program.Data).TrimEnd(Path.DirectorySeparatorChar);
        return Inside(run,root) &&
            Path.GetFileName(full).Equals("scenario.txt",StringComparison.OrdinalIgnoreCase);
    }
    // BVE6 normal mode must keep native vehicle/ATS behavior and the saved
    // DSOAL setting. Build *only* private text copies with absolute references
    // to the original assets, then reuse the audited float32 WAV staging.
    // No source WAV, Sound.txt, vehicle, ATS or plugin is ever written to.
    internal static Result StageNormalPreparedScenario(string preparedScenario,CancellationToken cancel){
        cancel.ThrowIfCancellationRequested();
        string scenario=Path.GetFullPath(preparedScenario);
        if(!IsPrivatePreparedScenario(scenario))
            throw new InvalidOperationException("通常起動の音声変換はランチャー作業コピーに限ります。");
        string preparedRun=Path.GetDirectoryName(scenario);
        string root=Path.GetFullPath(Program.Data).TrimEnd(Path.DirectorySeparatorChar);
        ValidatePrivateRun(preparedRun,root);
        EnsureNoReparse(scenario);
        string[] scenarioLines=Program.Read(scenario);
        if(scenarioLines.Length==0 || !Program.ScenarioHeader(scenarioLines[0]))
            throw new InvalidDataException("作業用シナリオを確認できません。");
        string vehicleRef=Program.Value(scenarioLines,"Vehicle");
        if(String.IsNullOrWhiteSpace(vehicleRef) ||
           vehicleRef.IndexOfAny(new[]{'|','*','?'})>=0)
            throw new InvalidDataException("通常起動の車両指定を一意に確認できません。");
        string originalVehicle=Full(preparedRun,vehicleRef);
        EnsureNoReparse(originalVehicle);
        string[] vehicleLines=Program.Read(originalVehicle);
        if(vehicleLines.Length==0 || !Program.VehicleHeader(vehicleLines[0]))
            throw new InvalidDataException("元の車両定義を確認できません。");
        string vehicleDir=Path.GetDirectoryName(originalVehicle);
        string soundRef=Program.Value(vehicleLines,"Sound");
        if(String.IsNullOrWhiteSpace(soundRef))
            throw new InvalidDataException("変換対象のSound.txtが指定されていません。");
        string originalSound=Full(vehicleDir,soundRef);
        EnsureNoReparse(originalSound);
        string[] soundLines=Program.Read(originalSound);
        var resolvedSound=new string[soundLines.Length];
        for(int i=0;i<soundLines.Length;i++){
            cancel.ThrowIfCancellationRequested();
            var p=Program.Pair(soundLines[i]);
            resolvedSound[i]=String.IsNullOrEmpty(p.Key)||String.IsNullOrEmpty(p.Value)
                ? soundLines[i]
                : p.Key+" = "+Full(Path.GetDirectoryName(originalSound),p.Value);
        }
        // A fresh sibling run keeps the normal-mode prepared scenario intact
        // while producing a consistent scenario -> vehicle -> Sound chain.
        string run=Path.Combine(root,"run-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+
            "-"+Guid.NewGuid().ToString("N").Substring(0,6));
        Directory.CreateDirectory(run);
        ValidatePrivateRun(run,root);
        string copiedSound=Path.Combine(run,"sound.txt");
        string copiedVehicle=Path.Combine(run,"vehicle.txt");
        // Keep the original ATS64/Ats32/Ats references *exactly* the same
        // files, resolving relative paths from the original vehicle folder.
        var nativeFileKeys=new HashSet<string>(new[]{"PerformanceCurve","Parameters",
            "Panel","MotorNoise","Ats","Ats32","Ats64"},StringComparer.OrdinalIgnoreCase);
        var resolvedVehicle=new string[vehicleLines.Length];
        for(int i=0;i<vehicleLines.Length;i++){
            cancel.ThrowIfCancellationRequested();
            var p=Program.Pair(vehicleLines[i]);
            if(p.Key.Equals("Sound",StringComparison.OrdinalIgnoreCase))
                resolvedVehicle[i]="Sound = "+copiedSound;
            else if(nativeFileKeys.Contains(p.Key) && !String.IsNullOrWhiteSpace(p.Value)){
                string source=Full(vehicleDir,p.Value);
                if(!File.Exists(source))throw new FileNotFoundException(
                    "通常起動の元車両参照が見つかりません: "+source);
                resolvedVehicle[i]=p.Key+" = "+source;
            }else resolvedVehicle[i]=vehicleLines[i];
        }
        // Retain BveEX sidecar selection when a native vehicle supplies it.
        var configs=new[]{
            new[]{Path.Combine(vehicleDir,"VehiclePluginUsing.xml"),
                  Path.Combine(run,"VehiclePluginUsing.xml")},
            new[]{Path.ChangeExtension(originalVehicle,".VehiclePluginUsing.xml"),
                  Path.Combine(run,"vehicle.VehiclePluginUsing.xml")}
        };
        foreach(string[] config in configs){
            cancel.ThrowIfCancellationRequested();
            if(!File.Exists(config[0]))continue;
            EnsureNoReparse(config[0]);
            var xml=XDocument.Load(config[0]);
            foreach(var attr in xml.Descendants().Attributes("Path")){
                if(String.IsNullOrWhiteSpace(attr.Value))continue;
                string source=Full(Path.GetDirectoryName(config[0]),attr.Value);
                if(!File.Exists(source))throw new FileNotFoundException(
                    "元のBveEXプラグインが見つかりません: "+source);
                attr.Value=source;
            }
            using(var stream=new FileStream(config[1],FileMode.CreateNew,
                FileAccess.Write,FileShare.None))xml.Save(stream);
        }
        // CreateNew guarantees this cannot overwrite a prepared or original
        // file even if a stale same-name file appears during the operation.
        WriteNewLines(copiedSound,resolvedSound);
        WriteNewLines(copiedVehicle,resolvedVehicle);
        string copiedScenario=Path.Combine(run,"scenario.txt");
        WriteNewLines(copiedScenario,ReplaceOne(scenarioLines,"Vehicle",copiedVehicle));
        return StagePreparedScenario(copiedScenario,cancel);
    }
    internal static Result StagePreparedScenario(string preparedScenario,CancellationToken cancel){
        cancel.ThrowIfCancellationRequested();
        string scenario=Path.GetFullPath(preparedScenario);
        string run=Path.GetDirectoryName(scenario);
        string privateRoot=Path.GetFullPath(Program.Data).TrimEnd(Path.DirectorySeparatorChar);
        if(!Inside(run,privateRoot) ||
           !Path.GetFileName(scenario).Equals("scenario.txt",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ランチャーの作業コピー以外は変換しません。");
        ValidatePrivateRun(run,privateRoot);
        string vehicle=Path.Combine(run,"vehicle.txt"),sound=Path.Combine(run,"sound.txt");
        EnsureNoReparse(scenario);EnsureNoReparse(vehicle);EnsureNoReparse(sound);
        string[] scenarioLines=Program.Read(scenario),vehicleLines=Program.Read(vehicle);
        if(scenarioLines.Length==0 || !Program.ScenarioHeader(scenarioLines[0]) ||
           vehicleLines.Length==0 || !Program.VehicleHeader(vehicleLines[0]) ||
           !String.Equals(Full(run,Program.Value(scenarioLines,"Vehicle")),vehicle,StringComparison.OrdinalIgnoreCase) ||
           !String.Equals(Full(run,Program.Value(vehicleLines,"Sound")),sound,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("起動用の車両とSound.txtの参照先が不一致です。");
        string[] lines=Program.Read(sound);
        var plans=new Dictionary<string,WavInfo>(StringComparer.OrdinalIgnoreCase);
        var paths=new string[lines.Length];
        for(int i=0;i<lines.Length;i++){
            cancel.ThrowIfCancellationRequested();
            var p=Program.Pair(lines[i]);
            if(String.IsNullOrEmpty(p.Key)||String.IsNullOrEmpty(p.Value))continue;
            string wav=Full(run,p.Value);
            paths[i]=wav;
            if(!BveDsoalCompatibility.IsUnsupportedWav(wav))continue;
            if(!plans.ContainsKey(wav))plans.Add(wav,InspectWav(wav,cancel));
        }
        if(plans.Count==0)throw new InvalidOperationException("この作業コピーには変換対象の整数PCM WAVがありません。");
        string outputFolder=Path.Combine(run,"audio-float32");
        if(Directory.Exists(outputFolder))
            throw new IOException("変換用フォルダが既に存在します。既存ファイルを上書きしません。");
        Directory.CreateDirectory(outputFolder);
        var mapped=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var result=new Result();
        int count=0;
        foreach(var plan in plans){
            cancel.ThrowIfCancellationRequested();
            string target=Path.Combine(outputFolder,"pcm-float-"+(++count).ToString("D4")+".wav");
            ConvertFile(plan.Key,target,plan.Value,cancel);
            if(BveDsoalCompatibility.IsUnsupportedWav(target))
                throw new InvalidDataException("変換したWAVが事前検証に通りませんでした: "+target);
            mapped.Add(plan.Key,target);
            result.ConvertedNames.Add(Path.GetFileName(plan.Key));
        }
        var replacements=new string[lines.Length];
        for(int i=0;i<lines.Length;i++){
            string target;
            if(paths[i]!=null && mapped.TryGetValue(paths[i],out target)){
                var p=Program.Pair(lines[i]);
                replacements[i]=p.Key+" = "+target;
                result.ChangedReferences++;
            }else replacements[i]=lines[i];
        }
        result.ConvertedFiles=mapped.Count;
        string newSound=Path.Combine(run,"sound.float32.txt");
        string newVehicle=Path.Combine(run,"vehicle.float32.txt");
        string newScenario=Path.Combine(run,"scenario.float32.txt");
        // CreateNew never overwrites prepared files, hardlinks, or a prior
        // result, even if a test is repeated or unexpectedly interrupted.
        WriteNewLines(newSound,replacements);
        WriteNewLines(newVehicle,ReplaceOne(vehicleLines,"Sound",newSound));
        WriteNewLines(newScenario,ReplaceOne(scenarioLines,"Vehicle",newVehicle));
        // BveEX can also use filename-specific plugin configuration; keep
        // the same plugin settings for the new private vehicle basename.
        string oldConfig=Path.Combine(run,"vehicle.VehiclePluginUsing.xml");
        if(File.Exists(oldConfig)){
            EnsureNoReparse(oldConfig);
            string newConfig=Path.Combine(run,"vehicle.float32.VehiclePluginUsing.xml");
            using(var original=File.OpenRead(oldConfig))
            using(var copy=new FileStream(newConfig,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                original.CopyTo(copy);
        }
        // Validate the complete *alternate* prepared scenario. The original
        // scenario/vehicle/Sound copies remain byte-for-byte unchanged.
        var finalCheck=BveDsoalCompatibility.InspectScenario(newScenario);
        if(!finalCheck.IsKnown || finalCheck.UnsupportedReferences>0)
            throw new InvalidDataException("変換用シナリオの再検査に失敗したためBVEを起動しません。"+
                (finalCheck.UnknownReason??("非対応音声"+finalCheck.UnsupportedReferences+"件")));
        result.SoundFile=newSound;
        result.VehicleFile=newVehicle;
        result.ScenarioFile=newScenario;
        return result;
    }
}
