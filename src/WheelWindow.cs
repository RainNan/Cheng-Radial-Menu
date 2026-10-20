using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace ChengOrbit;
internal sealed class WheelWindow:Window
{
    private readonly Configuration config;
    private readonly WheelSurface surface;
    private readonly Viewport3D viewport;
    private readonly AxisAngleRotation3D rotationX=new(new Vector3D(1,0,0),0),rotationY=new(new Vector3D(0,1,0),0);
    private readonly TranslateTransform3D sink=new();
    private readonly ScaleTransform3D scale=new(1,1,1);
    private readonly Spring rx=new(),ry=new(),depth=new(),opening=new(1);
    private Native.Point origin;
    private bool moved,settingsSelected,preview;
    private bool dropHover;
    public bool BindingGesture {get;private set;}
    public Func<bool>? BindingAllowed {get;set;}
    public event Action<int,string>? BindingRequested;
    private readonly Stopwatch clock=new();
    private double last;
    private readonly Border gear;
    private readonly TextBlock bindingHint;
    private readonly System.Windows.Shapes.Ellipse floorShadow;
    private int previewSelection=0;
    public int Selection=>surface.Selected;
    public bool SettingsSelected=>settingsSelected;
    public event Action? SettingsRequested;
    public WheelWindow(Configuration configuration)
    {
        config=configuration;Width=Height=620;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;Title="澄——桌面轮盘";
        var root=new Grid{Background=Brushes.Transparent};Content=root;AllowDrop=true;
        floorShadow=new System.Windows.Shapes.Ellipse{Width=380,Height=370,Fill=new SolidColorBrush(Color.FromArgb(70,20,46,62)),Effect=new BlurEffect{Radius=32},Margin=new Thickness(0,40,0,0),IsHitTestVisible=false};root.Children.Add(floorShadow);
        surface=new WheelSurface(config){Width=520,Height=520};surface.Measure(new Size(520,520));surface.Arrange(new Rect(0,0,520,520));
        viewport=new Viewport3D{IsHitTestVisible=false,Camera=new PerspectiveCamera(new Point3D(0,0,2.8),new Vector3D(0,0,-1),new Vector3D(0,1,0),45)};root.Children.Add(viewport);
        var mesh=new MeshGeometry3D{Positions=new Point3DCollection{new(-1,1,0),new(1,1,0),new(1,-1,0),new(-1,-1,0)},TextureCoordinates=new PointCollection{new(0,0),new(1,0),new(1,1),new(0,1)},TriangleIndices=new Int32Collection{0,2,1,0,3,2}};
        var material=new DiffuseMaterial(new VisualBrush(surface){AutoLayoutContent=true,Stretch=Stretch.Fill,ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,520,520)});
        var model=new GeometryModel3D(mesh,material){BackMaterial=material};var transforms=new Transform3DGroup();transforms.Children.Add(new RotateTransform3D(rotationX));transforms.Children.Add(new RotateTransform3D(rotationY));transforms.Children.Add(sink);transforms.Children.Add(scale);model.Transform=transforms;
        var group=new Model3DGroup();group.Children.Add(new AmbientLight(Colors.White));group.Children.Add(model);viewport.Children.Add(new ModelVisual3D{Content=group});
        gear=new Border{Width=48,Height=48,CornerRadius=new CornerRadius(24),Background=new SolidColorBrush(Color.FromArgb(170,240,250,255)),BorderBrush=new SolidColorBrush(Color.FromArgb(215,255,255,255)),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(511,480,0,0),Child=new TextBlock{Text="⚙",FontSize=26,Foreground=new SolidColorBrush(Color.FromRgb(55,85,105)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center},Effect=new DropShadowEffect{BlurRadius=18,ShadowDepth=5,Opacity=.17}};root.Children.Add(gear);
        bindingHint=new TextBlock{Text="拖到扇形，松开鼠标绑定",FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(36,71,91)),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,555,0,0),Padding=new Thickness(14,8,14,8),Background=new SolidColorBrush(Color.FromArgb(235,243,250,255)),Visibility=Visibility.Collapsed,IsHitTestVisible=false};root.Children.Add(bindingHint);
        DragEnter+=OnDragOver;DragOver+=OnDragOver;DragLeave+=(_,_)=>{dropHover=false;surface.Select(-1);bindingHint.Text="拖到扇形，松开鼠标绑定";};Drop+=OnDrop;
        SourceInitialized+=(_,_)=>{var helper=new WindowInteropHelper(this);var ex=Native.GetWindowLongPtr(helper.Handle,-20).ToInt64();Native.SetWindowLongPtr(helper.Handle,-20,new IntPtr(ex|0x08000080));HwndSource.FromHwnd(helper.Handle)?.AddHook((IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled)=>{if(msg==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});};
        Closed+=(_,_)=>CompositionTarget.Rendering-=Render;
        MouseLeftButtonUp+=(_,_)=>{if(preview&&settingsSelected)SettingsRequested?.Invoke();};
    }
    private double Radius=>205.0*config.WheelSize/410.0;
    public void Open(bool isPreview=false)
    {
        if(IsVisible)HideWheel();
        preview=isPreview;Native.GetCursorPos(out origin);moved=isPreview;settingsSelected=false;dropHover=false;
        BindingGesture=!isPreview&&(Native.GetAsyncKeyState(0x01)&0x8000)!=0;
        bindingHint.Visibility=BindingGesture?Visibility.Visible:Visibility.Collapsed;bindingHint.Text="拖到扇形，松开鼠标绑定";bindingHint.Foreground=new SolidColorBrush(Color.FromRgb(36,71,91));
        var monitor=Native.MonitorFromPoint(origin,2);var info=new Native.MonitorInfo{Size=Marshal.SizeOf<Native.MonitorInfo>()};Native.GetMonitorInfo(monitor,ref info);Native.GetDpiForMonitor(monitor,0,out uint dpi,out _);double factor=dpi/96.0;
        int size=(int)(620*factor);int left=(int)Math.Clamp(origin.X-size/2.0,info.Work.Left,Math.Max(info.Work.Left,info.Work.Right-size));int top=(int)Math.Clamp(origin.Y-size/2.0,info.Work.Top,Math.Max(info.Work.Top,info.Work.Bottom-size));
        // Capture before showing the overlay so it never samples itself.
        surface.Capture(left,top,size,size);
        Left=left/factor;Top=top/factor;Show();Native.SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),left,top,size,size,0x10|0x40);
        var handle=new WindowInteropHelper(this).Handle;long ex=Native.GetWindowLongPtr(handle,-20).ToInt64();Native.SetWindowLongPtr(handle,-20,new IntPtr(isPreview?ex|0x20:ex&~0x20));
        surface.Rebuild();surface.Select(-1);rx.Value=rx.Target=ry.Value=ry.Target=depth.Value=depth.Target=0;rx.Velocity=ry.Velocity=depth.Velocity=0;opening.Value=.88;opening.Velocity=0;opening.Target=1;
        clock.Restart();last=0;CompositionTarget.Rendering+=Render;
    }
    public void Preview(int index=0){previewSelection=Math.Max(0,index);Open(true);surface.Select(Math.Min(previewSelection,config.SlotCount-1));}
    public void HideWheel(){CompositionTarget.Rendering-=Render;Hide();clock.Stop();}
    public void RefreshPreview(){surface.Rebuild();if(preview&&IsVisible)surface.Select(Math.Min(previewSelection,config.SlotCount-1));}
    public void ShowBindingError(string message)
    {
        bindingHint.Text="绑定失败："+message;bindingHint.MaxWidth=430;bindingHint.TextWrapping=TextWrapping.Wrap;
        bindingHint.Foreground=new SolidColorBrush(Color.FromRgb(160,57,57));bindingHint.Visibility=Visibility.Visible;
    }
    public void PollPointer()
    {
        if(!IsVisible||preview||dropHover)return;
        Native.GetCursorPos(out var point);
        if(!moved){if(double.Hypot(point.X-origin.X,point.Y-origin.Y)<7)return;moved=true;}
        var p=PointFromScreen(new Point(point.X,point.Y));double x=p.X-310,y=p.Y-310;
        settingsSelected=double.Hypot(p.X-535,p.Y-504)<30;
        gear.Background=new SolidColorBrush(settingsSelected?Color.FromArgb(240,255,255,255):Color.FromArgb(170,240,250,255));
        double dist=double.Hypot(x,y),force=Math.Tanh(Math.Max(0,dist-38)/(Radius-38)*1.2);
        rx.Target=dist==0?0:y/dist*config.Tilt*force;ry.Target=dist==0?0:x/dist*config.Tilt*force;depth.Target=-.035*force;
        if(settingsSelected||dist<=38){surface.Select(-1);return;}
        int count=config.SlotCount;
        double width=Math.Tau/count,angle=(Math.Atan2(y,x)+Math.PI/2+Math.Tau)%Math.Tau;
        if(surface.Selected>=0){double d=Math.Atan2(Math.Sin(angle-surface.Selected*width),Math.Cos(angle-surface.Selected*width));if(Math.Abs(d)<width/2+.045)return;}
        surface.Select((int)Math.Floor((angle+width/2)/width)%count);
    }
    internal static int DropSlot(Point point,double radius,int count=Configuration.DefaultSlotCount)
    {
        if(count<Configuration.MinSlotCount||count>Configuration.MaxSlotCount)return -1;
        double x=point.X-310,y=point.Y-310,dist=double.Hypot(x,y);
        if(dist<38*radius/205||dist>radius)return -1;
        double angle=(Math.Atan2(y,x)+Math.PI/2+Math.Tau)%Math.Tau,width=Math.Tau/count;
        return (int)Math.Floor((angle+width/2)/width)%count;
    }
    private static bool TryFile(DragEventArgs e,out string path)
    {
        path="";
        if(!e.Data.GetDataPresent(DataFormats.FileDrop))return false;
        if(e.Data.GetData(DataFormats.FileDrop) is not string[] files||files.Length!=1)return false;
        string ext=System.IO.Path.GetExtension(files[0]);
        if(!ext.Equals(".exe",StringComparison.OrdinalIgnoreCase)&&!ext.Equals(".lnk",StringComparison.OrdinalIgnoreCase))return false;
        path=files[0];return true;
    }
    private void OnDragOver(object sender,DragEventArgs e)
    {
        e.Handled=true;e.Effects=DragDropEffects.None;
        if(preview||BindingAllowed?.Invoke()!=true||!TryFile(e,out _))return;
        BindingGesture=true;dropHover=true;bindingHint.Visibility=Visibility.Visible;
        int slot=DropSlot(e.GetPosition(this),Radius,config.SlotCount);surface.Select(slot);
        bindingHint.Text=slot<0?"拖到一个扇形，松开鼠标绑定":config.Apps[slot].IsEmpty?"松开鼠标，绑定到此位置":"松开鼠标，替换「"+config.Apps[slot].Name+"」";
        // A file is only referenced, never moved or removed from the drag source.
        if(slot>=0&&(e.AllowedEffects&DragDropEffects.Copy)!=0)e.Effects=DragDropEffects.Copy;
        else if(slot>=0&&(e.AllowedEffects&DragDropEffects.Link)!=0)e.Effects=DragDropEffects.Link;
    }
    private void OnDrop(object sender,DragEventArgs e)
    {
        e.Handled=true;e.Effects=DragDropEffects.None;dropHover=false;
        if(preview||BindingAllowed?.Invoke()!=true||!TryFile(e,out string path))return;
        int slot=DropSlot(e.GetPosition(this),Radius,config.SlotCount);if(slot<0)return;
        if((e.AllowedEffects&DragDropEffects.Copy)!=0)e.Effects=DragDropEffects.Copy;
        else if((e.AllowedEffects&DragDropEffects.Link)!=0)e.Effects=DragDropEffects.Link;
        else return;
        BindingGesture=true;BindingRequested?.Invoke(slot,path);
    }
    private void Render(object? sender,EventArgs e)
    {
        double now=clock.Elapsed.TotalSeconds,dt=Math.Min(.05,now-last);last=now;PollPointer();
        bool reduced=config.ReduceMotion||!SystemParameters.ClientAreaAnimation;
        foreach(var spring in new[]{rx,ry,depth,opening}){if(reduced)spring.Snap();else spring.Step(dt,190,29-config.Elasticity*.18);}
        rotationX.Angle=reduced?0:rx.Value;rotationY.Angle=reduced?0:ry.Value;sink.OffsetZ=reduced?0:depth.Value;
        double s=config.WheelSize/410.0*opening.Value;scale.ScaleX=scale.ScaleY=s;
        floorShadow.Width=config.WheelSize*.92;floorShadow.Height=config.WheelSize*.9;floorShadow.RenderTransform=new TranslateTransform(-ry.Value*.8,rx.Value*.8);
        surface.LightX=260-ry.Value*6;surface.LightY=120-rx.Value*6;surface.Advance(dt,reduced);
    }
}

internal sealed class WheelSurface:FrameworkElement
{
    private readonly Configuration config;
    private readonly List<Spring> widths=new(),highlights=new();
    private readonly Spring start=new(-Math.PI/2-Math.PI/7);
    private readonly List<ImageSource?> icons=new();
    private ImageSource? background;
    public int Selected {get;private set;}=-1;
    public double LightX=180,LightY=100;
    public WheelSurface(Configuration c){config=c;Rebuild();}
    public void Rebuild()
    {
        int n=config.SlotCount;
        if(widths.Count!=n){widths.Clear();highlights.Clear();for(int i=0;i<n;i++){widths.Add(new(Math.Tau/n));highlights.Add(new());}start.Value=start.Target=-Math.PI/2-(n>0?Math.PI/n:0);start.Velocity=0;Selected=-1;}
        icons.Clear();foreach(var app in config.Apps.GetRange(0,n))icons.Add(AppServices.IconFor(app.Path));
        var old=Selected;Selected=-2;Select(old>=n?-1:old);InvalidateVisual();
    }
    public void Capture(int x,int y,int width,int height)
    {
        try
        {
            using var bitmap=new System.Drawing.Bitmap(width,height);using(var g=System.Drawing.Graphics.FromImage(bitmap))g.CopyFromScreen(x,y,0,0,new System.Drawing.Size(width,height));
            using var stream=new System.IO.MemoryStream();bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);stream.Position=0;
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
            var source=new System.Windows.Controls.Image{Source=image,Width=520,Height=520,Stretch=Stretch.Fill,Effect=new BlurEffect{Radius=24}};source.Measure(new Size(520,520));source.Arrange(new Rect(0,0,520,520));
            var blur=new RenderTargetBitmap(520,520,96,96,PixelFormats.Pbgra32);blur.Render(source);blur.Freeze();background=blur;
        }
        catch{background=null;}
    }
    public void Select(int selected)
    {
        if(Selected==selected)return;Selected=selected;int n=widths.Count;if(n==0)return;
        double basic=Math.Tau/n,expand=n>1?config.Expansion*Math.PI/180:0;
        for(int i=0;i<n;i++){widths[i].Target=selected<0?basic:i==selected?basic+expand:basic-expand/(n-1);highlights[i].Target=i==selected?1:0;}
        start.Target=-Math.PI/2-basic/2;
        if(selected>=0){double before=0;for(int i=0;i<selected;i++)before+=widths[i].Target;start.Target=-Math.PI/2+selected*basic-widths[selected].Target/2-before;}
    }
    public void Advance(double dt,bool reduced)
    {
        foreach(var s in widths){if(reduced)s.Snap();else s.Step(dt,190,27);}foreach(var s in highlights){if(reduced)s.Snap();else s.Step(dt,190,27);}if(reduced)start.Snap();else start.Step(dt,190,27);InvalidateVisual();
    }
    private static SolidColorBrush Brush(byte a,byte r,byte g,byte b)=>new(Color.FromArgb(a,r,g,b));
    private static Point Polar(double radius,double angle)=>new(260+radius*Math.Cos(angle),260+radius*Math.Sin(angle));
    private static StreamGeometry Sector(double a,double b,double inner=39,double outer=200)
    {
        // Use sampled arcs so a single application (360°) remains a valid ring.
        var geometry=new StreamGeometry();using(var g=geometry.Open())
        {
            g.BeginFigure(Polar(inner,a),true,true);g.LineTo(Polar(outer,a),true,false);
            int steps=Math.Max(8,(int)Math.Ceiling((b-a)*35));for(int j=1;j<=steps;j++)g.LineTo(Polar(outer,a+(b-a)*j/steps),true,false);
            g.LineTo(Polar(inner,b),true,false);for(int j=steps-1;j>=0;j--)g.LineTo(Polar(inner,a+(b-a)*j/steps),true,false);
        }geometry.Freeze();return geometry;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);var center=new Point(260,260);
        dc.DrawEllipse(Brush(155,166,193,209),new Pen(Brush(205,240,253,255),1.5),new Point(260,265),202,202);
        dc.PushClip(new EllipseGeometry(center,200,200));
        if(background!=null)dc.DrawImage(background,new Rect(0,0,520,520));else dc.DrawRectangle(Brush(230,184,214,228),null,new Rect(0,0,520,520));
        var baseGlass=new LinearGradientBrush();baseGlass.StartPoint=new Point(0,0);baseGlass.EndPoint=new Point(1,1);baseGlass.GradientStops.Add(new(Color.FromArgb(138,255,255,255),0));baseGlass.GradientStops.Add(new(Color.FromArgb(64,220,242,255),.5));baseGlass.GradientStops.Add(new(Color.FromArgb(108,255,255,255),1));dc.DrawRectangle(baseGlass,null,new Rect(0,0,520,520));
        double angle=start.Value;
        for(int i=0;i<widths.Count;i++)
        {
            double end=angle+widths[i].Value;var shape=Sector(angle+.004,end-.004);dc.DrawGeometry(Brush(5,255,255,255),new Pen(Brush(70,255,255,255),.7),shape);
            double alpha=highlights[i].Value*config.Strength/100;
            if(alpha>.001)
            {
                var c=config.Highlight;var fill=new RadialGradientBrush{Center=new Point(.3,.2),GradientOrigin=new Point(.3,.2),RadiusX=1,RadiusY=1};fill.GradientStops.Add(new(Color.FromArgb((byte)(220*alpha),(byte)(c.R+(255-c.R)*.35),(byte)(c.G+(255-c.G)*.35),(byte)(c.B+(255-c.B)*.35)),0));fill.GradientStops.Add(new(Color.FromArgb((byte)(255*alpha),c.R,c.G,c.B),1));
                dc.DrawGeometry(fill,new Pen(Brush((byte)(235*alpha),244,254,255),1.6),shape);
                var arc=new StreamGeometry();using(var g=arc.Open()){g.BeginFigure(Polar(201,angle+.02),false,false);for(int k=1;k<=45;k++)g.LineTo(Polar(201,angle+.02+(end-angle-.04)*k/45),true,false);}dc.DrawGeometry(null,new Pen(Brush((byte)(255*alpha),255,255,255),3),arc);
            }
            angle=end;
        }
        var sheen=new RadialGradientBrush(Color.FromArgb(65,255,255,255),Colors.Transparent){Center=new Point(LightX/520,LightY/520),GradientOrigin=new Point(LightX/520,LightY/520),RadiusX=.65,RadiusY=.65};dc.DrawRectangle(sheen,null,new Rect(0,0,520,520));dc.Pop();
        dc.DrawEllipse(null,new Pen(Brush(230,255,255,255),1.8),center,201,201);dc.DrawEllipse(null,new Pen(Brush(85,255,255,255),.8),center,194,194);
        dc.DrawEllipse(Brush(24,255,255,255),new Pen(Brush(105,255,255,255),1),center,38,38);dc.DrawEllipse(Brush(210,255,255,255),null,center,2,2);
        angle=start.Value;
        for(int i=0;i<widths.Count;i++)
        {
            double end=angle+widths[i].Value;Point pos=Polar(137+highlights[i].Value*5,(angle+end)/2);double size=44*(1+highlights[i].Value*.12);var rect=new Rect(pos.X-size/2,pos.Y-size/2,size,size);
            if(icons[i]!=null)dc.DrawImage(icons[i],rect);else {dc.DrawRoundedRectangle(Brush(110,234,246,255),new Pen(Brush(130,255,255,255),1),rect,10,10);var text=new FormattedText(config.Apps[i].IsEmpty?"+":config.Apps[i].Name.Length>0?config.Apps[i].Name[..1]:"◇",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),22,Brush(200,45,105,155),VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(text,new Point(pos.X-text.Width/2,pos.Y-text.Height/2));}
            angle=end;
        }
        if(widths.Count==0){var text=new FormattedText("请添加软件",CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),17,Brush(255,45,75,95),1);dc.DrawText(text,new Point(260-text.Width/2,160));}
    }
}
