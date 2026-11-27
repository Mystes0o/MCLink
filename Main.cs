using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MCLink {
    partial class Window : Form {
        readonly TextBox port = new TextBox {Text="25565"};
        readonly TextBox ticket = new CampTextBox(), address = new TextBox {ReadOnly=true};
        readonly Button host=new CampButton(), join=new CampButton(), leave=new CampButton();
        readonly Label status=new Label();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=2500 };
        readonly List<long> samples=new List<long>();
        readonly Queue<bool> attempts=new Queue<bool>();
        Task crossplayShutdown;
        Session session;
        PlayerRouteBridge routeBridge;
        string guestConfig;
        DateTime nextGuestRestart;
        volatile RouteMode guestRoute;
        string probeFailure, routeFailure, discoveryFailure;
        bool? lastReachable;
        int local, remote, generation, guestRpcPort;
        bool hosting, probing, closing;
        Window() {
            BuildLayout();
            Stop();
            DiagnosticLog.Info("MCLink","已启动，等待建房或加入");
            timer.Tick+=async delegate { await Probe(); }; timer.Start();
            FormClosing+=CloseConnections;
        }
        async void CloseConnections(object sender, FormClosingEventArgs args) {
            if (closing) { args.Cancel=true; return; }
            timer.Stop(); Stop();
            if (crossplayWork.Count==0 && !SavingCrossplay) return;
            args.Cancel=true;
            closing=true;
            host.Enabled=join.Enabled=false;
            await Task.Yield();
            IEnumerable<Task> pending=crossplayWork;
            if (SavingCrossplay) pending=pending.Concat(new[] {crossplayShutdown});
            try { await Task.WhenAll(pending.ToArray()); }
            catch (Exception ex) { DiagnosticLog.Error("MCLink","关闭时等待房间任务失败",ex); }
            finally { closing=false; Close(); }
        }
        void Copy(string s) {
            if(s.Length==0) return;
            try {
                Clipboard.SetText(s);
            } catch(Exception ex) {
                DiagnosticLog.Error("MCLink","复制到剪贴板失败",ex);
                MessageBox.Show("剪贴板忙，请选中文本后手动复制。");
            }
        }
        async Task StartRoom(bool create, CrossplayServer managed=null) {
            if (!host.Enabled && managed==null) return;
            Room room=null;
            int gamePort=0;
            int? requestedGeyserPort=managed==null ? (int?)null : CrossplayServer.BedrockPort;
            try {
                if (create) {
                    gamePort=managed==null ? ReadGamePort(port.Text) : managed.JavaPort;
                } else room=Room.Parse(ticket.Text);
            } catch (ArgumentException ex) {
                DiagnosticLog.Error("房间",create ? "游戏端口校验失败" : "邀请码校验失败",ex);
                ShowConnection(ex.Message);
                var field = create ? port : ticket;
                field.Focus();
                field.SelectAll();
                return;
            }
            string[] relays;
            try { relays=room!=null && room.Relays!=null ? room.Relays : Room.ReadRelays(AppDomain.CurrentDomain.BaseDirectory); }
            catch (InvalidOperationException ex) {
                DiagnosticLog.Error("房间","读取中继配置失败",ex);
                if (managed!=null) Stop();
                ShowConnection(ex.Message);
                return;
            }
            int current=++generation;
            DiagnosticLog.Info("房间",(create ? "创建房间" : "加入房间")+"，候选中继 "+relays.Length+" 个");
            PlayerStatus players=null;
            try {
                host.Enabled=join.Enabled=false;
                UpdateAndroidLink();
                port.ReadOnly=ticket.ReadOnly=true;
                leave.Enabled=true;
                if(create) {
                    ShowConnection("正在检查本机游戏端口…");
                    players=await Task.Run(()=>GameProbe.CheckPlayers(gamePort));
                    if(current!=generation) return;
                    DiagnosticLog.Info("Java 探测","本机游戏端口 "+gamePort+" 已响应");
                    routeBridge=new PlayerRouteBridge(gamePort);
                    room=Room.Create(routeBridge.Port,true,requestedGeyserPort,managed==null ? null : relays);
                }
                hosting=create;
                remote=create ? gamePort : room.Port;
                if (create) {
                    local=remote;
                    session=new Session(room.Config(true,local,relays,bedrockPort:requestedGeyserPort));
                    if (managed!=null) {
                        await Task.Delay(400);
                        if (current!=generation) return;
                        if (!session.Running) throw new InvalidOperationException("EasyTier 启动失败，请检查联机组件和中继设置。");
                        DiagnosticLog.Info("Geyser","Bedrock 信令 TCP / 游戏 UDP 端口 "+requestedGeyserPort);
                        address.Text="127.0.0.1:"+gamePort;
                        copyAddress.Enabled=true;
                    }
                    UpdateAndroidLink();
                } else {
                    local=Room.FreePort();
                    do { guestRpcPort=Room.FreePort(); } while (guestRpcPort==local);
                    guestConfig=room.Config(false,local,relays);
                    guestRoute=RouteMode.Automatic;
                    session=new Session(guestConfig,rpcPort:guestRpcPort);
                    if (room.ReportsPlayerRoutes) routeBridge=new PlayerRouteBridge(local,()=>guestRoute);
                    address.Text="127.0.0.1:"+(routeBridge==null ? local : routeBridge.Port);
                    copyAddress.Enabled=false;
                    DiagnosticLog.Info("TCP 转发","游戏入口 "+address.Text+" → 本机代理端口 "+local+
                        " → 房主 "+Room.HostVirtualIp+":"+room.Port);
                    ShowRoute("正在确认线路");
                }
                ticket.Text=room.Ticket;
                copyTicket.Enabled=create;
                copyTicket.Visible=create;
                copyAddress.Visible=!create;
                ShowPlayers(players);
                ShowConnection(managed!=null ? "跨端房间已启动，房主游戏地址 "+address.Text :
                    create ? "房间已启动，请复制邀请码。" : "正在确认游戏响应…");
            } catch(Exception ex) {
                if(current!=generation) return;
                DiagnosticLog.Error("房间",create ? "建房失败" : "加入失败",ex);
                Stop();
                ShowConnection(create && managed==null && !(ex is System.IO.FileNotFoundException) ?
                    "建房失败，请核对游戏端口及局域网开放状态。"+ex.Message : ex.Message);
            }
        }
        async Task Probe() {
            if((session==null && guestConfig==null) || probing) return;
            if(crossplay!=null && !crossplay.Running) {
                DiagnosticLog.Warn("跨端服务端","本地 Java 服务端已退出，"+crossplay.ExitStatus);
                Stop(); ShowConnection("本地 Java 服务端已退出，请查看连接日志。"); return;
            }
            if(hosting && !session.Running) {
                DiagnosticLog.Warn("EasyTier","房主组网进程已退出，"+session.ExitStatus);
                Stop(); ShowConnection("组网进程已退出，请检查组件、网络后重新建房。"); return;
            }
            if (!hosting && (session==null || !session.Running)) {
                copyAddress.Enabled=false;
                guestRoute=RouteMode.Automatic;
                ShowRoute("正在重连");
                if (DateTime.UtcNow<nextGuestRestart) return;
                nextGuestRestart=DateTime.UtcNow.AddSeconds(10);
                if (session!=null) {
                    DiagnosticLog.Warn("EasyTier","访客组网进程已退出，"+session.ExitStatus);
                    session.Dispose(); session=null;
                }
                DiagnosticLog.Info("EasyTier","正在重启访客组网");
                try { session=new Session(guestConfig,rpcPort:guestRpcPort); }
                catch (Exception error) {
                    DiagnosticLog.Error("EasyTier","重启访客组网失败",error);
                    ShowConnection("正在重试启动组网："+error.Message); return;
                }
            }
            int current=generation, target=local, game=remote, rpc=guestRpcPort; probing=true;
            long ms=-1;
            PlayerStatus players=null;
            RouteMode route=RouteMode.Automatic;
            Exception gameError=null, queryError=null;
            try {
                if (session.Running) {
                    if (hosting) players=await Task.Run(()=>GameProbe.CheckPlayers(game));
                    else {
                        try { ms=await Task.Run(()=>GameProbe.Check(target,game)); }
                        catch (Exception ex) { gameError=ex; }
                        route=await Task.Run(()=>Session.ReadHostRoute(rpc,ex=>queryError=ex));
                    }
                }
            }
            catch(Exception ex) { gameError=ex; }
            finally {probing=false;}
            if(current!=generation) return;
            if (target!=local) return;
            bool reachable=hosting ? players!=null : ms>=0;
            ReportProbeError("Java 探测","游戏状态查询失败，端口 "+target,gameError,ref probeFailure);
            ReportProbeError("EasyTier","读取房主线路失败",queryError,ref routeFailure);
            if (lastReachable!=reachable) {
                if (reachable) DiagnosticLog.Info("Java 探测",hosting ? "本机游戏可达" : "房主游戏已响应，RTT "+ms+" ms");
                else DiagnosticLog.Warn("Java 探测",hosting ? "本机游戏无响应" : "房主游戏无响应，继续重试");
                lastReachable=reachable;
            }
            if(hosting) {
                ShowPlayers(players);
                ShowConnection(players==null ? "本机游戏端口无响应，请确认存档仍然开放。" :
                    crossplay!=null ? "房主游戏地址 "+address.Text+"，请把邀请码发给好友。" :
                    "本机存档可用，请把邀请码发给好友。");
                return;
            }
            attempts.Enqueue(ms>=0); if(attempts.Count>24) attempts.Dequeue();
            guestRoute=route;
            ShowRoute(route==RouteMode.DirectOnly ? "点对点直连" :
                route==RouteMode.RelayOnly ? "中继（仍在尝试直连）" : "线路未确认");
            copyAddress.Enabled=ms>=0;
            if(ms>=0) {
                var discoveryError=GameProbe.AnnounceLocalRoom(routeBridge==null ? local : routeBridge.Port);
                ReportProbeError("局域网发现","发送 Java 房间广播失败",discoveryError,ref discoveryFailure);
                samples.Add(ms);
                if(samples.Count>24) samples.RemoveAt(0);
            }
            long? p95=samples.Count==0 ? (long?)null :
                samples.OrderBy(v=>v).ElementAt((int)Math.Ceiling(samples.Count*.95)-1);
            ShowConnection(ms>=0 ? "游戏可连接，在「多人游戏」中选择 MCLink 房间" :
                "尚未收到游戏响应，请确认房主在线、端口正确且双方可访问中继。",
                ms>=0 ? (long?)ms : null,p95,attempts.Count(ok=>ok)+"/"+attempts.Count);
        }
        static void ReportProbeError(string source,string context,Exception error,ref string previous) {
            string signature=error==null ? null : error.GetType().Name+" "+error.Message;
            if (signature==previous) return;
            if (error!=null) DiagnosticLog.Error(source,context,error);
            else DiagnosticLog.Info(source,context.Replace("失败","已恢复"));
            previous=signature;
        }
        static int ReadGamePort(string input) {
            int number;
            if (!int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out number)
                || number < 1 || number > 65535)
                throw new ArgumentException("请输入 1 到 65535 之间的游戏端口。");
            return number;
        }
        void Stop() {
            if (session!=null || guestConfig!=null || crossplay!=null || crossplayCancellation!=null)
                DiagnosticLog.Info("房间","正在断开房间");
            if (crossplayCancellation!=null) crossplayCancellation.Cancel();
            guestConfig=null; guestRoute=RouteMode.Automatic; nextGuestRestart=DateTime.MinValue;
            lastReachable=null; probeFailure=routeFailure=discoveryFailure=null;
            generation++; if(session!=null) {session.Dispose();session=null;}
            if (routeBridge!=null) {routeBridge.Dispose();routeBridge=null;}
            if(hosting) ticket.Clear(); hosting=false; address.Clear(); samples.Clear(); attempts.Clear();
            if (crossplay!=null) {
                var stopped=crossplay;
                crossplay=null;
                crossplayShutdown=SaveCrossplay(stopped);
            }
            host.Enabled=join.Enabled=!SavingCrossplay;
            leave.Enabled=false; port.ReadOnly=ticket.ReadOnly=false;
            UpdateAndroidLink();
            copyTicket.Enabled=copyAddress.Enabled=false;
            copyTicket.Visible=false; copyAddress.Visible=true;
            ShowPlayers(null);
            ShowRoute("未连接");
            ShowConnection(SavingCrossplay ? "正在保存联机存档…" : "等待建房或加入");
        }
        bool SavingCrossplay { get { return crossplayShutdown!=null && !crossplayShutdown.IsCompleted; } }

        async Task SaveCrossplay(CrossplayServer stopped) {
            await Task.Yield();
            string message="等待建房或加入";
            try {
                await Task.Run(()=>stopped.Dispose());
                DiagnosticLog.Info("跨端服务端","本地服务端已停止");
            } catch (Exception ex) {
                DiagnosticLog.Error("跨端服务端","停止本地服务端失败",ex);
                message="停止本地服务端失败。"+ex.Message;
            }
            if (IsDisposed || closing) return;
            host.Enabled=join.Enabled=true;
            UpdateAndroidLink();
            ShowConnection(message);
        }

        [STAThread] static void Main() {Application.EnableVisualStyles();Application.Run(new Window());}
    }
}

