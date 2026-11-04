using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CCCPRacing.Export;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    async void LanExportTests()
    {
        try {
            string Arg(string key)=>OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith(key+"="))?[(key.Length+1)..];
            string path=Arg("--lan-export-source") ?? throw new ArgumentException("--lan-export-source required");
            string output=Arg("--lan-export-dir") ?? Path.Combine(TapeProjectDir,"media","lan-export-validation-20261028");
            Directory.CreateDirectory(output);
            string Sha()=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            string before=Sha();
            using var original=new Net.LanReplayFile.Reader(path);
            var source=new RaceScene.LanExportSource(path,original.Metadata.LocalId);
            float start=float.Parse(Arg("--lan-export-in") ?? "40",System.Globalization.CultureInfo.InvariantCulture);
            var request=new ExportRequest {InClock=start,OutClock=start+2,DestinationDir=output,Name="lan-bonus-two-cars",Audio=true,KeepDiagnostics=true};
            var env=new ExportEnvironment(OS.GetExecutablePath(),OS.HasFeature("editor")?ProjectSettings.GlobalizePath("res://").TrimEnd('/'):null,
                Path.Combine(output,"jobs"),output,null,null,System.Environment.ProcessId);
            var control=new ExportControl();var job=ExportJob.Start(source,request,control,env);
            var timeout=System.Diagnostics.Stopwatch.StartNew();int last=-1;
            while(!job.Done.IsCompleted) {
                await ToSignal(GetTree().CreateTimer(1),SceneTreeTimer.SignalName.Timeout);
                if((int)timeout.Elapsed.TotalSeconds/10!=last) {last=(int)timeout.Elapsed.TotalSeconds/10;GD.Print($"LANEXPORT {job.Progress.Phase} {job.Progress.FramesDone} {job.Progress.Message}");}
                if(timeout.Elapsed.TotalSeconds>900) control.Cancel();
            }
            await job.Done;
            bool passed=job.Progress.Phase==ExportPhase.Done && File.Exists(job.Progress.OutputPath) && before==Sha() && !Directory.Exists(Path.Combine(output,"jobs",job.JobId));
            GD.Print($"LANEXPORT RESULT {job.Progress.Phase} {job.Progress.Message} output={job.Progress.OutputPath} log={job.Progress.LogPath} originalUnchanged={before==Sha()} sourceMin={source.MinClockSeconds} sourceMax={source.DurationSeconds}");
            GetTree().Quit(passed?0:1);
        } catch(Exception e) {GD.PrintErr(e);GetTree().Quit(1);}
    }
}
