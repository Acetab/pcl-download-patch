using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace DownloadRepair {
    public sealed class RepairPlan {
        public string Path,Hash,BaseHash,Version,State,LegacyBackup;
        public byte[] Input,Original,Output;
        public bool Complete,KnownBuild,Native;
        public bool ThemesRequested,ThemesAlreadyUnlocked;
        public List<FeatureState> Features;
    }
    public sealed class BackupRecord {
        public int Format=2;
        public string Tool="2.1.1",TargetName,Version,OriginalHash,BeforeHash,PatchedHash,CreatedUtc;
    }
    public static class FileSafety {
        public static readonly string[] KnownOriginals={"6b6e1260cb5a1d6a303855f1c60cab3d0bc1aebba7e40a964180188b271e7386"};
        public static string Hash(byte[] b) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
        static byte[] ReadStream(Stream s) { SemanticPatcher.Require(s.Length>0&&s.Length<=80*1024*1024,"文件大小不在启动器支持范围内");var b=new byte[checked((int)s.Length)];s.Position=0;int read=0;while(read<b.Length){int n=s.Read(b,read,b.Length-read);if(n==0)throw new EndOfStreamException("文件读取不完整");read+=n;}return b; }
        public static byte[] Snapshot(string path) { using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))return ReadStream(f); }
        static string Full(string path) { if(String.IsNullOrWhiteSpace(path))throw new InvalidDataException("请选择 PCL 启动器 EXE");return System.IO.Path.GetFullPath(path); }
        public static string BackupRoot(string path) { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Full(path)),"PCL补丁备份"); }
        static string Clean(string name) { foreach(char c in System.IO.Path.GetInvalidFileNameChars())name=name.Replace(c,'_');return name; }
        static string FindLegacy(string path,string hash) {
            var files=new List<string>();string root=BackupRoot(path);if(Directory.Exists(root))files.AddRange(Directory.GetFiles(root,"*_原版备份_*.bak"));
            files.AddRange(Directory.GetFiles(System.IO.Path.GetDirectoryName(path),System.IO.Path.GetFileName(path)+".download-patch.*.bak"));
            var found=new Dictionary<string,string>();foreach(string f in files)try { var p=LegacyEngine.Inspect(f);if(p.State=="可应用补丁"&&Hash(p.Patched)==hash)found[p.Hash]=f; }catch{}
            SemanticPatcher.Require(found.Count==1,"检测到我们的旧版补丁，但没有找到唯一匹配的有效原版备份。请先还原旧补丁，或选择原版 EXE。");return found.Values.Single();
        }
        static string FindPreviousBase(string path,string hash){string root=BackupRoot(path);if(!Directory.Exists(root))return null;var choices=new Dictionary<string,string>();foreach(string dir in Directory.GetDirectories(root))try{var r=ReadRecord(dir);string backup=System.IO.Path.Combine(dir,"原版启动器.bak");if(r.TargetName==System.IO.Path.GetFileName(path)&&r.PatchedHash==hash&&Hash(Snapshot(backup))==r.OriginalHash)choices[r.OriginalHash]=backup;}catch{}SemanticPatcher.Require(choices.Count<=1,"找到多个不同的修复前备份，已停止自动重建");return choices.Count==1?choices.Values.Single():null;}
        public static RepairPlan Inspect(string path) { return Inspect(path,false); }
        public static RepairPlan Inspect(string path,bool themes) {
            path=Full(path);byte[] current=Snapshot(path);string hash=Hash(current);byte[] baseline=current;string legacy=null;
            bool old=false;try { old=LegacyEngine.Inspect(path).State=="已恢复下载"; }catch{}
            if(old) { legacy=FindLegacy(path,hash);baseline=Snapshot(legacy); }
            bool themesReady=ThemePatcher.IsUnlocked(current);
            if(!old&&themes&&!themesReady){string previous=FindPreviousBase(path,hash);if(previous!=null){legacy=previous;baseline=Snapshot(previous);}}
            var result=SemanticPatcher.Rewrite(baseline);
            if(old)result.Features[0].Ready=true;
            if(!old&&legacy!=null)foreach(var feature in result.Features)feature.Ready=true;
            byte[] output=themes?ThemePatcher.Apply(result.Bytes).Bytes:result.Bytes;bool complete=result.Complete&&(!themes||themesReady);
            return new RepairPlan{Path=path,Hash=hash,BaseHash=Hash(baseline),Input=current,Original=baseline,Output=output,Version=result.Version,Complete=complete,Native=result.Native,KnownBuild=KnownOriginals.Contains(Hash(baseline)),LegacyBackup=legacy,Features=result.Features,ThemesRequested=themes,ThemesAlreadyUnlocked=themesReady,State=complete?(themesReady?"下载与主题已可用":result.Native?"下载功能已可用":"已完成下载恢复"):(themes&&result.Complete?"可添加主题解锁":old?"可从旧版补丁升级":"结构检查通过，可以修复")};
        }
        static void WriteNew(string path,byte[] data) { using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){f.Write(data,0,data.Length);f.Flush(true);SemanticPatcher.Require(Hash(ReadStream(f))==Hash(data),"新文件写入校验失败");} }
        public static string CreateCopy(string input,string output) { return CreateCopy(input,output,false); }
        public static string CreateCopy(string input,string output,bool themes) {
            RepairPlan p=Inspect(input,themes);SemanticPatcher.Require(!p.Complete,"所选功能已经可用，无需生成副本");
            output=Full(output);SemanticPatcher.Require(!String.Equals(p.Path,output,StringComparison.OrdinalIgnoreCase),"副本必须使用新的文件名");
            SemanticPatcher.Require(String.Equals(System.IO.Path.GetDirectoryName(output),System.IO.Path.GetDirectoryName(p.Path),StringComparison.OrdinalIgnoreCase),"请把副本保存在原启动器目录，以继续使用原配置和游戏路径");
            using(var source=new FileStream(p.Path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                SemanticPatcher.Require(Hash(ReadStream(source))==p.Hash,"生成前输入文件发生变化，请重新检查");
                // CreateNew makes output creation race-safe, without overwriting an existing file.
                bool created=false;try { using(var f=new FileStream(output,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)) { created=true;f.Write(p.Output,0,p.Output.Length);f.Flush(true);SemanticPatcher.Require(Hash(ReadStream(f))==Hash(p.Output),"副本写入校验失败"); } }
                catch { if(created)File.Delete(output);throw; }
            }
            return output;
        }
        static string SaveBackup(RepairPlan p) {
            string root=BackupRoot(p.Path);Directory.CreateDirectory(root);
            foreach(string dir in Directory.GetDirectories(root))try { var r=ReadRecord(dir);if(r.TargetName==System.IO.Path.GetFileName(p.Path)&&r.OriginalHash==p.BaseHash&&r.PatchedHash==Hash(p.Output)&&Hash(Snapshot(System.IO.Path.Combine(dir,"原版启动器.bak")))==p.BaseHash)return dir; }catch{}
            string folder=System.IO.Path.Combine(root,Clean(System.IO.Path.GetFileNameWithoutExtension(p.Path))+"_v"+Clean(p.Version)+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(folder);
            WriteNew(System.IO.Path.Combine(folder,"原版启动器.bak"),p.Original);
            var record=new BackupRecord{TargetName=System.IO.Path.GetFileName(p.Path),Version=p.Version,OriginalHash=p.BaseHash,BeforeHash=p.Hash,PatchedHash=Hash(p.Output),CreatedUtc=DateTime.UtcNow.ToString("o")};
            WriteNew(System.IO.Path.Combine(folder,"修复记录.json"),Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(record)));
            SemanticPatcher.Require(Hash(Snapshot(System.IO.Path.Combine(folder,"原版启动器.bak")))==p.BaseHash,"原版备份复查失败");return folder;
        }
        static BackupRecord ReadRecord(string dir) {
            string text=File.ReadAllText(System.IO.Path.Combine(dir,"修复记录.json"),Encoding.UTF8);SemanticPatcher.Require(text.Length<=8192,"修复记录过大");
            var r=new JavaScriptSerializer().Deserialize<BackupRecord>(text);SemanticPatcher.Require(r!=null&&r.Format==2,"修复记录格式无效");return r;
        }
        static void NotRunning(string path) {
            foreach(var p in Process.GetProcesses())using(p)try { if(String.Equals(p.MainModule.FileName,path,StringComparison.OrdinalIgnoreCase))throw new IOException("请先退出目标 PCL（包括托盘），再修复或还原原文件"); }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        }
        static void AtomicReplace(string path,byte[] bytes,string expected) {
            NotRunning(path);string dir=System.IO.Path.GetDirectoryName(path);string stage=System.IO.Path.Combine(dir,".pcl-repair-"+Guid.NewGuid().ToString("N")+".tmp");string rollback=stage+".rollback";bool replaced=false,settled=false;
            try {
                // Keep writers excluded; ReplaceFile itself needs read/delete sharing.
                using(var original=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)) {
                    SemanticPatcher.Require(Hash(ReadStream(original))==expected,"目标文件在检查后发生变化，已停止");
                    WriteNew(stage,bytes);
                    File.Replace(stage,path,rollback);replaced=true;
                    SemanticPatcher.Require(Hash(Snapshot(path))==Hash(bytes),"替换后校验失败");
                }
                SemanticPatcher.Require(Hash(Snapshot(path))==Hash(bytes),"最终文件复查失败");
                settled=true;
            } catch {
                if(replaced&&File.Exists(rollback)) { File.Replace(rollback,path,null);SemanticPatcher.Require(Hash(Snapshot(path))==expected,"自动回滚未通过，请使用备份恢复");settled=true; }
                throw;
            } finally { if(File.Exists(stage))File.Delete(stage);if(settled&&File.Exists(rollback))File.Delete(rollback); }
        }
        public static string Apply(string path,bool automatic) { return Apply(path,automatic,false); }
        public static string Apply(string path,bool automatic,bool themes) {
            var p=Inspect(path,themes);if(p.Complete)return p.State+"，无需重复应用。";
            SemanticPatcher.Require(!automatic||p.KnownBuild,"这是尚未实测的新构建，自动入口已停止。请先打开补丁窗口检查并手动修复一次。");
            NotRunning(p.Path);string backup=SaveBackup(p);AtomicReplace(p.Path,p.Output,p.Hash);
            return "五个下载相关入口已恢复。"+(themes?"\r\n界面中的全部主题已可选择。":"")+"\r\n修复前备份："+backup;
        }
        public static string Restore(string path) {
            path=Full(path);string current=Hash(Snapshot(path));var options=new Dictionary<string,string>();string root=BackupRoot(path);
            if(Directory.Exists(root))foreach(string dir in Directory.GetDirectories(root))try { var r=ReadRecord(dir);if(r.TargetName!=System.IO.Path.GetFileName(path)||r.PatchedHash!=current)continue;string original=System.IO.Path.Combine(dir,"原版启动器.bak");if(Hash(Snapshot(original))==r.OriginalHash)options[r.OriginalHash]=original; }catch{}
            if(options.Count==0) {
                bool legacy=false;try { legacy=LegacyEngine.Inspect(path).State=="已恢复下载"; }catch{}
                if(legacy) { var original=FindLegacy(path,current);options[Hash(Snapshot(original))]=original; }
            }
            SemanticPatcher.Require(options.Count==1,"未找到与当前完整文件哈希对应的有效原版备份；没有覆盖文件。生成的副本可直接改回运行保留的原启动器。");
            byte[] bytes=Snapshot(options.Values.Single());SemanticPatcher.Require(Hash(bytes)==options.Keys.Single(),"还原前备份发生变化");AtomicReplace(path,bytes,current);return "已完整还原原版启动器，备份仍保留。";
        }
        public static bool CanRestore(string path) { try { string current=Hash(Snapshot(path));string root=BackupRoot(path);if(Directory.Exists(root))foreach(string dir in Directory.GetDirectories(root))try { var r=ReadRecord(dir);if(r.TargetName==System.IO.Path.GetFileName(path)&&r.PatchedHash==current&&Hash(Snapshot(System.IO.Path.Combine(dir,"原版启动器.bak")))==r.OriginalHash)return true; }catch{}return LegacyEngine.Inspect(path).State=="已恢复下载"; }catch{return false;} }
        public static void Launch(string path) { Launch(path,false); }
        public static void Launch(string path,bool themes) { path=Full(path);Apply(path,true,themes);Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=System.IO.Path.GetDirectoryName(path)}); }
    }
}
