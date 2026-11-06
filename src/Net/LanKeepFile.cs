using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CCCPRacing.Net;

/// <summary>
/// KEEP for a LAN race: a verified copy of the round's finished journal in the kept folder. The automatic
/// original is never moved, rewritten or deleted, and nothing is pruned. Pure file work, no Godot calls (worker).
/// </summary>
public static class LanKeepFile
{
    public sealed record Result(bool Ok,string Path,string Message);
    /// <summary>Test seam: the next save ends in this error after its partial copy (proves cleanup and the visible failure).</summary>
    public static string FailForTest;

    public static string KeptDir(string recordingsRoot)=>System.IO.Path.Combine(recordingsRoot,"kept");

    /// <summary>"ILYICH LAN 1-39-59" -> "ILYICH-LAN-1-39-59"; only letters, digits and single dashes.</summary>
    public static string FileStem(string name)
    {
        var sb=new System.Text.StringBuilder();
        foreach(char c0 in (name ?? "").Normalize(System.Text.NormalizationForm.FormD))      // Ä -> A + mark (the mark is dropped)
        {
            char c=char.ToUpperInvariant(c0);
            if(c is >= 'A' and <= 'Z' or >= '0' and <= '9') sb.Append(c);
            else if(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            else if(sb.Length>0 && sb[^1]!='-') sb.Append('-');
        }
        string stem=sb.ToString().Trim('-');
        return stem.Length==0?"LAN-RACE":stem.Length>40?stem[..40].TrimEnd('-'):stem;
    }

    public static Task<Result> Retain(string source,string keptDir,string name,DateTime now)
    {
        string fail=FailForTest;FailForTest=null;
        return Task.Run(()=>Run(source,keptDir,name,now,fail));
    }

    static Result Run(string source,string keptDir,string name,DateTime now,string fail)
    {
        string partial=null;
        bool createdPartial=false;
        try {
            if(string.IsNullOrEmpty(source) || !File.Exists(source)) return new(false,null,"THE RECORDING FILE IS GONE");
            using(var check=new LanReplayFile.Reader(source)) { }      // never keep a damaged or unfinished journal
            Directory.CreateDirectory(keptDir);
            string stem=FileStem(name)+"-"+now.ToString("yyyyMMdd-HHmmss");
            string target=System.IO.Path.Combine(keptDir,stem+".lanreplay");
            for(int n=2;File.Exists(target) || File.Exists(target+".partial");n++) target=System.IO.Path.Combine(keptDir,$"{stem}-{n}.lanreplay");
            partial=target+".partial";
            using(var from=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var to=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {createdPartial=true;from.CopyTo(to);to.Flush(true);}
            if(fail!=null) throw new IOException(fail);
            using(var verify=new LanReplayFile.Reader(partial)) { }
            File.Move(partial,target,false);partial=null;
            return new(true,target,null);
        } catch(Exception e) {
            return new(false,null,e is InvalidDataException or EndOfStreamException?"THE RECORDING IS DAMAGED":e.Message);
        } finally {
            if(createdPartial && partial!=null) try {File.Delete(partial);} catch { }      // never clean up a competing save's partial
        }
    }

    /// <summary>Kept files, newest first (for the log and tests; there is no in-game list yet).</summary>
    public static string[] List(string keptDir)=>Directory.Exists(keptDir)?
        Directory.GetFiles(keptDir,"*.lanreplay").OrderByDescending(File.GetLastWriteTimeUtc).ToArray():Array.Empty<string>();
}
