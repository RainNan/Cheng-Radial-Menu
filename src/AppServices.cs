using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChengOrbit;
internal static class AppServices
{
    private static readonly Dictionary<string,ImageSource?> IconCache=new(StringComparer.OrdinalIgnoreCase);
    public static ImageSource? IconFor(string path)
    {
        if(string.IsNullOrWhiteSpace(path))return null;
        if(IconCache.TryGetValue(path,out var cached))return cached;
        try {using var icon=System.Drawing.Icon.ExtractAssociatedIcon(path);if(icon!=null){var image=Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(64,64));image.Freeze();return IconCache[path]=image;}}
        catch(Exception) { }
        return IconCache[path]=null;
    }
    public static AppEntry FromPath(string path)
    {
        path=Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        string name=Path.GetFileNameWithoutExtension(path),arguments="",workingDirectory="";
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase))
        {
            if(!File.Exists(path))throw new FileNotFoundException("快捷方式已失效。",path);
            if(!visited.Add(path)||visited.Count>16)throw new InvalidOperationException("快捷方式相互指向，无法找到目标程序。");
            object? shell=null,link=null;
            try
            {
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);dynamic s=shell!;link=s.CreateShortcut(path);dynamic l=link;
                string target=l.TargetPath,args=l.Arguments,dir=l.WorkingDirectory;
                if(string.IsNullOrWhiteSpace(target))throw new InvalidOperationException("此快捷方式没有可绑定的本地目标程序。");
                arguments=string.IsNullOrWhiteSpace(args)?arguments:args+(string.IsNullOrWhiteSpace(arguments)?"":" "+arguments);
                if(string.IsNullOrWhiteSpace(workingDirectory)&&!string.IsNullOrWhiteSpace(dir))workingDirectory=Environment.ExpandEnvironmentVariables(dir);
                target=Environment.ExpandEnvironmentVariables(target);path=Path.GetFullPath(Path.IsPathRooted(target)?target:Path.Combine(Path.GetDirectoryName(path)!,target));
            }
            finally {if(link!=null)Marshal.FinalReleaseComObject(link);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
        }
        if(!Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请拖入 .exe 程序，或指向程序的 .lnk 快捷方式。");
        if(!File.Exists(path))throw new FileNotFoundException("找不到快捷方式指向的目标程序。",path);
        return new AppEntry{Name=name,Path=path,ProcessName=Path.GetFileNameWithoutExtension(path),Arguments=arguments,WorkingDirectory=workingDirectory};
    }
    public static bool IsFullscreen(Configuration config)
    {
        if(!config.PauseFullscreen)return false;
        var hwnd=Native.GetForegroundWindow();if(hwnd==IntPtr.Zero)return false;
        if(Native.ClassName(hwnd) is "Progman" or "WorkerW" or "Shell_TrayWnd")return false;
        Native.GetWindowThreadProcessId(hwnd,out var pid);
        if(pid==Environment.ProcessId)return false;
        try {using var p=Process.GetProcessById((int)pid);if(config.FullscreenExceptions.Split(new[]{',','，',';','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Any(x=>string.Equals(Path.GetFileNameWithoutExtension(x),p.ProcessName,StringComparison.OrdinalIgnoreCase)))return false;}catch{}
        var info=new Native.MonitorInfo{Size=Marshal.SizeOf<Native.MonitorInfo>()};
        if(!Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd,2),ref info)||!Native.GetWindowRect(hwnd,out var r))return false;
        return r.Left<=info.Monitor.Left&&r.Top<=info.Monitor.Top&&r.Right>=info.Monitor.Right&&r.Bottom>=info.Monitor.Bottom;
    }
    internal static IntPtr FindWindow(AppEntry app)
    {
        IntPtr found=IntPtr.Zero;
        // EnumWindows returns top-level windows in Z order: first match is the most recent window.
        Native.EnumWindows((hwnd,_)=>
        {
            if(!Native.IsWindowVisible(hwnd)||Native.GetWindow(hwnd,4)!=IntPtr.Zero||Native.GetWindowTextLength(hwnd)==0)return true;
            if(Native.DwmGetWindowAttribute(hwnd,14,out int cloaked,4)==0&&cloaked!=0)return true;
            var cls=Native.ClassName(hwnd);
            if(app.ProcessName.Equals("explorer",StringComparison.OrdinalIgnoreCase)&&cls!="CabinetWClass")return true;
            Native.GetWindowThreadProcessId(hwnd,out var pid);
            try
            {
                bool Matches(uint id){try{using var process=Process.GetProcessById((int)id);return string.Equals(process.ProcessName,app.ProcessName,StringComparison.OrdinalIgnoreCase)||(app.ProcessName=="CalculatorApp"&&process.ProcessName.Equals("Calculator",StringComparison.OrdinalIgnoreCase));}catch{return false;}}
                bool matches=Matches(pid);
                if(!matches&&cls=="ApplicationFrameWindow")Native.EnumChildWindows(hwnd,(child,_)=>{Native.GetWindowThreadProcessId(child,out uint childPid);if(Matches(childPid)){matches=true;return false;}return true;},IntPtr.Zero);
                if(!matches)return true;found=hwnd;return false;
            }catch{return true;}
        },IntPtr.Zero);
        return found;
    }
    public static async Task ActivateAsync(AppEntry app,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        if(app.IsEmpty)return;
        var found=FindWindow(app);
        if(found!=IntPtr.Zero)
        {
            if(!await ForegroundActivator.ActivateAsync(found,cancellation))throw new InvalidOperationException("窗口未能切到前台，请检查目标软件是否正在等待响应或以管理员权限运行。");
            return;
        }
        if(!File.Exists(app.Path))throw new FileNotFoundException("软件已移动或删除，请重新拖入该扇形绑定。",app.Path);
        using var process=Process.Start(new ProcessStartInfo(app.Path){UseShellExecute=true,Arguments=app.Arguments,WorkingDirectory=Directory.Exists(app.WorkingDirectory)?app.WorkingDirectory:Path.GetDirectoryName(app.Path)??""});
        if(process!=null)Native.AllowSetForegroundWindow((uint)process.Id);
        for(int attempt=0;attempt<30;attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            found=FindWindow(app);
            if(found!=IntPtr.Zero)
            {
                if(!await ForegroundActivator.ActivateAsync(found,cancellation))throw new InvalidOperationException("软件已打开，但窗口未能切到前台。");
                return;
            }
            await Task.Delay(100,cancellation);
        }
    }
}
