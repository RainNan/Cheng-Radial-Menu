using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChengOrbit;
internal static class ForegroundActivator
{
    internal static IntPtr ActivePopup(IntPtr window)
    {
        var target=window;
        for(int i=0;i<8;i++)
        {
            var popup=Native.GetLastActivePopup(target);
            if(popup==IntPtr.Zero||popup==target||!Native.IsWindowVisible(popup))break;
            target=popup;
        }
        return target;
    }
    internal static bool IsForeground(IntPtr window)
    {
        if(!Native.IsWindow(window)||Native.IsIconic(window))return false;
        var foreground=Native.GetForegroundWindow();
        return foreground!=IntPtr.Zero&&(foreground==window||Native.GetAncestor(foreground,3)==Native.GetAncestor(window,3));
    }
    internal static void RequestForeground(IntPtr window)
    {
        if(!Native.IsWindow(window)||Native.IsHungAppWindow(window))return;
        uint current=Native.GetCurrentThreadId();
        uint foreground=Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out _);
        uint target=Native.GetWindowThreadProcessId(window,out _);
        bool attachedForeground=false,attachedTarget=false;
        try
        {
            if(foreground!=0&&foreground!=current)attachedForeground=Native.AttachThreadInput(current,foreground,true);
            if(target!=0&&target!=current&&target!=foreground)attachedTarget=Native.AttachThreadInput(current,target,true);
            // Restore only minimized windows; preserve an existing maximized state.
            if(Native.IsIconic(window))Native.ShowWindowAsync(window,9);
            Native.SetWindowPos(window,IntPtr.Zero,0,0,0,0,0x1|0x2|0x10|0x40|0x4000);
            Native.BringWindowToTop(window);
            Native.SetForegroundWindow(window);
            Native.SetActiveWindow(window);
        }
        finally
        {
            // Never leave applications sharing an input queue after switching.
            if(attachedTarget)Native.AttachThreadInput(current,target,false);
            if(attachedForeground)Native.AttachThreadInput(current,foreground,false);
        }
    }
    public static async Task<bool> ActivateAsync(IntPtr main,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        if(!Native.IsWindow(main))return false;
        if(Native.IsIconic(main))Native.ShowWindowAsync(main,9);
        for(int attempt=0;attempt<4;attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            if(!Native.IsWindow(main))return false;
            var target=ActivePopup(main);
            if(IsForeground(target)&&!Native.IsIconic(main))return true;
            RequestForeground(target);
            // Restore/focus messages are processed by the destination application's
            // thread. Check the actual foreground HWND after it has had time to run.
            await Task.Delay(attempt==0?45:70,cancellation);
            if(IsForeground(ActivePopup(main))&&!Native.IsIconic(main))return true;
        }
        return false;
    }
}
