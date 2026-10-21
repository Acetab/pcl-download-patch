using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody=Mono.Cecil.Cil.MethodBody;

namespace DownloadRepair {
    public sealed class PaletteOperand { public string Kind; public object Value; }
    public sealed class PaletteInstruction { public string Op; public PaletteOperand Operand; }
    public sealed class PaletteHandler { public string Type,CatchType;public int TryStart,TryEnd,HandlerStart,HandlerEnd,FilterStart; }
    public sealed class PaletteField { public string FullName,Name,DeclaringType,Type,Data;public int ClassSize,PackingSize; }
    public sealed class PaletteTemplate { public int Format,MaxStackSize;public string SourceSha256,Method;public bool InitLocals;public string[] Locals;public PaletteInstruction[] Code;public PaletteHandler[] Handlers;public PaletteField[] DataFields; }
    public sealed class ThemeRecord { public int Format=1;public string Refresh,Check,Controls,ControlsMethod; }
    public sealed class ThemeResult { public byte[] Bytes;public bool AlreadyUnlocked; }
    public static class ThemePatcher {
        public const string Marker="Acetab.PclDownloadRepair.Themes.v1";
        public const string PaletteHash="02e882a8f90f333c3451656978fc8248c35e3e423a92f1aa4931640118200e5d";
        const string RefreshKey="System.Void PCL.ModSecret::ThemeRefresh(System.Int32)";
        const string CheckKey="System.Boolean PCL.ModSecret::ThemeCheckOne(System.Int32)";
        static void Need(bool b,string text){SemanticPatcher.Require(b,text);}
        static MethodDefinition Refresh(ModuleDefinition m){return SemanticPatcher.Method(SemanticPatcher.Type(m,"PCL.ModSecret"),"ThemeRefresh",1);}
        static MethodDefinition Check(ModuleDefinition m){return SemanticPatcher.Method(SemanticPatcher.Type(m,"PCL.ModSecret"),"ThemeCheckOne",1);}
        static MethodDefinition Controls(ModuleDefinition m){var task=SemanticPatcher.Method(SemanticPatcher.Type(m,"PCL.ModSecret"),"ThemeCheckAll",1);var refs=task.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldftn).Select(i=>i.Operand as MethodReference).ToArray();Need(refs.Length==1&&refs[0]!=null&&refs[0].DeclaringType.FullName.StartsWith("PCL.ModSecret/"),"主题刷新回调结构改变");var r=refs[0];return SemanticPatcher.Type(m,r.DeclaringType.FullName).Methods.Single(x=>x.FullName==r.FullName);}
        static bool AlwaysTrue(MethodDefinition f){var c=f.Body.Instructions.Where(i=>i.OpCode!=OpCodes.Nop).ToArray();return c.Length==2&&c[0].OpCode==OpCodes.Ldc_I4_1&&c[1].OpCode==OpCodes.Ret;}
        public static bool IsUnlocked(byte[] input){using(var m=SemanticPatcher.Read(input)){var marks=m.Resources.Where(r=>r.Name==Marker).ToArray();if(marks.Length==0)return false;Need(marks.Length==1&&marks[0] is EmbeddedResource,"主题修复标记无效");var record=new JavaScriptSerializer().Deserialize<ThemeRecord>(Encoding.UTF8.GetString(((EmbeddedResource)marks[0]).GetResourceData()));var shapes=SemanticPatcher.MethodShapes(m);Need(record!=null&&record.Format==1&&record.ControlsMethod==Controls(m).FullName&&AlwaysTrue(Check(m))&&shapes[RefreshKey]==record.Refresh&&shapes[CheckKey]==record.Check&&shapes[record.ControlsMethod]==record.Controls,"主题代码与修复标记不一致，请使用修复前备份");return true;}}
        public static ThemeResult Apply(byte[] input) {
            if(IsUnlocked(input))return new ThemeResult{Bytes=input,AlreadyUnlocked=true};
            using(var m=SemanticPatcher.Read(input)) {
                var before=SemanticPatcher.MethodShapes(m);var resources=m.Resources.OfType<EmbeddedResource>().ToDictionary(x=>x.Name,x=>FileSafety.Hash(x.GetResourceData()));
                var refresh=Refresh(m);var check=Check(m);var controls=Controls(m);
                Need(refresh.ReturnType.FullName=="System.Void"&&refresh.Parameters[0].ParameterType.FullName=="System.Int32"&&check.ReturnType.FullName=="System.Boolean"&&check.Parameters[0].ParameterType.FullName=="System.Int32","主题方法签名不匹配");
                Need(check.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="get_CurrentRank")&&check.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="UiLauncherThemeHide2"),"主题资格判断不是已识别的原始结构");
                Need(controls.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="ThemeCheckOne")&&controls.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name=="FrmSetupUI"),"主题控件刷新结构不匹配");
                var payload=LoadTemplate();var members=Index(m);AddDataFields(m,payload,members);AddArrayOperator(m,members);
                Need(refresh.Body.Instructions.Take(2).Any(i=>i.OpCode==OpCodes.Starg||i.OpCode==OpCodes.Starg_S)||refresh.Body.Instructions.Any(i=>i.OpCode==OpCodes.Switch&&((Instruction[])i.Operand).Length==15),"主题配色布局改变，当前主题模块停止");
                RestorePalette(m,refresh,payload,members);
                check.Body=new MethodBody(check);check.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_1);check.Body.GetILProcessor().Emit(OpCodes.Ret);
                RestoreControls(m,controls);
                var shapes=SemanticPatcher.MethodShapes(m);var record=new ThemeRecord{Refresh=shapes[RefreshKey],Check=shapes[CheckKey],Controls=shapes[controls.FullName],ControlsMethod=controls.FullName};
                m.Resources.Add(new EmbeddedResource(Marker,ManifestResourceAttributes.Private,Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(record))));
                byte[] output;using(var stream=new MemoryStream()){m.Write(stream,new WriterParameters{Timestamp=0,DeterministicMvid=true});output=stream.ToArray();}
                using(var saved=SemanticPatcher.Read(output)) {
                    var after=SemanticPatcher.MethodShapes(saved);foreach(var p in before)if(p.Key!=RefreshKey&&p.Key!=CheckKey&&p.Key!=controls.FullName)Need(after.ContainsKey(p.Key)&&after[p.Key]==p.Value,"非主题方法发生变化："+p.Key);
                    Need(after.Count==before.Count,"主题模块不应增加或删除方法");
                    foreach(var r in saved.Resources.OfType<EmbeddedResource>().Where(r=>r.Name!=Marker))Need(resources.ContainsKey(r.Name)&&resources[r.Name]==FileSafety.Hash(r.GetResourceData()),"原有资源发生变化："+r.Name);
                    Need(saved.Resources.Count==m.Resources.Count,"主题模块导致资源丢失");
                }
                Need(IsUnlocked(output),"生成后的主题恢复复查失败");return new ThemeResult{Bytes=output};
            }
        }
        static PaletteTemplate LoadTemplate(){using(var r=Assembly.GetExecutingAssembly().GetManifestResourceStream("theme-palette.json"))using(var stream=new MemoryStream()){Need(r!=null,"主题配色资源缺失");r.CopyTo(stream);byte[] b=stream.ToArray();Need(FileSafety.Hash(b)==PaletteHash,"主题配色资源校验失败");var t=new JavaScriptSerializer().Deserialize<PaletteTemplate>(Encoding.UTF8.GetString(b));Need(t.Format==1&&t.Method==RefreshKey&&t.Code.Length>200&&t.Code.Length<1000,"主题配色模板格式无效");return t;}}
        static Dictionary<string,MemberReference> Index(ModuleDefinition m){var d=new Dictionary<string,MemberReference>();Action<MemberReference> add=x=>{if(x!=null&&!d.ContainsKey(x.FullName))d.Add(x.FullName,x);};foreach(var x in m.GetTypeReferences())add(x);foreach(var x in m.GetMemberReferences())add(x);foreach(var t in m.GetTypes()){add(t);foreach(var f in t.Fields){add(f);add(f.FieldType);}foreach(var f in t.Methods){add(f);add(f.ReturnType);foreach(var p in f.Parameters)add(p.ParameterType);if(!f.HasBody)continue;foreach(var v in f.Body.Variables)add(v.VariableType);foreach(var i in f.Body.Instructions)add(i.Operand as MemberReference);}}return d;}
        static T Ref<T>(Dictionary<string,MemberReference> map,string name)where T:MemberReference{MemberReference r;Need(map.TryGetValue(name,out r)&&r is T,"主题引用缺失："+name);return (T)r;}
        static void AddDataFields(ModuleDefinition m,PaletteTemplate t,Dictionary<string,MemberReference> map){foreach(var p in t.DataFields){byte[] data=Convert.FromBase64String(p.Data);Need(data.Length==p.ClassSize&&data.Length<=128,"主题渐变数据无效");MemberReference old;if(map.TryGetValue(p.FullName,out old)){var f=old as FieldDefinition;Need(f!=null&&FileSafety.Hash(f.InitialValue)==FileSafety.Hash(data),"主题渐变数据冲突");continue;}var owner=SemanticPatcher.Type(m,p.DeclaringType);TypeDefinition type=m.GetTypes().SingleOrDefault(x=>x.FullName==p.Type);if(type==null){Need(p.Type.StartsWith(owner.FullName+"/")&&p.PackingSize==1,"主题渐变结构不匹配");type=new TypeDefinition("",p.Type.Substring(owner.FullName.Length+1),Mono.Cecil.TypeAttributes.NestedPrivate|Mono.Cecil.TypeAttributes.ExplicitLayout|Mono.Cecil.TypeAttributes.Sealed,m.ImportReference(typeof(ValueType))){ClassSize=p.ClassSize,PackingSize=(short)p.PackingSize};owner.NestedTypes.Add(type);}Need(type.ClassSize==p.ClassSize,"主题渐变结构大小不匹配");var field=new FieldDefinition(p.Name,Mono.Cecil.FieldAttributes.Assembly|Mono.Cecil.FieldAttributes.Static|Mono.Cecil.FieldAttributes.InitOnly,type){InitialValue=data};owner.Fields.Add(field);map[p.Type]=type;map[p.FullName]=field;}}
        static void AddArrayOperator(ModuleDefinition m,Dictionary<string,MemberReference> map){string key="MeloongCore.OneOf`2<!0,!1> MeloongCore.OneOf`2<System.Int32,System.Int32[]>::op_Implicit(!1)";if(map.ContainsKey(key))return;var scalar=Ref<MethodReference>(map,"MeloongCore.OneOf`2<!0,!1> MeloongCore.OneOf`2<System.Int32,System.Int32[]>::op_Implicit(!0)");var owner=(GenericInstanceType)scalar.DeclaringType;while(owner.ElementType.GenericParameters.Count<2)owner.ElementType.GenericParameters.Add(new GenericParameter("T"+owner.ElementType.GenericParameters.Count,owner.ElementType));var op=new MethodReference(scalar.Name,scalar.ReturnType,owner){HasThis=false};op.Parameters.Add(new ParameterDefinition(owner.ElementType.GenericParameters[1]));Need(op.FullName==key,"主题渐变转换签名不匹配");map[key]=op;}
        static void RestorePalette(ModuleDefinition m,MethodDefinition f,PaletteTemplate t,Dictionary<string,MemberReference> map){var ops=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static).Where(x=>x.FieldType==typeof(OpCode)).Select(x=>(OpCode)x.GetValue(null)).ToDictionary(x=>x.Name);var b=new MethodBody(f){InitLocals=t.InitLocals,MaxStackSize=t.MaxStackSize};f.Body=b;foreach(string name in t.Locals)b.Variables.Add(new VariableDefinition(Ref<TypeReference>(map,name)));foreach(var item in t.Code){OpCode op;Need(ops.TryGetValue(item.Op,out op),"未知主题指令");var i=Instruction.Create(OpCodes.Nop);i.OpCode=op;b.Instructions.Add(i);}for(int k=0;k<t.Code.Length;k++){var p=t.Code[k].Operand;object value=p.Value;switch(p.Kind){case "none":value=null;break;case "member":value=Ref<MemberReference>(map,(string)value);break;case "branch":value=b.Instructions[Convert.ToInt32(value)];break;case "switch":value=((System.Collections.IEnumerable)value).Cast<object>().Select(x=>b.Instructions[Convert.ToInt32(x)]).ToArray();break;case "local":value=b.Variables[Convert.ToInt32(value)];break;case "arg":value=f.Parameters[Convert.ToInt32(value)];break;case "double":value=Convert.ToDouble(value);break;case "float":value=Convert.ToSingle(value);break;case "long":value=Convert.ToInt64(value);break;case "int":value=b.Instructions[k].OpCode.OperandType==OperandType.ShortInlineI?(object)Convert.ToSByte(value):Convert.ToInt32(value);break;case "string":break;default:throw new InvalidDataException("未知主题操作数");}b.Instructions[k].Operand=value;}
            Func<int,Instruction> at=n=>n<0?null:b.Instructions[n];foreach(var h in t.Handlers)b.ExceptionHandlers.Add(new ExceptionHandler((ExceptionHandlerType)Enum.Parse(typeof(ExceptionHandlerType),h.Type)){TryStart=at(h.TryStart),TryEnd=at(h.TryEnd),HandlerStart=at(h.HandlerStart),HandlerEnd=at(h.HandlerEnd),FilterStart=at(h.FilterStart),CatchType=h.CatchType==null?null:Ref<TypeReference>(map,h.CatchType)});
        }
        static void RestoreControls(ModuleDefinition m,MethodDefinition f){var secret=SemanticPatcher.Type(m,"PCL.ModSecret");var setup=SemanticPatcher.Type(m,"PCL.PageSetupUI");var ui=SemanticPatcher.Type(m,"PCL.ModMain").Fields.Single(x=>x.Name=="FrmSetupUI");var effect=f.DeclaringType.Fields.Single(x=>x.Name=="$VB$Local_EffectSetup");var animation=SemanticPatcher.Type(m,"PCL.ModAnimation");var getAni=SemanticPatcher.Method(animation,"get_AniControlEnabled",0);var setAni=SemanticPatcher.Method(animation,"set_AniControlEnabled",1);
            f.Body=new MethodBody(f);var b=f.Body;var il=b.GetILProcessor();var end=Instruction.Create(OpCodes.Ret);var start=Instruction.Create(OpCodes.Ldsfld,ui);var cleanup=Instruction.Create(OpCodes.Call,getAni);
            il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,effect);il.Emit(OpCodes.Brfalse,end);il.Emit(OpCodes.Ldsfld,ui);il.Emit(OpCodes.Brfalse,end);
            il.Emit(OpCodes.Call,getAni);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Add);il.Emit(OpCodes.Call,setAni);il.Append(start);
            var enabled=m.ImportReference(typeof(System.Windows.UIElement).GetProperty("IsEnabled").GetSetMethod());
            for(int n=0;n<=14;n++){var get=SemanticPatcher.Method(setup,"get_RadioLauncherTheme"+n,0);if(n!=0)il.Emit(OpCodes.Ldsfld,ui);il.Emit(OpCodes.Callvirt,get);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Callvirt,enabled);}
            il.Emit(OpCodes.Ldsfld,ui);il.Emit(OpCodes.Callvirt,SemanticPatcher.Method(setup,"get_RadioLauncherTheme5",0));il.Emit(OpCodes.Ldc_R8,1.0);il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(System.Windows.UIElement).GetProperty("Opacity").GetSetMethod()));
            foreach(string name in new[]{"get_RadioLauncherTheme5Gray","get_LabLauncherTheme5Unlock","get_LabLauncherTheme8Copy","get_LabLauncherTheme9Copy","get_LabLauncherTheme11Click"}){il.Emit(OpCodes.Ldsfld,ui);il.Emit(OpCodes.Callvirt,SemanticPatcher.Method(setup,name,0));il.Emit(OpCodes.Ldc_I4_2);il.Emit(OpCodes.Callvirt,m.ImportReference(typeof(System.Windows.UIElement).GetProperty("Visibility").GetSetMethod()));}
            il.Emit(OpCodes.Leave,end);il.Append(cleanup);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Sub);il.Emit(OpCodes.Call,setAni);il.Emit(OpCodes.Endfinally);il.Append(end);b.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally){TryStart=start,TryEnd=cleanup,HandlerStart=cleanup,HandlerEnd=end});
        }
    }
}
