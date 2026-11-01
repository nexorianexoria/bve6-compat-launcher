using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading.Tasks;

internal sealed class CatalogItem {
 public Program.Entry Entry; public string Title,Group,Folder,Author,ImagePath,SupportHint,DataHint;
 public string DisplayFolder {get{return Folder??"";}}
 public string DisplaySection {get{string folder=DisplayFolder;if(Path.IsPathRooted(folder))return Path.GetFileName(folder.TrimEnd('\\','/'));return folder.Split(new[]{'\\','/'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??DisplayGroup;}}
 public string DisplayGroup {get{return Group??"";}}
 string cachedSearchText;
 public string SearchText {get{return cachedSearchText??(cachedSearchText=Title+" "+DisplayGroup+" "+DisplayFolder+" "+Author+" "+SupportHint+" "+DataHint+" "+Entry.Path);}}
}
internal static class CatalogData {
 public static List<CatalogItem> Build(IEnumerable<Program.Entry> entries,string root,bool vehicle){return Build(entries,root,vehicle,null,System.Threading.CancellationToken.None);}
 public static List<CatalogItem> Build(IEnumerable<Program.Entry> entries,string root,bool vehicle,Action<int> onProcessed){return Build(entries,root,vehicle,onProcessed,System.Threading.CancellationToken.None);}
 public static List<CatalogItem> Build(IEnumerable<Program.Entry> entries,string root,bool vehicle,Action<int> onProcessed,System.Threading.CancellationToken token,int maxWorkers=0){
  // A DataVersionHint is kept per worker because its documentation cache is mutable.
  // Preserve item order and final sort semantics with one result slot per input entry.
  var source=entries.ToArray();
  var result=new CatalogItem[source.Length];
  string prefix=Path.GetFullPath(root).TrimEnd('\\')+"\\";
  var options=new ParallelOptions{MaxDegreeOfParallelism=maxWorkers>0?maxWorkers:(vehicle?3:2),CancellationToken=token};
  Parallel.For(0,source.Length,options,
   ()=>new DataVersionHint(root),
   (i,state,hints)=>{
    token.ThrowIfCancellationRequested();
    var entry=source[i];
    string relative=entry.Path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)?entry.Path.Substring(prefix.Length):entry.Path;
    string folder=Path.GetDirectoryName(relative);if(String.IsNullOrEmpty(folder))folder="";
    var item=new CatalogItem{Entry=entry,Title=Path.GetFileNameWithoutExtension(entry.Path),Group=folder,Folder=folder,Author=""};
    string[] lines=null;
    try{
     lines=Program.Read(entry.Path);
     if(!vehicle){
      string title=Program.Value(lines,"Title"),route=Program.Value(lines,"RouteTitle");
      if(!String.IsNullOrWhiteSpace(title))item.Title=title;
      if(!String.IsNullOrWhiteSpace(route))item.Group=route;
      item.Author=Program.Value(lines,"Author")??"";
      string specified=Program.Value(lines,"Vehicle");
      item.SupportHint=ScenarioChoices.IsRandom(specified)?"車両をランダム選択":CompatibilityHint.ForVehicle(Program.DefaultVehicle(entry.Path,lines));
     }else item.SupportHint=CompatibilityHint.ForVehicle(entry.Path,lines);
     item.ImagePath=Previews.Resolve(entry.Path,vehicle,lines);
    }catch(Exception ex){Diagnostics.Write("CATALOG_METADATA",entry.Path+" : "+ex.Message);item.SupportHint="形式を判定できません";}
    try{item.DataHint=hints.Describe(entry.Path,item.Title,vehicle,item.SupportHint,lines);}
    catch(Exception ex){Diagnostics.Write("DATA_HINT",entry.Path+" : "+ex.Message);item.DataHint=DataVersionHint.Quick(entry.Path,vehicle,item.SupportHint);}
    result[i]=item;
    if(onProcessed!=null)onProcessed(i);
    return hints;
   },_=>{});
  return result.OrderBy(x=>x.Group,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase).ToList();
 }
 public static List<CatalogItem> RefreshCached(IEnumerable<CatalogItem> source,string root,bool vehicle,System.Threading.CancellationToken token){
  var hints=new DataVersionHint(root);var result=new List<CatalogItem>();foreach(var item in source){token.ThrowIfCancellationRequested();string[] lines=null;
   try{if(!vehicle&&File.Exists(item.Entry.Path)&&new FileInfo(item.Entry.Path).Length<=2*1024*1024)lines=Program.Read(item.Entry.Path);}catch(IOException){}catch(UnauthorizedAccessException){}
   string hint;try{hint=hints.Describe(item.Entry.Path,item.Title,vehicle,item.SupportHint,lines);}catch(Exception ex){Diagnostics.Write("DATA_HINT",item.Entry.Path+" : "+ex.Message);hint=DataVersionHint.Quick(item.Entry.Path,vehicle,item.SupportHint);}
   result.Add(new CatalogItem{Entry=item.Entry,Title=item.Title,Group=item.Group,Folder=item.Folder,Author=item.Author,ImagePath=item.ImagePath,SupportHint=item.SupportHint,DataHint=hint});
  }return result;
 }
}

internal sealed class CatalogView:ScrollableControl {
 sealed class Tile {public CatalogItem Item;public Rectangle Bounds;}
 sealed class Heading {public string Text;public Rectangle Bounds;}

 readonly List<Tile> tiles=new List<Tile>();readonly List<Heading> headings=new List<Heading>();
 readonly Dictionary<string,Image> images=new Dictionary<string,Image>(StringComparer.OrdinalIgnoreCase);
 readonly Queue<string> waiting=new Queue<string>();readonly HashSet<string> pending=new HashSet<string>(StringComparer.OrdinalIgnoreCase);readonly Queue<string> cacheOrder=new Queue<string>();
 List<CatalogItem> items=new List<CatalogItem>();CatalogItem selected;string defaultPath;int workers;bool thumbnailsPaused;readonly ToolTip tooltip=new ToolTip();string hovered;
 public event EventHandler SelectionChanged;
 string emptyText="該当する項目がありません";
 public string EmptyText {get{return emptyText;}set{emptyText=value;Invalidate();}}
 public CatalogItem SelectedItem {get{return selected;}}
 public int ItemCount {get{return items.Count;}}
 public bool ShowVehicleLocation {get;set;}
 public CatalogView(){AutoScroll=true;DoubleBuffered=true;BackColor=Color.FromArgb(246,249,253);TabStop=true;SetStyle(ControlStyles.Selectable|ControlStyles.ResizeRedraw,true);AccessibleRole=AccessibleRole.List;}
 public void SetItems(IEnumerable<CatalogItem> source){string old=selected==null?null:selected.Entry.Path;items=source.OrderBy(x=>x.DisplaySection,StringComparer.CurrentCultureIgnoreCase).ThenBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase).ToList();while(waiting.Count>0)pending.Remove(waiting.Dequeue());selected=old==null?null:items.FirstOrDefault(x=>x.Entry.Path.Equals(old,StringComparison.OrdinalIgnoreCase));Reflow();if(old!=null&&selected==null)RaiseSelection();}
 public void ClearSelection(){if(selected==null)return;selected=null;Invalidate();RaiseSelection();}
 public bool SelectPath(string path){var found=items.FirstOrDefault(x=>x.Entry.Path.Equals(path,StringComparison.OrdinalIgnoreCase));if(found==null)return false;SelectItem(found);EnsureVisible(found);return true;}
 public void SetDefault(string path){defaultPath=path;Invalidate();}
 void RaiseSelection(){if(SelectionChanged!=null)SelectionChanged(this,EventArgs.Empty);}
 void SelectItem(CatalogItem item){if(selected==item)return;selected=item;Invalidate();RaiseSelection();}
 protected override void OnResize(EventArgs e){base.OnResize(e);Reflow();}
 void Reflow(){if(tiles==null)return;tiles.Clear();headings.Clear();int width=Math.Max(220,ClientSize.Width-SystemInformation.VerticalScrollBarWidth-16);int columns=Math.Max(1,width/320),tileWidth=width/columns,y=8;int rowHeight=ShowVehicleLocation?164:132,tileHeight=ShowVehicleLocation?157:125;foreach(var group in items.GroupBy(x=>x.DisplaySection,StringComparer.CurrentCultureIgnoreCase)){if(!String.IsNullOrWhiteSpace(group.Key)){headings.Add(new Heading{Text=group.Key,Bounds=new Rectangle(8,y,width,28)});y+=30;}int index=0;foreach(var item in group){tiles.Add(new Tile{Item=item,Bounds=new Rectangle(8+(index%columns)*tileWidth,y+(index/columns)*rowHeight,tileWidth-8,tileHeight)});index++;}y+=((index+columns-1)/columns)*rowHeight+8;}AutoScrollMinSize=new Size(0,y+4);Invalidate();}
 // View-layer badges deliberately use only explicit version evidence. ATS guesses stay internal.
 static string BadgeVersion(CatalogItem item){
  string displayed=DataVersionHint.Display(item.DataHint);
  if(displayed=="BVE5"||displayed=="BVE6"||displayed=="BVE5 / BVE6")return displayed;
  return "未判定";
 }
 static int DrawBadge(Graphics graphics,Font font,string label,int x,int y,Color background,Color foreground){
  int width=TextRenderer.MeasureText(label,font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+18;
  using(var brush=new SolidBrush(background))using(var path=UiDrawing.Round(new RectangleF(x,y,width,23),5))graphics.FillPath(brush,path);
  TextRenderer.DrawText(graphics,label,font,new Rectangle(x+9,y+3,width-12,17),foreground,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);
  return width;
 }
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e);
  e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
  Rectangle visible=new Rectangle(-AutoScrollPosition.X,-AutoScrollPosition.Y,ClientSize.Width,ClientSize.Height);
  using(var subFont=new Font(Font.FontFamily,Math.Max(8,Font.Size-2)))
  using(var tagFont=new Font(Font.FontFamily,Math.Max(8,Font.Size-2),FontStyle.Bold))
  using(var headingFont=new Font(Font,FontStyle.Bold)){
   foreach(var heading in headings){
    if(heading.Bounds.Bottom<visible.Top)continue;
    if(heading.Bounds.Top>visible.Bottom)break;
    var rect=heading.Bounds;rect.Offset(AutoScrollPosition);
    TextRenderer.DrawText(e.Graphics,heading.Text,headingFont,rect,Color.FromArgb(72,91,119),TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);
    int titleWidth=Math.Min(rect.Width-12,TextRenderer.MeasureText(heading.Text,headingFont).Width+12);
    if(titleWidth<rect.Width-12)using(var line=new Pen(Color.FromArgb(221,229,240)))e.Graphics.DrawLine(line,rect.Left+titleWidth,rect.Top+rect.Height/2,rect.Right-4,rect.Top+rect.Height/2);
   }
   foreach(var tile in tiles){
    if(tile.Bounds.Bottom<visible.Top)continue;
    if(tile.Bounds.Top>visible.Bottom)break;
    var bounds=tile.Bounds;bounds.Offset(AutoScrollPosition);
    bool chosen=tile.Item==selected;
    bool isDefault=defaultPath!=null&&tile.Item.Entry.Path.Equals(defaultPath,StringComparison.OrdinalIgnoreCase);
    Color background=chosen?Color.FromArgb(232,242,255):Color.White;
    using(var fill=new SolidBrush(background))using(var shape=UiDrawing.Round(bounds,10))e.Graphics.FillPath(fill,shape);
    using(var pen=new Pen(chosen?Color.FromArgb(39,105,192):isDefault?Color.FromArgb(143,182,216):Color.FromArgb(223,232,244),chosen?2:1))
    using(var shape=UiDrawing.Round(new RectangleF(bounds.X+0.5f,bounds.Y+0.5f,bounds.Width-1,bounds.Height-1),10))e.Graphics.DrawPath(pen,shape);
    Rectangle picture=new Rectangle(bounds.X+10,bounds.Y+10,90,ShowVehicleLocation?137:104);
    Image thumbnail=Thumbnail(tile.Item.ImagePath);
    using(var imageBg=new SolidBrush(Color.FromArgb(243,246,250)))using(var imageShape=UiDrawing.Round(picture,7))e.Graphics.FillPath(imageBg,imageShape);
    if(thumbnail!=null){
     float scale=Math.Min((float)picture.Width/thumbnail.Width,(float)picture.Height/thumbnail.Height);
     int w=Math.Max(1,(int)(thumbnail.Width*scale)),h=Math.Max(1,(int)(thumbnail.Height*scale));
     e.Graphics.DrawImage(thumbnail,new Rectangle(picture.X+(picture.Width-w)/2,picture.Y+(picture.Height-h)/2,w,h));
    }else{
     TextRenderer.DrawText(e.Graphics,tile.Item.ImagePath==null?"画像なし":images.ContainsKey(tile.Item.ImagePath)?"表示不可":"読込中",subFont,picture,Color.SlateGray,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
    }
    int left=picture.Right+10,textWidth=Math.Max(40,bounds.Right-left-10);
    if(ShowVehicleLocation){
     TextRenderer.DrawText(e.Graphics,tile.Item.Title,headingFont,new Rectangle(left,bounds.Y+8,textWidth,30),Color.FromArgb(27,48,77),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
     TextRenderer.DrawText(e.Graphics,Path.GetFileName(tile.Item.Entry.Path),subFont,new Rectangle(left,bounds.Y+42,textWidth,18),Color.FromArgb(93,108,131),TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
     string folder=tile.Item.DisplayFolder;
     if(String.IsNullOrWhiteSpace(folder))folder=Path.GetDirectoryName(tile.Item.Entry.Path);
     TextRenderer.DrawText(e.Graphics,"保存場所: "+folder,subFont,new Rectangle(left,bounds.Y+64,textWidth,47),Color.FromArgb(83,101,128),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
    }else{
     TextRenderer.DrawText(e.Graphics,tile.Item.Title,headingFont,new Rectangle(left,bounds.Y+10,textWidth,39),Color.FromArgb(27,48,77),TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
     TextRenderer.DrawText(e.Graphics,Path.GetFileName(tile.Item.Entry.Path),subFont,new Rectangle(left,bounds.Y+53,textWidth,18),Color.FromArgb(93,108,131),TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
     if(!String.IsNullOrEmpty(tile.Item.DisplayFolder))TextRenderer.DrawText(e.Graphics,tile.Item.DisplayFolder,subFont,new Rectangle(left,bounds.Y+72,textWidth,17),Color.FromArgb(130,143,160),TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
    }
    string version=BadgeVersion(tile.Item);Color tagBg=version=="BVE5"?Color.FromArgb(230,239,255):version=="BVE6"?Color.FromArgb(226,245,237):version=="BVE5 / BVE6"?Color.FromArgb(239,231,251):Color.FromArgb(238,241,246);
    Color tagText=version=="BVE5"?Color.FromArgb(30,90,174):version=="BVE6"?Color.FromArgb(12,112,77):version=="BVE5 / BVE6"?Color.FromArgb(107,65,156):Color.FromArgb(105,118,137);
    int x=left;int y=bounds.Y+(ShowVehicleLocation?126:95);
    x+=DrawBadge(e.Graphics,tagFont,version,x,y,tagBg,tagText)+5;
    if(tile.Item.SupportHint=="車両をランダム選択"&&x+112<bounds.Right-4)DrawBadge(e.Graphics,subFont,"車両ランダム",x,y,Color.FromArgb(242,238,252),Color.FromArgb(112,77,166));
   }
  }
  if(items.Count==0)TextRenderer.DrawText(e.Graphics,emptyText,Font,new Rectangle(12,12,ClientSize.Width-24,40),Color.Gray,TextFormatFlags.WordBreak);
 }
 internal void SetThumbnailLoadingPaused(bool paused){
  if(thumbnailsPaused==paused)return;
  thumbnailsPaused=paused;
  if(paused){while(waiting.Count>0)pending.Remove(waiting.Dequeue());}
  else{Invalidate();Pump();}
 }
 Image Thumbnail(string path){if(path==null)return null;Image result;if(images.TryGetValue(path,out result))return result;if(thumbnailsPaused)return null;if(pending.Add(path)){waiting.Enqueue(path);Pump();}return null;}
 void Pump(){if(thumbnailsPaused)return;while(workers<2&&waiting.Count>0){string path=waiting.Dequeue();workers++;LoadThumbnail(path);}}
 async void LoadThumbnail(string path){
  Image result=null;
  try{
   var task=Task.Run(()=>{using(var source=Previews.Load(path)){var thumb=new Bitmap(96,96);using(var g=Graphics.FromImage(thumb)){g.Clear(Color.White);g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;float scale=Math.Min(96f/source.Width,96f/source.Height);int w=Math.Max(1,(int)(source.Width*scale)),h=Math.Max(1,(int)(source.Height*scale));g.DrawImage(source,new Rectangle((96-w)/2,(96-h)/2,w,h));}return (Image)thumb;}});
   if(await Task.WhenAny(task,Task.Delay(15000))==task)result=await task;
   else{Diagnostics.Write("THUMBNAIL_TIMEOUT",path);Task cleanup=task.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion&&t.Result!=null)t.Result.Dispose();else if(t.IsFaulted){var ignored=t.Exception;}},TaskScheduler.Default);}
  }catch(Exception ex){Diagnostics.Write("THUMBNAIL_ERROR",path+" : "+ex.Message);}
  finally{workers--;pending.Remove(path);}
  if(IsDisposed||Disposing){if(result!=null)result.Dispose();return;}
  images[path]=result;cacheOrder.Enqueue(path);while(cacheOrder.Count>192){string oldest=cacheOrder.Dequeue();Image old;if(images.TryGetValue(oldest,out old)){images.Remove(oldest);if(old!=null)old.Dispose();}}Invalidate();Pump();
 }
 Tile Hit(Point point){point.Offset(-AutoScrollPosition.X,-AutoScrollPosition.Y);return tiles.FirstOrDefault(x=>x.Bounds.Contains(point));}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){Focus();var tile=Hit(e.Location);if(tile!=null)SelectItem(tile.Item);}}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);var tile=Hit(e.Location);string text=tile==null?null:tile.Item.Title+Environment.NewLine+tile.Item.Entry.Path+Environment.NewLine+"表示: "+BadgeVersion(tile.Item)+Environment.NewLine+"対応版の表示は確認できた記載に基づきます。";if(text!=hovered){hovered=text;tooltip.SetToolTip(this,text);}}
 protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);int index=selected==null?-1:items.IndexOf(selected);int next=index;if(e.KeyCode==Keys.Down||e.KeyCode==Keys.Right)next=Math.Min(items.Count-1,index+1);else if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Left)next=Math.Max(0,index-1);else if(e.KeyCode==Keys.Home)next=0;else if(e.KeyCode==Keys.End)next=items.Count-1;else return;if(next>=0&&next<items.Count){SelectItem(items[next]);EnsureVisible(items[next]);}e.Handled=true;}
 protected override bool IsInputKey(Keys keyData){var key=keyData&Keys.KeyCode;return key==Keys.Up||key==Keys.Down||key==Keys.Left||key==Keys.Right||base.IsInputKey(keyData);}
 void EnsureVisible(CatalogItem item){var tile=tiles.FirstOrDefault(x=>x.Item==item);if(tile==null)return;int top=-AutoScrollPosition.Y;if(tile.Bounds.Top<top)AutoScrollPosition=new Point(0,tile.Bounds.Top);else if(tile.Bounds.Bottom>top+ClientSize.Height)AutoScrollPosition=new Point(0,Math.Max(0,tile.Bounds.Bottom-ClientSize.Height));}
 protected override void Dispose(bool disposing){if(disposing){tooltip.Dispose();foreach(var image in images.Values)if(image!=null)image.Dispose();images.Clear();waiting.Clear();}base.Dispose(disposing);}
}
