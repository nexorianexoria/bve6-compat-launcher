using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
internal static class UiDrawing {
    internal static GraphicsPath Round(RectangleF r,float radius){var p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    internal static Bitmap Train(){var image=new Bitmap(28,28);using(var g=Graphics.FromImage(image)){g.SmoothingMode=SmoothingMode.AntiAlias;using(var brush=new SolidBrush(Color.FromArgb(24,73,128)))using(var body=Round(new RectangleF(5,2,18,21),4))g.FillPath(brush,body);g.FillRectangle(Brushes.White,8,6,12,8);g.FillEllipse(Brushes.White,8,17,3,3);g.FillEllipse(Brushes.White,17,17,3,3);using(var pen=new Pen(Color.FromArgb(24,73,128),2)){g.DrawLine(pen,10,22,6,27);g.DrawLine(pen,18,22,22,27);}}return image;}
}
internal sealed class SearchField:Panel {
    public SearchField(TextBox box,string label){DoubleBuffered=true;BackColor=Color.White;Padding=new Padding(30,6,3,3);box.Dock=DockStyle.Fill;box.BorderStyle=BorderStyle.None;var clear=new Button{Text="×",AccessibleName=label+"を消す",Dock=DockStyle.Right,Width=26,FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Color.SlateGray,Cursor=Cursors.Hand,Visible=box.TextLength>0};clear.FlatAppearance.BorderSize=0;clear.Click+=(s,e)=>{box.Clear();box.Focus();};box.TextChanged+=(s,e)=>clear.Visible=box.TextLength>0;Controls.Add(box);Controls.Add(clear);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var pen=new Pen(Color.FromArgb(201,215,232)))using(var path=UiDrawing.Round(new RectangleF(0.5f,0.5f,Width-1,Height-1),6))e.Graphics.DrawPath(pen,path);using(var pen=new Pen(Color.FromArgb(32,56,93),1.5f)){int y=(Height-12)/2;e.Graphics.DrawEllipse(pen,10,y,10,10);e.Graphics.DrawLine(pen,18,y+8,23,y+13);}}
}
internal sealed class DetailTextView:Control {
    readonly ToolTip tooltip=new ToolTip();readonly ContextMenuStrip menu=new ContextMenuStrip();
    public DetailTextView(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);TabStop=true;BackColor=Color.White;menu.Items.Add("情報をコピー",null,(s,e)=>Copy());ContextMenuStrip=menu;}
    protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);tooltip.SetToolTip(this,Text);AccessibleName=Text;Invalidate();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);string[] lines=(Text??"").Replace("\r","").Split('\n');using(var bold=new Font(Font.FontFamily,11,FontStyle.Bold))using(var small=new Font(Font.FontFamily,9)){
        if(lines.Length>0)TextRenderer.DrawText(e.Graphics,lines[0],bold,new Rectangle(0,2,Width,24),Color.FromArgb(16,44,82),TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);
        for(int i=1;i<lines.Length&&i<4;i++)TextRenderer.DrawText(e.Graphics,lines[i],small,new Rectangle(0,24+(i-1)*20,Width,20),Color.FromArgb(98,112,136),TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter);
        }if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,ClientRectangle);
    }
    void Copy(){try{if(!String.IsNullOrEmpty(Text))Clipboard.SetText(Text);}catch(System.Runtime.InteropServices.ExternalException){}}
    protected override void OnKeyDown(KeyEventArgs e){if(e.Control&&e.KeyCode==Keys.C){Copy();e.Handled=true;}base.OnKeyDown(e);}
    protected override void Dispose(bool disposing){if(disposing){tooltip.Dispose();menu.Dispose();}base.Dispose(disposing);}
}
internal sealed class ModernButton:Button {
    public string Symbol {get;set;}
    public bool Emphasized {get;set;}
    bool hovering;
    public ModernButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Margin=new Padding(3);}
    protected override void OnMouseEnter(EventArgs e){hovering=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovering=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent==null?SystemColors.Control:Parent.BackColor);}
    protected override void OnPaint(PaintEventArgs e){
        // Clear the complete client area before painting the rounded shape. The native
        // button background can otherwise leave stale pixels at the corners and edges.
        e.Graphics.Clear(Parent==null?SystemColors.Control:Parent.BackColor);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color fill=Emphasized?BackColor:Color.White;
        if(!Enabled)fill=Color.FromArgb(219,226,234);else if(hovering)fill=Emphasized?ControlPaint.Light(BackColor,0.1f):Color.FromArgb(240,247,255);
        using(var path=UiDrawing.Round(new RectangleF(0.5f,0.5f,Math.Max(1,Width-1),Math.Max(1,Height-1)),5))using(var brush=new SolidBrush(fill)){e.Graphics.FillPath(brush,path);if(!Emphasized)using(var pen=new Pen(Color.FromArgb(203,216,231)))e.Graphics.DrawPath(pen,path);}
        Color textColor=!Enabled?Color.Gray:Emphasized?Color.White:Color.FromArgb(21,48,88);
        var textSize=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);bool icon=!String.IsNullOrEmpty(Symbol);
        int total=textSize.Width+(icon?25:0);int left=Math.Max(7,(Width-total)/2);
        if(icon)DrawSymbol(e.Graphics,Symbol,new Rectangle(left,(Height-18)/2,18,18),!Enabled?Color.Gray:Emphasized?Color.White:Color.FromArgb(17,104,209));
        var textRect=new Rectangle(left+(icon?25:0),0,Math.Max(0,Width-left-(icon?25:0)-5),Height);
        TextRenderer.DrawText(e.Graphics,Text,Font,textRect,textColor,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(4,4,Math.Max(1,Width-8),Math.Max(1,Height-8)));
    }
    static void DrawSymbol(Graphics g,string symbol,Rectangle r,Color color){
        using(var p=new Pen(color,1.8f))using(var b=new SolidBrush(color)){
            p.LineJoin=LineJoin.Round;p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;int x=r.X,y=r.Y;
            if(symbol=="play")g.FillPolygon(b,new[]{new Point(x+4,y+1),new Point(x+4,y+17),new Point(x+16,y+9)});
            else if(symbol=="folder"){g.DrawLines(p,new[]{new Point(x+1,y+5),new Point(x+1,y+15),new Point(x+17,y+15),new Point(x+17,y+6),new Point(x+8,y+6),new Point(x+6,y+3),new Point(x+1,y+3),new Point(x+1,y+5)});}
            else if(symbol=="refresh"){g.DrawArc(p,x+2,y+2,13,13,30,285);g.FillPolygon(b,new[]{new Point(x+12,y+1),new Point(x+17,y+4),new Point(x+12,y+7)});}
            else if(symbol=="document"||symbol=="log"){g.DrawLines(p,new[]{new Point(x+4,y+1),new Point(x+12,y+1),new Point(x+16,y+5),new Point(x+16,y+17),new Point(x+4,y+17),new Point(x+4,y+1)});g.DrawLine(p,x+11,y+2,x+11,y+6);g.DrawLine(p,x+11,y+6,x+15,y+6);if(symbol=="log"){g.DrawLine(p,x+7,y+10,x+13,y+10);g.DrawLine(p,x+7,y+13,x+12,y+13);}}
            else if(symbol=="image"){g.DrawRectangle(p,x+1,y+3,16,12);g.DrawEllipse(p,x+4,y+5,3,3);g.DrawLines(p,new[]{new Point(x+3,y+13),new Point(x+8,y+8),new Point(x+12,y+12),new Point(x+14,y+10),new Point(x+16,y+13)});}
            else if(symbol=="dots"){for(int i=0;i<3;i++)g.FillEllipse(b,x+3+i*5,y+8,2.5f,2.5f);}
        }
    }
}
