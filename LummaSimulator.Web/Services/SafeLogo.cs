using System.Buffers.Binary;
using System.IO.Compression;
using LummaSimulator.Web.Models;
namespace LummaSimulator.Web.Services;
// Decode-bounded PNG validation and canonicalization: RGB/RGBA 8-bit, non-interlaced.
// Unknown ancillary metadata is discarded; SVG/JPEG/polyglots/oversized images are rejected.
public static class SafeLogo
{
    public static byte[] Validate(byte[] bytes)
    {
        try {
            if(bytes.Length is <57 or >262144||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new InvalidDataException();
            using var canonical=new MemoryStream();canonical.Write(bytes,0,8);using var compressed=new MemoryStream();
            var offset=8;var header=false;var end=false;var expected=0;var row=0;
            while(offset<bytes.Length){
                if(offset+12>bytes.Length)throw new InvalidDataException();var length=checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset,4)));
                if(length<0||length>bytes.Length-offset-12)throw new InvalidDataException();
                var type=System.Text.Encoding.ASCII.GetString(bytes,offset+4,4);var data=bytes.AsSpan(offset+8,length);
                uint crc=0xffffffff;foreach(var b in bytes.AsSpan(offset+4,length+4)){crc^=b;for(var bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}
                if((crc^0xffffffff)!=BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset+8+length,4)))throw new InvalidDataException();
                if(!header&&type!="IHDR")throw new InvalidDataException();
                if(type=="IHDR"){
                    if(header||length!=13)throw new InvalidDataException();header=true;
                    var width=BinaryPrimitives.ReadUInt32BigEndian(data);var height=BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                    if(width is <1 or >1024||height is <1 or >1024||data[8]!=8||data[9] is not (2 or 6)||data[10]!=0||data[11]!=0||data[12]!=0)throw new InvalidDataException();
                    row=checked((int)width*(data[9]==6?4:3)+1);expected=checked(row*(int)height);
                } else if(type=="IDAT")compressed.Write(data);
                else if(type=="IEND"){if(length!=0||offset+12!=bytes.Length)throw new InvalidDataException();end=true;}
                else if(type.Length!=4||type.Any(c=>!char.IsAsciiLetter(c))||char.IsUpper(type[0]))throw new InvalidDataException();
                if(type is "IHDR" or "IDAT" or "IEND")canonical.Write(bytes,offset,length+12);
                offset+=length+12;
            }
            if(!header||!end||compressed.Length==0)throw new InvalidDataException();compressed.Position=0;
            using var zip=new ZLibStream(compressed,CompressionMode.Decompress);var count=0;var buffer=new byte[8192];int read;
            while((read=zip.Read(buffer,0,buffer.Length))>0){for(var i=0;i<read;i++)if((count+i)%row==0&&buffer[i]>4)throw new InvalidDataException();count=checked(count+read);if(count>expected)throw new InvalidDataException();}
            if(count!=expected)throw new InvalidDataException();return canonical.ToArray();
        }catch(Exception e) when(e is InvalidDataException or OverflowException or ArgumentException){throw new DemoRequestException("P65LogoInvalid");}
    }
}
