using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing;
using System.Reflection;
// Reads PE/CLI metadata directly. Never loads or executes the target assembly.
public sealed class LegacyPlan {
    public string Path, Hash, State, Detail;
    public int Offset, ChangedBytes;
    public byte[] Original, Patched;
}
public static class LegacyEngine {
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
    static byte[] Hex(string text) { string[] items=text.Split(' '); byte[] b=new byte[items.Length]; for(int k=0;k<items.Length;k++)b[k]=Convert.ToByte(items[k],16); return b; }
    public static LegacyPlan Inspect(string path) {
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
        return new LegacyPlan {Path=path,Hash=Hash(b),State=already?"已恢复下载":"可应用补丁",Offset=o+26,ChangedBytes=changed,Original=b,Patched=patched,Detail="仅修改下载页面 LoaderInit 中的 48 字节区域；保留更新功能和其余代码。"};
    }
}
