using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace ChengOrbit;
public sealed class AppEntry
{
    public string Name { get; set; }="";
    public string Path { get; set; }="";
    public string ProcessName { get; set; }="";
    public string Arguments { get; set; }="";
    public string WorkingDirectory { get; set; }="";
    [JsonIgnore] public bool IsEmpty=>string.IsNullOrWhiteSpace(Path);
    public override string ToString()=>Name;
}
public sealed class Configuration
{
    public const int MinSlotCount=3;
    public const int MaxSlotCount=12;
    public const int DefaultSlotCount=8;
    public int SlotCount { get; set; }=DefaultSlotCount;
    public int Tilt { get; set; }=12;
    public int Elasticity { get; set; }=45;
    public int Expansion { get; set; }=24;
    public int R { get; set; }=50;
    public int G { get; set; }=160;
    public int B { get; set; }=255;
    public int Strength { get; set; }=85;
    public int WheelSize { get; set; }=410;
    public bool PauseFullscreen { get; set; }=true;
    public bool ReduceMotion { get; set; }
    public string FullscreenExceptions { get; set; }="";
    public List<AppEntry> Apps { get; set; }=DefaultApps();
    public static readonly string DirectoryPath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ChengOrbit");
    public static readonly string FilePath=System.IO.Path.Combine(DirectoryPath,"settings.json");
    public static List<AppEntry> DefaultApps()
    {
        string win=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var apps=new List<AppEntry>{new(){Name="文件资源管理器",Path=System.IO.Path.Combine(win,"explorer.exe"),ProcessName="explorer"},new(){Name="记事本",Path=System.IO.Path.Combine(win,"System32","notepad.exe"),ProcessName="notepad"},new(){Name="计算器",Path=System.IO.Path.Combine(win,"System32","calc.exe"),ProcessName="CalculatorApp"},new(){Name="终端",Path=System.IO.Path.Combine(win,"System32","WindowsPowerShell","v1.0","powershell.exe"),ProcessName="powershell"},new(){Name="任务管理器",Path=System.IO.Path.Combine(win,"System32","Taskmgr.exe"),ProcessName="Taskmgr"},new(){Name="画图",Path=System.IO.Path.Combine(win,"System32","mspaint.exe"),ProcessName="mspaint"}};
        var edge=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe");
        if(File.Exists(edge))apps.Insert(0,new(){Name="Microsoft Edge",Path=edge,ProcessName="msedge"});
        var slots=apps.Where(a=>File.Exists(a.Path)).Take(MaxSlotCount).ToList();while(slots.Count<MaxSlotCount)slots.Add(new());return slots;
    }
    public static Configuration Load()
    {
        try
        {
            var c=JsonSerializer.Deserialize<Configuration>(File.ReadAllText(FilePath))??new();c.Clamp();
            for(int i=0;i<c.Apps.Count;i++)if(!c.Apps[i].IsEmpty&&System.IO.Path.GetExtension(c.Apps[i].Path).Equals(".lnk",StringComparison.OrdinalIgnoreCase))try{var resolved=AppServices.FromPath(c.Apps[i].Path);if(!string.IsNullOrWhiteSpace(c.Apps[i].Name))resolved.Name=c.Apps[i].Name;c.Apps[i]=resolved;}catch{ }
            return c;
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException) {return new();}
    }
    public void Clamp()
    {
        SlotCount=Math.Clamp(SlotCount,MinSlotCount,MaxSlotCount);
        Tilt=Math.Clamp(Tilt,4,22);Elasticity=Math.Clamp(Elasticity,0,100);Expansion=Math.Clamp(Expansion,8,38);
        R=Math.Clamp(R,0,255);G=Math.Clamp(G,0,255);B=Math.Clamp(B,0,255);Strength=Math.Clamp(Strength,0,100);WheelSize=Math.Clamp(WheelSize,300,500);
        Apps=(Apps??new()).Take(MaxSlotCount).Select(a=>a??new AppEntry()).ToList();while(Apps.Count<MaxSlotCount)Apps.Add(new());
        foreach(var app in Apps){app.Name??="";app.Path??="";app.ProcessName??="";app.Arguments??="";app.WorkingDirectory??="";}
        FullscreenExceptions??="";
    }
    public void Save()
    {
        Clamp();Directory.CreateDirectory(DirectoryPath);
        var temp=FilePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,FilePath,true);
    }
    public Color Highlight=>Color.FromRgb((byte)R,(byte)G,(byte)B);
    public void Bind(int slot,AppEntry app)
    {
        if(slot<0||slot>=SlotCount)throw new ArgumentOutOfRangeException(nameof(slot));
        if(app.IsEmpty)throw new ArgumentException("请选择一个有效的软件。");
        Clamp();var previous=Apps[slot];Apps[slot]=app;
        try{Save();}catch{Apps[slot]=previous;throw;}
    }
}
internal sealed class Spring
{
    public double Value,Velocity,Target;
    public Spring(double initial=0) {Value=Target=initial;}
    public void Step(double dt,double stiffness=190,double damping=23)
    {
        dt=Math.Clamp(dt,0,.05);int steps=Math.Max(1,(int)Math.Ceiling(dt*240));double h=dt/steps;
        for(int i=0;i<steps;i++){Velocity+=(stiffness*(Target-Value)-damping*Velocity)*h;Value+=Velocity*h;}
        if(Math.Abs(Target-Value)<.00001&&Math.Abs(Velocity)<.0001){Value=Target;Velocity=0;}
    }
    public void Snap(){Value=Target;Velocity=0;}
}
