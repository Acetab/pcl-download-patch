using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Resources;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DownloadRepair {
    public sealed class FeatureState {
        public string Name; public bool Ready;
        public FeatureState(string name, bool ready) { Name=name; Ready=ready; }
    }
    public sealed class RewriteResult {
        public string Version; public bool Complete, Native; public byte[] Bytes;
        public List<FeatureState> Features=new List<FeatureState>();
    }
    // Only parses metadata/IL. Selected launcher code is never loaded into the patcher CLR.
    public static class SemanticPatcher {
        public const string Marker="Acetab.PclDownloadRepair.v2";
        public const string Worker="PCL.DownloadRepairWorkerV2";
        public static readonly string[] Installers={"InstallPackCurseForge","InstallPackHMCL","InstallPackMMC","InstallPackMCBBS","InstallPackModrinth","InstallPackLauncherPack","InstallPackCompress"};
        static readonly string[] Jobs={"PCL.PageDownloadInstall::LoaderInit/0","PCL.PageDownloadModify::LoadInstanceFinished/1","PCL.ModModpack::ModpackInstall/0","PCL.ModModpack::ModpackInstall/3","PCL.PageDownloadResourceDetail::InitializeStackResourceVersions/1"};
        public static void Require(bool condition,string reason) { if(!condition)throw new InvalidDataException(reason); }
        sealed class EmbeddedResolver:DefaultAssemblyResolver {
            readonly Dictionary<string,byte[]> embedded=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            public EmbeddedResolver() { foreach(string dir in GetSearchDirectories())RemoveSearchDirectory(dir);AddSearchDirectory(System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)); }
            public void Index(ModuleDefinition m) {
                foreach(var r in m.Resources.OfType<EmbeddedResource>().Where(x=>x.Name=="PCL.Resources.resources"))using(var reader=new ResourceReader(r.GetResourceStream())) {
                    var entries=reader.GetEnumerator();while(entries.MoveNext()) {
                        // GetResourceData avoids ResourceReader.Value object deserialization.
                        string kind;byte[] raw;reader.GetResourceData((string)entries.Key,out kind,out raw);
                        if(kind!="ResourceTypeCode.ByteArray"||raw.Length<6)continue;
                        int length=BitConverter.ToInt32(raw,0);if(length!=raw.Length-4||raw[4]!=77||raw[5]!=90)continue;
                        var bytes=new byte[length];Buffer.BlockCopy(raw,4,bytes,0,length);embedded[(string)entries.Key]=bytes;
                    }
                }
            }
            public override AssemblyDefinition Resolve(AssemblyNameReference name) {
                byte[] bytes;if(embedded.TryGetValue(name.Name,out bytes)) { var a=AssemblyDefinition.ReadAssembly(new MemoryStream(bytes,false),new ReaderParameters{InMemory=true,AssemblyResolver=this});Require(a.Name.Name==name.Name,"内嵌依赖身份不匹配");RegisterAssembly(a);return a; }
                Require(name.Name=="mscorlib"||name.Name=="netstandard"||name.Name.StartsWith("System")||name.Name.StartsWith("Microsoft.")||new[]{"WindowsBase","PresentationCore","PresentationFramework"}.Contains(name.Name),"未找到启动器内嵌依赖："+name.Name);
                return base.Resolve(name);
            }
        }
        public static ModuleDefinition Read(byte[] b) { var resolver=new EmbeddedResolver();var m=ModuleDefinition.ReadModule(new MemoryStream(b,false),new ReaderParameters{InMemory=true,ReadingMode=ReadingMode.Deferred,AssemblyResolver=resolver});resolver.Index(m);return m; }
        public static TypeDefinition Type(ModuleDefinition m,string name) { var t=m.GetTypes().Where(x=>x.FullName==name).ToArray();Require(t.Length==1,"类型结构不匹配："+name);return t[0]; }
        public static MethodDefinition Method(TypeDefinition t,string name,int args) { var a=t.Methods.Where(x=>x.Name==name&&x.Parameters.Count==args).ToArray();Require(a.Length==1&&a[0].HasBody,"方法结构不匹配："+t.Name+"."+name);return a[0]; }
        static bool Call(Instruction i,string name) { var r=i.Operand as MethodReference;return r!=null&&r.Name==name; }
        static bool Notice(Instruction i) { return i.OpCode==OpCodes.Ldstr&&((string)i.Operand).Contains("不再提供 MC 下载"); }
        static bool Loader(FieldReference f) { return f!=null&&f.DeclaringType.FullName=="PCL.ModDownload"&&f.Name=="DlClientListLoader"; }
        static int Number(Instruction i) { switch(i.OpCode.Code) { case Code.Ldc_I4_0:return 0;case Code.Ldc_I4_1:return 1;case Code.Ldc_I4_2:return 2;case Code.Ldc_I4_3:return 3;case Code.Ldc_I4_4:return 4;case Code.Ldc_I4_5:return 5;case Code.Ldc_I4_M1:return -1;case Code.Ldc_I4:case Code.Ldc_I4_S:return Convert.ToInt32(i.Operand);default:return Int32.MinValue; } }
        static FieldDefinition Flag(ModuleDefinition m) { return Type(m,"PCL.ModSecret").Fields.SingleOrDefault(x=>x.Name=="RestrictMcDownload"); }
        static MethodDefinition CardMethod(ModuleDefinition m) { var a=Type(m,"PCL.PageDownloadModify").Methods.Where(x=>x.Name=="LoadInstanceFinished"&&x.HasBody).ToArray();Require(a.Length==1,"版本修改入口不唯一");return a[0]; }
        static Instruction CardValue(MethodDefinition f) { var a=f.Body.Instructions.Where(i=>Call(i,"get_CardMinecraft")).ToArray();Require(a.Length==1&&a[0].Next!=null&&a[0].Next.Next!=null&&Call(a[0].Next.Next,"set_Visibility"),"原版卡片结构不匹配");Require(Number(a[0].Next)==0||Number(a[0].Next)==2,"原版卡片状态不匹配");return a[0].Next; }
        static void Bounds(MethodDefinition f) {
            var list=f.Body.Instructions;
            foreach(var i in list) { var one=i.Operand as Instruction;var many=i.Operand as Instruction[];Require(one==null||list.Contains(one),"无效跳转："+f.Name);Require(many==null||many.All(list.Contains),"无效分派："+f.Name); }
            foreach(var h in f.Body.ExceptionHandlers)foreach(var i in new[]{h.TryStart,h.TryEnd,h.HandlerStart,h.HandlerEnd,h.FilterStart})Require(i==null||list.Contains(i),"异常处理边界无效："+f.Name);
        }
        static void LongBranches(MethodDefinition f) {
            foreach(var i in f.Body.Instructions) switch(i.OpCode.Code) {
                case Code.Br_S:i.OpCode=OpCodes.Br;break;case Code.Brfalse_S:i.OpCode=OpCodes.Brfalse;break;case Code.Brtrue_S:i.OpCode=OpCodes.Brtrue;break;
                case Code.Beq_S:i.OpCode=OpCodes.Beq;break;case Code.Bne_Un_S:i.OpCode=OpCodes.Bne_Un;break;case Code.Bge_S:i.OpCode=OpCodes.Bge;break;case Code.Bge_Un_S:i.OpCode=OpCodes.Bge_Un;break;
                case Code.Bgt_S:i.OpCode=OpCodes.Bgt;break;case Code.Bgt_Un_S:i.OpCode=OpCodes.Bgt_Un;break;case Code.Ble_S:i.OpCode=OpCodes.Ble;break;case Code.Ble_Un_S:i.OpCode=OpCodes.Ble_Un;break;
                case Code.Blt_S:i.OpCode=OpCodes.Blt;break;case Code.Blt_Un_S:i.OpCode=OpCodes.Blt_Un;break;case Code.Leave_S:i.OpCode=OpCodes.Leave;break;
            }
        }
        static List<FeatureState> States(ModuleDefinition m) {
            var install=Method(Type(m,"PCL.PageDownloadInstall"),"LoaderInit",0);
            bool vanilla=install.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldsfld&&Loader(i.Operand as FieldReference));
            Require(vanilla||install.Body.Instructions.Count(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="关闭下载")==1,"下载页不是已识别的原版/停用结构");
            var modpack=Type(m,"PCL.ModModpack");var dialog=Method(modpack,"ModpackInstall",0);var dispatch=Method(modpack,"ModpackInstall",3);
            bool select=dialog.Body.Instructions.Any(i=>Call(i,"SelectFile")||Call(i,"ShowDialog"));
            Require(select||dialog.Body.Instructions.Count(Notice)==1,"整合包选择入口结构不匹配");
            bool routes=Installers.All(n=>dispatch.Body.Instructions.Count(i=>Call(i,n))==1);
            Require(routes||dispatch.Body.Instructions.Count(Notice)==1,"整合包安装分支结构不匹配");
            var detail=Method(Type(m,"PCL.PageDownloadResourceDetail"),"InitializeStackResourceVersions",1);
            bool resource=detail.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldftn&&Call(i,"Install_Click"));
            Require(resource||detail.Body.Instructions.Count(i=>i.OpCode==OpCodes.Ldftn&&Call(i,"Save_Click"))==1,"资源详情页结构不匹配");
            return new List<FeatureState>{new FeatureState("原版游戏下载页",vanilla),new FeatureState("版本修改页的原版卡片",Number(CardValue(CardMethod(m)))==0),new FeatureState("本地整合包选择入口",select),new FeatureState("七类整合包识别与安装",routes),new FeatureState("资源详情页的整合包安装",resource)};
        }
        public static RewriteResult Rewrite(byte[] input) {
            ValidatePe(input);
            using(var m=Read(input)) {
                Require(m.Assembly.Name.Name=="Plain Craft Launcher 2","所选程序不是支持的 PCL 官方分支");
                Require(!m.Assembly.Name.HasPublicKey&&(m.Attributes&ModuleAttributes.StrongNameSigned)==0,"暂不处理带强名称签名的启动器");
                var result=new RewriteResult{Version=m.Assembly.Name.Version.ToString(),Bytes=input};
                result.Features=States(m);var flag=Flag(m);
                bool marker=m.Resources.Any(x=>x.Name==Marker);
                result.Complete=result.Features.All(x=>x.Ready);
                if(result.Complete) {
                    Require(flag==null||(flag.HasConstant&&flag.Constant is bool&&!(bool)flag.Constant)||!marker,"修复标记与下载开关状态不一致");
                    result.Native=!marker;return result;
                }
                Require(!marker,"修复标记存在，但部分入口已损坏或被其他工具修改");
                Require(result.Features.All(x=>!x.Ready),"检测到部分入口已修改，请先使用原补丁还原，或选择未修改的启动器");
                Require(flag!=null&&flag.HasConstant&&flag.Constant is bool&&(bool)flag.Constant,"下载限制常量与入口结构不一致");
                var before=MethodShapes(m);var resources=ResourceHashes(m);
                RestoreVanilla(m);RestoreCard(m);RestoreDispatch(m);RestoreDialog(m);RestoreResource(m);
                flag.Constant=false;
                foreach(var f in m.GetTypes().SelectMany(t=>t.Methods).Where(f=>f.HasBody)) if(IsJob(f)||f.DeclaringType.FullName==Worker) { LongBranches(f);Bounds(f); }
                Require(States(m).All(x=>x.Ready),"内存中的恢复结果未通过检查");
                m.Resources.Add(new EmbeddedResource(Marker,ManifestResourceAttributes.Private,Encoding.UTF8.GetBytes("format=2\ntool=2.1.1\noriginal="+FileSafety.Hash(input)+"\n")));
                using(var output=new MemoryStream()) { m.Write(output,new WriterParameters{Timestamp=0,DeterministicMvid=true});result.Bytes=output.ToArray(); }
                using(var saved=Read(result.Bytes)) {
                    Require(States(saved).All(x=>x.Ready),"生成后的入口校验失败");
                    Require(saved.Resources.Count(x=>x.Name==Marker)==1,"生成后的修复标记无效");
                    var after=MethodShapes(saved);foreach(var pair in before)if(!IsJobKey(pair.Key))Require(after.ContainsKey(pair.Key)&&after[pair.Key]==pair.Value,"非目标方法发生变化："+pair.Key);
                    foreach(var pair in ResourceHashes(saved).Where(x=>x.Key!=Marker))Require(resources.ContainsKey(pair.Key)&&resources[pair.Key]==pair.Value,"原有资源发生变化："+pair.Key);
                    Require(ResourceHashes(saved).Count==resources.Count+1,"原有资源丢失");
                    foreach(var f in saved.GetTypes().SelectMany(t=>t.Methods).Where(f=>f.HasBody))Bounds(f);
                }
                return result;
            }
        }
        static void RestoreVanilla(ModuleDefinition m) {
            var f=Method(Type(m,"PCL.PageDownloadInstall"),"LoaderInit",0);var code=f.Body.Instructions;
            Require(f.Body.ExceptionHandlers.Count==0,"下载页出现未知异常处理结构");
            var begin=code.Single(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="关闭下载");
            var end=code.Where(i=>i.OpCode==OpCodes.Newobj&&i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Name.StartsWith("LoaderTask`2")).ToArray();
            Require(end.Length==1,"停用加载器构造不唯一");int a=code.IndexOf(begin),b=code.IndexOf(end[0]);Require(b>a,"停用加载器顺序异常");
            var remove=new HashSet<Instruction>(code.Skip(a+1).Take(b-a));
            foreach(var i in code.Where(i=>!remove.Contains(i))) { var dest=i.Operand as Instruction;var branches=i.Operand as Instruction[];Require(dest==null||!remove.Contains(dest),"下载页存在跨入待替换区域的跳转");Require(branches==null||!branches.Any(remove.Contains),"下载页分派跨入待替换区域"); }
            var field=Type(m,"PCL.ModDownload").Fields.Single(x=>x.Name=="DlClientListLoader");
            begin.OpCode=OpCodes.Ldsfld;begin.Operand=field;foreach(var i in remove)code.Remove(i);
        }
        static void RestoreCard(ModuleDefinition m) { var i=CardValue(CardMethod(m));Require(Number(i)==2,"卡片已被其他程序修改");i.OpCode=OpCodes.Ldc_I4_0;i.Operand=null; }
        static void RestoreDispatch(ModuleDefinition m) {
            var t=Type(m,"PCL.ModModpack");var f=Method(t,"ModpackInstall",3);var b=f.Body;var il=b.GetILProcessor();
            Require(b.Variables.Count>4&&b.Variables[0].VariableType.Name.StartsWith("LoaderCombo`1")&&b.Variables[1].VariableType.FullName=="System.IO.Compression.ZipArchive"&&b.Variables[2].VariableType.FullName=="System.String"&&b.Variables[4].VariableType.FullName=="System.Int32","整合包局部变量布局改变");
            var blocked=b.Instructions.Single(Notice);var ctor=blocked.Next;var throwing=ctor==null?null:ctor.Next;
            Require(ctor!=null&&ctor.OpCode==OpCodes.Newobj&&Call(ctor,".ctor")&&throwing!=null&&throwing.OpCode==OpCodes.Throw,"整合包限制异常结构改变");
            var outer=b.ExceptionHandlers.Where(e=>e.HandlerType==ExceptionHandlerType.Finally&&e.HandlerEnd==null).ToArray();
            Require(outer.Length==1&&outer[0].TryEnd==throwing.Next&&outer[0].HandlerStart==throwing.Next&&b.Instructions.Last().OpCode==OpCodes.Endfinally,"压缩包释放边界改变");
            var targets=Installers.Select(n=>t.Methods.Single(x=>x.Name==n)).ToArray();
            foreach(var target in targets) {
                var supplied=new[]{f.Parameters[0].ParameterType,b.Variables[1].VariableType,b.Variables[2].VariableType,f.Parameters[1].ParameterType,f.Parameters[2].ParameterType};
                Require(target.IsStatic&&target.Parameters.Count>=2&&target.Parameters.Count<=5&&target.ReturnType.FullName==f.ReturnType.FullName,"整合包安装器签名改变："+target.Name);
                for(int k=0;k<target.Parameters.Count;k++)Require(target.Parameters[k].ParameterType.FullName==supplied[k].FullName,"整合包安装器参数改变："+target.Name);
            }
            var finish=Instruction.Create(OpCodes.Ldloc,b.Variables[0]);il.Append(finish);il.Append(Instruction.Create(OpCodes.Ret));outer[0].HandlerEnd=finish;
            var labels=targets.Select(x=>Instruction.Create(OpCodes.Nop)).ToArray();
            // Keep the destination instruction itself: existing leave/catch boundaries target it.
            blocked.OpCode=OpCodes.Ldloc;blocked.Operand=b.Variables[4];il.Remove(ctor);il.Remove(throwing);
            var add=new List<Instruction>{Instruction.Create(OpCodes.Switch,labels.Take(5).ToArray()),Instruction.Create(OpCodes.Ldloc,b.Variables[4]),Instruction.Create(OpCodes.Ldc_I4,9),Instruction.Create(OpCodes.Beq,labels[5]),Instruction.Create(OpCodes.Br,labels[6])};
            for(int k=0;k<targets.Length;k++) {
                add.Add(labels[k]);add.Add(Instruction.Create(OpCodes.Ldarg,f.Parameters[0]));add.Add(Instruction.Create(OpCodes.Ldloc,b.Variables[1]));
                if(targets[k].Parameters.Count>2)add.Add(Instruction.Create(OpCodes.Ldloc,b.Variables[2]));
                if(targets[k].Parameters.Count>3)add.Add(Instruction.Create(OpCodes.Ldarg,f.Parameters[1]));
                if(targets[k].Parameters.Count>4)add.Add(Instruction.Create(OpCodes.Ldarg,f.Parameters[2]));
                add.Add(Instruction.Create(OpCodes.Call,targets[k]));add.Add(Instruction.Create(OpCodes.Stloc,b.Variables[0]));add.Add(Instruction.Create(OpCodes.Leave,finish));
            }
            foreach(var i in add)il.InsertBefore(outer[0].HandlerStart,i);
        }
        static void RestoreDialog(ModuleDefinition m) {
            var t=Type(m,"PCL.ModModpack");var f=Method(t,"ModpackInstall",0);
            Require(f.Body.ExceptionHandlers.Count==0&&f.Body.Instructions.Count(Notice)==1&&f.Body.Instructions.Count<=12,"整合包选择入口不是预期的停用结构");
            Require(!m.GetTypes().Any(x=>x.FullName==Worker),"整合包工作类型名冲突");
            var w=new TypeDefinition("PCL","DownloadRepairWorkerV2",TypeAttributes.NotPublic|TypeAttributes.Sealed|TypeAttributes.BeforeFieldInit,m.TypeSystem.Object);m.Types.Add(w);
            var file=new FieldDefinition("ArchivePath",FieldAttributes.Public,m.TypeSystem.String);w.Fields.Add(file);
            var ctor=new MethodDefinition(".ctor",MethodAttributes.Public|MethodAttributes.SpecialName|MethodAttributes.RTSpecialName,m.TypeSystem.Void);w.Methods.Add(ctor);
            var c=ctor.Body.GetILProcessor();c.Emit(OpCodes.Ldarg_0);c.Emit(OpCodes.Call,m.ImportReference(typeof(object).GetConstructor(System.Type.EmptyTypes)));c.Emit(OpCodes.Ret);
            var run=new MethodDefinition("Install",MethodAttributes.Public,m.TypeSystem.Void);w.Methods.Add(run);var r=run.Body.GetILProcessor();
            r.Emit(OpCodes.Ldarg_0);r.Emit(OpCodes.Ldfld,file);r.Emit(OpCodes.Ldnull);r.Emit(OpCodes.Ldnull);r.Emit(OpCodes.Call,Method(t,"ModpackInstall",3));r.Emit(OpCodes.Pop);r.Emit(OpCodes.Ret);
            var threading=Method(Type(m,"PCL.ModBase"),"RunInThread",1);Require(threading.Parameters[0].ParameterType.FullName=="System.Action","后台安装入口签名改变");
            f.Body=new MethodBody(f);f.Body.InitLocals=true;var d=new VariableDefinition(m.ImportReference(typeof(Microsoft.Win32.OpenFileDialog)));var accepted=new VariableDefinition(m.ImportReference(typeof(bool?)));var worker=new VariableDefinition(w);f.Body.Variables.Add(d);f.Body.Variables.Add(accepted);f.Body.Variables.Add(worker);
            var il=f.Body.GetILProcessor();var exit=Instruction.Create(OpCodes.Ret);
            il.Emit(OpCodes.Newobj,m.ImportReference(typeof(Microsoft.Win32.OpenFileDialog).GetConstructor(System.Type.EmptyTypes)));il.Emit(OpCodes.Stloc,d);
            il.Emit(OpCodes.Ldloc,d);il.Emit(OpCodes.Ldstr,"选择整合包压缩文件");il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(Microsoft.Win32.OpenFileDialog).GetProperty("Title").GetSetMethod()));
            il.Emit(OpCodes.Ldloc,d);il.Emit(OpCodes.Ldstr,"整合包文件|*.zip;*.mrpack;*.rar");il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(Microsoft.Win32.OpenFileDialog).GetProperty("Filter").GetSetMethod()));
            il.Emit(OpCodes.Ldloc,d);il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(Microsoft.Win32.OpenFileDialog).GetMethod("ShowDialog",System.Type.EmptyTypes)));il.Emit(OpCodes.Stloc,accepted);
            il.Emit(OpCodes.Ldloca,accepted);il.Emit(OpCodes.Call,m.ImportReference(typeof(bool?).GetMethod("GetValueOrDefault",System.Type.EmptyTypes)));il.Emit(OpCodes.Brfalse,exit);
            il.Emit(OpCodes.Newobj,ctor);il.Emit(OpCodes.Stloc,worker);il.Emit(OpCodes.Ldloc,worker);il.Emit(OpCodes.Ldloc,d);il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(Microsoft.Win32.OpenFileDialog).GetProperty("FileName").GetGetMethod()));il.Emit(OpCodes.Stfld,file);
            il.Emit(OpCodes.Ldloc,worker);il.Emit(OpCodes.Ldftn,run);il.Emit(OpCodes.Newobj,m.ImportReference(typeof(Action).GetConstructor(new[]{typeof(object),typeof(IntPtr)})));il.Emit(OpCodes.Call,threading);il.Append(exit);
        }
        static void RestoreResource(ModuleDefinition m) {
            var t=Type(m,"PCL.PageDownloadResourceDetail");var f=Method(t,"InitializeStackResourceVersions",1);var b=f.Body;var il=b.GetILProcessor();
            var pointer=b.Instructions.Single(i=>i.OpCode==OpCodes.Ldftn&&Call(i,"Save_Click"));var first=pointer.Previous;var ctor=pointer.Next;var empty=ctor==null?null:ctor.Next;
            Require(first!=null&&first.OpCode==OpCodes.Ldarg_0&&ctor.OpCode==OpCodes.Newobj&&empty!=null&&empty.OpCode==OpCodes.Ldnull&&empty.Next!=null,"资源列表事件绑定结构改变");
            var field=t.Fields.Single(x=>x.Name=="TargetResourceType");
            var categoryChecks=b.Instructions.Where(i=>i.OpCode==OpCodes.Ldfld&&i.Operand is FieldReference&&((FieldReference)i.Operand).FullName==field.FullName).ToArray();
            Require(categoryChecks.Any(i=>i.Next!=null&&Number(i.Next)==2),"资源类别的整合包编号改变");
            var install=t.Methods.Single(x=>x.Name=="Install_Click");var save=t.Methods.Single(x=>x.Name=="Save_Click");
            var icon=Type(m,"PCL.MyIconButton").NestedTypes.Single(x=>x.Name=="ClickEventHandler").Methods.Single(x=>x.IsConstructor);
            var start=Instruction.Create(OpCodes.Ldarg_0);var normal=Instruction.Create(OpCodes.Ldarg_0);var join=empty.Next;
            var add=new[]{start,Instruction.Create(OpCodes.Ldfld,field),Instruction.Create(OpCodes.Ldc_I4_2),Instruction.Create(OpCodes.Bne_Un,normal),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldftn,install),Instruction.Create(OpCodes.Newobj,(MethodReference)ctor.Operand),Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Ldftn,save),Instruction.Create(OpCodes.Newobj,icon),Instruction.Create(OpCodes.Br,join),normal};
            // Retarget every old incoming edge; neither a short branch nor an EH boundary may skip the new condition.
            foreach(var i in b.Instructions) { if(i.Operand==first)i.Operand=start;var a=i.Operand as Instruction[];if(a!=null)for(int k=0;k<a.Length;k++)if(a[k]==first)a[k]=start; }
            foreach(var h in b.ExceptionHandlers) { if(h.TryStart==first)h.TryStart=start;if(h.TryEnd==first)h.TryEnd=start;if(h.HandlerStart==first)h.HandlerStart=start;if(h.HandlerEnd==first)h.HandlerEnd=start; }
            foreach(var i in add)il.InsertBefore(first,i);
            // The 'normal' ldarg.0 replaces the original first instruction.
            first.OpCode=OpCodes.Nop;
        }
        static string Key(MethodDefinition f) { return f.DeclaringType.FullName+"::"+f.Name+"/"+f.Parameters.Count; }
        static bool IsJob(MethodDefinition f) { return Jobs.Contains(Key(f))||(f.DeclaringType.FullName=="PCL.PageDownloadModify"&&f.Name=="LoadInstanceFinished"); }
        static bool IsJobKey(string full) { return full.Contains("PCL.PageDownloadInstall::LoaderInit(")||full.Contains("PCL.PageDownloadModify::LoadInstanceFinished(")||full.Contains("PCL.ModModpack::ModpackInstall(")||full.Contains("PCL.PageDownloadResourceDetail::InitializeStackResourceVersions("); }
        public static Dictionary<string,string> MethodShapes(ModuleDefinition m) {
            var result=new Dictionary<string,string>();
            foreach(var f in m.GetTypes().SelectMany(t=>t.Methods)) {
                if(!f.HasBody) { result[f.FullName]="abstract/native";continue; }
                var b=f.Body;var text=new StringBuilder();text.Append(b.InitLocals);foreach(var v in b.Variables)text.Append('|').Append(v.VariableType.FullName);
                foreach(var i in b.Instructions) { text.Append(';').Append(i.OpCode.Name).Append(':');var member=i.Operand as MemberReference;var target=i.Operand as Instruction;var many=i.Operand as Instruction[];var local=i.Operand as VariableDefinition;var arg=i.Operand as ParameterDefinition;
                    if(member!=null)text.Append(member.FullName);else if(target!=null)text.Append('@').Append(b.Instructions.IndexOf(target));else if(many!=null)foreach(var a in many)text.Append('@').Append(b.Instructions.IndexOf(a));else if(local!=null)text.Append('v').Append(local.Index);else if(arg!=null)text.Append('a').Append(arg.Index);else text.Append(i.Operand);
                }
                foreach(var e in b.ExceptionHandlers)text.Append("EH").Append(e.HandlerType).Append(':').Append(b.Instructions.IndexOf(e.TryStart)).Append(':').Append(b.Instructions.IndexOf(e.TryEnd)).Append(':').Append(b.Instructions.IndexOf(e.HandlerStart)).Append(':').Append(b.Instructions.IndexOf(e.HandlerEnd)).Append(':').Append(b.Instructions.IndexOf(e.FilterStart)).Append(':').Append(e.CatchType==null?"":e.CatchType.FullName);
                result[f.FullName]=FileSafety.Hash(Encoding.UTF8.GetBytes(text.ToString()));
            }
            return result;
        }
        static Dictionary<string,string> ResourceHashes(ModuleDefinition m) { return m.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>FileSafety.Hash(r.GetResourceData())); }
        static void ValidatePe(byte[] b) {
            Require(b.Length>=256&&BitConverter.ToUInt16(b,0)==0x5a4d,"请选择有效的 Windows 启动器 EXE");int pe=BitConverter.ToInt32(b,60);Require(pe>=64&&pe+24<b.Length&&BitConverter.ToUInt32(b,pe)==0x4550,"PE 文件头损坏");int opt=pe+24;int magic=BitConverter.ToUInt16(b,opt);Require(magic==0x10b||magic==0x20b,"未知 PE 格式");int security=opt+(magic==0x10b?96:112)+32;Require(security+8<=b.Length,"PE 数据目录损坏");Require(BitConverter.ToUInt32(b,security)==0&&BitConverter.ToUInt32(b,security+4)==0,"暂不处理带数字签名的启动器");
        }
    }
}
