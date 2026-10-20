using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace ChengOrbit;
internal sealed class Program:System.Windows.Application
{
    private Configuration config=null!;
    private WheelWindow wheel=null!;
    private SettingsWindow? settings;
    private Forms.NotifyIcon tray=null!;
    private readonly Native.HookProc callback;
    private readonly Native.HookProc mouseCallback;
    private IntPtr hook;
    private IntPtr mouseHook;
    private bool swallowLeftUp;
    private bool paused;
    private readonly AltHoldState alt=new();
    private Mutex? mutex;
    private CancellationTokenSource? activation;
    private Program(){callback=KeyboardHook;mouseCallback=MouseHook;ShutdownMode=ShutdownMode.OnExplicitShutdown;}
    [STAThread] public static int Main()
    {
        var app=new Program();app.Run();return 0;
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);mutex=new Mutex(true,@"Local\ChengOrbit.Desktop",out bool created);if(!created){Shutdown();return;}
        config=Configuration.Load();try{config.Save();}catch{}
        wheel=new WheelWindow(config);wheel.SettingsRequested+=OpenSettings;
        wheel.BindingAllowed=()=>alt.IsHeld&&!alt.Cancelled;
        wheel.BindingRequested+=Bind;
        tray=new Forms.NotifyIcon{Icon=LoadIcon(),Text="澄——桌面轮盘 · 按住左 Alt",Visible=true};
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开设置",null,(_,_)=>Dispatcher.Invoke(OpenSettings));menu.Items.Add("预览轮盘（按 Esc 关闭）",null,(_,_)=>Dispatcher.Invoke(()=>wheel.Preview()));
        var pauseItem=new Forms.ToolStripMenuItem("暂停轮盘"){CheckOnClick=true};pauseItem.CheckedChanged+=(_,_)=>{paused=pauseItem.Checked;Cancel();tray.Text=paused?"澄——桌面轮盘 · 已暂停":"澄——桌面轮盘 · 按住左 Alt";};menu.Items.Add(pauseItem);menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,(_,_)=>Dispatcher.Invoke(()=>{Shutdown();}));tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(OpenSettings);
        hook=Native.SetWindowsHookEx(13,callback,Native.GetModuleHandle(null),0);
        if(hook==IntPtr.Zero){MessageBox.Show("无法注册全局键盘监听："+new Win32Exception(Marshal.GetLastWin32Error()).Message,"澄——桌面轮盘");Shutdown();return;}
        mouseHook=Native.SetWindowsHookEx(14,mouseCallback,Native.GetModuleHandle(null),0);
        if(mouseHook==IntPtr.Zero){MessageBox.Show("无法注册鼠标取消操作："+new Win32Exception(Marshal.GetLastWin32Error()).Message,"澄——桌面轮盘");Shutdown();return;}
        Microsoft.Win32.SystemEvents.SessionSwitch+=OnSessionSwitch;
        DispatcherUnhandledException+=(_,args)=>{Log(args.Exception);Cancel();tray.ShowBalloonTip(4000,"澄——桌面轮盘",args.Exception.Message,Forms.ToolTipIcon.Warning);args.Handled=true;};
        if(!e.Args.Contains("--background"))OpenSettings();
    }
    private static System.Drawing.Icon LoadIcon(){try{return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??System.Drawing.SystemIcons.Application;}catch{return System.Drawing.SystemIcons.Application;}}
    private void OpenSettings()
    {
        Cancel();wheel.HideWheel();
        if(settings==null){settings=new SettingsWindow(config,()=>wheel.RefreshPreview(),()=>wheel.Preview());settings.Closed+=(_,_)=>{settings=null;wheel.HideWheel();};}
        settings.Show();if(settings.WindowState==WindowState.Minimized)settings.WindowState=WindowState.Normal;settings.Activate();
    }
    private IntPtr KeyboardHook(int code,IntPtr wp,IntPtr lp)
    {
        if(code<0)return Native.CallNextHookEx(hook,code,wp,lp);
        var data=Marshal.PtrToStructure<Native.KeyData>(lp);
        if((data.Flags&0x10)!=0)return Native.CallNextHookEx(hook,code,wp,lp);
        bool down=wp.ToInt64() is 0x100 or 0x104,up=wp.ToInt64() is 0x101 or 0x105;
        if(down)activation?.Cancel();
        if(data.Key==0xA4)
        {
            if(down)
            {
                if(alt.IsHeld)return new IntPtr(1);
                // Leave combinations already in progress (Ctrl+Alt, Win+Alt, Shift+Alt) to Windows.
                if(paused||(Native.GetAsyncKeyState(0x11)&0x8000)!=0||(Native.GetAsyncKeyState(0x10)&0x8000)!=0||(Native.GetAsyncKeyState(0x5B)&0x8000)!=0||(Native.GetAsyncKeyState(0x5C)&0x8000)!=0||AppServices.IsFullscreen(config))return Native.CallNextHookEx(hook,code,wp,lp);
                alt.Begin();long session=alt.Id;Dispatcher.BeginInvoke(()=>{if(alt.IsCurrent(session)&&alt.IsHeld&&!alt.Cancelled)wheel.Open();});return new IntPtr(1);
            }
            if(up&&alt.IsHeld)
            {
                var release=alt.Release();
                Dispatcher.BeginInvoke(()=>{if(!alt.IsCurrent(release.Id)||alt.IsHeld)return;if(release.Commit)Commit();else wheel.HideWheel();});
                return release.ForwardUp?Native.CallNextHookEx(hook,code,wp,lp):new IntPtr(1);
            }
        }
        if(alt.IsHeld&&down&&!alt.Cancelled)
        {
            alt.CancelSelection(data.Key!=0x1B);long session=alt.Id;Dispatcher.BeginInvoke(()=>{if(alt.IsCurrent(session))wheel.HideWheel();});
            if(data.Key==0x1B)return new IntPtr(1);
            Native.ReplayAltAndKey(data.Key,data.Scan,(data.Flags&1)!=0);return new IntPtr(1);
        }
        if(down&&data.Key==0x1B&&wheel.IsVisible){Dispatcher.BeginInvoke(()=>wheel.HideWheel());return new IntPtr(1);}
        return Native.CallNextHookEx(hook,code,wp,lp);
    }
    private async void Commit()
    {
        if(!wheel.IsVisible)return;wheel.PollPointer();int selected=wheel.Selection;bool gear=wheel.SettingsSelected;wheel.HideWheel();
        if(wheel.BindingGesture)return;
        if(gear){OpenSettings();return;}if(selected<0||selected>=config.SlotCount||config.Apps[selected].IsEmpty)return;
        activation?.Cancel();activation?.Dispose();var request=new CancellationTokenSource();activation=request;
        try{await AppServices.ActivateAsync(config.Apps[selected],request.Token);}
        catch(OperationCanceledException){ }
        catch(Exception e){Log(e);tray.ShowBalloonTip(5000,"无法打开软件",e.Message,Forms.ToolTipIcon.Warning);}
        finally{if(ReferenceEquals(activation,request))activation=null;request.Dispose();}
    }
    private IntPtr MouseHook(int code,IntPtr wp,IntPtr lp)
    {
        if(code<0)return Native.CallNextHookEx(mouseHook,code,wp,lp);
        if(wp.ToInt64() is 0x201 or 0x204 or 0x207)activation?.Cancel();
        if(wp.ToInt64()==0x202&&swallowLeftUp){swallowLeftUp=false;return new IntPtr(1);}
        if(wp.ToInt64()==0x201&&alt.IsHeld&&!alt.Cancelled)
        {
            alt.CancelSelection(false);swallowLeftUp=true;long session=alt.Id;
            Dispatcher.BeginInvoke(()=>{if(alt.IsCurrent(session))wheel.HideWheel();},DispatcherPriority.Send);
            return new IntPtr(1);
        }
        return Native.CallNextHookEx(mouseHook,code,wp,lp);
    }
    private void Bind(int slot,string path)
    {
        try
        {
            var entry=AppServices.FromPath(path);config.Bind(slot,entry);
            alt.CancelSelection(false);wheel.HideWheel();wheel.RefreshPreview();
        }
        catch(Exception e){Log(e);wheel.ShowBindingError(e.Message);}
    }
    private void Cancel(){activation?.Cancel();alt.Reset();if(wheel!=null)wheel.HideWheel();}
    private void OnSessionSwitch(object sender,Microsoft.Win32.SessionSwitchEventArgs e)=>Dispatcher.BeginInvoke(Cancel);
    private static void Log(Exception error){try{Directory.CreateDirectory(Configuration.DirectoryPath);File.AppendAllText(Path.Combine(Configuration.DirectoryPath,"errors.log"),DateTimeOffset.Now+" "+error+Environment.NewLine);}catch{}}
    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.SessionSwitch-=OnSessionSwitch;
        activation?.Cancel();
        if(hook!=IntPtr.Zero)Native.UnhookWindowsHookEx(hook);if(mouseHook!=IntPtr.Zero)Native.UnhookWindowsHookEx(mouseHook);if(tray!=null){tray.Visible=false;tray.Dispose();}if(wheel!=null)wheel.Close();settings?.Close();mutex?.Dispose();base.OnExit(e);
    }
}
