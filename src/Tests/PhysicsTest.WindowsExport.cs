using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CCCPRacing.Export;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void WindowsExportTests()
    {
        string root=Path.Combine(Path.GetTempPath(),"cccp-windows-export-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string Put(string path,string text) {Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,text);return path;}
        string Hash(string dir)=>string.Join("\n",Directory.GetFiles(dir,"*",SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.Ordinal).Select(x=>Path.GetRelativePath(dir,x)+":"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(x)))));
        try {
            Check(CCCPRacing.Race.ExportDriver.ParentAlive(Environment.ProcessId), "Windows export: parent liveness uses the operating system process API");
            Check(!CCCPRacing.Race.ExportDriver.ParentAlive(int.MaxValue) && CCCPRacing.Race.ExportDriver.ParentAlive(0), "Windows export: an exited parent stops rendering while an unspecified parent is allowed");
            foreach(string backend in new[]{"vulkan","d3d12"}) {
                string install=Path.Combine(root,"Pelit ääkköset "+backend);Directory.CreateDirectory(install);
                string exe=Put(Path.Combine(install,"CCCPRacing.exe"),"WINDOWS EXE");
                Put(Path.ChangeExtension(exe,".pck"),"THE GAME PACK");
                Put(Path.Combine(install,"data_CCCPRacing_windows_x86_64","CCCPRacing.dll"),"MANAGED GAME");
                Put(Path.Combine(install,"data_CCCPRacing_windows_x86_64","runtime","hostfxr.dll"),"RUNTIME");
                string native=Put(Path.Combine(install,"dxcompiler.dll"),"BACKEND DEPENDENCY");
                File.SetAttributes(native,File.GetAttributes(native)|FileAttributes.ReadOnly);
                string original=Put(Path.Combine(install,"override.cfg"),"[rendering]\nrendering_device/driver.windows=\""+backend+"\"\n[display]\nwindow/size/mode=4\nwindow/size/viewport_width=1280\n[application]\nboot_splash/show_image=true\n");
                string before=Hash(install),job=Path.Combine(root,"job "+backend);Directory.CreateDirectory(job);
                long estimated=ExportBootstrap.CopyBytesFor(exe,false,null,true);
                var launch=ExportBootstrap.BuildFor(job,exe,null,Path.Combine(job,"plan.json"),Path.Combine(job,"data"),60,"TEST",480,270,true);
                Check(File.ReadAllText(launch.Exe)=="WINDOWS EXE" && File.ReadAllText(Path.ChangeExtension(launch.Exe,".pck"))=="THE GAME PACK","Windows export: executable and PCK copied into the owned "+backend+" bootstrap");
                Check(File.Exists(Path.Combine(launch.WorkDir,"data_CCCPRacing_windows_x86_64","runtime","hostfxr.dll")) && File.Exists(Path.Combine(launch.WorkDir,"dxcompiler.dll")),"Windows export: managed runtime tree and native backend DLLs are copied ("+backend+")");
                Check((File.GetAttributes(Path.Combine(launch.WorkDir,"dxcompiler.dll"))&FileAttributes.ReadOnly)==0 && (File.GetAttributes(native)&FileAttributes.ReadOnly)!=0,"Windows export: copied files are removable while the installation keeps its attributes ("+backend+")");
                Check(launch.CopiedBytes==estimated && estimated>new FileInfo(exe).Length,"Windows export: free-space estimate includes copied PCK, runtime and DLLs ("+backend+")");
                string over=File.ReadAllText(Path.Combine(launch.WorkDir,"override.cfg"));
                Check(over.Contains("rendering_device/driver.windows=\""+backend+"\"") && over.Contains("window/size/viewport_width=480") && over.Contains("window/size/viewport_height=270") && over.Contains("window/size/mode=0") && over.Contains("window/size/no_focus=true") && over.Contains("boot_splash/show_image=false"),"Windows export: backend survives while child-only display and splash settings are applied ("+backend+")");
                Check(!launch.Args.Contains("x11") && launch.Args.Contains("--write-movie") && launch.Args.Contains("480x270") && launch.Args.Contains("--export-plan="+Path.Combine(job,"plan.json")) && launch.Args.Contains(Path.Combine(job,"godot.log")),"Windows export: native Windows launch arguments preserve paths with spaces and accents ("+backend+")");
                Directory.Delete(launch.WorkDir,true);
                Check(Hash(install)==before && File.ReadAllText(original).Contains("window/size/mode=4"),"Windows export: removing the staged copy leaves the complete installation unchanged ("+backend+")");
            }
            string project=Path.Combine(root,"dev project"),editor=Put(Path.Combine(root,"Godot Mono.exe"),"EDITOR");
            Put(Path.Combine(project,"project.godot"),"[application]\nconfig/name=\"CCCP Racing\"\n");
            Put(Path.Combine(project,".godot","mono","temp","bin","Debug","CCCPRacing.dll"),"COMPILED GAME");
            Put(Path.Combine(project,"assets","sprite.png"),"SPRITE");Put(Path.Combine(project,"tracks","ilyich.json"),"TRACK");
            Put(Path.Combine(project,"src","game.cs"),"CODE");Put(Path.Combine(project,"media","original.bin"),"DO NOT COPY MEDIA");
            Put(Path.Combine(project,"build","package.zip"),"DO NOT COPY RELEASES");Put(Path.Combine(project,".git","config"),"DO NOT COPY HISTORY");
            var dev=ExportBootstrap.BuildFor(Path.Combine(root,"dev job"),editor,project,"PLAN","DATA",30,"TEST",480,270,true);
            Check(dev.Exe==editor && dev.Args.Contains("--path") && File.Exists(Path.Combine(dev.WorkDir,".godot","mono","temp","bin","Debug","CCCPRacing.dll")),"Windows export: editor mode uses its original engine and a copied runtime project");
            Check(!Directory.Exists(Path.Combine(dev.WorkDir,"media")) && !Directory.Exists(Path.Combine(dev.WorkDir,"build")) && !Directory.Exists(Path.Combine(dev.WorkDir,".git")),"Windows export: developer bootstrap excludes captures, packages and Git history");
            Check(dev.CopiedBytes==ExportBootstrap.CopyBytesFor(editor,true,project,true),"Windows export: developer copy estimate matches actual allowed files");
            string parentData=Environment.GetEnvironmentVariable("APPDATA");
            var psi=new ProcessStartInfo(editor){UseShellExecute=false};string ready=ExportWindowHider.ConfigureWindows(psi,Path.Combine(root,"hidden job"));
            Check(psi.WindowStyle==ProcessWindowStyle.Hidden && psi.CreateNoWindow && !psi.UseShellExecute,"Windows export: renderer launches with hidden startup and no console");
            Check(psi.Environment["APPDATA"].StartsWith(root) && psi.Environment["LOCALAPPDATA"].StartsWith(root) && Environment.GetEnvironmentVariable("APPDATA")==parentData,"Windows export: child data environment is isolated without changing the parent");
            Check(psi.Environment["CCCP_EXPORT_WINDOW_READY"]==ready && !ExportWindowHider.Ready(ready),"Windows export: parent waits for the native hidden-window acknowledgment");
            Put(ready,"hidden-win32-v1\n");Check(ExportWindowHider.Ready(ready),"Windows export: Windows native readiness marker is accepted");
            Put(ready,"hidden-x11-v1\n");Check(ExportWindowHider.Ready(ready),"Windows export: Linux native readiness remains accepted");
            Put(ready,"unexpected\n");Check(!ExportWindowHider.Ready(ready),"Windows export: invalid readiness is refused");
            string markers=Path.Combine(root,"markers.txt");
            using(var stream=new FileStream(markers,FileMode.Create,FileAccess.Write,FileShare.ReadWrite))
            using(var writer=new StreamWriter(stream){AutoFlush=true}) {
                writer.WriteLine("CAMERA_KEYS 2");writer.WriteLine("CAMERA_MOTION 3");
                string text=ExportJob.ReadSharedText(markers);
                Check(text.Contains("CAMERA_KEYS 2") && text.Contains("CAMERA_MOTION 3"),"Windows export: capability markers can be read while the renderer keeps its writer open");
            }
            int attempts=0;
            int result=ExportJob.RetryIo(()=>{if(++attempts<3)throw new IOException("temporary file sharing conflict");return attempts;});
            Check(result==3 && attempts==3,"Windows export: transient file sharing conflicts are retried");
            string owned=Path.Combine(root,"owned-job");Put(Path.Combine(owned,".cccp-export-job"),"owned-job\n");
            string copied=Put(Path.Combine(owned,"game","runtime.dll"),"COPY");File.SetAttributes(copied,File.GetAttributes(copied)|FileAttributes.ReadOnly);
            Check(ExportJob.DeleteJobDir(owned,root,"owned-job")==null && !Directory.Exists(owned),"Windows export: owned cleanup removes copied read-only files");
        } finally {
            if(Directory.Exists(root)) {
                foreach(var file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))File.SetAttributes(file,File.GetAttributes(file)&~FileAttributes.ReadOnly);
                Directory.Delete(root,true);
            }
        }
    }
}
