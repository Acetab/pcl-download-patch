using System;using System.IO;using System.Linq;using System.Reflection;using System.Resources;using System.Collections.Generic;using System.Runtime.CompilerServices;
class VerifyRuntime {
    static Dictionary<string,byte[]> embedded=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string,Assembly> loaded=new Dictionary<string,Assembly>(StringComparer.OrdinalIgnoreCase);
    [STAThread]static int Main(string[] a){try{
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object s,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name;Assembly result;if(loaded.TryGetValue(name,out result))return result;byte[] data;if(!embedded.TryGetValue(name,out data))return null;result=Assembly.Load(data);loaded[name]=result;return result;};
        var assembly=Assembly.LoadFile(Path.GetFullPath(a[0]));using(var reader=new ResourceReader(assembly.GetManifestResourceStream("PCL.Resources.resources"))){var entries=reader.GetEnumerator();while(entries.MoveNext()){string type;byte[] raw;reader.GetResourceData((string)entries.Key,out type,out raw);if(type=="ResourceTypeCode.ByteArray"&&raw.Length>6&&raw[4]==77&&raw[5]==90){var bytes=new byte[raw.Length-4];Buffer.BlockCopy(raw,4,bytes,0,bytes.Length);embedded[(string)entries.Key]=bytes;}}}
        var names=new Dictionary<string,string>{{"PCL.PageDownloadInstall","LoaderInit"},{"PCL.PageDownloadModify","LoadInstanceFinished"},{"PCL.ModModpack","ModpackInstall"},{"PCL.PageDownloadResourceDetail","InitializeStackResourceVersions"},{"PCL.DownloadRepairWorkerV2","Install"}};int count=0;
        foreach(var item in names)foreach(var f in assembly.GetType(item.Key,true).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Where(f=>f.Name==item.Value)){RuntimeHelpers.PrepareMethod(f.MethodHandle);Console.WriteLine("JIT_OK "+f.DeclaringType.FullName+" "+f);count++;}
        if(count!=6)throw new Exception("Expected five restored methods plus new worker");Console.WriteLine("JIT_TESTS_OK no UI or downloads executed");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
