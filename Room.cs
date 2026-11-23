using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MCLink {
    internal sealed class Room {
        internal readonly string Key;
        internal readonly int Port;
        internal Room(string key, int port) { Key = key; Port = port; }
        internal string Ticket { get { return "MCL1-" + Key + "-" + Port; } }
        internal static Room Create(int port) {
            byte[] bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return new Room(BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(), port);
        }
        internal static Room Parse(string text) {
            var m = Regex.Match(text.Trim(), @"\AMCL1-([a-f0-9]{32})-([0-9]{1,5})\z");
            int port;
            if (!m.Success || !int.TryParse(m.Groups[2].Value, out port) || port < 1 || port > 65535)
                throw new ArgumentException("邀请码格式不正确，请复制完整的 MCL1 邀请码。");
            return new Room(m.Groups[1].Value, port);
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
        internal string Config(bool host, int localPort, string peer, string listener) {
            // A separate ACL per endpoint. Guest accepts no incoming application traffic.
            var sb = new StringBuilder();
            sb.AppendLine("instance_name = \"mclink-" + Guid.NewGuid().ToString("N") + "\"");
            sb.AppendLine(host ? "ipv4 = \"10.144.144.1\"" : "dhcp = true");
            sb.AppendLine("listeners = [\"" + listener + "\"]");
            sb.AppendLine("rpc_portal = \"127.0.0.1:0\"");
            sb.AppendLine("[network_identity]\nnetwork_name = \"mc-" + Key.Substring(0,12) + "\"\nnetwork_secret = \"" + Key + "\"");
            sb.AppendLine("[[peer]]\nuri = \"" + peer + "\"");
            if (!host) sb.AppendLine("[[port_forward]]\nbind_addr = \"127.0.0.1:" + localPort + "\"\ndst_addr = \"10.144.144.1:" + Port + "\"\nproto = \"tcp\"");
            sb.AppendLine("[flags]\nno_tun = true\nbind_device = false\nrelay_network_whitelist = \"\"");
            sb.AppendLine("[[acl.acl_v1.chains]]\nname = \"room-inbound\"\nchain_type = 1\ndefault_action = 2\nenabled = true");
            if (host) sb.AppendLine("[[acl.acl_v1.chains.rules]]\nname = \"minecraft\"\npriority = 1000\naction = 1\nprotocol = 1\nports = [\"" + Port + "\"]\nenabled = true");
            return sb.ToString();
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

