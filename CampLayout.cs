using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MCLink {
    partial class Window {
        readonly Button copyTicket = new CampButton(), copyAddress = new CampButton();
        readonly Label rttValue = new Label(), p95Value = new Label(), samplesValue = new Label();
        readonly Label routeValue = new Label();
        readonly LinkLabel playerRouteLink = new LinkLabel();
        string[] playerRouteRows = new string[0];
        ListBox playerRouteList;
        readonly Label playerCount = new Label(), playerNames = new Label();
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

        [DllImport("user32.dll")]
        static extern uint GetDpiForSystem();

        [StructLayout(LayoutKind.Sequential)]
        struct DpiBounds {
            internal int Left, Top, Right, Bottom;
        }

        static float InitialDpiScale() {
            try { return Math.Max(96,GetDpiForSystem())/96f; }
            catch (EntryPointNotFoundException) {
                using(var desktop=Graphics.FromHwnd(IntPtr.Zero)) return desktop.DpiX/96f;
            }
        }

        protected override void WndProc(ref Message message) {
            const int dpiChanged=0x02E0;
            if (message.Msg==dpiChanged && WindowState==FormWindowState.Normal) {
                var bounds=(DpiBounds)Marshal.PtrToStructure(message.LParam,typeof(DpiBounds));
                Bounds=Rectangle.FromLTRB(bounds.Left,bounds.Top,bounds.Right,bounds.Bottom);
                message.Result=IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
        }

        void BuildLayout() {
            SuspendLayout();
            Text="MCLink · 好友联机";
            AutoScaleMode=AutoScaleMode.None;
            FormBorderStyle=FormBorderStyle.None;
            StartPosition=FormStartPosition.CenterScreen;
            BackColor=Color.FromArgb(36,33,31);
            var area=Screen.PrimaryScreen.WorkingArea;
            float dpiScale=InitialDpiScale();
            float margin=30*dpiScale;
            float scale=Math.Min(.75f*dpiScale,Math.Min((area.Width-margin)/1536f,(area.Height-margin)/1024f));
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
            SkinButton(host,"创建房间",new Rectangle(84,606,626,90),async delegate { await StartRoom(true); },
                new CampCaption(264,624,262,56,285,34,Color.FromArgb(19,31,11),true));
            SkinButton(join,"加入房间",new Rectangle(827,606,628,90),async delegate { await StartRoom(false); },
                new CampCaption(1000,624,282,56,1025,34,Color.FromArgb(216,245,189),true));
            PlaceAndroidLink();
            BindEnter(port,host);
            BindEnter(ticket,join);

            Field(address,"游戏直连地址",null,new Rectangle(824,878,235,39),25);
            var copyCaption=new CampCaption(1143,860,114,48,1251,22,Color.FromArgb(223,244,189),true);
            SkinButton(copyAddress,"复制地址",new Rectangle(1077,849,194,72),delegate {Copy(address.Text);},copyCaption);
            SkinButton(copyTicket,"复制邀请码",new Rectangle(1077,849,194,72),delegate {Copy(ticket.Text);},copyCaption);
            copyTicket.Visible=false;
            SkinButton(leave,"退出房间",new Rectangle(1289,849,198,72),delegate { Stop(); },
                new CampCaption(1351,860,125,48,1466,23,Color.FromArgb(255,241,221),true));

            port.TabIndex=0; host.TabIndex=1;
            ticket.TabIndex=2; join.TabIndex=3;
            address.TabIndex=4; copyAddress.TabIndex=5;
            copyTicket.TabIndex=5; leave.TabIndex=6;
            Readout(status,new Rectangle(129,858,185,53),29,Color.FromArgb(177,219,95));
            Readout(routeValue,new Rectangle(805,773,674,43),24,White);
            routeValue.BackColor=Color.Transparent;
            routeValue.TextAlign=ContentAlignment.MiddleRight;
            routeValue.AutoEllipsis=true;
            routeValue.AccessibleName="当前线路";
            playerRouteLink.Text=playerRouteLink.AccessibleName="查看玩家线路";
            playerRouteLink.LinkColor=playerRouteLink.ActiveLinkColor=Color.FromArgb(177,219,95);
            playerRouteLink.BackColor=Color.Transparent;
            playerRouteLink.TextAlign=ContentAlignment.MiddleRight;
            playerRouteLink.LinkClicked+=delegate { ShowPlayerRoutes(); };
            Place(playerRouteLink,new Rectangle(1199,773,280,43),24,true);
            Readout(rttValue,new Rectangle(357,878,118,39),31,White);
            Readout(p95Value,new Rectangle(510,878,119,39),31,White);
            Readout(samplesValue,new Rectangle(663,878,127,39),31,White);
            Readout(playerCount,new Rectangle(338,842,731,37),24,Color.FromArgb(177,219,95));
            Readout(playerNames,new Rectangle(338,879,731,42),27,White);
            playerCount.Padding=playerNames.Padding=new Padding(9,0,0,0);
            playerCount.AccessibleName="在线玩家人数";
            playerNames.AccessibleName="在线玩家昵称";
            playerNames.AutoEllipsis=true;
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
            canvas.Fields.Add(input);
            canvas.Controls.Add(input);
            if (cue!=null) ((CampTextBox)input).Placeholder=cue;
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

        void SkinButton(Button button,string name,Rectangle rect,EventHandler click,CampCaption caption=null) {
            var skin=(CampButton)button;
            skin.Text=name;
            skin.AccessibleName=name;
            skin.Art=canvas.Art;
            skin.Source=rect;
            skin.Caption=caption;
            skin.FlatStyle=FlatStyle.Flat;
            skin.Click+=click;
            Place(skin,rect);
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

        void ShowPlayers(PlayerStatus players) {
            playerCount.Visible=playerNames.Visible=playerRouteLink.Visible=hosting;
            rttValue.Visible=p95Value.Visible=samplesValue.Visible=address.Visible=routeValue.Visible=!hosting;
            if (!hosting) {
                playerRouteRows=new string[0];
                RefreshPlayerRouteList();
                return;
            }

            playerCount.Text="在线玩家（含房主）";
            if (players!=null && players.Online.HasValue) playerCount.Text+="  "+players.Online.Value+" 人";
            var routes=routeBridge.Routes();
            var names=players==null ? new string[0] : players.Names;
            var rows=new List<string>();
            foreach (var name in names) {
                string route;
                bool reported=routes.TryGetValue(name,out route);
                rows.Add(name+"  ·  "+(reported ? route : "本机或线路未知"));
                routes.Remove(name);
            }
            foreach (var pair in routes.OrderBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase))
                rows.Add(pair.Key+"  ·  "+pair.Value+"（游戏未确认在线）");
            if (rows.Count==0) {
                rows.Add(players==null ? "正在等待游戏状态…" :
                    players.Online==0 ? "暂无玩家" : "暂未获取玩家昵称");
            }
            playerNames.Text=String.Join("　",rows);
            tooltip.SetToolTip(playerNames,String.Join(Environment.NewLine,rows));
            if (!playerRouteRows.SequenceEqual(rows)) {
                playerRouteRows=rows.ToArray();
                RefreshPlayerRouteList();
            }
        }

        void ShowPlayerRoutes() {
            float scale=canvas.Width/1152f;
            using (var detailFont=new Font("Microsoft YaHei UI",Math.Max(14,17*scale),FontStyle.Regular,GraphicsUnit.Pixel))
            using (var dialog=new Form {
                Text="玩家线路", StartPosition=FormStartPosition.CenterParent,
                ClientSize=new Size((int)(500*scale),(int)(280*scale)), MinimizeBox=false, MaximizeBox=false,
                AutoScaleMode=AutoScaleMode.None, Font=detailFont, ShowInTaskbar=false
            })
            using (var list=new ListBox {
                Dock=DockStyle.Fill, IntegralHeight=false, HorizontalScrollbar=true,
                AccessibleName="玩家及当前线路"
            }) {
                dialog.Controls.Add(list);
                playerRouteList=list;
                try {
                    RefreshPlayerRouteList();
                    dialog.ShowDialog(this);
                } finally { playerRouteList=null; }
            }
        }

        void RefreshPlayerRouteList() {
            if (playerRouteList==null) return;
            playerRouteList.BeginUpdate();
            try {
                playerRouteList.Items.Clear();
                playerRouteList.Items.AddRange(playerRouteRows);
            } finally { playerRouteList.EndUpdate(); }
        }

        void ShowRoute(string description) {
            routeValue.Text="当前线路："+description;
            tooltip.SetToolTip(routeValue,routeValue.Text);
        }

        void ShowConnection(string state, long? milliseconds=null, long? p95=null, string samples=null) {
            string caption=state;
            bool crossplayReady=state.StartsWith("跨端房间已启动") || state.StartsWith("房主游戏地址");
            if(state.StartsWith("等待建房")) caption="等待联机";
            else if(crossplayReady || state.StartsWith("本机存档可用") || state.StartsWith("房间已启动")) caption="房间已开启";
            else if(state.StartsWith("请输入") || state.Contains("邀请码格式")) caption="输入有误";
            else if(state.StartsWith("尚未收到")) caption="等待游戏响应";
            else if(state.StartsWith("正在保存")) caption="正在保存…";
            else if(state.StartsWith("正在")) caption="正在连接…";
            else if(state.StartsWith("游戏可连接")) caption="游戏可连接";
            else if(state!="游戏可连接") caption="连接有问题";
            status.Text=caption;
            tooltip.SetToolTip(status,state);
            canvas.StatusIcon=caption=="游戏可连接" || caption=="房间已开启";
            canvas.Notice=caption=="等待联机" || (caption=="房间已开启" && !crossplayReady) ? "" : state;
            canvas.Invalidate();
            rttValue.Text=milliseconds.HasValue ? milliseconds+" ms" : "—";
            p95Value.Text=p95.HasValue ? p95+" ms" : "—";
            samplesValue.Text=samples ?? "—";
        }
    }

    sealed class CampTextBox : TextBox {
        internal string Placeholder;
        internal bool ShowPlaceholder { get { return !Focused && TextLength==0 && !String.IsNullOrEmpty(Placeholder); } }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void WndProc(ref Message message) {
            const int paint=0x000F, print=0x0317, printClient=0x0318;
            base.WndProc(ref message);
            if (message.Msg==paint && ShowPlaceholder) {
                using (var graphics=Graphics.FromHwnd(Handle)) PaintPlaceholder(graphics);
            } else if ((message.Msg==print || message.Msg==printClient) && ShowPlaceholder) {
                using (var graphics=Graphics.FromHdc(message.WParam)) PaintPlaceholder(graphics);
            }
        }

        void PaintPlaceholder(Graphics graphics) {
            TextRenderer.DrawText(graphics,Placeholder,Font,ClientRectangle,Color.FromArgb(187,180,174),
                TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        }
    }

    sealed class CampCaption {
        readonly Rectangle area;
        readonly int sampleX;
        readonly float size;
        readonly Color color;
        readonly bool centered, bold;

        internal CampCaption(int x,int y,int width,int height,int sampleX,float size,Color color,
                             bool centered=false,bool bold=true) {
            area=new Rectangle(x,y,width,height);
            this.sampleX=sampleX;
            this.size=size;
            this.color=color;
            this.centered=centered;
            this.bold=bold;
        }

        internal void Paint(PaintEventArgs paint,Image art,string text,float scale,Point origin) {
            var rect=Rectangle.FromLTRB(
                (int)Math.Round((area.Left-origin.X)*scale),(int)Math.Round((area.Top-origin.Y)*scale),
                (int)Math.Round((area.Right-origin.X)*scale),(int)Math.Round((area.Bottom-origin.Y)*scale));
            if (!rect.IntersectsWith(paint.ClipRectangle)) return;

            // Sample the nearby unlettered background instead of rescaling the original glyphs.
            paint.Graphics.DrawImage(art,rect,new Rectangle(sampleX,area.Y,6,area.Height),GraphicsUnit.Pixel);
            var flags=TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|
                TextFormatFlags.NoPadding|TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.EndEllipsis;
            if (centered) flags|=TextFormatFlags.HorizontalCenter;
            using(var font=new Font("Microsoft YaHei UI",size*scale,bold ? FontStyle.Bold : FontStyle.Regular,GraphicsUnit.Pixel))
                TextRenderer.DrawText(paint.Graphics,text,font,rect,color,flags);
        }
    }

    sealed class CampButton : Button {
        internal static readonly Color AccentColor=Color.FromArgb(177,219,95);
        internal static readonly Color WarmWhite=Color.FromArgb(251,249,243);
        internal Image Art;
        internal Rectangle Source;
        internal CampCaption Caption;
        internal bool Accent;
        bool hovered;

        internal CampButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer,true);
        }

        protected override void OnPaint(PaintEventArgs e) {
            if(Art==null) {
                PaintSolid(e.Graphics);
                return;
            }
            e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(Art,ClientRectangle,Source,GraphicsUnit.Pixel);
            if (Caption!=null) Caption.Paint(e,Art,Text,Width/(float)Source.Width,Source.Location);
            if(!Enabled || hovered) {
                using(var tint=new SolidBrush(Enabled ? Color.FromArgb(32,Color.White) : Color.FromArgb(105,42,39,37)))
                    e.Graphics.FillRectangle(tint,Rectangle.Inflate(ClientRectangle,-2,-3));
            }
            if(Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-6,-5),Color.White,Color.Black);
        }
        void PaintSolid(Graphics graphics) {
            Color fill=!Enabled ? Color.FromArgb(55,52,49) :
                Accent ? (hovered ? Color.FromArgb(200,234,131) : AccentColor) :
                hovered ? Color.FromArgb(79,72,61) : Color.FromArgb(62,56,49);
            Color ink=!Enabled ? Color.FromArgb(163,156,150) :
                Accent ? Color.FromArgb(24,36,15) : WarmWhite;
            using (var brush=new SolidBrush(fill)) graphics.FillRectangle(brush,ClientRectangle);
            using (var border=new Pen(Focused ? AccentColor : Color.FromArgb(115,105,82)))
                graphics.DrawRectangle(border,0,0,Width-1,Height-1);
            var content=Rectangle.Inflate(ClientRectangle,-4,-2);
            TextRenderer.DrawText(graphics,Text,Font,content,ink,
                TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.HorizontalCenter|
                TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(graphics,Rectangle.Inflate(ClientRectangle,-3,-3),ink,fill);
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
        static readonly Color CaptionWhite=Color.FromArgb(250,247,241);
        static readonly Color CaptionMuted=Color.FromArgb(226,221,216);
        static readonly KeyValuePair<string,CampCaption>[] Captions = {
            new KeyValuePair<string,CampCaption>("MCLink",new CampCaption(80,8,126,32,209,25,CaptionWhite)),
            new KeyValuePair<string,CampCaption>("房主",new CampCaption(166,392,144,56,320,39,CaptionWhite)),
            new KeyValuePair<string,CampCaption>("好友",new CampCaption(917,392,144,56,1070,39,CaptionWhite)),
            new KeyValuePair<string,CampCaption>("创建房间，让好友连接到你的世界。",new CampCaption(90,450,550,41,644,26,CaptionMuted,false,false)),
            new KeyValuePair<string,CampCaption>("输入邀请码，加入好友的世界。",new CampCaption(834,450,556,41,1396,26,CaptionMuted,false,false)),
            new KeyValuePair<string,CampCaption>("游戏端口",new CampCaption(91,519,151,50,242,32,CaptionWhite)),
            new KeyValuePair<string,CampCaption>("连接状态",new CampCaption(126,769,178,49,309,34,CaptionWhite)),
            new KeyValuePair<string,CampCaption>("往返延迟",new CampCaption(358,845,114,32,471,23,CaptionWhite,false,false)),
            new KeyValuePair<string,CampCaption>("P95",new CampCaption(510,845,114,32,610,23,CaptionWhite,false,false)),
            new KeyValuePair<string,CampCaption>("探测成功",new CampCaption(663,845,120,32,779,23,CaptionWhite,false,false)),
            new KeyValuePair<string,CampCaption>("地址",new CampCaption(825,845,124,32,951,23,CaptionWhite,false,false))
        };

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
            float factor=Width/1536f;
            foreach (var caption in Captions) caption.Value.Paint(e,Art,caption.Key,factor,Point.Empty);
            if(Notice.Length>0) {
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
