using System;using System.IO;using System.Linq;using System.Text;using System.Reflection;using System.Runtime.CompilerServices;using DownloadRepair;using Mono.Cecil;using Mono.Cecil.Cil;
class EngineTests {
    static int count;
    static void Ok(bool b,string text){if(!b)throw new Exception(text);count++;Console.WriteLine("PASS "+text);}
    static void Reject(Action a,string text){bool failed=false;try{a();}catch{failed=true;}Ok(failed,text);}
    static string Child(string root,string name,byte[] data){string dir=Path.Combine(root,name);Directory.CreateDirectory(dir);string path=Path.Combine(dir,"Plain Craft Launcher.exe");File.WriteAllBytes(path,data);return path;}
    static int Main(string[] a){try{Dependency.Install();Run(a);return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    [MethodImpl(MethodImplOptions.NoInlining)]static void Run(string[] a){
        byte[] original=File.ReadAllBytes(a[0]);string hash=FileSafety.Hash(original);string root=Path.GetFullPath(a[1]);Directory.CreateDirectory(root);
        string input=Child(root,"copy",original);var p=FileSafety.Inspect(input);Ok(!p.Complete&&p.Features.Count==5&&p.Features.All(x=>!x.Ready),"five restricted entries recognized");
        Ok(FileSafety.Hash(p.Output)==FileSafety.Hash(FileSafety.Inspect(input).Output),"deterministic rebuild");
        string copy=Path.Combine(Path.GetDirectoryName(input),"PCL.repaired.exe");FileSafety.CreateCopy(input,copy);Ok(FileSafety.Hash(File.ReadAllBytes(input))==hash&&FileSafety.Inspect(copy).Complete,"copy leaves source unchanged and restores all entries");
        string copyHash=FileSafety.Hash(File.ReadAllBytes(copy));Reject(()=>FileSafety.CreateCopy(input,copy),"existing output rejected");Ok(FileSafety.Hash(File.ReadAllBytes(copy))==copyHash,"existing output preserved");
        Reject(()=>FileSafety.CreateCopy(input,input),"same-path copy rejected");
        string target=Child(root,"inplace",original);FileSafety.Apply(target,false);Ok(FileSafety.Inspect(target).Complete,"atomic in-place repair");
        string backups=FileSafety.BackupRoot(target);var dirs=Directory.GetDirectories(backups);Ok(dirs.Length==1&&FileSafety.Hash(File.ReadAllBytes(Path.Combine(dirs[0],"原版启动器.bak")))==hash,"readable separate original backup");
        FileSafety.Apply(target,false);Ok(Directory.GetDirectories(backups).Length==1,"repeat apply is idempotent");FileSafety.Restore(target);Ok(FileSafety.Hash(File.ReadAllBytes(target))==hash,"exact full restore");
        FileSafety.Apply(target,false);Ok(Directory.GetDirectories(backups).Length==1,"verified backup reused");
        string bak=Path.Combine(dirs[0],"原版启动器.bak");File.WriteAllBytes(bak,new byte[]{1,2,3});string before=FileSafety.Hash(File.ReadAllBytes(target));Reject(()=>FileSafety.Restore(target),"corrupt backup rejected");Ok(FileSafety.Hash(File.ReadAllBytes(target))==before,"failed restore leaves target unchanged");File.WriteAllBytes(bak,original);FileSafety.Restore(target);
        using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.None))Reject(()=>FileSafety.Apply(target,false),"locked target rejected");Ok(FileSafety.Hash(File.ReadAllBytes(target))==hash,"locked failure leaves bytes unchanged");
        byte[] signed=(byte[])original.Clone();int pe=BitConverter.ToInt32(signed,60),sec=pe+24+(BitConverter.ToUInt16(signed,pe+24)==0x10b?96:112)+32;Buffer.BlockCopy(BitConverter.GetBytes(512),0,signed,sec,4);Reject(()=>SemanticPatcher.Rewrite(signed),"signed target rejected");Reject(()=>SemanticPatcher.Rewrite(new byte[]{0,1,2}),"malformed input rejected");
        byte[] changed;using(var m=SemanticPatcher.Read(original)){var f=SemanticPatcher.Method(SemanticPatcher.Type(m,"PCL.PageDownloadInstall"),"LoaderInit",0);f.Body.GetILProcessor().InsertBefore(f.Body.Instructions[0],Instruction.Create(OpCodes.Nop));using(var stream=new MemoryStream()){m.Write(stream);changed=stream.ToArray();}}
        string unknown=Child(root,"changed-length",changed);var future=FileSafety.Inspect(unknown);Ok(!future.KnownBuild&&!future.Complete,"changed method length structurally accepted");Reject(()=>FileSafety.Apply(unknown,true),"unknown build blocked by automatic launch");Ok(FileSafety.Hash(File.ReadAllBytes(unknown))==FileSafety.Hash(changed),"unknown auto-check makes no change");FileSafety.Apply(unknown,false);Ok(FileSafety.Inspect(unknown).Complete,"explicit structural repair of changed length");FileSafety.Restore(unknown);Ok(FileSafety.Hash(File.ReadAllBytes(unknown))==FileSafety.Hash(changed),"unknown build restores exact original");
        var old=LegacyEngine.Inspect(input);string legacy=Child(root,"legacy",old.Patched);Directory.CreateDirectory(FileSafety.BackupRoot(legacy));File.WriteAllBytes(Path.Combine(FileSafety.BackupRoot(legacy),"Plain Craft Launcher_v2.99.0.0_原版备份_20261020_163521.bak"),original);
        var upgrade=FileSafety.Inspect(legacy);Ok(upgrade.LegacyBackup!=null&&upgrade.BaseHash==hash,"1.x backup matched for upgrade");FileSafety.Apply(legacy,false);Ok(FileSafety.Inspect(legacy).Complete,"1.x upgraded without stacking");FileSafety.Restore(legacy);Ok(FileSafety.Hash(File.ReadAllBytes(legacy))==hash,"upgrade restores true original, not old patched bytes");
        string missing=Child(root,"legacy-missing",old.Patched);Reject(()=>FileSafety.Inspect(missing),"legacy without original backup rejected");
        string broken=Child(root,"unknown-pattern",original);using(var m=SemanticPatcher.Read(original)){var f=SemanticPatcher.Method(SemanticPatcher.Type(m,"PCL.PageDownloadInstall"),"LoaderInit",0);f.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="关闭下载").Operand="unexpected";m.Write(broken);}Reject(()=>FileSafety.Inspect(broken),"unknown restriction pattern rejected");
        Ok(FileSafety.Hash(File.ReadAllBytes(a[0]))==hash,"user original fixture unchanged");
        Console.WriteLine("ENGINE_TESTS_OK "+count+" checks; repaired fixture: "+copy);Console.WriteLine("OUTPUT_SHA256 "+copyHash);
    }
}
