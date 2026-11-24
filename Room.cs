using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MCLink {
    internal enum RouteMode { Automatic=0, DirectOnly=1, RelayOnly=2 }

    internal sealed class Room {
        internal const string HostVirtualIp="10.144.144.1";
        static readonly string[] RelaySchemes = { "tcp", "udp", "ws", "wss", "quic" };
        internal readonly string Key;
        internal readonly int Port;
        internal readonly bool ReportsPlayerRoutes;
        internal readonly int? BedrockPort;
        internal readonly string[] Relays;
        internal Room(string key, int port, bool reportsPlayerRoutes, int? bedrockPort=null, string[] relays=null) {
            Key = key; Port = port; ReportsPlayerRoutes = reportsPlayerRoutes;
            BedrockPort=bedrockPort; Relays=relays;
        }
        internal string Ticket {
            get {
                if (!BedrockPort.HasValue) return (ReportsPlayerRoutes ? "MCL2-" : "MCL1-") + Key + "-" + Port;
                string peers=Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", Relays)))
                    .TrimEnd('=').Replace('+','-').Replace('/','_');
                return "MCL3-"+Key+"-"+Port+"-"+BedrockPort.Value+"-"+peers;
            }
        }
        internal static Room Create(int port, bool reportsPlayerRoutes = false, int? bedrockPort=null, string[] relays=null) {
            byte[] bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var room=new Room(BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(), port, reportsPlayerRoutes, bedrockPort, relays);
            if (bedrockPort.HasValue && room.Ticket.Length>8192)
                throw new ArgumentException("中继地址过长，请在 relay.txt 中减少节点数量。");
            return room;
        }
        internal static Room Parse(string text) {
            text=text.Trim();
            if (text.StartsWith("MCL3-",StringComparison.Ordinal)) {
                var cross=Regex.Match(text.Length>8192 ? "" : text, @"\AMCL3-([a-f0-9]{32})-([0-9]{1,5})-([0-9]{1,5})-([A-Za-z0-9_-]+)\z");
                int java, bedrock;
                if (cross.Success && int.TryParse(cross.Groups[2].Value,out java) && java>0 && java<=65535
                    && int.TryParse(cross.Groups[3].Value,out bedrock) && bedrock>0 && bedrock<=65535 && java!=bedrock) {
                    try {
                        string encoded=cross.Groups[4].Value.Replace('-','+').Replace('_','/');
                        encoded=encoded.PadRight((encoded.Length+3)/4*4,'=');
                        var peers=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(encoded)).Split('\n');
                        if (peers.All(ValidRelay)) return new Room(cross.Groups[1].Value,java,true,bedrock,peers);
                    } catch (FormatException) {} catch (DecoderFallbackException) {}
                }
                throw new ArgumentException("跨端房间码无效，请复制完整的 MCL3 房间码。");
            }
            var m = Regex.Match(text.Trim(), @"\AMCL([12])-([a-f0-9]{32})-([0-9]{1,5})\z");
            int port;
            if (!m.Success || !int.TryParse(m.Groups[3].Value, out port) || port < 1 || port > 65535)
                throw new ArgumentException("邀请码格式不正确，请复制完整的 MCL1 或 MCL2 邀请码。");
            return new Room(m.Groups[2].Value, port, m.Groups[1].Value=="2");
        }
        internal static int FreePort() {
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            try { return ((IPEndPoint)socket.LocalEndpoint).Port; }
            finally { socket.Stop(); }
        }
        internal static string EnginePath() {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime");
            var exe = Directory.Exists(dir) ? Directory.GetFiles(dir, "easytier-core.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
            if (exe == null) throw new FileNotFoundException("未找到 EasyTier，请先运行 setup.ps1 安装项目依赖。");
            return exe;
        }
        internal static string[] ReadRelays(string directory) {
            string path = Path.Combine(directory, "relay.txt");
            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch (FileNotFoundException) { throw new InvalidOperationException("请在 MCLink.exe 同目录创建 relay.txt，并填写中继地址。"); }
            catch (IOException ex) { throw new InvalidOperationException("无法读取 relay.txt。", ex); }
            catch (UnauthorizedAccessException ex) { throw new InvalidOperationException("无法读取 relay.txt。", ex); }

            var relays = new List<string>();
            for (int i = 0; i < lines.Length; i++) {
                string value = lines[i].Trim();
                if (value.Length == 0 || value.StartsWith("#", StringComparison.Ordinal)) continue;
                if (!ValidRelay(value))
                    throw new InvalidOperationException("relay.txt 第 " + (i + 1) + " 行不是有效的中继地址，请检查协议、主机名和端口。");
                relays.Add(value);
            }
            if (relays.Count == 0)
                throw new InvalidOperationException("请在 relay.txt 中填写房主和好友都能连接的 EasyTier 中继地址。");
            return relays.Distinct(StringComparer.Ordinal).ToArray();
        }
        static bool ValidRelay(string value) {
            Uri uri;
            return !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl)
                && value.IndexOfAny(new[] { '"', '\\' })<0
                && Uri.TryCreate(value, UriKind.Absolute, out uri)
                && RelaySchemes.Contains(uri.Scheme) && uri.Host.Length>0 && uri.Port>0
                && uri.UserInfo.Length==0 && uri.Fragment.Length==0;
        }
        internal string Config(bool host, int localPort, IEnumerable<string> peers, string listener,
                               RouteMode mode = RouteMode.Automatic, int? bedrockPort = null) {
            // A separate ACL per endpoint. Guest accepts no incoming application traffic.
            var sb = new StringBuilder();
            sb.AppendLine("instance_name = \"mclink-" + Guid.NewGuid().ToString("N") + "\"");
            sb.AppendLine(host ? "ipv4 = \""+HostVirtualIp+"\"" : "dhcp = true");
            sb.AppendLine("listeners = [\"" + listener + "\"]");
            sb.AppendLine("rpc_portal = \"127.0.0.1:0\"");
            AppendNetwork(sb,peers);
            if (!host) sb.AppendLine("[[port_forward]]\nbind_addr = \"127.0.0.1:" + localPort + "\"\ndst_addr = \""+HostVirtualIp+":" + Port + "\"\nproto = \"tcp\"");
            sb.AppendLine("[flags]\nno_tun = true\nbind_device = false\nrelay_network_whitelist = \"\"");
            if (mode == RouteMode.DirectOnly) sb.AppendLine("p2p_only = true");
            if (mode == RouteMode.RelayOnly) sb.AppendLine("disable_p2p = true");
            AppendInboundAcl(sb);
            if (host) {
                AppendAllowedPort(sb,"minecraft",1000,1,Port);
                if (bedrockPort.HasValue) {
                    AppendAllowedPort(sb,"geyser-signaling",1001,1,bedrockPort.Value);
                    AppendAllowedPort(sb,"geyser-game",1002,2,bedrockPort.Value);
                }
            }
            return sb.ToString();
        }

        void AppendNetwork(StringBuilder sb,IEnumerable<string> peers) {
            sb.AppendLine("[network_identity]\nnetwork_name = \"mc-" + Key.Substring(0,12) + "\"\nnetwork_secret = \"" + Key + "\"");
            foreach (string peer in peers) sb.AppendLine("[[peer]]\nuri = \"" + peer + "\"");
        }

        static void AppendInboundAcl(StringBuilder sb) {
            sb.AppendLine("[[acl.acl_v1.chains]]\nname = \"room-inbound\"\nchain_type = 1\ndefault_action = 2\nenabled = true");
        }

        static void AppendAllowedPort(StringBuilder sb,string name,int priority,int protocol,int port) {
            sb.AppendLine("[[acl.acl_v1.chains.rules]]\nname = \""+name+"\"\npriority = "+priority+
                "\naction = 1\nprotocol = "+protocol+"\nports = [\""+port+"\"]\nenabled = true");
        }
    }

    internal sealed class PlayerStatus {
        internal readonly int? Online;
        internal readonly string[] Names;

        PlayerStatus(int? online, string[] names) {
            Online=online;
            Names=names;
        }

        internal static PlayerStatus Parse(string json) {
            var serializer=new JavaScriptSerializer {MaxJsonLength=1048576, RecursionLimit=32};
            Dictionary<string, object> status;
            try { status=serializer.DeserializeObject(json) as Dictionary<string, object>; }
            catch (ArgumentException) { status=null; }
            catch (InvalidOperationException) { status=null; }

            object rawPlayers;
            var players=status!=null && status.TryGetValue("players",out rawPlayers) ?
                rawPlayers as Dictionary<string, object> : null;
            if (players==null) return new PlayerStatus(null,new string[0]);
            object rawOnline, rawSample;
            int? online=players.TryGetValue("online",out rawOnline) ? rawOnline as int? : null;
            if (online<0) online=null;
            var sample=players.TryGetValue("sample",out rawSample) ? rawSample as object[] : null;
            var names=new List<string>();
            var seen=new HashSet<string>(StringComparer.Ordinal);
            if (sample!=null) {
                foreach (var entry in sample.OfType<Dictionary<string, object>>()) {
                    if (online.HasValue && names.Count>=online.Value) break;
                    object rawName;
                    var name=entry.TryGetValue("name",out rawName) ? rawName as string : null;
                    if (String.IsNullOrWhiteSpace(name) || name.Length>64 || name.Any(char.IsControl)) continue;
                    name=name.Trim();
                    if (seen.Add(name)) names.Add(name);
                }
            }
            return new PlayerStatus(online,names.ToArray());
        }
    }

    internal static class GameProbe {
        static void VarInt(Stream stream, int n) {
            while (n >= 128) { stream.WriteByte((byte)(n | 128)); n >>= 7; }
            stream.WriteByte((byte)n);
        }
        internal static int ReadSize(Stream stream) {
            int result = 0;
            for (int s = 0; s <= 28; s += 7) {
                int b = stream.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                if (s == 28 && b > 7) throw new InvalidDataException();
                result |= (b & 127) << s;
                if (b < 128) return result;
            }
            throw new InvalidDataException();
        }
        internal static byte[] Frame(Stream stream) {
            int size = ReadSize(stream);
            if (size < 1 || size > 1048576) throw new InvalidDataException();
            byte[] data = new byte[size];
            for (int pos=0; pos<size;) {
                int count=stream.Read(data,pos,size-pos);
                if (count==0) throw new EndOfStreamException();
                pos+=count;
            }
            return data;
        }
        internal static long Check(int localPort, int gamePort) {
            return Check(localPort,gamePort,null);
        }
        internal static PlayerStatus CheckPlayers(int port) {
            PlayerStatus players=null;
            Check(port,port,json => players=PlayerStatus.Parse(json));
            return players;
        }
        static long Check(int localPort, int gamePort, Action<string> readStatus) {
            using (var socket = new TcpClient()) {
                socket.NoDelay = true;
                var connect = socket.BeginConnect(IPAddress.Loopback,localPort,null,null);
                using (var wait = connect.AsyncWaitHandle)
                    if (!wait.WaitOne(1800)) throw new TimeoutException();
                socket.EndConnect(connect);
                socket.ReceiveTimeout=2200; socket.SendTimeout=2200;
                using (var net=socket.GetStream())
                using (var packet=new MemoryStream()) {
                    packet.WriteByte(0); VarInt(packet,47);
                    byte[] addr=Encoding.ASCII.GetBytes("localhost");
                    VarInt(packet,addr.Length); packet.Write(addr,0,addr.Length);
                    packet.WriteByte((byte)(gamePort >> 8)); packet.WriteByte((byte)gamePort); packet.WriteByte(1);
                    VarInt(net,(int)packet.Length); packet.WriteTo(net);
                    net.Write(new byte[]{1,0},0,2);
                    var status=Frame(net);
                    using(var response=new MemoryStream(status)) {
                        if(response.ReadByte()!=0 || ReadSize(response)!=response.Length-response.Position || response.ReadByte()!=123)
                            throw new InvalidDataException("不是 Java 版状态响应。");
                        if (readStatus!=null) {
                            int start=(int)response.Position-1;
                            readStatus(Encoding.UTF8.GetString(status,start,status.Length-start));
                        }
                    }
                    byte[] ping=BitConverter.GetBytes(DateTime.UtcNow.Ticks);
                    byte[] request=new byte[10]; request[0]=9; request[1]=1;
                    Array.Copy(ping,0,request,2,8);
                    var clock=Stopwatch.StartNew(); net.Write(request,0,10);
                    byte[] pong=Frame(net);
                    if(pong.Length!=9 || pong[0]!=1 || !pong.Skip(1).SequenceEqual(ping)) throw new InvalidDataException();
                    return clock.ElapsedMilliseconds;
                }
            }
        }
    }
}

