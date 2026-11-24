using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MCLink {
    // The route report shares the Minecraft connection so it cannot go stale independently.
    internal sealed class PlayerRouteBridge : IDisposable {
        sealed class Link {
            internal readonly TcpClient Source, Target = new TcpClient();
            internal string Name;
            internal RouteMode Route;
            internal Link(TcpClient source) { Source=source; }
            internal void Close() { Source.Close(); Target.Close(); }
        }

        readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);
        readonly HashSet<Link> links=new HashSet<Link>();
        readonly int targetPort;
        readonly Func<RouteMode> report;
        bool disposed;

        // The guest prepends a report. The host removes it and observes the login name.
        internal PlayerRouteBridge(int targetPort, Func<RouteMode> report=null) {
            this.targetPort=targetPort;
            this.report=report;
            listener.Start();
            Port=((IPEndPoint)listener.LocalEndpoint).Port;
            Accept();
        }

        internal int Port { get; private set; }

        internal Dictionary<string,string> Routes() {
            lock (links) {
                return links.Where(link=>link.Name!=null)
                    .GroupBy(link=>link.Name,StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group=>group.Key,group=> {
                        var mode=group.First().Route;
                        if (group.Any(link=>link.Route!=mode)) return "待确认";
                        if (mode==RouteMode.DirectOnly) return "接入时直连";
                        if (mode==RouteMode.RelayOnly) return "接入时中继";
                        return "接入时线路未确认";
                    },StringComparer.OrdinalIgnoreCase);
            }
        }

        async void Accept() {
            try {
                while (true) {
                    var client=await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    var link=new Link(client);
                    lock (links) {
                        if (disposed) { link.Close(); return; }
                        links.Add(link);
                    }
                    Forward(link);
                }
            } catch (SocketException) {}
              catch (ObjectDisposedException) {}
              catch (InvalidOperationException) {}
        }

        async void Forward(Link link) {
            try {
                link.Source.NoDelay=link.Target.NoDelay=true;
                await link.Target.ConnectAsync(IPAddress.Loopback,targetPort).ConfigureAwait(false);
                var input=link.Source.GetStream();
                var output=link.Target.GetStream();
                if (report!=null) {
                    // Zero is not a valid first Minecraft packet length.
                    var header=new byte[] {0,(byte)report()};
                    await output.WriteAsync(header,0,header.Length).ConfigureAwait(false);
                } else {
                    using (var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                    using (timeout.Token.Register(link.Source.Close))
                        await ReadHostPrefix(link,input,output).ConfigureAwait(false);
                }
                var upload=Pump(input,output,link.Target);
                var download=Pump(output,input,link.Source);
                await (await Task.WhenAny(upload,download).ConfigureAwait(false)).ConfigureAwait(false);
                await Task.WhenAll(upload,download).ConfigureAwait(false);
            } catch (IOException) {}
              catch (SocketException) {}
              catch (ObjectDisposedException) {}
              catch (InvalidOperationException) {}
            finally {
                link.Close();
                lock (links) links.Remove(link);
            }
        }

        static async Task Pump(Stream source, Stream destination, TcpClient target) {
            await source.CopyToAsync(destination,16384).ConfigureAwait(false);
            var socket=target.Client;
            if (socket!=null) socket.Shutdown(SocketShutdown.Send);
        }

        async Task ReadHostPrefix(Link link, Stream input, Stream output) {
            var buffer=new byte[1];
            await ReadByte(input,buffer).ConfigureAwait(false);
            if (buffer[0]!=0) {
                // Status probes and unmarked streams retain their original bytes.
                await output.WriteAsync(buffer,0,1).ConfigureAwait(false);
                return;
            }
            await ReadByte(input,buffer).ConfigureAwait(false);
            var route=(RouteMode)buffer[0];
            try {
                var handshake=await ForwardPacket(input,output).ConfigureAwait(false);
                using (var packet=new MemoryStream(handshake)) {
                    if (GameProbe.ReadSize(packet)!=0) return;
                    GameProbe.ReadSize(packet); // Protocol version.
                    int addressLength=GameProbe.ReadSize(packet);
                    if (addressLength>packet.Length-packet.Position-3) return;
                    packet.Position+=addressLength+2; // Address and port.
                    int intent=GameProbe.ReadSize(packet);
                    if (intent!=2 && intent!=3) return; // Login or transfer.
                }
                var login=await ForwardPacket(input,output).ConfigureAwait(false);
                using (var packet=new MemoryStream(login)) {
                    if (GameProbe.ReadSize(packet)!=0) return;
                    int length=GameProbe.ReadSize(packet);
                    if (length<1 || length>64 || length>packet.Length-packet.Position) return;
                    var name=Encoding.UTF8.GetString(login,(int)packet.Position,length);
                    if (String.IsNullOrWhiteSpace(name) || name.Length>16 || name.Any(char.IsControl)) return;
                    lock (links) {
                        link.Name=name;
                        link.Route=route;
                    }
                }
            } catch (InvalidDataException) {
                // Keep forwarding when the game uses an unrecognized packet format.
            } catch (EndOfStreamException) {}
        }

        static async Task<byte[]> ForwardPacket(Stream source, Stream destination) {
            var prefix=new byte[5];
            int count=0;
            do {
                if (await source.ReadAsync(prefix,count,1).ConfigureAwait(false)==0)
                    throw new EndOfStreamException();
            } while ((prefix[count++] & 128)!=0 && count<prefix.Length);
            await destination.WriteAsync(prefix,0,count).ConfigureAwait(false);
            int length;
            using (var header=new MemoryStream(prefix,0,count)) length=GameProbe.ReadSize(header);
            if (length<1 || length>4096) throw new InvalidDataException();
            var packet=new byte[length];
            int offset=0;
            while (offset<length) {
                int read=await source.ReadAsync(packet,offset,length-offset).ConfigureAwait(false);
                if (read==0) throw new EndOfStreamException();
                await destination.WriteAsync(packet,offset,read).ConfigureAwait(false);
                offset+=read;
            }
            return packet;
        }

        static async Task ReadByte(Stream source, byte[] buffer) {
            if (await source.ReadAsync(buffer,0,1).ConfigureAwait(false)==0) throw new EndOfStreamException();
        }

        public void Dispose() {
            Link[] active;
            lock (links) {
                if (disposed) return;
                disposed=true;
                active=links.ToArray();
                links.Clear();
            }
            listener.Stop();
            foreach (var link in active) link.Close();
        }
    }
}
