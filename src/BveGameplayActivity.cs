using System;
using System.Diagnostics;
using System.Windows.Forms;

// Passive, UI-thread-only observation. No modification to BVE or OS priorities.
internal sealed class BveGameplayActivity : IDisposable {
 readonly Timer timer = new Timer { Interval = 3000 };
 bool started;
 bool running;
 public bool IsRunning { get { return running; } }
 public event Action<bool> Changed;

 public BveGameplayActivity(){
  timer.Tick += (s,e) => Check();
 }
 public void Start(){
  if(started)return;
  started=true;
  Check();
  timer.Start();
 }
 public void Check(){
  Process[] processes;
  try{processes=Process.GetProcessesByName("BveTs");}
  catch(Exception ex){
   Diagnostics.Write("GAME_ACTIVITY_CHECK",ex.Message);
   return;
  }
  bool active=processes.Length>0;
  foreach(var process in processes)process.Dispose();
  if(active==running)return;
  running=active;
  if(Changed!=null)Changed(active);
 }
 public void Dispose(){timer.Stop();timer.Dispose();}
}