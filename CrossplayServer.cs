using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace MCLink {
    sealed class CrossplayServer : IDisposable {
        internal const int BedrockPort=19132;
        internal const string GameVersion="26.3";
        internal const string EulaUrl="https://www.minecraft.net/eula";
        internal static string[] DataRoots() {
            var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var portable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtime","crossplay-data");
            return String.IsNullOrWhiteSpace(local) ? new[] {portable} :
                new[] {Path.Combine(local,"MCLink","Crossplay"),portable};
        }

        internal static string WritableRoot(string[] roots) {
            foreach(var root in roots) {
                try {
                    CheckWritable(Path.Combine(root,"worlds"));
                    CheckWritable(Path.Combine(root,"components"));
                    return root;
                } catch(UnauthorizedAccessException) {} catch(IOException) {}
            }
            throw new IOException("无法写入联机存档和组件目录。请将 MCLink 移到可写入的文件夹后重试。\n"+String.Join("\n",roots));
        }

        internal static string[] ExistingWorlds(string root) {
            try {
                var worlds=Path.Combine(root,"worlds");
                return Directory.GetDirectories(worlds)
                    .Where(path=>File.Exists(Path.Combine(path,"world","level.dat")))
                    .OrderByDescending(path=>path,StringComparer.OrdinalIgnoreCase).ToArray();
            } catch(UnauthorizedAccessException) { return new string[0]; }
              catch(IOException) { return new string[0]; }
        }

        static void CheckWritable(string directory) {
            Directory.CreateDirectory(directory);
            string probe=Path.Combine(directory,".mclink-write-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(probe);
            try {
                using(File.Create(Path.Combine(probe,"access"),1,FileOptions.DeleteOnClose)) {}
            } finally { Directory.Delete(probe,false); }
        }
        const string PaperSha="5efbc6ce4b8e128bfa77fa0d3e79132dfeaaab827abe0f87dd930535e5e489fd";
        const string GeyserSha="8682df3e77fd4a7e745ed671fbbb727aca32a77d89c97a50f5f77f20fdb191fa";
        const string FloodgateSha="21570aff9ce17d6983928e8552777760e1ede5050026b04c686b0ae112e6fd7e";
        readonly FileStream worldLease;
        readonly Process process;
        internal readonly int JavaPort;
        volatile bool paperReady, floodgateReady, geyserReady;
        readonly object logLock=new object();
        string lastError;
        internal bool Running { get { return !process.HasExited; } }
        internal string ExitStatus { get { return "退出码 "+process.ExitCode; } }

        internal static CrossplayServer Start(string directory,string source,Action<string> progress,CancellationToken cancel,
                                              string cacheOverride=null,string javaOverride=null) {
            Directory.CreateDirectory(directory);
            var lease=new FileStream(Path.Combine(directory,"mclink.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            CrossplayServer server=null;
            try {
                var world=Path.Combine(directory,"world");
                if (source!=null && Directory.Exists(world))
                    throw new IOException("已有同名联机存档，请使用「继续开房」或选择其他名称。");
                string cache=cacheOverride ?? Path.Combine(WritableRoot(DataRoots()),"components");
                Directory.CreateDirectory(cache);
                string java;
                using (var componentsLease=new FileStream(Path.Combine(cache,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                    progress("正在检查 Java 和联机组件…");
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    java=javaOverride ?? FindJava(cache,progress,cancel);
                    PrepareJar(cache,"paper.jar","https://fill-data.papermc.io/v1/objects/"+PaperSha+"/paper-26.3-259.jar",PaperSha,progress,cancel);
                    PrepareJar(cache,"Geyser-Spigot.jar","https://download.geysermc.org/v2/projects/geyser/versions/2.12.1/builds/1308/downloads/spigot",GeyserSha,progress,cancel);
                    PrepareJar(cache,"floodgate-spigot.jar","https://download.geysermc.org/v2/projects/floodgate/versions/2.2.5/builds/141/downloads/spigot",FloodgateSha,progress,cancel);
                }
                if (source!=null) ImportWorld(source,world,cancel);
                cancel.ThrowIfCancellationRequested();
                int javaPort=Room.FreePort();
                while (javaPort==BedrockPort) javaPort=Room.FreePort();
                var plugins=Path.Combine(directory,"plugins");
                Directory.CreateDirectory(plugins);
                File.Copy(Path.Combine(cache,"Geyser-Spigot.jar"),Path.Combine(plugins,"Geyser-Spigot.jar"),true);
                File.Copy(Path.Combine(cache,"floodgate-spigot.jar"),Path.Combine(plugins,"floodgate-spigot.jar"),true);
                ConfigureGeyser(plugins);
                File.WriteAllText(Path.Combine(directory,"eula.txt"),"eula=true\n");
                File.WriteAllText(Path.Combine(directory,"server.properties"),
                    "server-ip=127.0.0.1\nserver-port="+javaPort+"\nonline-mode=true\nlevel-name=world\nmotd=MCLink\n");
                progress("正在启动 Java 世界、Geyser 和 Floodgate…");
                server=new CrossplayServer(directory,java,Path.Combine(cache,"paper.jar"),javaPort,lease);
                var timer=Stopwatch.StartNew();
                while (timer.Elapsed<TimeSpan.FromMinutes(5)) {
                    cancel.ThrowIfCancellationRequested();
                    if (!server.Running) throw new IOException(server.Failure("游戏服务端提前退出"));
                    if (server.paperReady && server.floodgateReady && server.geyserReady && IsGameReady(javaPort))
                        return server;
                    cancel.WaitHandle.WaitOne(250);
                }
                throw new IOException(server.Failure("游戏服务端启动超时"));
            } catch {
                if (server==null) lease.Dispose();
                else server.Dispose();
                throw;
            }
        }

        CrossplayServer(string directory,string java,string jar,int javaPort,FileStream lease) {
            JavaPort=javaPort;
            worldLease=lease;
            string args="-Xms1G -Xmx2G -DgeyserTransport=nethernet -DgeyserUdpAddress=127.0.0.1"
                +" -DgeyserUdpPort="+BedrockPort+" -DgeyserSignalingMode=builtin -DgeyserSignalingPort="+BedrockPort
                +" -DgeyserWebrtcPort="+BedrockPort+" -DgeyserAdvertiseAddresses="+Room.HostVirtualIp
                +" -jar \""+jar+"\" --nogui";
            process=new Process {StartInfo=new ProcessStartInfo(java,args) {
                WorkingDirectory=directory, UseShellExecute=false, CreateNoWindow=true,
                RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true
            }};
            process.OutputDataReceived+=(sender,eventArgs)=>Capture(eventArgs,false);
            process.ErrorDataReceived+=(sender,eventArgs)=>Capture(eventArgs,true);
            try {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            } catch {
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) {}
                process.Dispose();
                throw;
            }
        }

        void Capture(DataReceivedEventArgs args,bool stderr) {
            var line=args.Data;
            if (line==null) return;
            DiagnosticLog.ProcessLine("跨端服务端",line,stderr);
            if (line.IndexOf("Done (",StringComparison.Ordinal)>=0 && line.Contains("For help")) paperReady=true;
            if (line.Contains("[floodgate]") && line.Contains("Took") && line.Contains("boot Floodgate")) floodgateReady=true;
            if (line.Contains("Built-in signaling started")) geyserReady=true;
            if (line.IndexOf("ERROR",StringComparison.OrdinalIgnoreCase)>=0 || line.IndexOf("Exception",StringComparison.Ordinal)>=0)
                lock(logLock) lastError=line;
        }

        string Failure(string message) {
            lock(logLock) return message+"。请查看该联机存档目录的 logs/latest.log。"+(lastError==null ? "" : "\n"+lastError);
        }

        static bool IsGameReady(int port) {
            try { return GameProbe.CheckPlayers(port)!=null; }
            catch (InvalidDataException) { return false; }
            catch (IOException) { return false; }
            catch (SocketException) { return false; }
            catch (TimeoutException) { return false; }
        }

        public void Dispose() {
            try {
                if (process.HasExited) return;
                try { process.StandardInput.WriteLine("stop"); process.StandardInput.Flush(); }
                catch (IOException) {}
                if (!process.WaitForExit(45000)) {
                    // Keep the world lease until a running server has finished saving.
                    if (paperReady) process.WaitForExit();
                    else { process.Kill(); process.WaitForExit(5000); }
                }
            } finally {
                process.Dispose();
                worldLease.Dispose();
            }
        }

        static void ConfigureGeyser(string plugins) {
            var directory=Path.Combine(plugins,"Geyser-Spigot");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"config.yml"),"java:\n  auth-type: floodgate\n");
        }

        static void ImportWorld(string source,string destination,CancellationToken cancel) {
            var incoming=destination+"-import-"+Guid.NewGuid().ToString("N");
            if (!File.Exists(Path.Combine(source,"level.dat"))) throw new IOException("请选择含有 level.dat 的 Java 存档文件夹。");
            var lockPath=Path.Combine(source,"session.lock");
            using (var originalLease=File.Exists(lockPath) ? new FileStream(lockPath,FileMode.Open,FileAccess.Read,FileShare.None) : null) {
                try {
                    CopyWorld(new DirectoryInfo(source),incoming,cancel);
                    cancel.ThrowIfCancellationRequested();
                    Directory.Move(incoming,destination);
                } finally {
                    if (Directory.Exists(incoming)) Directory.Delete(incoming,true);
                }
            }
        }

        static void CopyWorld(DirectoryInfo source,string destination,CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested();
            if ((source.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("请选择不含目录链接的存档。");
            Directory.CreateDirectory(destination);
            foreach (var file in source.EnumerateFiles()) {
                cancel.ThrowIfCancellationRequested();
                if (file.Name=="session.lock") continue;
                if ((file.Attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("请选择不含文件链接的存档。");
                file.CopyTo(Path.Combine(destination,file.Name));
            }
            foreach (var folder in source.EnumerateDirectories()) CopyWorld(folder,Path.Combine(destination,folder.Name),cancel);
        }

        static void PrepareJar(string cache,string name,string url,string checksum,Action<string> progress,CancellationToken cancel) {
            string file=Path.Combine(cache,name);
            if (Matches(file,checksum)) return;
            progress("正在下载并校验 "+name+"…");
            Download(url,file,checksum,cancel);
        }

        static string FindJava(string cache,Action<string> progress,CancellationToken cancel) {
            var candidate=Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(candidate)) {
                var java=Path.Combine(candidate,"bin","java.exe");
                if (Java25(java)) return java;
            }
            foreach (var path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
                var java=Path.Combine(path.Trim().Trim('"'),"java.exe");
                if (Path.IsPathRooted(java) && Java25(java)) return java;
            }
            var installed=Path.Combine(cache,"java25");
            if (Directory.Exists(installed)) {
                var java=Directory.GetFiles(installed,"java.exe",SearchOption.AllDirectories).FirstOrDefault();
                if (java!=null && Java25(java)) return java;
            }
            progress("正在安装 Java 25 运行环境…");
            const string api="https://api.adoptium.net/v3/assets/latest/25/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";
            string zip=Path.Combine(cache,"java25.zip");
            using (var client=new WebClient())
            using (cancel.Register(client.CancelAsync)) {
                var assets=(IList)new JavaScriptSerializer().DeserializeObject(client.DownloadStringTaskAsync(api).GetAwaiter().GetResult());
                if (assets==null || assets.Count==0) throw new IOException("未找到可下载的 Windows Java 25 运行环境。");
                var binary=(IDictionary)((IDictionary)assets[0])["binary"];
                var package=(IDictionary)binary["package"];
                Download((string)package["link"],zip,(string)package["checksum"],cancel);
            }
            string unpack=installed+"-"+Guid.NewGuid().ToString("N");
            try {
                ZipFile.ExtractToDirectory(zip,unpack);
                var java=Directory.GetFiles(unpack,"java.exe",SearchOption.AllDirectories).FirstOrDefault();
                if (java==null || !Java25(java)) throw new IOException("下载的 Java 运行环境无法使用。");
                cancel.ThrowIfCancellationRequested();
                if (Directory.Exists(installed)) Directory.Move(installed,installed+"-old-"+Guid.NewGuid().ToString("N"));
                Directory.Move(unpack,installed);
                return installed+java.Substring(unpack.Length);
            } finally {
                if (Directory.Exists(unpack)) Directory.Delete(unpack,true);
                File.Delete(zip);
            }
        }

        static bool Java25(string java) {
            if (!File.Exists(java)) return false;
            try {
                using (var test=Process.Start(new ProcessStartInfo(java,"-version") {
                    UseShellExecute=false, CreateNoWindow=true, RedirectStandardError=true
                })) {
                    if (!test.WaitForExit(4000)) { test.Kill(); return false; }
                    return test.ExitCode==0 && System.Text.RegularExpressions.Regex.IsMatch(
                        test.StandardError.ReadToEnd(),@"version ""25[.+""]");
                }
            } catch (System.ComponentModel.Win32Exception) { return false; }
        }

        static bool Matches(string path,string checksum) {
            if (!File.Exists(path)) return false;
            using (var stream=File.OpenRead(path))
            using (var sha=SHA256.Create())
                return string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""),checksum,StringComparison.OrdinalIgnoreCase);
        }

        static void Download(string url,string path,string checksum,CancellationToken cancel) {
            cancel.ThrowIfCancellationRequested();
            var uri=new Uri(url);
            if (uri.Scheme!="https" || checksum==null || checksum.Length!=64)
                throw new IOException("联机组件下载地址或校验信息无效。");
            string part=path+"."+Guid.NewGuid().ToString("N")+".part";
            try {
                using (var client=new WebClient())
                using (cancel.Register(client.CancelAsync)) {
                    client.Headers[HttpRequestHeader.UserAgent]="MCLink/1.0 (https://github.com/Mystes0o/MCLink)";
                    client.DownloadFileTaskAsync(uri,part).GetAwaiter().GetResult();
                }
                cancel.ThrowIfCancellationRequested();
                if (!Matches(part,checksum)) throw new IOException("组件 SHA-256 校验失败。");
                if (File.Exists(path)) File.Replace(part,path,null);
                else File.Move(part,path);
            } finally { File.Delete(part); }
        }
    }
}
