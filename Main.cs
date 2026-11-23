using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MCLink {
    class Window : Form {
        readonly NumericUpDown port = new NumericUpDown {Minimum=1, Maximum=65535, Value=25565};
        readonly TextBox ticket = new TextBox(), address = new TextBox {ReadOnly=true};
        readonly Button host=new Button(), join=new Button(), leave=new Button();
        readonly Label status=new Label();
        readonly Timer timer=new Timer { Interval=2500 };
        readonly List<long> samples=new List<long>();
        readonly Queue<bool> attempts=new Queue<bool>();
        Session session;
        int local, remote, generation;
        bool hosting, probing;
        const string Peer="tcp://public.easytier.cn:11010";
        Window() {
            Text="MCLink · 好友联机"; Font=new Font("Microsoft YaHei UI",10);
            ClientSize=new Size(616,385); FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false;
            Add(new Label {Text="Minecraft Java 版好友联机", Font=new Font(Font,FontStyle.Bold)},20,16,570,30);
            Add(new Label {Text="房主：在存档中按 Esc → 对局域网开放，填入聊天栏显示的端口。"},20,55,580,30);
            Add(port,20,89,130,32); ButtonAt(host,"创建房间",165,85,125,async delegate { await StartRoom(true); });
            Add(new Label {Text="好友：粘贴房主的邀请码，再点加入。请仅向同玩的好友分享。"},20,135,580,28);
            Add(ticket,20,166,406,28); ButtonAt(join,"加入房间",439,162,145,async delegate { await StartRoom(false); });
            ButtonAt(new Button(),"复制邀请码",20,204,130,delegate { if(session!=null && hosting) Copy(ticket.Text); });
            Add(new Label {Text="游戏 → 多人游戏 → 直接连接"},20,254,370,28);
            Add(address,20,282,222,28); ButtonAt(new Button(),"复制地址",250,278,116,delegate { Copy(address.Text); });
            ButtonAt(leave,"退出房间",439,278,145,delegate { Stop(); });
            Add(status,20,332,580,50); Stop();
            timer.Tick+=async delegate { await Probe(); }; timer.Start();
            FormClosing+=delegate {timer.Stop(); Stop();};
        }
        void Add(Control c,int x,int y,int w,int h) { c.SetBounds(x,y,w,h); Controls.Add(c); }
        void ButtonAt(Button b,string title,int x,int y,int w,EventHandler action) {
            b.Text=title; Add(b,x,y,w,34); b.Click+=action;
        }
        void Copy(string s) {
            if(s.Length==0) return;
            try {Clipboard.SetText(s);} catch(Exception) {MessageBox.Show("剪贴板忙，请选中文本后手动复制。");}
        }
        async Task StartRoom(bool create) {
            int current=++generation;
            try {
                host.Enabled=join.Enabled=port.Enabled=false; ticket.ReadOnly=true;
                Room room=create ? Room.Create((int)port.Value) : Room.Parse(ticket.Text);
                if(create) {
                    status.Text="正在检查本机游戏端口…";
                    await Task.Run(()=>GameProbe.Check(room.Port,room.Port));
                    if(current!=generation) return;
                }
                hosting=create; remote=room.Port; local=create ? remote : Room.FreePort();
                session=new Session(room.Config(create,local,Peer,"udp://0.0.0.0:0"));
                ticket.Text=room.Ticket; address.Text=create ? "" : "127.0.0.1:"+local;
                leave.Enabled=true;
                status.Text=create ? "房间已启动，请复制邀请码。" : "正在连接房主的游戏…";
            } catch(Exception ex) {
                if(current!=generation) return;
                Stop(); status.Text=create && !(ex is System.IO.FileNotFoundException) ?
                    "建房失败，请核对游戏端口及局域网开放状态。"+ex.Message : ex.Message;
            }
        }
        async Task Probe() {
            if(session==null || probing) return;
            if(!session.Running) {Stop(); status.Text="组网进程已退出，请检查组件、网络后重新建房或加入。"; return;}
            int current=generation, target=local, game=remote; probing=true;
            long ms=-1;
            try { ms=await Task.Run(()=>GameProbe.Check(target,game)); }
            catch(Exception) {}
            finally {probing=false;}
            if(current!=generation) return;
            if(hosting) {
                status.Text=ms<0 ? "本机游戏端口无响应，请确认存档仍然开放。" : "本机存档可用，请把邀请码发给好友。";
                return;
            }
            attempts.Enqueue(ms>=0); if(attempts.Count>24) attempts.Dequeue();
            if(ms<0) {status.Text="尚未收到游戏响应，请确认房主在线、端口正确且双方可访问引导节点。";return;}
            samples.Add(ms); if(samples.Count>24) samples.RemoveAt(0);
            long p95=samples.OrderBy(v=>v).ElementAt((int)Math.Ceiling(samples.Count*.95)-1);
            status.Text=String.Format("游戏可连接　往返 {0} ms　最近成功样本 P95 {1} ms　最近探测成功 {2}/{3}",ms,p95,attempts.Count(ok=>ok),attempts.Count);
        }
        void Stop() {
            generation++; if(session!=null) {session.Dispose();session=null;}
            if(hosting) ticket.Clear(); hosting=false; address.Clear(); samples.Clear(); attempts.Clear();
            host.Enabled=join.Enabled=port.Enabled=true; leave.Enabled=false; ticket.ReadOnly=false;
            status.Text="等待建房或加入";
        }
        [STAThread] static void Main() {Application.EnableVisualStyles();Application.Run(new Window());}
    }
}

