using System;using System.IO;using System.IO.Compression;using System.Linq;using System.Reflection;using System.Resources;using System.Collections.Generic;using Mono.Cecil;using Mono.Cecil.Cil;using DownloadRepair;
using MA=Mono.Cecil.MethodAttributes;using FA=Mono.Cecil.FieldAttributes;
class DispatchTests {
    static Dictionary<string,byte[]> libraries=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string,Assembly> loaded=new Dictionary<string,Assembly>(StringComparer.OrdinalIgnoreCase);
    static void Extract(Stream s) { using(var r=new ResourceReader(s)){var entries=r.GetEnumerator();while(entries.MoveNext()){string type;byte[] raw;r.GetResourceData((string)entries.Key,out type,out raw);if(type=="ResourceTypeCode.ByteArray"&&raw.Length>6&&raw[4]==77&&raw[5]==90){var b=new byte[raw.Length-4];Buffer.BlockCopy(raw,4,b,0,b.Length);libraries[(string)entries.Key]=b;}}} }
    static MethodDefinition Make(TypeDefinition t,string name,TypeReference result,IEnumerable<ParameterDefinition> parameters) { var f=new MethodDefinition(name,MA.Private|MA.Static,result);foreach(var p in parameters)f.Parameters.Add(new ParameterDefinition(p.ParameterType));t.Methods.Add(f);return f; }
    [STAThread]static int Main(string[] a){try{Run(a);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    static void Run(string[] a){
        string root=Path.GetFullPath(a[1]);Directory.CreateDirectory(root);string fixture=Path.Combine(root,"dispatch-fixture.exe");
        using(var m=SemanticPatcher.Read(File.ReadAllBytes(a[0]))) {
            Extract(((EmbeddedResource)m.Resources.Single(x=>x.Name=="PCL.Resources.resources")).GetResourceStream());
            var t=SemanticPatcher.Type(m,"PCL.ModModpack");var selected=new FieldDefinition("TestSelected",FA.Public|FA.Static,m.TypeSystem.Int32);var fail=new FieldDefinition("TestFail",FA.Public|FA.Static,m.TypeSystem.Boolean);t.Fields.Add(selected);t.Fields.Add(fail);
            int id=0;foreach(string name in SemanticPatcher.Installers) {
                var f=t.Methods.Single(x=>x.Name==name);f.Body=new Mono.Cecil.Cil.MethodBody(f);var il=f.Body.GetILProcessor();var okay=Instruction.Create(OpCodes.Ldarg,f.Parameters[1]);
                il.Emit(OpCodes.Ldsfld,fail);il.Emit(OpCodes.Brfalse,okay);il.Emit(OpCodes.Ldstr,"installer exception fixture");il.Emit(OpCodes.Newobj,m.ImportReference(typeof(InvalidOperationException).GetConstructor(new[]{typeof(string)})));il.Emit(OpCodes.Throw);
                // Access the archive inside each real dispatch branch: it must still be open here.
                il.Append(okay);il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(ZipArchive).GetProperty("Entries").GetGetMethod()));il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(System.Collections.ObjectModel.ReadOnlyCollection<ZipArchiveEntry>).GetProperty("Count").GetGetMethod()));il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldc_I4,id++);il.Emit(OpCodes.Stsfld,selected);il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Ret);
            }
            var dispatch=SemanticPatcher.Method(t,"ModpackInstall",3);var logger=dispatch.Body.Instructions.Select(x=>x.Operand as MethodReference).First(x=>x!=null&&x.DeclaringType.FullName=="MeloongCore.Logger"&&x.Name=="Info");
            var quiet=Make(t,"TestQuiet",m.TypeSystem.Void,logger.Parameters);quiet.Body.GetILProcessor().Emit(OpCodes.Ret);
            var open=dispatch.Body.Instructions.Select(x=>x.Operand as MethodReference).Single(x=>x!=null&&x.Name=="OpenZip");var localOpen=Make(t,"TestOpen",open.ReturnType,open.Parameters);var oi=localOpen.Body.GetILProcessor();oi.Emit(OpCodes.Ldarg_0);oi.Emit(OpCodes.Call,m.ImportReference(typeof(ZipFile).GetMethod("OpenRead",new[]{typeof(string)})));oi.Emit(OpCodes.Ret);
            foreach(var i in dispatch.Body.Instructions) { var r=i.Operand as MethodReference;if(r==null)continue;if(r.FullName==logger.FullName)i.Operand=quiet;else if(r.FullName==open.FullName)i.Operand=localOpen;else if(r.Name=="get_McFolderSelected"){i.OpCode=OpCodes.Ldstr;i.Operand=root+"\\";} }
            m.Write(fixture);
        }
        AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name;Assembly found;if(loaded.TryGetValue(name,out found))return found;byte[] b;if(!libraries.TryGetValue(name,out b))return null;found=Assembly.Load(b);loaded[name]=found;return found;};
        var target=Assembly.LoadFile(fixture).GetType("PCL.ModModpack",true);var method=target.GetMethod("ModpackInstall",new[]{typeof(string),typeof(string),typeof(string)});var chosen=target.GetField("TestSelected");var failing=target.GetField("TestFail");
        var cases=new[]{
            new[]{"curseforge","manifest.json","{\"minecraft\":{\"version\":\"1.21.1\"}}","0"},new[]{"hmcl","modpack.json","{}","1"},new[]{"mmc","mmc-pack.json","{}","2"},new[]{"mcbbs","mcbbs.packmeta","{}","3"},new[]{"mcbbs-addons","manifest.json","{\"addons\":[]}","3"},new[]{"modrinth","modrinth.index.json","{}","4"},new[]{"launcher-zip","modpack.zip","fixture","5"},new[]{"launcher-mrpack","modpack.mrpack","fixture","5"},new[]{"generic","readme.txt","fixture","6"},
            new[]{"nested-cf","inner/manifest.json","{}","0"},new[]{"nested-hmcl","inner/modpack.json","{}","1"},new[]{"nested-mmc","inner/mmc-pack.json","{}","2"},new[]{"nested-mcbbs","inner/mcbbs.packmeta","{}","3"},new[]{"nested-modrinth","inner/modrinth.index.json","{}","4"},new[]{"nested-launcher","inner/modpack.mrpack","fixture","5"}
        };
        foreach(var item in cases) {
            string zip=Path.Combine(root,item[0]+".zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create))using(var writer=new StreamWriter(z.CreateEntry(item[1]).Open()))writer.Write(item[2]);
            chosen.SetValue(null,-1);method.Invoke(null,new object[]{zip,"fixture-instance",null});if((int)chosen.GetValue(null)!=Int32.Parse(item[3]))throw new Exception("Incorrect installer: "+item[0]);
            using(File.Open(zip,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}Console.WriteLine("PASS route, archive alive during install, disposed after: "+item[0]);
        }
        string exceptionZip=Path.Combine(root,"modrinth.zip");failing.SetValue(null,true);bool rejected=false;try{method.Invoke(null,new object[]{exceptionZip,"fixture-instance",null});}catch(TargetInvocationException e){rejected=e.InnerException is InvalidOperationException;}if(!rejected)throw new Exception("Expected installer exception");using(File.Open(exceptionZip,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}Console.WriteLine("PASS archive disposed after installer exception");
        Console.WriteLine("DISPATCH_TESTS_OK 15 recognition cases + exception cleanup; installer bodies replaced by probes, no game downloads");
    }
}
