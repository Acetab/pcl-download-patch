using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Reflection;
using System.Diagnostics;
using System.Windows.Forms;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;

[assembly: AssemblyTitle("PCL 下载功能修复（第三方）")]
[assembly: AssemblyDescription("正式版：下载恢复与可选的全部主题解锁。")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]
namespace DownloadRepair {
    public static class Dependency {
        static bool installed;
        public static void Install() {
            if(installed)return;installed=true;
            AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e) {
                if(new AssemblyName(e.Name).Name!="Mono.Cecil")return null;
                using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mono.Cecil.dll"))using(var mem=new MemoryStream()) { if(s==null)throw new IOException("内置处理组件缺失");s.CopyTo(mem);return Assembly.Load(mem.ToArray()); }
            };
        }
    }
    public sealed class RepairWindow:Form {
        TextBox path=new TextBox(),detail=new TextBox();Label state=new Label(),version=new Label();Label[] feature=new Label[5];Button copy=new Button(),apply=new Button(),restore=new Button(),shortcut=new Button();RepairPlan plan;
        CheckBox unlockThemes=new CheckBox();Label themeState=new Label();
        bool renderOnly;
        public void SetThemeSelection(bool value){unlockThemes.Checked=value;}
        protected override bool ShowWithoutActivation { get { return renderOnly; } }
        public RepairWindow(string initial):this(initial,false){}
        public RepairWindow(string initial,bool preview) {
            renderOnly=preview;
            Text="PCL 下载与主题修复 · 2.1 正式版";Font=new Font("Microsoft YaHei UI",9F);AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(760,675);MinimumSize=new Size(776,714);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.White;AllowDrop=true;
            using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("patch.ico"))if(s!=null)Icon=new Icon(s);
            var title=new Label{Text="PCL 下载功能修复",Font=new Font(Font.FontFamily,20,FontStyle.Bold),AutoSize=false,Location=new Point(26,24),Size=new Size(600,40)};
            var subtitle=new Label{Text="恢复原版下载与整合包安装，可选解锁启动器主题",ForeColor=Color.FromArgb(90,90,90),Location=new Point(28,72),Size=new Size(690,24)};
            var choose=new Button{Text="选择启动器…",Location=new Point(620,115),Size=new Size(112,32),Anchor=AnchorStyles.Top|AnchorStyles.Right};
            path.SetBounds(28,118,578,28);path.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;
            state.SetBounds(28,170,704,28);state.Font=new Font(Font.FontFamily,12,FontStyle.Bold);state.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
            version.SetBounds(28,204,704,23);version.ForeColor=Color.DimGray;
            var line=new Label{BackColor=Color.FromArgb(225,228,231),Location=new Point(28,240),Size=new Size(704,1),Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right};
            for(int k=0;k<5;k++) { feature[k]=new Label{Text="—  等待选择启动器",Location=new Point(32,258+k*29),Size=new Size(690,25),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(feature[k]); }
            unlockThemes.Text="同时解锁全部启动器主题";unlockThemes.SetBounds(28,415,300,28);themeState.SetBounds(28,446,520,23);themeState.ForeColor=Color.DimGray;
            var puzzle=new LinkLabel{Text="极客蓝解谜方法",Location=new Point(590,446),Size=new Size(140,23),Anchor=AnchorStyles.Top|AnchorStyles.Right};puzzle.LinkClicked+=delegate{MessageBox.Show(this,PuzzleHelp,"极客蓝解谜方法",MessageBoxButtons.OK,MessageBoxIcon.Information);};Controls.AddRange(new Control[]{unlockThemes,themeState,puzzle});unlockThemes.CheckedChanged+=delegate{Inspect();};
            detail.SetBounds(28,481,704,58);detail.Multiline=true;detail.ReadOnly=true;detail.BorderStyle=BorderStyle.None;detail.BackColor=Color.White;detail.ForeColor=Color.DimGray;detail.ScrollBars=ScrollBars.Vertical;detail.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;
            copy.Text="生成修复副本";copy.SetBounds(28,558,184,40);copy.BackColor=Color.FromArgb(32,104,172);copy.ForeColor=Color.White;copy.FlatStyle=FlatStyle.Flat;copy.FlatAppearance.BorderSize=0;
            apply.Text="修复原文件";apply.SetBounds(224,558,152,40);restore.Text="还原修复前";restore.SetBounds(388,558,152,40);
            var help=new Button{Text="使用说明",Location=new Point(580,558),Size=new Size(152,40),Anchor=AnchorStyles.Top|AnchorStyles.Right};
            shortcut.Text="创建自动检查启动入口";shortcut.SetBounds(28,620,200,30);
            var footer=new Label{Text="第三方工具 · Acetab · 2.1.0",ForeColor=Color.DimGray,Location=new Point(250,627),Size=new Size(480,23),TextAlign=ContentAlignment.MiddleRight,Anchor=AnchorStyles.Top|AnchorStyles.Right};
            Controls.AddRange(new Control[]{title,subtitle,path,choose,state,version,line,detail,copy,apply,restore,help,shortcut,footer});
            var recheck=new LinkLabel{Text="重新检查",Location=new Point(659,205),Size=new Size(74,24),Anchor=AnchorStyles.Top|AnchorStyles.Right};Controls.Add(recheck);recheck.BringToFront();recheck.LinkClicked+=delegate{Inspect();};
            path.TextChanged+=delegate{plan=null;copy.Enabled=apply.Enabled=restore.Enabled=shortcut.Enabled=false;state.Text="按回车或点击“重新检查”";};path.Leave+=delegate{Inspect();};
            choose.Click+=delegate{using(var d=new OpenFileDialog{Filter="PCL 启动器 (*.exe)|*.exe",Title="选择 PCL 启动器"})if(d.ShowDialog(this)==DialogResult.OK){path.Text=d.FileName;Inspect();}};
            path.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Enter){Inspect();e.SuppressKeyPress=true;}};
            copy.Click+=delegate{CreateCopy();};apply.Click+=delegate{Do(delegate{return FileSafety.Apply(path.Text,false,unlockThemes.Checked);});};restore.Click+=delegate{Do(delegate{return FileSafety.Restore(path.Text);});};help.Click+=delegate{new HelpWindow(Icon).ShowDialog(this);};shortcut.Click+=delegate{CreateShortcut();};
            DragEnter+=delegate(object s,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};DragDrop+=delegate(object s,DragEventArgs e){var a=(string[])e.Data.GetData(DataFormats.FileDrop);if(a.Length==1){path.Text=a[0];Inspect();}};
            path.Text=initial;Shown+=delegate{Inspect();};
        }
        public void Inspect() {
            plan=null;copy.Enabled=apply.Enabled=restore.Enabled=shortcut.Enabled=false;
            try {
                plan=FileSafety.Inspect(path.Text,unlockThemes.Checked);state.Text=plan.State;state.ForeColor=Color.FromArgb(27,83,125);
                themeState.Text=plan.ThemesAlreadyUnlocked?"主题已解锁；主题偏好仍由 PCL 保存。":unlockThemes.Checked?"将解锁界面中的全部主题，不更改赞助或更新权限。":"本次不修改主题；需要时勾选上方选项。";
                version.Text="PCL "+plan.Version+" · "+(plan.KnownBuild?"已纳入本地验证的构建":plan.Complete?"下载入口已可用":"未实测构建，结构检查已通过");
                for(int k=0;k<5;k++) { feature[k].Text=(plan.Features[k].Ready?"✓  ":"○  ")+plan.Features[k].Name+(plan.Features[k].Ready?"  · 已可用":"  · 可恢复");feature[k].ForeColor=Color.FromArgb(45,50,55); }
                detail.Text=plan.LegacyBackup!=null?"已找到匹配的原版备份，将以备份重建所选修复。\r\n生成副本保留当前文件；原地修复会先保存完整备份。":plan.Complete?"所选功能已可用。副本可直接改回运行保留的原启动器。":"推荐先生成修复副本，原启动器保留。\r\n原地修复会把修复前完整文件单独存入 PCL补丁备份。";
                copy.Enabled=apply.Enabled=!plan.Complete;restore.Enabled=FileSafety.CanRestore(path.Text);shortcut.Enabled=true;
            }catch(Exception e){state.Text="尚未修改文件";state.ForeColor=Color.FromArgb(110,75,30);version.Text="请选择已解压的 PCL 启动器";detail.Text=e.Message;themeState.Text=unlockThemes.Checked?"主题检查未通过；可取消勾选，只检查下载恢复。":"";for(int k=0;k<5;k++)feature[k].Text="—  未通过检查";}
        }
        void Do(Func<string> action) { UseWaitCursor=true;try{string message=action();Inspect();MessageBox.Show(this,message,"完成",MessageBoxButtons.OK,MessageBoxIcon.Information);}catch(Exception e){MessageBox.Show(this,e.Message,"未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);Inspect();}finally{UseWaitCursor=false;} }
        void CreateCopy() {
            if(plan==null)return;using(var d=new SaveFileDialog{Title="保存修复副本（原启动器保留）",Filter="启动器 (*.exe)|*.exe",InitialDirectory=System.IO.Path.GetDirectoryName(plan.Path),FileName=System.IO.Path.GetFileNameWithoutExtension(plan.Path)+(unlockThemes.Checked?".下载与主题恢复.exe":".下载恢复.exe"),OverwritePrompt=false})if(d.ShowDialog(this)==DialogResult.OK)Do(delegate{string output=FileSafety.CreateCopy(path.Text,d.FileName,unlockThemes.Checked);return "已生成修复副本：\r\n"+output+"\r\n\r\n关闭原启动器后，运行此副本即可。";});
        }
        void CreateShortcut() {
            if(plan==null)return;using(var d=new SaveFileDialog{Title="保存启动入口",Filter="Windows 快捷方式 (*.lnk)|*.lnk",FileName="启动 PCL（先检查下载修复）.lnk",InitialDirectory=System.IO.Path.GetDirectoryName(plan.Path)})if(d.ShowDialog(this)==DialogResult.OK)Do(delegate {
                object shell=Activator.CreateInstance(System.Type.GetTypeFromProgID("WScript.Shell"));object link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{d.FileName});var t=link.GetType();
                t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{Application.ExecutablePath});t.InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{(unlockThemes.Checked?"--launch-themes":"--launch")+" \""+plan.Path+"\""});t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{System.IO.Path.GetDirectoryName(plan.Path)});t.InvokeMember("Description",BindingFlags.SetProperty,null,link,new object[]{"启动前检查所选修复；未知构建会停止并提示"});t.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);return "已创建启动入口。\r\n以后从该入口启动；遇到尚未验证的新构建会停止并提示。\r\n请保持补丁工具和启动器的路径不变。";
            });
        }
        public const string PuzzleHelp="原版玩法：先解锁至少三个隐藏主题，再点击极客蓝旁的文字，打开解谜入口。\r\n\r\n最终答案的直接形式：PuzzleUnlock + 你的 PCL 识别码最后 4 位，不加空格，区分大小写。原代码也接受最后 5 位或 6 位。\r\n\r\n在支持土豆码输入的快照版中，把答案填入“输入土豆码”。这是彩蛋答案，不是赞助授权码。\r\n\r\n本工具勾选“同时解锁全部启动器主题”后，极客蓝可直接选择，无需谜题或土豆码。";
    }
    public sealed class HelpWindow:Form {
        public HelpWindow(Icon icon) { Text="使用说明与适配范围";Icon=icon;ClientSize=new Size(720,570);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft YaHei UI",10);var body=new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,BackColor=Color.White,BorderStyle=BorderStyle.None};using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("help.txt"))using(var r=new StreamReader(s,Encoding.UTF8))body.Text=r.ReadToEnd().Replace("\r\n","\n").Replace("\n",Environment.NewLine);Controls.Add(body); }
    }
    public static class Program {
        [STAThread]public static int Main(string[] args) { Dependency.Install();return Run(args); }
        [MethodImpl(MethodImplOptions.NoInlining)]static int Run(string[] args) {
            try {
                if(args.Length==2&&(args[0]=="--check"||args[0]=="--check-themes")) { var p=FileSafety.Inspect(args[1],args[0]=="--check-themes");Console.WriteLine(new JavaScriptSerializer().Serialize(new{p.State,p.Version,p.KnownBuild,p.Complete,p.Hash,p.BaseHash,OutputHash=FileSafety.Hash(p.Output),p.LegacyBackup,p.ThemesRequested,p.ThemesAlreadyUnlocked,Features=p.Features}));return p.Complete?2:0; }
                if(args.Length==3&&(args[0]=="--copy"||args[0]=="--copy-themes")) { Console.WriteLine(FileSafety.CreateCopy(args[1],args[2],args[0]=="--copy-themes"));return 0; }
                if(args.Length==2&&(args[0]=="--apply"||args[0]=="--apply-themes")) { Console.WriteLine(FileSafety.Apply(args[1],false,args[0]=="--apply-themes"));return 0; }
                if(args.Length==2&&args[0]=="--restore") { Console.WriteLine(FileSafety.Restore(args[1]));return 0; }
                if(args.Length==2&&(args[0]=="--launch"||args[0]=="--launch-themes")) { FileSafety.Launch(args[1],args[0]=="--launch-themes");return 0; }
                if(args.Length>0&&args[0].StartsWith("--")&&args[0]!="--preview"&&args[0]!="--preview-themes")throw new ArgumentException("命令参数不正确，请查看内置使用说明");
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                if(args.Length==3&&(args[0]=="--preview"||args[0]=="--preview-themes")) { using(var f=new RepairWindow(args[1],true)) { f.SetThemeSelection(args[0]=="--preview-themes");f.ShowInTaskbar=false;f.Opacity=0;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-32000,-32000);f.Show();f.PerformLayout();f.Inspect();Application.DoEvents();using(var bitmap=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bitmap,new Rectangle(0,0,f.Width,f.Height));bitmap.Save(args[2],System.Drawing.Imaging.ImageFormat.Png);}f.Close(); }return 0; }
                string initial=args.Length==1?args[0]:System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Plain Craft Launcher.exe");Application.Run(new RepairWindow(initial));return 0;
            }catch(Exception e){Console.Error.WriteLine(e.ToString());if(args.Length>0&&args[0]=="--launch")MessageBox.Show(e.Message,"启动前检查未通过",MessageBoxButtons.OK,MessageBoxIcon.Warning);return 1;}
        }
    }
}
