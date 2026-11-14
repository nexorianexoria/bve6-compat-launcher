using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

// Read-only inspection of an EXISTING DSOAL log. No BVE/audio device is
// launched, and log messages are not interpreted as unique WAVs or voices.
internal static class BveDsoalSavedLog {
    private const long MaxFileBytes=2*1024*1024;
    private static readonly Regex Source=new Regex(
        @"^[0-9a-fA-F]+:err:dsound:Buffer::setLocation Out of any sources$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Pcm24=new Regex(
        @"^[0-9a-fA-F]+:fixme:dsound:ConvertFormat 24-bit integer samples not supported$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Speaker=new Regex(
        @"^[0-9a-fA-F]+:warn:dsound:GetSpeakerConfig PhysicalSpeakers is not a VT_UI4: 0x[0-9a-fA-F]+$",
        RegexOptions.CultureInvariant);

    internal sealed class Segment {
        internal string Kind;
        internal int From,To,Count;
    }
    internal sealed class Result {
        internal string Path,Status="UNKNOWN",Reason;
        internal int TotalLines,SourceErrors,Pcm24Errors,SpeakerWarnings,OtherLines;
        internal string ShaBefore,ShaAfter;
        internal readonly List<Segment> Segments=new List<Segment>();
        internal readonly List<string> UnknownExamples=new List<string>();
    }
    private static string Hash(string file,CancellationToken cancellation) {
        using(var sha=SHA256.Create())
        using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
            byte[] buffer=new byte[8192];
            long total=0;
            int size;
            while((size=stream.Read(buffer,0,buffer.Length))>0){
                cancellation.ThrowIfCancellationRequested();
                total+=size;
                if(total>MaxFileBytes)
                    throw new InvalidDataException("ログが2MBを超えたため安全のため解析を中止しました。");
                sha.TransformBlock(buffer,0,size,buffer,0);
            }
            cancellation.ThrowIfCancellationRequested();
            sha.TransformFinalBlock(new byte[0],0,0);
            return BitConverter.ToString(sha.Hash).Replace("-","");
        }
    }
    internal static Result Inspect(string path) {
        return Inspect(path,CancellationToken.None);
    }
    internal static Result Inspect(string path,CancellationToken cancellation) {
        var r=new Result{Path=path};
        try {
            cancellation.ThrowIfCancellationRequested();
            if(String.IsNullOrWhiteSpace(path))throw new InvalidDataException("ログが指定されていません。");
            var info=new FileInfo(path);
            if(!info.Exists)throw new FileNotFoundException("保存済みログが見つかりません。",path);
            if(info.Length==0)throw new InvalidDataException("空のログです。");
            if(info.Length>MaxFileBytes)throw new InvalidDataException("2MBを超えるため、この診断では読み込みません。");
            r.ShaBefore=Hash(path,cancellation);
            cancellation.ThrowIfCancellationRequested();
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                // Accept only a UTF-8 BOM. Do not use auto BOM detection,
                // which would incorrectly allow UTF-16 input.
                byte[] prefix=new byte[3];
                int prefixSize=stream.Read(prefix,0,prefix.Length);
                stream.Position=prefixSize==3 && prefix[0]==0xEF &&
                    prefix[1]==0xBB && prefix[2]==0xBF ? 3 : 0;
                using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),false)){
                string line;
                while((line=reader.ReadLine())!=null){
                    cancellation.ThrowIfCancellationRequested();
                    // Also stop if a writer appended data after the initial size check.
                    if(stream.Position>MaxFileBytes)throw new InvalidDataException("読み取り中にログが2MBを超えました。");
                    r.TotalLines++;
                    string clean=line.TrimEnd();
                    string kind;
                    if(Source.IsMatch(clean)){r.SourceErrors++;kind="source_pool";}
                    else if(Pcm24.IsMatch(clean)){r.Pcm24Errors++;kind="pcm24";}
                    else if(Speaker.IsMatch(clean)){r.SpeakerWarnings++;kind="speaker_warning";}
                    else{
                        r.OtherLines++;kind="unknown";
                        if(r.UnknownExamples.Count<10)r.UnknownExamples.Add(r.TotalLines+"行: "+line);
                    }
                    if(r.Segments.Count==0 || r.Segments[r.Segments.Count-1].Kind!=kind)
                        r.Segments.Add(new Segment{Kind=kind,From=r.TotalLines,To=r.TotalLines,Count=1});
                    else {
                        var s=r.Segments[r.Segments.Count-1];
                        s.To=r.TotalLines;s.Count++;
                    }
                }
                }
            }
            cancellation.ThrowIfCancellationRequested();
            r.ShaAfter=Hash(path,cancellation);
            cancellation.ThrowIfCancellationRequested();
            if(r.ShaBefore!=r.ShaAfter){r.Status="UNKNOWN";r.Reason="読み取り中にログが変化しました。";}
            else if(r.TotalLines==0){r.Status="UNKNOWN";r.Reason="空のログです。";}
            else if(r.OtherLines>0){r.Status="UNKNOWN";r.Reason="分類できないログ行があります。";}
            else if(r.SourceErrors>0 || r.Pcm24Errors>0){r.Status="ERRORS_OBSERVED_CAUSE_UNKNOWN";}
            else {r.Status="NO_MATCHED_ERRORS_CAUSE_UNKNOWN";}
        }catch(Exception ex) when(ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException ||
                                  ex is ArgumentException || ex is NotSupportedException ||
                                  ex is DecoderFallbackException){
            r.Status="UNKNOWN";
            r.Reason=ex.Message;
            r.TotalLines=r.SourceErrors=r.Pcm24Errors=r.SpeakerWarnings=r.OtherLines=0;
            r.Segments.Clear();r.UnknownExamples.Clear();
            if(r.ShaBefore!=null){try{r.ShaAfter=Hash(path,CancellationToken.None);}catch(IOException){}catch(InvalidDataException){}}
        }
        return r;
    }
    internal static string Format(Result r){
        var text=new List<string>{
            "DSOAL 保存済みログの診断（読み取り専用）", "",
            "対象: "+(r.Path??"未指定"),
            "判定: "+r.Status,
            "詳細: "+(r.Reason??"分類したログ行から根本原因は判断できません。"),
            "ログ行数: "+r.TotalLines,
            "音声ソース割当エラーのメッセージ: "+r.SourceErrors+"件",
            "24bit PCM形式非対応のメッセージ: "+r.Pcm24Errors+"件",
            "スピーカー設定警告のメッセージ: "+r.SpeakerWarnings+"件",
            "その他／未分類: "+r.OtherLines+"件",
            "入力SHA-256（前）: "+(r.ShaBefore??"未取得"),
            "入力SHA-256（後）: "+(r.ShaAfter??"未取得"),
            "", "連続したメッセージの区間（時刻ではなく行順）:"
        };
        foreach(var s in r.Segments)text.Add(s.From+"～"+s.To+"行: "+s.Kind+" / "+s.Count+"件");
        if(r.UnknownExamples.Count>0){
            text.Add("");text.Add("分類できなかった行（最大10件）:");
            text.AddRange(r.UnknownExamples);
        }
        text.Add("");
        text.Add("注意: 表示件数はログメッセージ数であり、WAVファイル数・同時再生数・ソース上限ではありません。");
        text.Add("DSOAL内部の割当失敗と24bit PCM拒否は別の記録です。因果関係は確認できません。");
        text.Add("ログには個別WAV・source ID・割当上限・メッセージ時刻がないため、根本原因は不明です。");
        text.Add("ログに該当エラーが無くても、音が正常再生された証拠にはなりません。");
        text.Add("この機能はログの読み取りだけを行い、BVE・音声デバイスの起動や元ログの変更をしません。");
        return String.Join(Environment.NewLine,text.ToArray());
    }
}
