using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal class ModernWindow:Form {
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    public ModernWindow(){FormBorderStyle=FormBorderStyle.None;Padding=new Padding(1);}
    protected void Caption(Panel header){
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Right,Width=144,FlowDirection=FlowDirection.LeftToRight,WrapContents=false,Margin=new Padding(0)};
        string[] names={"—","□","×"};string[] labels={"最小化","最大化・元に戻す","閉じる"};
        for(int i=0;i<3;i++){int action=i;var button=new Button{Text=names[i],AccessibleName=labels[i],FlatStyle=FlatStyle.Flat,Size=new Size(46,36),Margin=new Padding(0),ForeColor=Color.FromArgb(21,47,80),TabStop=true};button.FlatAppearance.BorderSize=0;button.Click+=(s,e)=>{if(action==0)WindowState=FormWindowState.Minimized;else if(action==1)ToggleMaximize();else Close();};buttons.Controls.Add(button);}header.Controls.Add(buttons);buttons.BringToFront();
        MouseEventHandler drag=(s,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};
        header.MouseDown+=drag;header.DoubleClick+=(s,e)=>ToggleMaximize();foreach(Control item in header.Controls)if(item is Label||item is PictureBox)item.MouseDown+=drag;
    }
    void ToggleMaximize(){MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    protected override void WndProc(ref Message m){
        base.WndProc(ref m);if(m.Msg!=0x84||WindowState==FormWindowState.Maximized)return;
        var p=PointToClient(new Point(unchecked((short)(long)m.LParam),unchecked((short)((long)m.LParam>>16))));int grip=6;bool l=p.X<grip,r=p.X>=ClientSize.Width-grip,t=p.Y<grip,b=p.Y>=ClientSize.Height-grip;
        if(t&&l)m.Result=new IntPtr(13);else if(t&&r)m.Result=new IntPtr(14);else if(b&&l)m.Result=new IntPtr(16);else if(b&&r)m.Result=new IntPtr(17);else if(l)m.Result=new IntPtr(10);else if(r)m.Result=new IntPtr(11);else if(t)m.Result=new IntPtr(12);else if(b)m.Result=new IntPtr(15);
    }
}
