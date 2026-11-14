using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

// Read-only preflight for formats rejected by the bundled DSOAL runtime.
// It does not modify audio, scenario, vehicle, plugin, or system settings.
internal static class BveDsoalCompatibility {
    internal sealed class Check {
        internal string VehicleFile;
        internal string SoundFile;
        internal int TotalReferences;
        internal int UniqueWavFiles;
        internal int UnsupportedReferences;
        internal int UnsupportedUniqueFiles;
        internal string FirstExample;
        internal string UnknownReason;
        // Diagnostic preview only. Full counts always include every reference.
        internal readonly List<string> UnsupportedExamples=new List<string>();
        internal bool IsKnown {get{return String.IsNullOrEmpty(UnknownReason);}}
    }
    internal enum GuiDecision { KeepConfigured, StandardOnce, Cancel }

    // Pure decision function for offline tests. Only an explicit Yes can
    // override the saved background-audio choice for this one launch.
    internal static bool NeedsNativeChoice(Check check) {
        return check==null || !check.IsKnown || check.UnsupportedReferences>0;
    }
    // A known integer-PCM limitation in the bundled DSOAL can be handled
    // transparently by the isolated float32 staging path. This is NOT a
    // blanket claim that the train data or BVE itself is unsupported.
    internal static bool NeedsAutomaticPcmStaging(bool bve5,bool compat,
        bool backgroundRequested,Check check) {
        return !bve5 && compat && backgroundRequested && check!=null &&
            check.IsKnown && check.UnsupportedReferences>0;
    }
    internal static GuiDecision DecideGui(bool bve5,bool backgroundRequested,
        Check check,bool? explicitYes) {
        if(bve5 || !backgroundRequested || !NeedsNativeChoice(check))
            return GuiDecision.KeepConfigured;
        return explicitYes==true ? GuiDecision.StandardOnce : GuiDecision.Cancel;
    }

    internal static Check InspectScenario(string scenario) {
        return InspectScenario(scenario,null,CancellationToken.None);
    }
    // The main launcher may let the user select a vehicle different from the
    // scenario default. Inspect the effective selection without preparing a
    // copy or touching the original vehicle / Sound.txt / WAV files.
    internal static Check InspectScenario(string scenario,string selectedVehicle) {
        return InspectScenario(scenario,selectedVehicle,CancellationToken.None);
    }
    internal static Check InspectScenario(string scenario,string selectedVehicle,CancellationToken cancellation) {
        var result=new Check();
        try {
            cancellation.ThrowIfCancellationRequested();
            if(String.IsNullOrWhiteSpace(scenario))throw new InvalidDataException("シナリオが指定されていません。");
            string[] scenarioLines=Program.Read(scenario);
            if(scenarioLines.Length==0 || !Program.ScenarioHeader(scenarioLines[0]))
                throw new InvalidDataException("シナリオの形式を確認できません。");
            string vehicle;
            if(!String.IsNullOrWhiteSpace(selectedVehicle)){
                vehicle=Path.GetFullPath(selectedVehicle);
            }else{
                string vehicleSpec=Program.Value(scenarioLines,"Vehicle");
                if(String.IsNullOrWhiteSpace(vehicleSpec))throw new InvalidDataException("車両の指定がありません。");
                // A random/weighted expression is resolved by BVE itself. Do not
                // guess which alternative BVE will choose in a preflight.
                if(vehicleSpec.IndexOfAny(new[]{'|','*','?'})>=0)
                    throw new InvalidDataException("抽選式の車両指定は事前判定できません。");
                vehicle=Resolve(Path.GetDirectoryName(scenario),vehicleSpec);
            }
            result.VehicleFile=vehicle;
            string[] vehicleLines=Program.Read(vehicle);
            if(vehicleLines.Length==0 || !Program.VehicleHeader(vehicleLines[0]))
                throw new InvalidDataException("車両定義の形式を確認できません。");
            string soundSpec=Program.Value(vehicleLines,"Sound");
            // The vehicle Sound setting is optional in the existing launcher.
            // With no Sound.txt, there are no vehicle WAV references to inspect.
            if(String.IsNullOrWhiteSpace(soundSpec))return result;
            string sound=Resolve(Path.GetDirectoryName(vehicle),soundSpec);
            result.SoundFile=sound;
            string[] soundLines=Program.Read(sound);
            // Numeric and named Sound.txt keys are both WAV references.
            // Count duplicate references, but read each WAV header once.
            var unique=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<soundLines.Length;i++){
                cancellation.ThrowIfCancellationRequested();
                var pair=Program.Pair(soundLines[i]);
                if(String.IsNullOrEmpty(pair.Key) || String.IsNullOrEmpty(pair.Value))continue;
                string wav=Resolve(Path.GetDirectoryName(sound),pair.Value);
                result.TotalReferences++;
                bool unsupported;
                if(!unique.TryGetValue(wav,out unsupported)){
                    unsupported=IsUnsupportedWav(wav);
                    unique.Add(wav,unsupported);
                    result.UniqueWavFiles++;
                    if(unsupported)result.UnsupportedUniqueFiles++;
                }
                if(!unsupported)continue;
                result.UnsupportedReferences++;
                string example="Sound.txt "+(i+1)+"行: "+Path.GetFileName(wav);
                if(result.FirstExample==null)result.FirstExample=example;
                if(result.UnsupportedExamples.Count<200)result.UnsupportedExamples.Add(example);
            }
        }catch(Exception e) when(e is IOException || e is UnauthorizedAccessException ||
                                e is ArgumentException || e is InvalidDataException ||
                                e is NotSupportedException){
            result.UnknownReason=e.Message;
        }
        return result;
    }

    // Read-only human-facing report. It deliberately does NOT equate a WAV
    // header check with actual audio playback, ATS or graphics compatibility.
    internal static string FormatReport(Check check,string scenario,bool backgroundRequested){
        var lines=new List<string>{
            "BVE6 音声形式の互換性診断（読み取り専用）",
            "",
            "シナリオ: "+(scenario??"(未指定)"),
            "使用する車両: "+(check==null || check.VehicleFile==null ? "未確認" : check.VehicleFile),
            "Sound.txt: "+(check==null || check.SoundFile==null ? "指定なし／未確認" : check.SoundFile),
            "BVE6バックグラウンド音声設定: "+(backgroundRequested ? "ON" : "OFF"),
            ""
        };
        if(check==null || !check.IsKnown){
            lines.Add("判定: 不明（最後まで確認できませんでした）");
            lines.Add("理由: "+(check==null ? "検査結果がありません。" : check.UnknownReason));
            lines.Add("確認できた参照数だけで正常／非対応を判定しません。");
        }else{
            lines.Add("判定: "+(check.SoundFile==null ? "Sound.txt指定なし（車両WAV参照の検査対象なし）" :
                check.UnsupportedReferences>0 ? "DSOALで非対応の音声形式を検出" :
                "検査したWAV形式に非対応のものは検出されませんでした"));
            lines.Add("Sound.txtのWAV参照: "+check.TotalReferences+"件");
            lines.Add("異なるWAVファイル: "+check.UniqueWavFiles+"件");
            lines.Add("非対応の参照: "+check.UnsupportedReferences+"件");
            lines.Add("非対応の異なるファイル: "+check.UnsupportedUniqueFiles+"件");
            if(check.UnsupportedExamples.Count>0){
                lines.Add("");lines.Add("非対応の参照（Sound.txtの行番号とファイル名）:");
                lines.AddRange(check.UnsupportedExamples);
                if(check.UnsupportedReferences>check.UnsupportedExamples.Count)
                    lines.Add("ほか "+(check.UnsupportedReferences-check.UnsupportedExamples.Count)+"件（表示上限200件）");
            }
            if(check.UnsupportedReferences>0 && backgroundRequested){
                lines.Add("");
                lines.Add("対処: BVE6互換起動では、元のWAVを変更せずに一時作業コピーの自動変換を試みます。");
                lines.Add("安全に変換できなければ、標準DirectSoundでの起動を選択できます。");
                lines.Add("標準音声ではバックグラウンド再生機能は利用できません。");
            }
        }
        lines.Add("");
        lines.Add("注意: WAVの形式だけを調べています。実際に音が鳴るか、DSOALの音源割当、ATS・描画・走行時の互換性は確認できません。");
        lines.Add("この診断ではBVEを起動せず、元のシナリオ・車両・音声・設定を変更しません。");
        return String.Join(Environment.NewLine,lines.ToArray());
    }

    // Headless --auto-compat has no way to ask the user whether to disable
    // background playback for just this run. Reject before starting BVE
    // rather than silently choosing another audio backend or showing tens
    // of per-file BVE load errors. The interactive launcher offers a choice.
    internal static void RequireSupportedForHeadlessLaunch(Check check) {
        if(check==null || !check.IsKnown)
            throw new InvalidDataException(
                "バックグラウンド音声の形式を確認できないため、自動起動を中止しました。"+
                (check==null ? "" : " "+check.UnknownReason));
        if(check.UnsupportedReferences>0)
            throw new InvalidDataException(
                "バックグラウンド音声が対応していないWAV参照が"+
                check.UnsupportedReferences+"件あります。"+
                "通常のランチャー画面から標準音声で起動するか、"+
                "バックグラウンド音声をオフにしてから自動起動してください。"+
                " 音声・車両ファイルは変更していません。");
    }

    private static string Resolve(string directory,string relative) {
        return Path.GetFullPath(Path.Combine(directory,relative.Trim()));
    }

    // DSOAL's ConvertFormat rejects 24/32-bit signed PCM (including the
    // equivalent WAVE_FORMAT_EXTENSIBLE PCM case). It accepts 8/16 PCM and
    // IEEE float32, verified separately with the user's bundled DLLs.
    internal static bool IsUnsupportedWav(string file) {
        using(var s=File.OpenRead(file))
        using(var reader=new BinaryReader(s,Encoding.ASCII)){
            if(s.Length<12 || new String(reader.ReadChars(4))!="RIFF")
                throw new InvalidDataException("RIFF形式でない音声: "+file);
            uint riffLength=reader.ReadUInt32();
            if(new String(reader.ReadChars(4))!="WAVE")
                throw new InvalidDataException("WAVE形式でない音声: "+file);
            long end=8L+riffLength;
            if(riffLength<4 || end!=s.Length)
                throw new InvalidDataException("RIFFサイズが不正: "+file);
            bool? unsupported=null;
            bool hasData=false;
            while(s.Position+8<=end){
                string id=new String(reader.ReadChars(4));
                uint length=reader.ReadUInt32();
                long start=s.Position;
                long next=start+length+(length&1);
                if(next>end)throw new InvalidDataException("WAVチャンク長が不正: "+file);
                if(id=="fmt "){
                    if(unsupported.HasValue)
                        throw new InvalidDataException("WAV形式情報が重複: "+file);
                    if(length<16)throw new InvalidDataException("WAV形式情報が不足: "+file);
                    ushort tag=reader.ReadUInt16();
                    ushort channels=reader.ReadUInt16();
                    reader.ReadUInt32();reader.ReadUInt32();reader.ReadUInt16();
                    ushort bits=reader.ReadUInt16();
                    if(tag==1){
                        if(bits==24 || bits==32)unsupported=true;
                        else if((bits==8 || bits==16) && (channels==1 || channels==2))unsupported=false;
                        else throw new InvalidDataException("未確認のPCM形式: "+file);
                    }
                    else if(tag==3){
                        if(bits==32 && (channels==1 || channels==2))unsupported=false;
                        else throw new InvalidDataException("未確認の浮動小数点WAV形式: "+file);
                    }
                    else if(tag==0xfffe && length>=40){
                        ushort cbSize=reader.ReadUInt16();
                        if(cbSize<22 || 18L+cbSize>length)
                            throw new InvalidDataException("拡張WAV形式情報が不正: "+file);
                        ushort validBits=reader.ReadUInt16();
                        reader.ReadUInt32();
                        var subtype=new Guid(reader.ReadBytes(16));
                        if(validBits!=0 && validBits!=bits)
                            throw new InvalidDataException("パディングされたWAV形式を事前判定できません: "+file);
                        if(subtype==new Guid("00000001-0000-0010-8000-00aa00389b71")){
                            if(bits==24 || bits==32)unsupported=true;
                            else if((bits==8 || bits==16) && (channels==1 || channels==2))unsupported=false;
                            else throw new InvalidDataException("未確認の拡張WAV形式: "+file);
                        }
                        else if(subtype==new Guid("00000003-0000-0010-8000-00aa00389b71") &&
                           bits==32 && (channels==1 || channels==2))unsupported=false;
                        else throw new InvalidDataException("未確認の拡張WAV形式: "+file);
                    }
                    else throw new InvalidDataException("未確認のWAV形式番号: "+tag+" "+file);
                }
                else if(id=="data")hasData=true;
                s.Position=next;
            }
            if(s.Position!=end)
                throw new InvalidDataException("WAVチャンク境界が不正: "+file);
            if(!unsupported.HasValue)
                throw new InvalidDataException("WAV形式情報がありません: "+file);
            if(!hasData)
                throw new InvalidDataException("WAVデータチャンクがありません: "+file);
            return unsupported.Value;
        }
    }
}
