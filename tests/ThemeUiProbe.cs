using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DownloadRepair;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody=Mono.Cecil.Cil.MethodBody;

// Controlled WPF replay: actual patched Loaded/callback bodies and actual WPF hit
// testing. Stub only constructors, unrelated settings/background work and dispatch.
// Does not launch PCL, show a window, read user settings, or access the network.
class ThemeUiProbe {
    static readonly BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static readonly Dictionary<string,byte[]> embedded=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,Assembly> assemblies=new Dictionary<string,Assembly>(StringComparer.OrdinalIgnoreCase);
    static int count;
    static void Check(bool value,string text){if(!value)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
    [STAThread]static int Main(string[] args){try{
        Dependency.Install();AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
        byte[] bytes=File.ReadAllBytes(args[0]);
        Run(bytes,true);Run(bytes,false);
        Console.WriteLine("THEME_UI_PROBE_OK "+count+" checks; controlled WPF replay, no target startup");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    static Assembly Resolve(object sender,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name;Assembly a;if(assemblies.TryGetValue(name,out a))return a;byte[] bytes;if(!embedded.TryGetValue(name,out bytes))return null;a=Assembly.Load(bytes);assemblies[name]=a;return a;}
    static void Empty(MethodDefinition f){f.Body=new MethodBody(f);f.Body.GetILProcessor().Emit(OpCodes.Ret);}
    static void Constructor(ModuleDefinition m,string name,System.Reflection.ConstructorInfo parent){var f=SemanticPatcher.Method(SemanticPatcher.Type(m,name),".ctor",0);f.Body=new MethodBody(f);var il=f.Body.GetILProcessor();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,m.ImportReference(parent));il.Emit(OpCodes.Ret);}
    static byte[] Prepare(byte[] bytes,bool reproduce){using(var m=SemanticPatcher.Read(bytes)){
        var page=SemanticPatcher.Type(m,"PCL.PageSetupUI");var secret=SemanticPatcher.Type(m,"PCL.ModSecret");
        if(reproduce){
            var loaded=SemanticPatcher.Method(page,"PageSetupUI_Loaded",2);loaded.Body.Instructions.Single(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="get_PanLauncherHide").Next.OpCode=OpCodes.Ldc_I4_0;
            var callbackRef=(MethodReference)SemanticPatcher.Method(secret,"ThemeCheckAll",1).Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldftn).Operand;
            var callback=SemanticPatcher.Type(m,callbackRef.DeclaringType.FullName).Methods.Single(f=>f.FullName==callbackRef.FullName);
            foreach(var get in callback.Body.Instructions.Where(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="get_PanLauncherHide").ToArray()){
                var start=get.Previous;var group=new[]{start,get,get.Next,get.Next.Next};foreach(var i in group)callback.Body.Instructions.Remove(i);
            }
        }
        foreach(var t in m.GetTypes())foreach(var f in t.Methods.Where(f=>f.Name==".cctor"&&f.HasBody))Empty(f);
        Constructor(m,"PCL.MyRadioBox",typeof(Grid).GetConstructor(Type.EmptyTypes));
        Constructor(m,"PCL.MyScrollViewer",typeof(ScrollViewer).GetConstructor(Type.EmptyTypes));
        Constructor(m,"PCL.MyPageRight",typeof(System.Windows.Documents.AdornerDecorator).GetConstructor(Type.EmptyTypes));
        var ctor=SemanticPatcher.Method(page,".ctor",0);ctor.Body=new MethodBody(ctor);var il=ctor.Body.GetILProcessor();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,SemanticPatcher.Method(SemanticPatcher.Type(m,"PCL.MyPageRight"),".ctor",0));il.Emit(OpCodes.Ret);
        Empty(SemanticPatcher.Method(page,"Refresh",0));Empty(SemanticPatcher.Method(page,"SliderLoad",0));
        var run=SemanticPatcher.Method(SemanticPatcher.Type(m,"PCL.ModBase"),"RunInUi",2);run.Body=new MethodBody(run);il=run.Body.GetILProcessor();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(Action).GetMethod("Invoke")));il.Emit(OpCodes.Ret);
        var animation=SemanticPatcher.Type(m,"PCL.ModAnimation");var getAni=SemanticPatcher.Method(animation,"get_AniControlEnabled",0);getAni.Body=new MethodBody(getAni);getAni.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_0);getAni.Body.GetILProcessor().Emit(OpCodes.Ret);Empty(SemanticPatcher.Method(animation,"set_AniControlEnabled",1));
        foreach(var r in m.Resources.OfType<EmbeddedResource>().Where(r=>r.Name=="PCL.Resources.resources"))using(var reader=new ResourceReader(r.GetResourceStream())){var items=reader.GetEnumerator();while(items.MoveNext()){string kind;byte[] raw;reader.GetResourceData((string)items.Key,out kind,out raw);if(kind=="ResourceTypeCode.ByteArray"&&raw.Length>6&&raw[4]==77&&raw[5]==90){var data=new byte[raw.Length-4];Buffer.BlockCopy(raw,4,data,0,data.Length);embedded[(string)items.Key]=data;}}}
        using(var stream=new MemoryStream()){m.Write(stream);return stream.ToArray();}
    }}
    static void Set(object page,string name,object value){var f=page.GetType().GetField("_"+name,All);if(f==null)throw new Exception("Missing UI backing field "+name);f.SetValue(page,value);}
    static bool Under(DependencyObject node,DependencyObject parent){while(node!=null){if(node==parent)return true;node=VisualTreeHelper.GetParent(node);}return false;}
    static void Run(byte[] bytes,bool reproduce){
        var a=Assembly.Load(Prepare(bytes,reproduce));var type=a.GetType("PCL.PageSetupUI",true);var page=Activator.CreateInstance(type);var radioType=a.GetType("PCL.MyRadioBox",true);
        var root=new Grid{Width=300,Height=450,Background=Brushes.White};var canvas=new Canvas();root.Children.Add(canvas);var radios=new FrameworkElement[15];
        for(int n=0;n<15;n++){var radio=(Grid)Activator.CreateInstance(radioType);radio.Width=180;radio.Height=30;radio.Background=Brushes.Transparent;radio.IsEnabled=n<6;Canvas.SetTop(radio,n*30);canvas.Children.Add(radio);Set(page,"RadioLauncherTheme"+n,radio);radios[n]=radio;}
        var overlay=new Border{Background=Brushes.LightGray,Visibility=Visibility.Collapsed,Child=new TextBlock{Text="sponsor overlay"}};root.Children.Add(overlay);Set(page,"PanLauncherHide",overlay);
        Set(page,"PanBack",Activator.CreateInstance(a.GetType("PCL.MyScrollViewer",true)));
        Set(page,"RadioLauncherTheme5Gray",Activator.CreateInstance(radioType));foreach(string name in new[]{"LabLauncherTheme5Unlock","LabLauncherTheme8Copy","LabLauncherTheme9Copy","LabLauncherTheme11Click"})Set(page,name,new TextBlock());
        a.GetType("PCL.ModMain",true).GetField("FrmSetupUI",All).SetValue(null,page);
        var parameters=new System.Windows.Interop.HwndSourceParameters("PCL theme test host"){Width=300,Height=450,PositionX=-32000,PositionY=-32000,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080};
        using(var host=new System.Windows.Interop.HwndSource(parameters)){host.RootVisual=root;
        var loaded=type.GetMethod("PageSetupUI_Loaded",All);loaded.Invoke(page,new object[]{page,new RoutedEventArgs()});root.Measure(new Size(300,450));root.Arrange(new Rect(0,0,300,450));root.UpdateLayout();
        var hit=root.InputHitTest(new Point(20,45)) as DependencyObject;
        Console.WriteLine("UI_STATE reproduced="+reproduce+" rootVisible="+root.IsVisible+" overlayVisible="+overlay.IsVisible+" overlaySize="+overlay.ActualWidth+"x"+overlay.ActualHeight+" hit="+(hit==null?"null":hit.GetType().FullName));
        if(reproduce){Check(overlay.Visibility==Visibility.Visible,"reproduced 2.1.0 page load showing sponsor overlay");Check(Under(hit,overlay),"reproduced overlay intercepting theme click");}
        else{
            Check(overlay.Visibility==Visibility.Collapsed&&!overlay.IsHitTestVisible,"fixed page load keeps overlay hidden and non-interactive");Check(radios.All(r=>r.IsEnabled),"all fifteen actual WPF theme controls enabled");
            for(int n=0;n<15;n++)Check(Under(root.InputHitTest(new Point(20,n*30+15)) as DependencyObject,radios[n]),"theme "+n+" receives pointer hit");
            overlay.Visibility=Visibility.Visible;overlay.IsHitTestVisible=true;loaded.Invoke(page,new object[]{page,new RoutedEventArgs()});Check(overlay.Visibility==Visibility.Collapsed&&!overlay.IsHitTestVisible,"reopening page removes overlay again");
        }
        }
    }
}
