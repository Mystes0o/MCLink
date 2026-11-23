using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MCLink {
    partial class Window {
        readonly Button copyTicket = new CampButton(), copyAddress = new CampButton();
        readonly Label rttValue = new Label(), p95Value = new Label(), samplesValue = new Label();
        readonly List<Action<float>> layout = new List<Action<float>>();
        CampCanvas canvas;
        ToolTip tooltip;
        static readonly Color InputColor = Color.FromArgb(55, 52, 51);
        static readonly Color StatusColor = Color.FromArgb(43, 40, 39);
        static readonly Color White = Color.FromArgb(251, 249, 243);

        [DllImport("user32.dll", CharSet=CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, string text);
        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        void BuildLayout() {
            SuspendLayout();
            Text="MCLink · 好友联机";
            AutoScaleMode=AutoScaleMode.None;
            FormBorderStyle=FormBorderStyle.None;
            StartPosition=FormStartPosition.CenterScreen;
            BackColor=Color.FromArgb(36,33,31);
            var area=Screen.PrimaryScreen.WorkingArea;
            float scale=Math.Min(.75f,Math.Min((area.Width-30)/1536f,(area.Height-30)/1024f));
            ClientSize=new Size((int)(1536*scale),(int)(1024*scale));
            canvas=new CampCanvas();
            Controls.Add(canvas);

            Chrome("最小化", new Rectangle(1335,4,55,42), delegate { WindowState=FormWindowState.Minimized; });
            Chrome("最大化或还原", new Rectangle(1403,4,55,42), ToggleMaximized);
            Chrome("关闭", new Rectangle(1470,4,60,42), Close);
            canvas.MouseDown+=delegate(object sender, MouseEventArgs args) {
                if (args.Button!=MouseButtons.Left) return;
                int x=args.X*1536/canvas.Width, y=args.Y*1024/canvas.Height;
                if(new Rectangle(254,513,449,66).Contains(x,y)) {port.Focus();return;}
                if(new Rectangle(834,513,613,66).Contains(x,y)) {ticket.Focus();return;}
                if(new Rectangle(819,877,244,42).Contains(x,y)) {address.Focus();return;}
                if(y>=50) return;
                if (args.Clicks==2) { ToggleMaximized(); return; }
                ReleaseCapture();
                SendMessage(Handle,0xA1,new IntPtr(2),null);
            };
            var menu=new ContextMenuStrip();
            var motion=menu.Items.Add("暂停背景动效");
            motion.Click+=delegate {
                canvas.Animated=!canvas.Animated;
                motion.Text=canvas.Animated ? "暂停背景动效" : "播放背景动效";
            };
            canvas.ContextMenuStrip=menu;
            FormClosed+=delegate {menu.Dispose();};

            Field(port,"游戏端口",null,new Rectangle(276,520,413,52),37);
            Field(ticket,"邀请码","粘贴邀请码",new Rectangle(857,520,577,52),34);
            SkinButton(host,"创建房间",new Rectangle(84,606,626,90),async delegate { await StartRoom(true); });
            SkinButton(join,"加入房间",new Rectangle(827,606,628,90),async delegate { await StartRoom(false); });
            BindEnter(port,host);
            BindEnter(ticket,join);

            Field(address,"游戏直连地址",null,new Rectangle(824,878,235,39),25);
            SkinButton(copyAddress,"复制地址",new Rectangle(1077,849,194,72),delegate {Copy(address.Text);});
            SkinButton(copyTicket,"复制邀请码",new Rectangle(1077,849,194,72),delegate {Copy(ticket.Text);});
            ((CampButton)copyTicket).DrawOwnText=true;
            copyTicket.Visible=false;
            SkinButton(leave,"退出房间",new Rectangle(1289,849,198,72),delegate { Stop(); });

            port.TabIndex=0; host.TabIndex=1;
            ticket.TabIndex=2; join.TabIndex=3;
            address.TabIndex=4; copyAddress.TabIndex=5;
            copyTicket.TabIndex=5; leave.TabIndex=6;
            Readout(status,new Rectangle(129,858,185,53),29,Color.FromArgb(177,219,95));
            Readout(rttValue,new Rectangle(357,878,118,39),31,White);
            Readout(p95Value,new Rectangle(510,878,119,39),31,White);
            Readout(samplesValue,new Rectangle(663,878,127,39),31,White);
            tooltip=new ToolTip { AutoPopDelay=10000 };
            FormClosed+=delegate {tooltip.Dispose();};
            Resize+=delegate { ResizeCamp(); };
            ResizeCamp();
            ResumeLayout(false);
        }

        void ToggleMaximized() {
            WindowState=WindowState==FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        void Chrome(string caption, Rectangle place, Action action) {
            var button=new CampButton {TabStop=false};
            SkinButton(button,caption,place,delegate { action(); });
        }

        void ResizeCamp() {
            if (WindowState==FormWindowState.Minimized || ClientSize.Width==0 || ClientSize.Height==0) return;
            float factor=Math.Min(ClientSize.Width/1536f,ClientSize.Height/1024f);
            int width=(int)Math.Round(1536*factor), height=(int)Math.Round(1024*factor);
            canvas.Bounds=new Rectangle((ClientSize.Width-width)/2,(ClientSize.Height-height)/2,width,height);
            foreach(var place in layout) place(factor);
            canvas.Invalidate();
        }

        void Place(Control child, Rectangle frame, float pixels=0, bool bold=false) {
            canvas.Controls.Add(child);
            layout.Add(delegate(float scale) {
                if (pixels>0 && Math.Abs(child.Font.Size-pixels*scale)>.05) {
                    var font=new Font("Microsoft YaHei UI",pixels*scale,bold ? FontStyle.Bold : FontStyle.Regular,GraphicsUnit.Pixel);
                    child.Font=font;
                }
                int width=(int)Math.Round(frame.Width*scale), height=(int)Math.Round(frame.Height*scale);
                int y=(int)Math.Round(frame.Y*scale);
                child.SetBounds((int)Math.Round(frame.X*scale),y,width,height);
            });
        }

        void Field(TextBox input,string name,string cue,Rectangle rect,float pixels) {
            input.BorderStyle=BorderStyle.None;
            input.BackColor=input.ReadOnly ? StatusColor : InputColor;
            input.ForeColor=White;
            input.AccessibleName=name;
            input.GotFocus+=delegate {canvas.Invalidate();};
            input.LostFocus+=delegate {canvas.Invalidate();};
            if (cue!=null) {
                input.HandleCreated+=delegate {SendMessage(input.Handle,0x1501,IntPtr.Zero,cue);};
            }
            canvas.Fields.Add(input);
            canvas.Controls.Add(input);
            layout.Add(delegate(float s) {
                if(Math.Abs(input.Font.Size-pixels*s)>.05)
                    input.Font=new Font("Microsoft YaHei UI",pixels*s,FontStyle.Regular,GraphicsUnit.Pixel);
                int y=(int)Math.Round(rect.Y*s)+(int)Math.Max(0,(rect.Height*s-input.PreferredHeight)/2);
                input.SetBounds((int)Math.Round(rect.X*s),y,(int)Math.Round(rect.Width*s),input.PreferredHeight);
            });
        }

        void Readout(Label label,Rectangle rect,float pixels,Color color) {
            label.ForeColor=color; label.BackColor=StatusColor; label.UseMnemonic=false;
            label.TextAlign=ContentAlignment.MiddleLeft;
            Place(label,rect,pixels,true);
        }

        void SkinButton(Button button,string name,Rectangle rect,EventHandler click) {
            var skin=(CampButton)button;
            skin.Text=name;
            skin.AccessibleName=name;
            skin.Art=canvas.Art;
            skin.Source=rect;
            skin.FlatStyle=FlatStyle.Flat;
            skin.Click+=click;
            Place(skin,rect,31,true);
        }

        static void BindEnter(TextBox input,Button button) {
            input.PreviewKeyDown+=delegate(object sender,PreviewKeyDownEventArgs e) {
                if(e.KeyData==Keys.Enter) e.IsInputKey=true;
            };
            input.KeyDown+=delegate(object sender,KeyEventArgs e) {
                if(e.KeyData!=Keys.Enter) return;
                e.SuppressKeyPress=true;
                button.PerformClick();
            };
        }

        void ShowConnection(string state, long? milliseconds=null, long? p95=null, string samples=null) {
            string caption=state;
            if(state.StartsWith("等待建房")) caption="等待联机";
            else if(state.StartsWith("本机存档可用") || state.StartsWith("房间已启动")) caption="房间已开启";
            else if(state.StartsWith("请输入") || state.Contains("邀请码格式")) caption="输入有误";
            else if(state.StartsWith("尚未收到")) caption="等待游戏响应";
            else if(state.StartsWith("正在")) caption="正在连接…";
            else if(state!="游戏可连接") caption="连接有问题";
            status.Text=caption;
            tooltip.SetToolTip(status,state);
            canvas.StatusIcon=caption=="游戏可连接" || caption=="房间已开启";
            canvas.Notice=caption=="输入有误" || caption=="连接有问题" || caption=="等待游戏响应" ? state : "";
            canvas.Invalidate();
            rttValue.Text=milliseconds.HasValue ? milliseconds+" ms" : "—";
            p95Value.Text=p95.HasValue ? p95+" ms" : "—";
            samplesValue.Text=samples ?? "—";
        }
    }

    sealed class CampButton : Button {
        internal Image Art;
        internal Rectangle Source;
        internal bool DrawOwnText;
        bool hovered;

        internal CampButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer,true);
        }

        protected override void OnPaint(PaintEventArgs e) {
            if(Art==null) return;
            e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(Art,ClientRectangle,Source,GraphicsUnit.Pixel);
            if(DrawOwnText) {
                using(var fill=new SolidBrush(Color.FromArgb(60,74,37)))
                    e.Graphics.FillRectangle(fill,Width*.07f,Height*.18f,Width*.86f,Height*.62f);
                TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Color.FromArgb(223,244,189),
                    TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            }
            if(!Enabled || hovered) {
                using(var tint=new SolidBrush(Enabled ? Color.FromArgb(32,Color.White) : Color.FromArgb(105,42,39,37)))
                    e.Graphics.FillRectangle(tint,Rectangle.Inflate(ClientRectangle,-2,-3));
            }
            if(Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-6,-5),Color.White,Color.Black);
        }
        protected override void OnMouseEnter(EventArgs e) {base.OnMouseEnter(e);hovered=true;Invalidate();}
        protected override void OnMouseLeave(EventArgs e) {base.OnMouseLeave(e);hovered=false;Invalidate();}
    }

    sealed class CampCanvas : Panel {
        readonly Timer frames=new Timer {Interval=50};
        readonly Stopwatch clock=Stopwatch.StartNew();
        internal readonly Bitmap Art;
        internal readonly List<TextBox> Fields=new List<TextBox>();
        internal bool StatusIcon;
        internal string Notice="";

        internal CampCanvas() {
            DoubleBuffered=true;
            using(var data=typeof(CampCanvas).Assembly.GetManifestResourceStream("MCLink.Camp"))
            using(var image=Image.FromStream(data))
                Art=new Bitmap(image);
            frames.Tick+=delegate {
                if(Visible && FindForm()!=null && FindForm().WindowState!=FormWindowState.Minimized)
                    Invalidate(new Rectangle(Width*633/1536,Height*195/1024,Width*81/1536+2,Height*102/1024+2));
            };
            frames.Start();
        }

        internal bool Animated {
            get {return frames.Enabled;}
            set {frames.Enabled=value; if(value) clock.Start(); else clock.Stop();}
        }

        protected override void OnPaintBackground(PaintEventArgs e) {
            if(Art==null || Width==0 || Height==0) return;
            e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(Art,ClientRectangle,new Rectangle(0,0,1536,1024),GraphicsUnit.Pixel);
            var state=e.Graphics.Save();
            e.Graphics.ScaleTransform(Width/1536f,Height/1024f);
            using(var input=new SolidBrush(Color.FromArgb(55,52,51))) {
                e.Graphics.FillRectangle(input,257,517,442,60);
                e.Graphics.FillRectangle(input,838,517,606,60);
            }
            using(var status=new SolidBrush(Color.FromArgb(43,40,39))) {
                e.Graphics.FillRectangle(status,126,859,190,53);
                e.Graphics.FillRectangle(status,352,878,124,40);
                e.Graphics.FillRectangle(status,506,878,127,40);
                e.Graphics.FillRectangle(status,658,878,131,40);
                e.Graphics.FillRectangle(status,821,878,242,40);
                if(!StatusIcon) {
                    e.Graphics.FillRectangle(status,66,863,49,48);
                    using(var wait=new SolidBrush(Color.FromArgb(196,159,97)))
                        e.Graphics.FillRectangle(wait,83,881,15,15);
                }
            }
            using(var focus=new Pen(Color.FromArgb(201,239,143),2)) {
                if(Fields.Count>0 && Fields[0].Focused) e.Graphics.DrawRectangle(focus,254,513,449,66);
                if(Fields.Count>1 && Fields[1].Focused) e.Graphics.DrawRectangle(focus,834,513,613,66);
                if(Fields.Count>2 && Fields[2].Focused) e.Graphics.DrawRectangle(focus,820,877,244,42);
            }
            for(int i=0;i<6;i++) {
                double phase=(clock.Elapsed.TotalSeconds*(.45+i*.03)+i*.19)%1;
                int x=672+(i-3)*5+(int)(Math.Sin(phase*5+i)*8);
                int y=268-(int)(phase*(57+i*4));
                using(var ember=new SolidBrush(Color.FromArgb((int)(226*(1-phase)),255,198-i*12,59)))
                    e.Graphics.FillRectangle(ember,x,y,i%2+3,i%2+3);
            }
            e.Graphics.Restore(state);
            if(Notice.Length>0) {
                float factor=Width/1536f;
                using(var font=new Font("Microsoft YaHei UI",20*factor,FontStyle.Regular,GraphicsUnit.Pixel))
                    TextRenderer.DrawText(e.Graphics,Notice,font,new Rectangle((int)(70*factor),(int)(924*factor),(int)(1390*factor),(int)(25*factor)),Color.FromArgb(244,203,137),TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
        }

        protected override void Dispose(bool disposing) {
            if(disposing) { frames.Dispose(); Art.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
