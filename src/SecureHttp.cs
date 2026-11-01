using System;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;

internal static class SecureHttp {
    static Stream Open(string url){
        Uri uri;
        if(!Uri.TryCreate(url,UriKind.Absolute,out uri))throw new InvalidDataException("URLが正しくありません。");
        // Validate the caller's URL before adding our own cache-busting value.
        ValidateUrl(uri);
        string expectedQuery=null;
        if(String.Equals(uri.AbsoluteUri,Release.UpdateUrl,StringComparison.Ordinal)){
            expectedQuery="?check="+Guid.NewGuid().ToString("N");
            uri=new Uri(uri.AbsoluteUri+expectedQuery);
        }
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        for(int i=0;i<8;i++){
            ValidateRedirect(uri,expectedQuery);
            var request=(HttpWebRequest)WebRequest.Create(uri);request.AllowAutoRedirect=false;request.Timeout=20000;request.ReadWriteTimeout=20000;request.UserAgent="BVE6-Launcher";
            request.CachePolicy=new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);
            request.Headers[HttpRequestHeader.CacheControl]="no-cache";
            HttpWebResponse response;
            try{response=(HttpWebResponse)request.GetResponse();}
            catch(WebException e){if(e.Response==null)throw;response=(HttpWebResponse)e.Response;}
            int code=(int)response.StatusCode;
            if(code>=300&&code<400){string location=response.Headers[HttpResponseHeader.Location];response.Dispose();if(String.IsNullOrEmpty(location))throw new IOException("移転先がありません。");uri=new Uri(uri,location);continue;}
            if(code!=200){response.Dispose();throw new IOException("更新ファイルを取得できません (HTTP "+code+")。");}
            return new OwnedStream(response);
        }
        throw new IOException("更新先の転送回数が多すぎます。");
    }
    internal static void ValidateUrl(Uri uri){if(uri==null||uri.Scheme!=Uri.UriSchemeHttps||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Query.Length!=0||uri.Fragment.Length!=0||!uri.Host.Equals("raw.githubusercontent.com",StringComparison.OrdinalIgnoreCase)||!uri.AbsolutePath.StartsWith("/"+Release.Repository+"/main/",StringComparison.Ordinal))throw new InvalidDataException("許可されたGitHub公開先以外からの取得は行いません。");}
    internal static void ValidateRedirect(Uri uri,string expectedQuery){
        if(uri==null)throw new InvalidDataException("URLが正しくありません。");
        if(uri.Query.Length==0){ValidateUrl(uri);return;}
        Uri update=new Uri(Release.UpdateUrl);
        if(expectedQuery==null||!String.Equals(uri.Query,expectedQuery,StringComparison.Ordinal)||!String.Equals(uri.AbsolutePath,update.AbsolutePath,StringComparison.Ordinal))throw new InvalidDataException("更新情報のURLが正しくありません。");
        var withoutQuery=new Uri(uri.GetLeftPart(UriPartial.Path));ValidateUrl(withoutQuery);
        if(uri.Fragment.Length!=0)throw new InvalidDataException("更新情報のURLが正しくありません。");
    }
    internal static string Text(string url){using(var stream=Open(url))using(var memory=new MemoryStream()){Copy(stream,memory,131072);return Encoding.UTF8.GetString(memory.ToArray());}}
    internal static void File(string url,string path){using(var stream=Open(url))using(var target=System.IO.File.Create(path)){Copy(stream,target,150L*1024*1024);}}
    static void Copy(Stream input,Stream output,long limit){byte[] buffer=new byte[32768];long total=0;int n;while((n=input.Read(buffer,0,buffer.Length))>0){total+=n;if(total>limit)throw new IOException("ファイルのサイズが大きすぎます。");output.Write(buffer,0,n);}}
    sealed class OwnedStream:Stream {
        readonly HttpWebResponse response;readonly Stream inner;
        internal OwnedStream(HttpWebResponse r){response=r;inner=r.GetResponseStream();}
        protected override void Dispose(bool disposing){if(disposing){inner.Dispose();response.Dispose();}base.Dispose(disposing);}
        public override int Read(byte[] b,int o,int c){return inner.Read(b,o,c);}public override bool CanRead{get{return true;}}public override bool CanSeek{get{return false;}}public override bool CanWrite{get{return false;}}
        public override long Length{get{throw new NotSupportedException();}}public override long Position{get{throw new NotSupportedException();}set{throw new NotSupportedException();}}
        public override void Flush(){}public override long Seek(long o,SeekOrigin s){throw new NotSupportedException();}public override void SetLength(long v){throw new NotSupportedException();}public override void Write(byte[] b,int o,int c){throw new NotSupportedException();}
    }
}
