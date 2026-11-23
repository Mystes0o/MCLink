using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MCLink {
    internal sealed class Session : IDisposable {
        readonly string dir;
        Process process;
        internal bool Running { get { return process != null && !process.HasExited; } }
        internal readonly StringBuilder Errors = new StringBuilder();
        internal Session(string config, string temporaryRoot=null) {
            var exe=Room.EnginePath();
            dir=Path.Combine(temporaryRoot ?? Path.GetTempPath(),"mclink-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try {
                string path=Path.Combine(dir,"room.toml");
                File.WriteAllText(path,config,new UTF8Encoding(false));
                var info=new ProcessStartInfo(exe,"-c \""+path+"\"") {
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
