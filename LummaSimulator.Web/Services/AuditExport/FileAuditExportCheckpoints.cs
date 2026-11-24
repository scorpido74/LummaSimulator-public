using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LummaSimulator.Web.Services.AuditExport;

public sealed class FileAuditExportCheckpoints(DemoStore store) : IAuditExportCheckpoints
{
    private sealed record Checkpoint(int Version,string Exporter,string SourceIdentity,Guid StreamId,long LastSequence);
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    public IAuditExportCheckpointLease Open(string exporter)
    {
        if(!Regex.IsMatch(exporter,"\\A[A-Za-z][A-Za-z0-9]{0,47}\\z")) throw new AuditExportException("InvalidExporterName");
        var path=Path.Combine(Path.GetDirectoryName(store.DatabasePath)!,$"identity-audit-export-{exporter}.checkpoint.json");
        var database=OperatingSystem.IsWindows() ? store.DatabasePath.ToUpperInvariant() : store.DatabasePath;
        var identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Lumma.IdentityExport.v1:"+database)));
        var fileLock=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try {
            Checkpoint checkpoint;
            if(File.Exists(path)) {
                if(new FileInfo(path).Length>4096) throw new AuditExportException("InvalidCheckpoint");
                try { checkpoint=JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(path),JsonOptions) ?? throw new AuditExportException("InvalidCheckpoint"); }
                catch(JsonException) {throw new AuditExportException("InvalidCheckpoint");}
                if(checkpoint.Version!=1 || checkpoint.Exporter!=exporter || checkpoint.SourceIdentity!=identity || checkpoint.StreamId==Guid.Empty || checkpoint.LastSequence<0)
                    throw new AuditExportException("InvalidCheckpoint");
            } else {
                checkpoint=new(1,exporter,identity,Guid.NewGuid(),0);
                Save(path,checkpoint); // Persist stable stream identity before the first external write.
            }
            return new Lease(path,fileLock,checkpoint);
        } catch {fileLock.Dispose();throw;}
    }
    private static void Save(string path,Checkpoint value)
    {
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false),1024,leaveOpen:true)) {writer.Write(JsonSerializer.Serialize(value,JsonOptions));writer.Flush();}
            stream.Flush(flushToDisk:true);
        }
        File.Move(temporary,path,overwrite:true);
    }
    private sealed class Lease(string path,FileStream fileLock,Checkpoint checkpoint) : IAuditExportCheckpointLease
    {
        private Checkpoint _checkpoint=checkpoint;
        public Guid StreamId => _checkpoint.StreamId;
        public long LastSequence => _checkpoint.LastSequence;
        public void Advance(long sequence)
        {
            if(sequence<LastSequence) throw new AuditExportException("CheckpointRegression");
            var next=_checkpoint with {LastSequence=sequence};Save(path,next);_checkpoint=next;
        }
        public void Dispose() => fileLock.Dispose();
    }
}
