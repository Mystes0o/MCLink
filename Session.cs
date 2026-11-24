using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MCLink {
    internal sealed class Session : IDisposable {
        readonly string dir;
        Process process;
        internal bool Running { get { return process != null && !process.HasExited; } }
        internal readonly StringBuilder Errors = new StringBuilder();
        internal Session(string config, string temporaryRoot=null, int rpcPort=0) {
            var exe=Room.EnginePath();
            dir=Path.Combine(temporaryRoot ?? Path.GetTempPath(),"mclink-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                string path=Path.Combine(dir,"room.toml");
                File.WriteAllText(path,config,new UTF8Encoding(false));
                var args="-c \""+path+"\"";
                // The standalone CLI portal is configured by the core process, outside the room TOML.
                if (rpcPort!=0) args+=" -r 127.0.0.1:"+rpcPort;
                var info=new ProcessStartInfo(exe,args) {
                    WorkingDirectory=dir, UseShellExecute=false, CreateNoWindow=true,
                    RedirectStandardOutput=true, RedirectStandardError=true
                };
                process=new Process { StartInfo=info };
                process.OutputDataReceived+=Capture; process.ErrorDataReceived+=Capture;
                process.Start();
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
            } catch { Dispose(); throw; }
        }
        void Capture(object sender,DataReceivedEventArgs e) {
            if(e.Data==null) return;
            lock(Errors) { if(Errors.Length>12000) Errors.Clear(); Errors.AppendLine(e.Data); }
        }

        internal static RouteMode ReadHostRoute(int rpcPort) {
            try {
                var cli=Path.Combine(Path.GetDirectoryName(Room.EnginePath()),"easytier-cli.exe");
                if (!File.Exists(cli)) return RouteMode.Automatic;
                var options=new ProcessStartInfo(cli,"-p 127.0.0.1:"+rpcPort+" -o json route list") {
                    UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true,
                    StandardOutputEncoding=Encoding.UTF8
                };
                using (var query=Process.Start(options)) {
                    var output=query.StandardOutput.ReadToEndAsync();
                    if (!query.WaitForExit(1000)) {
                        if (!query.HasExited) query.Kill();
                        return RouteMode.Automatic;
                    }
                    if (query.ExitCode!=0) return RouteMode.Automatic;
                    return ParseHostRoute(output.GetAwaiter().GetResult());
                }
            } catch (System.ComponentModel.Win32Exception) {}
              catch (IOException) {}
              catch (InvalidOperationException) {}
              catch (ArgumentException) {}
            return RouteMode.Automatic;
        }

        static RouteMode ParseHostRoute(string json) {
            var rows=new JavaScriptSerializer().DeserializeObject(json) as object[];
            if (rows!=null) foreach (var item in rows) {
                var row=item as Dictionary<string,object>;
                object rawAddress, rawHops;
                if (row==null || !row.TryGetValue("ipv4",out rawAddress) ||
                    !row.TryGetValue("path_len",out rawHops)) continue;
                var address=rawAddress as string;
                var hops=rawHops as int?;
                if (address==null || address.Split('/')[0]!=Room.HostVirtualIp || !(hops>0)) continue;
                return hops==1 ? RouteMode.DirectOnly : RouteMode.RelayOnly;
            }
            return RouteMode.Automatic;
        }
        public void Dispose() {
            if(process!=null) {
                try { if(!process.HasExited) process.Kill(); process.WaitForExit(3000); }
                catch(InvalidOperationException) {}
                finally { process.Dispose(); process=null; }
            }
            try { File.Delete(Path.Combine(dir,"room.toml")); Directory.Delete(dir,false); } catch(IOException) {}
        }
    }
}
