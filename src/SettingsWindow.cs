using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ChengOrbit;
internal sealed class SettingsWindow:Window
{
    private readonly Configuration config;
    private readonly Action changed;
    private readonly Action preview;
    private readonly StackPanel body=new();
    private readonly TextBlock status=new(){FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(155,64,64)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
    private bool building;
    public SettingsWindow(Configuration c,Action change,Action showPreview)
    {
        config=c;changed=change;preview=showPreview;Title="澄——桌面轮盘 · 设置";Width=680;Height=850;MinWidth=560;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=new LinearGradientBrush(Color.FromRgb(229,242,247),Color.FromRgb(246,245,238),50);FontFamily=new FontFamily("Microsoft YaHei UI");Foreground=new SolidColorBrush(Color.FromRgb(38,66,83));FontSize=13;
        var style=new Style(typeof(Button));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,8,12,8)));style.Setters.Add(new Setter(Control.MarginProperty,new Thickness(0,0,7,0)));style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromArgb(220,255,255,255))));style.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(210,227,237))));style.Setters.Add(new Setter(Control.CursorProperty,System.Windows.Input.Cursors.Hand));
        var template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border x:Name='surface' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' CornerRadius='10' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#E3F3FE'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Background' Value='#C6E5FA'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#387FAB'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");style.Setters.Add(new Setter(Control.TemplateProperty,template));Resources.Add(typeof(Button),style);
        var outer=new DockPanel{Margin=new Thickness(26),Background=Background};Content=outer;
        var top=new StackPanel{Margin=new Thickness(0,0,0,22)};DockPanel.SetDock(top,Dock.Top);outer.Children.Add(top);top.Children.Add(new TextBlock{Text="Cheng Radial Menu",FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(97,129,145))});top.Children.Add(new TextBlock{Text="澄——桌面轮盘",FontSize=27,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,5,0,8)});top.Children.Add(new TextBlock{Text="按住左 Alt 选择软件，松开执行；新按下鼠标左键取消。",FontSize=12});
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=body};outer.Children.Add(scroll);Build();
        KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)Close();};
    }
    private StackPanel Card(string heading,string? description=null)
    {
        var inner=new StackPanel{Margin=new Thickness(17)};body.Children.Add(new Border{Background=new SolidColorBrush(Color.FromArgb(175,255,255,255)),BorderBrush=new SolidColorBrush(Color.FromArgb(235,255,255,255)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Margin=new Thickness(0,0,10,16),Child=inner});
        inner.Children.Add(new TextBlock{Text=heading,FontSize=15,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,description==null?12:5)});
        if(description!=null)inner.Children.Add(new TextBlock{Text=description,FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(99,124,139)),Margin=new Thickness(0,0,0,12),TextWrapping=TextWrapping.Wrap});return inner;
    }
    private void Save()
    {
        if(building)return;
        try{config.Save();changed();status.Text="";}catch(Exception e){status.Text="保存失败："+e.Message;}
    }
    private Button Button(string label,Action action){var b=new Button{Content=label};b.Click+=(_,_)=>action();return b;}
    private void Build()
    {
        building=true;body.Children.Clear();body.Children.Add(status);
        var colors=Card("选中高光","RGB 和强度实时生效；预览轮盘可直接看到配色。");
        var swatch=new Border{Height=36,CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,0,12)};colors.Children.Add(swatch);
        void Swatch(){swatch.Background=new SolidColorBrush(config.Highlight);}
        var rgb=new Grid{Margin=new Thickness(0,0,0,10)};colors.Children.Add(rgb);for(int i=0;i<3;i++)rgb.ColumnDefinitions.Add(new ColumnDefinition());
        var fields=new TextBox[3];var channelNames=new[]{"R · 红色","G · 绿色","B · 蓝色"};var current=new[]{config.R,config.G,config.B};
        for(int i=0;i<3;i++)
        {
            int index=i;var col=new StackPanel{Margin=new Thickness(0,0,10,0)};Grid.SetColumn(col,i);rgb.Children.Add(col);col.Children.Add(new TextBlock{Text=channelNames[i],FontSize=11,Margin=new Thickness(0,0,0,5)});
            var input=new TextBox{Text=current[i].ToString(),Padding=new Thickness(10,7,10,7),BorderBrush=new SolidColorBrush(Color.FromRgb(211,227,236))};AutomationProperties.SetName(input,channelNames[i]+"，0 到 255");col.Children.Add(input);fields[i]=input;
            input.TextChanged+=(_,_)=>{if(building||!int.TryParse(input.Text,out int value))return;value=Math.Clamp(value,0,255);if(index==0)config.R=value;else if(index==1)config.G=value;else config.B=value;Swatch();Save();};
            input.LostKeyboardFocus+=(_,_)=>input.Text=(index==0?config.R:index==1?config.G:config.B).ToString();
        }
        Swatch();var colorButtons=new WrapPanel{Margin=new Thickness(0,0,0,12)};colors.Children.Add(colorButtons);
        colorButtons.Children.Add(Button("选择颜色…",()=>{using var dialog=new System.Windows.Forms.ColorDialog{Color=System.Drawing.Color.FromArgb(config.R,config.G,config.B),FullOpen=true};if(dialog.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;config.R=dialog.Color.R;config.G=dialog.Color.G;config.B=dialog.Color.B;building=true;fields[0].Text=config.R.ToString();fields[1].Text=config.G.ToString();fields[2].Text=config.B.ToString();building=false;Swatch();Save();}));
        colorButtons.Children.Add(Button("预览轮盘",preview));
        Slider(colors,"高光强度",0,100,config.Strength,"%",v=>config.Strength=v);
        var motion=Card("玻璃与物理","鼠标所在侧下沉，另一侧抬起。回到中心取消，轮盘自然回正。");
        Slider(motion,"轮盘格子数量",Configuration.MinSlotCount,Configuration.MaxSlotCount,config.SlotCount," 格",v=>config.SlotCount=v);
        motion.Children.Add(new TextBlock{Text="减少格子只隐藏后面的软件，增加回来即可恢复。",FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(99,124,139)),Margin=new Thickness(0,0,0,14)});
        Slider(motion,"轮盘大小",300,500,config.WheelSize," px",v=>config.WheelSize=v);
        Slider(motion,"倾斜幅度",4,22,config.Tilt,"°",v=>config.Tilt=v);
        Slider(motion,"回弹感",0,100,config.Elasticity,"",v=>config.Elasticity=v);
        Slider(motion,"扇形展开",8,38,config.Expansion,"°",v=>config.Expansion=v);
        Check(motion,"减弱动态效果",config.ReduceMotion,v=>config.ReduceMotion=v);
        var system=Card("运行与快捷键","绑定：先拖动程序或快捷方式，再按住左 Alt，拖到扇形后松开鼠标。快捷方式会绑定其目标程序；拖入已有位置将替换绑定。");
        var startup=new CheckBox{Content="登录 Windows 时启动",IsChecked=StartupEnabled(),Margin=new Thickness(0,3,0,12)};system.Children.Add(startup);startup.Click+=(_,_)=>{try{using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(startup.IsChecked==true)key.SetValue("ChengOrbit","\""+Environment.ProcessPath+"\" --background");else key.DeleteValue("ChengOrbit",false);}catch(Exception e){status.Text=e.Message;startup.IsChecked=StartupEnabled();}};
        Check(system,"全屏软件中暂停轮盘",config.PauseFullscreen,v=>config.PauseFullscreen=v);
        system.Children.Add(new TextBlock{Text="全屏例外（进程名，逗号分隔，例如 Code, msedge）",FontSize=11,Margin=new Thickness(0,6,0,5)});
        var exceptions=new TextBox{Text=config.FullscreenExceptions,Padding=new Thickness(8),BorderBrush=new SolidColorBrush(Color.FromRgb(211,227,236))};AutomationProperties.SetName(exceptions,"全屏软件例外进程名");system.Children.Add(exceptions);exceptions.LostKeyboardFocus+=(_,_)=>{config.FullscreenExceptions=exceptions.Text;Save();};
        var bottom=new WrapPanel{Margin=new Thickness(0,0,0,14)};body.Children.Add(bottom);bottom.Children.Add(Button("恢复视觉默认值",()=>{config.SlotCount=Configuration.DefaultSlotCount;config.R=50;config.G=160;config.B=255;config.Strength=85;config.WheelSize=410;config.Tilt=12;config.Elasticity=45;config.Expansion=24;config.ReduceMotion=false;Save();Build();}));bottom.Children.Add(Button("打开配置目录",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",Configuration.DirectoryPath){UseShellExecute=true})));
        body.Children.Add(new TextBlock{Text="Cheng Radial Menu 0.1.7  ·  关闭此窗口后继续在托盘运行。\n玻璃背景在呼出时采样，不上传屏幕或配置。",FontSize=10,Foreground=new SolidColorBrush(Color.FromRgb(108,131,144)),Margin=new Thickness(0,0,0,12)});building=false;
    }
    private void Slider(Panel parent,string label,int min,int max,int value,string suffix,Action<int> setter)
    {
        var group=new StackPanel{Margin=new Thickness(0,0,0,13)};parent.Children.Add(group);var text=new TextBlock{Text=label+"   "+value+suffix,FontSize=12};group.Children.Add(text);var slider=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new Thickness(0,8,0,0)};AutomationProperties.SetName(slider,label);group.Children.Add(slider);slider.ValueChanged+=(_,e)=>{setter((int)e.NewValue);text.Text=label+"   "+(int)e.NewValue+suffix;Save();};
    }
    private void Check(Panel parent,string label,bool value,Action<bool> setter)
    {
        var box=new CheckBox{Content=label,IsChecked=value,Margin=new Thickness(0,3,0,12)};parent.Children.Add(box);box.Click+=(_,_)=>{setter(box.IsChecked==true);Save();};
    }
    private static bool StartupEnabled(){using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");return key?.GetValue("ChengOrbit")!=null;}
}
