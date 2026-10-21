using System;using System.IO;using System.Linq;using System.Reflection;using System.Resources;using System.Collections.Generic;using System.Runtime.CompilerServices;
class VerifyThemeRuntime {
    static Dictionary<string,byte[]> embedded=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);static Dictionary<string,Assembly> loaded=new Dictionary<string,Assembly>(StringComparer.OrdinalIgnoreCase);
    [STAThread]static int Main(string[] a){try{
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object s,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name;Assembly result;if(loaded.TryGetValue(name,out result))return result;byte[] data;if(!embedded.TryGetValue(name,out data))return null;result=Assembly.Load(data);loaded[name]=result;return result;};
        var assembly=Assembly.LoadFile(Path.GetFullPath(a[0]));using(var reader=new ResourceReader(assembly.GetManifestResourceStream("PCL.Resources.resources"))){var items=reader.GetEnumerator();while(items.MoveNext()){string kind;byte[] raw;reader.GetResourceData((string)items.Key,out kind,out raw);if(kind=="ResourceTypeCode.ByteArray"&&raw.Length>6&&raw[4]==77&&raw[5]==90){var bytes=new byte[raw.Length-4];Buffer.BlockCopy(raw,4,bytes,0,bytes.Length);embedded[(string)items.Key]=bytes;}}}
        var secret=assembly.GetType("PCL.ModSecret",true);foreach(string name in new[]{"ThemeRefresh","ThemeCheckOne"}){var f=secret.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);RuntimeHelpers.PrepareMethod(f.MethodHandle);Console.WriteLine("JIT_OK "+f);}
        // A compiler-generated closure is located by its unique EffectSetup capture, not an index.
        var closure=secret.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic).Single(t=>t.GetField("$VB$Local_EffectSetup",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!=null);var callback=closure.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Single(f=>f.Name=="_Lambda$__0");RuntimeHelpers.PrepareMethod(callback.MethodHandle);Console.WriteLine("JIT_OK "+callback.DeclaringType+" "+callback);
        var loadedMethod=assembly.GetType("PCL.PageSetupUI",true).GetMethod("PageSetupUI_Loaded",BindingFlags.NonPublic|BindingFlags.Instance);RuntimeHelpers.PrepareMethod(loadedMethod.MethodHandle);Console.WriteLine("JIT_OK "+loadedMethod);
        Console.WriteLine("THEME_JIT_OK four cosmetic methods; no theme UI or target startup executed");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
