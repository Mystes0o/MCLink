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
        readonly TextBox ticket = new TextBox(), address = new TextBox {ReadOnly=true};
        readonly Button host=new CampButton(), join=new CampButton(), leave=new CampButton();
        readonly Label status=new Label();
        readonly Timer timer=new Timer { Interval=2500 };
        readonly List<long> samples=new List<long>();
        readonly Queue<bool> attempts=new Queue<bool>();
        Session session;
        int local, remote, generation;
        bool hosting, probing;
        const string Peer="tcp://public.easytier.cn:11010";
        Window() {
            BuildLayout();
            Stop();
            timer.Tick+=async delegate { await Probe(); }; timer.Start();
            FormClosing+=delegate {timer.Stop(); Stop();};
        }
        void Copy(string s) {
            if(s.Length==0) return;
            try {Clipboard.SetText(s);} catch(Exception) {MessageBox.Show("剪贴板忙，请选中文本后手动复制。");}
        }
        async Task StartRoom(bool create) {
            if (!host.Enabled) return;
            Room room=null;
            int gamePort=0;
            try {
                if (create) gamePort=ReadGamePort(port.Text);
                else room=Room.Parse(ticket.Text);
            } catch (ArgumentException ex) {
                ShowConnection(ex.Message);
                var field = create ? port : ticket;
                field.Focus();
                field.SelectAll();
                return;
            }
            int current=++generation;
            try {
                host.Enabled=join.Enabled=false;
                port.ReadOnly=ticket.ReadOnly=true;
                leave.Enabled=true;
                if(create) {
                    room=Room.Create(gamePort);
                    ShowConnection("正在检查本机游戏端口…");
                    await Task.Run(()=>GameProbe.Check(room.Port,room.Port));
                    if(current!=generation) return;
                }
                hosting=create; remote=room.Port; local=create ? remote : Room.FreePort();
                session=new Session(room.Config(create,local,Peer,"udp://0.0.0.0:0"));
                ticket.Text=room.Ticket; address.Text=create ? "" : "127.0.0.1:"+local;
                copyTicket.Enabled=create;
                copyTicket.Visible=create;
                copyAddress.Visible=!create;
                ShowConnection(create ? "房间已启动，请复制邀请码。" : "正在连接房主的游戏…");
            } catch(Exception ex) {
                if(current!=generation) return;
                Stop();
                ShowConnection(create && !(ex is System.IO.FileNotFoundException) ?
                    "建房失败，请核对游戏端口及局域网开放状态。"+ex.Message : ex.Message);
            }
        }
        async Task Probe() {
            if(session==null || probing) return;
            if(!session.Running) {Stop(); ShowConnection("组网进程已退出，请检查组件、网络后重新建房或加入。"); return;}
            int current=generation, target=local, game=remote; probing=true;
            long ms=-1;
            try { ms=await Task.Run(()=>GameProbe.Check(target,game)); }
            catch(Exception) {}
            finally {probing=false;}
            if(current!=generation) return;
            if(hosting) {
                ShowConnection(ms<0 ? "本机游戏端口无响应，请确认存档仍然开放。" : "本机存档可用，请把邀请码发给好友。");
                return;
            }
            attempts.Enqueue(ms>=0); if(attempts.Count>24) attempts.Dequeue();
            copyAddress.Enabled=ms>=0;
            if(ms>=0) {samples.Add(ms); if(samples.Count>24) samples.RemoveAt(0);}
            long? p95=samples.Count==0 ? (long?)null :
                samples.OrderBy(v=>v).ElementAt((int)Math.Ceiling(samples.Count*.95)-1);
            ShowConnection(ms>=0 ? "游戏可连接" :
                "尚未收到游戏响应，请确认房主在线、端口正确且双方可访问引导节点。",
                ms>=0 ? (long?)ms : null,p95,attempts.Count(ok=>ok)+"/"+attempts.Count);
        }
        static int ReadGamePort(string input) {
            int number;
            if (!int.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out number)
                || number < 1 || number > 65535)
                throw new ArgumentException("请输入 1 到 65535 之间的游戏端口。");
            return number;
        }
        void Stop() {
            generation++; if(session!=null) {session.Dispose();session=null;}
            if(hosting) ticket.Clear(); hosting=false; address.Clear(); samples.Clear(); attempts.Clear();
            host.Enabled=join.Enabled=true; leave.Enabled=false; port.ReadOnly=ticket.ReadOnly=false;
            copyTicket.Enabled=copyAddress.Enabled=false;
            copyTicket.Visible=false; copyAddress.Visible=true;
            ShowConnection("等待建房或加入");
        }
        [STAThread] static void Main() {Application.EnableVisualStyles();Application.Run(new Window());}
    }
}

