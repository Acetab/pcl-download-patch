using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing;
using System.Reflection;

[assembly: AssemblyTitle("PCL 下载功能补丁（第三方）")]
[assembly: AssemblyDescription("独立补丁工具，内置说明、备份和还原。非 PCL 官方发布。")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

// Reads PE/CLI metadata directly. Never loads or executes the target assembly.
public sealed class PatchPlan {
    public string Path, Hash, State, Detail;
    public int Offset, ChangedBytes;
    public byte[] Original, Patched;
}
public static class PatchEngine {
    public const string OriginalHash = "6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386";
    public static string Hash(byte[] b) { using (SHA256 s=SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
    static void Need(bool ok, string why) { if (!ok) throw new InvalidDataException(why); }
    sealed class Pe {
        public byte[] B; public int Strings, US, Tables, TypeStart, FieldStart, MethodStart;
        public int S,G,Blob, TDSize,FSize,MSize; public uint[] Rows=new uint[64];
        int Sections, SecCount;
        public int U16(int o) { return BitConverter.ToUInt16(B,o); }
        public int I32(int o) { return BitConverter.ToInt32(B,o); }
        public uint U32(int o) { return BitConverter.ToUInt32(B,o); }
        public int Idx(int o,int size) { return size==2?U16(o):checked((int)U32(o)); }
        public int Size(int t) { return Rows[t]<65536?2:4; }
        public string Str(int n) { int p=Strings+n,e=p; Need(n>=0 && p<B.Length,"无效字符串索引"); while(e<B.Length && B[e]!=0)e++; return Encoding.UTF8.GetString(B,p,e-p); }
        public int Rva(uint r) {
            for(int i=0;i<SecCount;i++) { int p=Sections+40*i; uint v=U32(p+12), size=Math.Max(U32(p+8),U32(p+16)); if(r>=v && (ulong)r<(ulong)v+size) { int o=checked((int)(r-v+U32(p+20))); Need(o>=0 && o<B.Length,"RVA 越界"); return o; } }
            throw new InvalidDataException("无法映射程序地址");
        }
        public Pe(byte[] b) {
            B=b; Need(B.Length>256 && U16(0)==0x5a4d,"不是 Windows EXE"); int pe=I32(60); Need(U32(pe)==0x4550,"PE 格式错误"); SecCount=U16(pe+6); int opt=pe+24; int magic=U16(opt); Need(magic==0x10b || magic==0x20b,"不支持的 PE 格式"); int dirs=opt+(magic==0x10b?96:112); Sections=opt+U16(pe+20);
            Need(U32(dirs+32)==0 && U32(dirs+36)==0,"此文件带有数字签名，当前补丁拒绝修改");
            int cli=Rva(U32(dirs+14*8)); Need(U32(cli+32)==0 && U32(cli+36)==0,"此文件带有程序集强名称，当前补丁拒绝修改");
            int md=Rva(U32(cli+8)); Need(U32(md)==0x424a5342,"未找到 .NET 元数据"); int at=md+16+I32(md+12); at=(at+3)&~3; int count=U16(at+2); at+=4;
            for(int i=0;i<count;i++) { int off=I32(at); int n=at+8,e=n; while(B[e]!=0)e++; string name=Encoding.ASCII.GetString(B,n,e-n); if(name=="#Strings")Strings=md+off; if(name=="#US")US=md+off; if(name=="#~"||name=="#-")Tables=md+off; at=(e+4)&~3; }
            Need(Strings>0 && US>0 && Tables>0,"缺少必要元数据表"); int heap=B[Tables+6]; S=(heap&1)!=0?4:2; G=(heap&2)!=0?4:2; Blob=(heap&4)!=0?4:2;
            ulong valid=BitConverter.ToUInt64(B,Tables+8); at=Tables+24; for(int i=0;i<64;i++)if((valid&(1UL<<i))!=0) {Rows[i]=U32(at);at+=4;}
            int resolution=(Math.Max(Math.Max(Rows[0],Rows[26]),Math.Max(Rows[35],Rows[1]))<(1<<14))?2:4;
            int typeCoded=Math.Max(Rows[2],Math.Max(Rows[1],Rows[27]))<(1<<14)?2:4;
            TDSize=4+2*S+typeCoded+Size(4)+Size(6); FSize=2+S+Blob; MSize=8+S+Blob+Size(8);
            at+=checked((int)Rows[0])*(2+S+3*G); at+=checked((int)Rows[1])*(resolution+2*S); TypeStart=at;
            at+=checked((int)Rows[2])*TDSize; at+=checked((int)Rows[3])*Size(4); FieldStart=at;
            at+=checked((int)Rows[4])*FSize; at+=checked((int)Rows[5])*Size(6); MethodStart=at;
        }
        public int Type(string full) { for(int i=1;i<=Rows[2];i++){int p=TypeStart+(i-1)*TDSize; string n=Str(Idx(p+4,S)),ns=Str(Idx(p+4+S,S)); if(ns+"."+n==full)return i;} throw new InvalidDataException("找不到类型："+full); }
        public int Method(int type,string name) { int p=TypeStart+(type-1)*TDSize; int first=Idx(p+TDSize-Size(6),Size(6)); int last=type<Rows[2]?Idx(p+2*TDSize-Size(6),Size(6)):(int)Rows[6]+1; int found=0; for(int i=first;i<last;i++){int q=MethodStart+(i-1)*MSize;if(Str(Idx(q+8,S))==name){Need(found==0,"同名方法不唯一");found=i;}} Need(found!=0,"找不到方法："+name);return 0x06000000|found; }
        public int Field(int type,string name) { int p=TypeStart+(type-1)*TDSize; int k=p+TDSize-Size(6)-Size(4); int first=Idx(k,Size(4)); int last=type<Rows[2]?Idx(k+TDSize,Size(4)):(int)Rows[4]+1; for(int i=first;i<last;i++){int q=FieldStart+(i-1)*FSize;if(Str(Idx(q+2,S))==name)return 0x04000000|i;} throw new InvalidDataException("找不到字段："+name); }
        public int Body(int token,out int size) { int p=MethodStart+((token&0xffffff)-1)*MSize; int o=Rva(U32(p)); if((B[o]&3)==2){size=B[o]>>2;return o+1;} Need((B[o]&3)==3,"不支持的方法头"); Need((U16(o)&8)==0,"方法包含异常处理区，拒绝修改"); size=I32(o+4); return o+((U16(o)>>12)*4); }
        public string UserString(int token) { Need((token>>24)==0x70,"字符串令牌错误");int p=US+(token&0xffffff),len=B[p++]; if((len&0x80)!=0){if((len&0xc0)==0x80)len=((len&0x3f)<<8)|B[p++];else {len=((len&0x1f)<<24)|(B[p++]<<16)|(B[p++]<<8)|B[p++];}} Need(len>0 && p+len<=B.Length,"字符串越界");return Encoding.Unicode.GetString(B,p,len-1); }
    }
    public static PatchPlan Inspect(string path) {
        path=System.IO.Path.GetFullPath(path); byte[] b=File.ReadAllBytes(path); Pe p=new Pe(b); int type=p.Type("PCL.PageDownloadInstall"); int meth=p.Method(type,"LoaderInit"); int field=p.Field(p.Type("PCL.ModDownload"),"DlClientListLoader"); int size; int o=p.Body(meth,out size);
        Need(size==94,"下载页面结构已改变（方法长度不是 94），请更新补丁。原文件未修改。");
        byte[] template=Hex("02 28 12 05 00 06 02 02 6F 07 06 00 06 02 6F 05 06 00 06 02 6F 51 05 00 06 14 72 33 47 01 70 7E FB 09 00 04 2C 07 7E FB 09 00 04 2B 16 7E F9 09 00 04 FE 06 67 14 00 06 73 8D 05 00 0A 25 80 FB 09 00 04 14 18 73 8E 05 00 0A 02 FE 06 1C 06 00 06 73 8D 05 00 0A 14 17 28 CA 00 00 2B 2A");
        bool[] variable=new bool[94]; int[] tokenOffsets={2,9,15,21,27,32,39,46,52,57,63,70,77,82,89}; foreach(int i in tokenOffsets)for(int j=0;j<4;j++)variable[i+j]=true;
        bool already=b[o+26]==0x7e && BitConverter.ToInt32(b,o+27)==field; if(already)for(int i=31;i<74;i++)if(b[o+i]!=0)already=false;
        for(int i=0;i<94;i++)if(!(already && i>=26 && i<74) && !variable[i])Need(b[o+i]==template[i],"下载页面指令结构不匹配，拒绝修改。请更新补丁。");
        Need(BitConverter.ToInt32(b,o+2)==p.Method(type,"ControlInit"),"初始化方法不匹配");
        Need(BitConverter.ToInt32(b,o+9)==p.Method(type,"get_LoadMinecraft"),"下载控件不匹配");
        Need(BitConverter.ToInt32(b,o+15)==p.Method(type,"get_PanLoad"),"加载面板不匹配");
        Need(BitConverter.ToInt32(b,o+21)==p.Method(type,"get_PanAllBack"),"内容面板不匹配");
        Need((BitConverter.ToInt32(b,o+89)>>24)==0x2b,"页面加载调用不匹配");
        if(!already) {
            Need(p.UserString(BitConverter.ToInt32(b,o+27))=="关闭下载","不是预期的下载限制页面，拒绝修改");
            Need(BitConverter.ToInt32(b,o+32)==BitConverter.ToInt32(b,o+39) && BitConverter.ToInt32(b,o+32)==BitConverter.ToInt32(b,o+63),"下载委托缓存结构不匹配");
        }
        byte[] patched=(byte[])b.Clone(); if(!already){Array.Clear(patched,o+26,48);patched[o+26]=0x7e;Array.Copy(BitConverter.GetBytes(field),0,patched,o+27,4);}
        int changed=0;for(int i=0;i<b.Length;i++)if(b[i]!=patched[i])changed++;
        return new PatchPlan {Path=path,Hash=Hash(b),State=already?"已恢复下载":"可应用补丁",Offset=o+26,ChangedBytes=changed,Original=b,Patched=patched,Detail="仅修改下载页面 LoaderInit 中的 48 字节区域；保留更新功能和其余代码。"};
    }
    static byte[] Hex(string s){string[] a=s.Split(' ');byte[] b=new byte[a.Length];for(int i=0;i<a.Length;i++)b[i]=Convert.ToByte(a[i],16);return b;}
    public static string BackupFolder(string path) { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)),"PCL补丁备份"); }
    static string BackupPattern(string path) { return System.IO.Path.GetFileNameWithoutExtension(path)+"_v*_原版备份_*.bak"; }
    static string SaveBackup(string path, byte[] original, string hash, string version) {
        string folder=BackupFolder(path); Directory.CreateDirectory(folder);
        foreach(string f in Directory.GetFiles(folder,BackupPattern(path)))if(Hash(File.ReadAllBytes(f))==hash)return f;
        if(String.IsNullOrEmpty(version))version="未知版本";
        foreach(char c in System.IO.Path.GetInvalidFileNameChars())version=version.Replace(c,'_');
        string stem=System.IO.Path.GetFileNameWithoutExtension(path)+"_v"+version+"_原版备份_"+DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backup=System.IO.Path.Combine(folder,stem+".bak"); int index=2;
        while(File.Exists(backup))backup=System.IO.Path.Combine(folder,stem+"_"+(index++)+".bak");
        using(FileStream save=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None)){save.Write(original,0,original.Length);save.Flush(true);}
        return backup;
    }
    public static string Apply(string path) {
        PatchPlan p=Inspect(path); if(p.State=="已恢复下载")return "已经恢复下载，无需重复应用。";
        path=p.Path; string backup; string version=FileVersionInfo.GetVersionInfo(path).FileVersion;
        using(FileStream lockFile=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) {
            byte[] now=new byte[checked((int)lockFile.Length)];int total=0;while(total<now.Length){int n=lockFile.Read(now,total,now.Length-total);if(n==0)throw new IOException("读取文件失败");total+=n;}
            Need(Hash(now)==p.Hash,"检查期间文件发生变化，已停止");
            backup=SaveBackup(path,now,p.Hash,version);
            Need(Hash(File.ReadAllBytes(backup))==p.Hash,"备份校验失败，原文件未修改");
            try {lockFile.Position=0;lockFile.Write(p.Patched,0,p.Patched.Length);lockFile.Flush(true);lockFile.Position=0;byte[] check=new byte[p.Patched.Length];total=0;while(total<check.Length){int n=lockFile.Read(check,total,check.Length-total);if(n==0)throw new IOException("写入后读取失败");total+=n;}Need(Hash(check)==Hash(p.Patched),"写入校验失败");}
            catch {lockFile.Position=0;lockFile.Write(now,0,now.Length);lockFile.Flush(true);throw;}
        }
        return "已恢复下载功能。\r\n原文件备份："+backup;
    }
    public static string Restore(string path) {
        PatchPlan current=Inspect(path);Need(current.State=="已恢复下载","当前文件没有应用本补丁，无需还原");
        path=current.Path; List<string> candidates=new List<string>();string folder=BackupFolder(path);
        if(Directory.Exists(folder))candidates.AddRange(Directory.GetFiles(folder,BackupPattern(path)));
        candidates.AddRange(Directory.GetFiles(System.IO.Path.GetDirectoryName(path),System.IO.Path.GetFileName(path)+".download-patch.*.bak"));
        Dictionary<string,string> matches=new Dictionary<string,string>(); foreach(string f in candidates) {
            try{PatchPlan p=Inspect(f);if(p.State=="可应用补丁" && Hash(p.Patched)==current.Hash)matches[p.Hash]=f;}catch{}
        }
        Need(matches.Count==1,"未找到唯一、匹配当前版本的有效备份，已停止还原");string chosen=null;foreach(string f in matches.Values)chosen=f;byte[] original=File.ReadAllBytes(chosen);
        using(FileStream f=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){byte[] now=new byte[checked((int)f.Length)];int total=0;while(total<now.Length){int n=f.Read(now,total,now.Length-total);if(n==0)throw new IOException("读取失败");total+=n;}Need(Hash(now)==current.Hash,"还原前文件发生变化，已停止");try{f.Position=0;f.Write(original,0,original.Length);f.Flush(true);f.Position=0;byte[] check=new byte[original.Length];total=0;while(total<check.Length){int n=f.Read(check,total,check.Length-total);if(n==0)throw new IOException("还原校验读取失败");total+=n;}Need(Hash(check)==Hash(original),"还原校验失败");}catch{f.Position=0;f.Write(now,0,now.Length);f.Flush(true);throw;}}
        return "已还原原版启动器，备份仍保留。";
    }
}
public sealed class PatchWindow:Form {
    TextBox path=new TextBox(); TextBox status=new TextBox(); Button apply=new Button(),restore=new Button();
    public PatchWindow(string initial) {
        Text="PCL 下载功能补丁（第三方） · 1.1.1";ClientSize=new Size(640,370);Font=new Font("Microsoft YaHei UI",9);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;AllowDrop=true;
        using(Stream icon=Assembly.GetExecutingAssembly().GetManifestResourceStream("patch.ico"))if(icon!=null)Icon=new Icon(icon);
        Label title=new Label{Text="PCL 下载功能补丁",Font=new Font("Microsoft YaHei UI",15,FontStyle.Bold),Location=new Point(20,18),Size=new Size(580,32)};
        Label hint=new Label{Text="关闭 PCL 后使用。选择启动器 EXE，或将它拖入本窗口。",Location=new Point(20,62),Size=new Size(600,24)};
        path.SetBounds(20,96,490,27);Button browse=new Button{Text="选择文件…",Location=new Point(520,94),Size=new Size(100,30)};
        status.SetBounds(20,138,600,114);status.Multiline=true;status.ReadOnly=true;status.ScrollBars=ScrollBars.Vertical;
        apply.Text="恢复下载";apply.SetBounds(20,273,150,36);restore.Text="还原原版";restore.SetBounds(185,273,150,36);Button check=new Button{Text="重新检查",Location=new Point(470,273),Size=new Size(150,36)};
        Controls.AddRange(new Control[]{title,hint,path,browse,status,apply,restore,check});
        Button help=new Button{Text="使用说明 / 关于",Location=new Point(20,325),Size=new Size(150,30)};
        Label credit=new Label{Text="第三方工具 · PCL 原作者：龙腾猫跃 · 仅实测 2.99.0.0",Location=new Point(185,332),Size=new Size(435,24)};
        Controls.AddRange(new Control[]{help,credit});help.Click+=delegate{new HelpWindow(Icon).ShowDialog(this);};
        browse.Click+=delegate{using(OpenFileDialog d=new OpenFileDialog{Filter="启动器程序 (*.exe)|*.exe",Title="选择 PCL 启动器"})if(d.ShowDialog()==DialogResult.OK){path.Text=d.FileName;RefreshInfo();}};
        check.Click+=delegate{RefreshInfo();};apply.Click+=delegate{Act(false);};restore.Click+=delegate{Act(true);};
        DragEnter+=delegate(object s,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};DragDrop+=delegate(object s,DragEventArgs e){string[] f=(string[])e.Data.GetData(DataFormats.FileDrop);if(f.Length==1){path.Text=f[0];RefreshInfo();}};
        path.Text=initial;Shown+=delegate{RefreshInfo();};
    }
    void RefreshInfo(){apply.Enabled=restore.Enabled=false;try{PatchPlan p=PatchEngine.Inspect(path.Text);status.Text=p.State+"\r\n版本："+FileVersionInfo.GetVersionInfo(path.Text).FileVersion+"\r\n"+p.Detail+"\r\n备份文件夹："+PatchEngine.BackupFolder(p.Path);apply.Enabled=p.State=="可应用补丁";restore.Enabled=p.State=="已恢复下载";}catch(Exception e){status.Text="尚未修改文件。\r\n"+e.Message;}}
    void Act(bool undo){try{string result=undo?PatchEngine.Restore(path.Text):PatchEngine.Apply(path.Text);RefreshInfo();MessageBox.Show(result,"完成",MessageBoxButtons.OK,MessageBoxIcon.Information);}catch(Exception e){MessageBox.Show(e.Message+"\r\n请确认 PCL 已退出；结构不兼容时请更新补丁。","未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);RefreshInfo();}}
}
public sealed class HelpWindow:Form {
    public HelpWindow(Icon icon) {
        Text="使用说明 / 关于";Icon=icon;ClientSize=new Size(700,560);Font=new Font("Microsoft YaHei UI",9);StartPosition=FormStartPosition.CenterParent;MinimumSize=new Size(500,400);
        Label credit=new Label{Text="PCL 原作者：龙腾猫跃。此补丁由 Acetab 独立维护，非官方发布。",Dock=DockStyle.Top,Height=35,Padding=new Padding(12,10,0,0)};
        TextBox body=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,WordWrap=true,BackColor=SystemColors.Window};
        using(Stream resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("help.txt")) {
            if(resource==null)body.Text="未找到内置说明，请重新下载完整版本。";
            else using(StreamReader reader=new StreamReader(resource,Encoding.UTF8))body.Text=reader.ReadToEnd().Replace("\r\n","\n").Replace("\n",Environment.NewLine);
        }
        FlowLayoutPanel links=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42,Padding=new Padding(10,8,0,0)};
        AddLink(links,"项目源码与新版","https://github.com/Acetab/pcl-download-patch");
        AddLink(links,"PCL 原项目","https://github.com/Meloong-Git/PCL");
        AddLink(links,"支持原作者 / PCL 官网","https://meloong.com/PCL");
        Controls.Add(body);Controls.Add(credit);Controls.Add(links);
    }
    static void AddLink(FlowLayoutPanel panel,string label,string url){LinkLabel l=new LinkLabel{Text=label,AutoSize=true,Margin=new Padding(0,0,20,0)};l.LinkClicked+=delegate{try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception e){MessageBox.Show(e.Message);}};panel.Controls.Add(l);}
}
public static class Program {
    [STAThread]public static void Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);string initial=args.Length==1?System.IO.Path.GetFullPath(args[0]):System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Plain Craft Launcher.exe");Application.Run(new PatchWindow(initial));}
}
