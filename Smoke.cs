using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
namespace MCLink {
    class Smoke {
        static void Main() {
            try { Run(); }
            catch(Exception e) {Console.Error.WriteLine(e.GetType().Name+": "+e.Message+" "+e.StackTrace); Environment.ExitCode=2;}
        }
        static void Run() {
            var game=new TcpListener(IPAddress.Loopback,0); game.Start();
            int port=((IPEndPoint)game.LocalEndpoint).Port;
            var thread=new Thread(delegate() {
                while(true) {
                    var c=game.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate {
                        using(c) try {
                            var s=c.GetStream(); s.ReadTimeout=3000;
                            GameProbe.Frame(s); GameProbe.Frame(s);
                            s.Write(new byte[]{4,0,2,123,125},0,5);
                            var p=GameProbe.Frame(s); s.WriteByte((byte)p.Length); s.Write(p,0,p.Length);
                        } catch(IOException) {}
                    });
                }
            }); thread.IsBackground=true;thread.Start();
            var room=Room.Create(port);
            if(Room.Parse(room.Ticket).Key!=room.Key) throw new Exception("Ticket round trip");
            try {Room.Parse(room.Ticket+" ");}catch {throw new Exception("Ticket whitespace");}
            Console.WriteLine("Local test game: "+GameProbe.Check(port,port)+" ms");
            int bridge=Room.FreePort(), seed=Room.FreePort(), rpc=Room.FreePort();
            string endpoint="tcp://127.0.0.1:"+seed;
            var hostConfig=room.Config(true,port,new string[0],endpoint);
            using(var host=new Session(hostConfig,AppDomain.CurrentDomain.BaseDirectory))
            using(var guest=new Session(room.Config(false,bridge,new[] { endpoint },"tcp://127.0.0.1:0"),
                AppDomain.CurrentDomain.BaseDirectory,rpcPort:rpc)) {
                for(int i=0;i<30;i++) {
                    Thread.Sleep(500);
                    try {
                        long ms=GameProbe.Check(bridge,port);
                        if (Session.ReadHostRoute(rpc)!=RouteMode.DirectOnly) continue;
                        Console.WriteLine("PASS two EasyTier processes, invite, TCP forward, Java status + ping/pong, direct route: "+ms+" ms");
                        return;
                    }catch(Exception e) { if(i==1 || i==29) Console.WriteLine("Probe: "+e.Message); if(!host.Running || !guest.Running) break; }
                }
                lock(host.Errors) Console.WriteLine(host.Errors.ToString().Replace(room.Key,"[key]"));
                lock(guest.Errors) Console.WriteLine(guest.Errors.ToString().Replace(room.Key,"[key]"));
                Environment.ExitCode=1;
            }
        }
    }
}
