using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LummaSimulator.Shared
{
    // Public admission links only. No capabilities, browser state or participant identity.
    public static class ParticipantLink
    {
        public static string EventPath(string id, string kind = "prize")
        {
            if (!Guid.TryParseExact(id, "D", out _)) throw new ArgumentException("Invalid event identity.");
            if (kind is not ("prize" or "experience" or "edition")) throw new ArgumentException("Invalid event kind.");
            return "/Index?eventKind=" + kind + "&eventId=" + Uri.EscapeDataString(id);
        }
        public static Uri ForEvent(string origin, string id, string pathBase = "", string kind = "prize")
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0
                || origin != uri.GetLeftPart(UriPartial.Authority) || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new ArgumentException("An exact HTTPS origin is required.");
            if (pathBase.Length > 0 && (!pathBase.StartsWith("/", StringComparison.Ordinal) || pathBase.IndexOfAny(new[] {'?', '#', '\\'}) >= 0))
                throw new ArgumentException("Invalid portal path base.");
            return new Uri(origin + pathBase.TrimEnd('/') + EventPath(id, kind));
        }
        public static bool LaptopOnly(Uri uri) => uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    // QR Model 2, byte mode, versions 1-10, level L, mask 0, four-module quiet zone.
    // Bounded public ASCII URLs; never truncate a link or approximate a QR with artwork.
    public sealed class ParticipantQr
    {
        private readonly bool[,] modules;
        private readonly bool[,] function;
        public int Size { get; }
        public int Version { get; }
        public bool GetModule(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size && modules[x,y];
        private static readonly int[] Ecc = {0,7,10,15,20,26,18,20,24,30,18};
        private static readonly int[] Blocks = {0,1,1,1,1,1,2,2,2,2,4};
        private static int RawModules(int v)
        {
            int result = (16*v+128)*v+64;
            if (v >= 2) { int n=v/7+2; result-=(25*n-10)*n-55; if(v>=7)result-=36; }
            return result;
        }
        private ParticipantQr(int version, byte[] data)
        {
            Version=version; Size=version*4+17; modules=new bool[Size,Size];function=new bool[Size,Size];
            DrawFunctions();
            byte[] words=Interleave(data,version); int index=0;
            for(int right=Size-1;right>=1;right-=2) {
                if(right==6)right=5;
                for(int row=0;row<Size;row++) {
                    int y=((right+1)&2)==0 ? Size-1-row : row;
                    for(int col=0;col<2;col++) {
                        int x=right-col;if(function[x,y])continue;
                        bool bit=index<words.Length*8 && ((words[index>>3]>>(7-(index&7)))&1)!=0;
                        modules[x,y]=bit^((x+y)%2==0);index++;
                    }
                }
            }
            if(index!=RawModules(version))throw new InvalidOperationException("QR layout mismatch.");
        }
        public static ParticipantQr Encode(string text)
        {
            if(string.IsNullOrEmpty(text)||text.Any(c=>c>127||char.IsControl(c)))throw new ArgumentException("QR requires a public ASCII URL.");
            byte[] bytes=Encoding.ASCII.GetBytes(text);int version,capacity=0,countBits=0;
            for(version=1;version<=10;version++) {
                capacity=RawModules(version)/8-Ecc[version]*Blocks[version];countBits=version<10?8:16;
                if(bytes.Length<(1<<countBits)&&4+countBits+bytes.Length*8<=capacity*8)break;
            }
            if(version>10)throw new ArgumentException("Participant URL is too long for this QR display.");
            var bits=new List<bool>();Append(bits,4,4);Append(bits,bytes.Length,countBits);
            foreach(byte b in bytes)Append(bits,b,8);
            Append(bits,0,Math.Min(4,capacity*8-bits.Count));while(bits.Count%8!=0)bits.Add(false);
            var data=new byte[capacity];int used=bits.Count/8;
            for(int i=0;i<bits.Count;i++)if(bits[i])data[i/8]|=(byte)(1<<(7-i%8));
            for(int i=used;i<capacity;i++)data[i]=(i-used)%2==0?(byte)0xEC:(byte)0x11;
            return new ParticipantQr(version,data);
        }
        private static void Append(List<bool> bits,int value,int count)
        { for(int i=count-1;i>=0;i--)bits.Add(((value>>i)&1)!=0); }
        private void Set(int x,int y,bool value) { modules[x,y]=value;function[x,y]=true; }
        private void Finder(int cx,int cy)
        {
            for(int dy=-4;dy<=4;dy++)for(int dx=-4;dx<=4;dx++) {
                int x=cx+dx,y=cy+dy;if(x<0||y<0||x>=Size||y>=Size)continue;
                int distance=Math.Max(Math.Abs(dx),Math.Abs(dy));Set(x,y,distance!=2&&distance!=4);
            }
        }
        private void DrawFunctions()
        {
            for(int i=0;i<Size;i++){Set(6,i,i%2==0);Set(i,6,i%2==0);}
            Finder(3,3);Finder(Size-4,3);Finder(3,Size-4);
            if(Version>=2) {
                int count=Version/7+2,step=(Version*4+count*2+1)/(count*2-2)*2;
                var positions=new int[count];positions[0]=6;
                for(int i=count-1,p=Size-7;i>=1;i--,p-=step)positions[i]=p;
                for(int i=0;i<count;i++)for(int j=0;j<count;j++) {
                    if(i==0&&(j==0||j==count-1)||i==count-1&&j==0)continue;
                    for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)Set(positions[i]+dx,positions[j]+dy,Math.Max(Math.Abs(dx),Math.Abs(dy))!=1);
                }
            }
            int rem=8;for(int i=0;i<10;i++)rem=(rem<<1)^((rem>>9)*0x537);
            int format=((8<<10)|rem)^0x5412;
            for(int i=0;i<=5;i++)Set(8,i,((format>>i)&1)!=0);
            Set(8,7,((format>>6)&1)!=0);Set(8,8,((format>>7)&1)!=0);Set(7,8,((format>>8)&1)!=0);
            for(int i=9;i<15;i++)Set(14-i,8,((format>>i)&1)!=0);
            for(int i=0;i<8;i++)Set(Size-1-i,8,((format>>i)&1)!=0);
            for(int i=8;i<15;i++)Set(8,Size-15+i,((format>>i)&1)!=0);
            Set(8,Size-8,true);
            if(Version>=7) {
                rem=Version;for(int i=0;i<12;i++)rem=(rem<<1)^((rem>>11)*0x1F25);
                int bits=(Version<<12)|rem;
                for(int i=0;i<18;i++){int a=Size-11+i%3,b=i/3;bool bit=((bits>>i)&1)!=0;Set(a,b,bit);Set(b,a,bit);}
            }
        }
        private static byte Multiply(byte x,byte y)
        {
            int result=0;for(int i=7;i>=0;i--){result=(result<<1)^((result>>7)*0x11D);result^=((y>>i)&1)*x;}return (byte)result;
        }
        private static byte[] Remainder(byte[] data,int degree)
        {
            var divisor=new byte[degree];divisor[degree-1]=1;byte root=1;
            for(int i=0;i<degree;i++) {
                for(int j=0;j<degree;j++){divisor[j]=Multiply(divisor[j],root);if(j+1<degree)divisor[j]^=divisor[j+1];}
                root=Multiply(root,2);
            }
            var result=new byte[degree];
            foreach(byte b in data) {
                byte factor=(byte)(b^result[0]);Array.Copy(result,1,result,0,degree-1);result[degree-1]=0;
                for(int i=0;i<degree;i++)result[i]^=Multiply(divisor[i],factor);
            }
            return result;
        }
        private static byte[] Interleave(byte[] data,int version)
        {
            int blocks=Blocks[version],ecc=Ecc[version],raw=RawModules(version)/8;
            int shortCount=blocks-raw%blocks,shortData=raw/blocks-ecc,offset=0;
            var chunks=new List<byte[]>();var checks=new List<byte[]>();var result=new List<byte>();
            for(int i=0;i<blocks;i++) {
                int length=shortData+(i>=shortCount?1:0);var block=new byte[length];Array.Copy(data,offset,block,0,length);offset+=length;
                chunks.Add(block);checks.Add(Remainder(block,ecc));
            }
            for(int i=0;i<=shortData;i++)foreach(var block in chunks)if(i<block.Length)result.Add(block[i]);
            for(int i=0;i<ecc;i++)foreach(var check in checks)result.Add(check[i]);
            return result.ToArray();
        }
        public string ToSvg()
        {
            var path=new StringBuilder();
            for(int y=0;y<Size;y++)for(int x=0;x<Size;x++)if(modules[x,y])path.Append("M").Append(x+4).Append(',').Append(y+4).Append("h1v1h-1z");
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 "+(Size+8)+" "+(Size+8)+"\" shape-rendering=\"crispEdges\"><path fill=\"white\" d=\"M0 0h"+(Size+8)+"v"+(Size+8)+"H0z\"/><path fill=\"black\" d=\""+path+"\"/></svg>";
        }
    }
}
