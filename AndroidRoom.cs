using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MCLink {
    partial class Window {
        readonly CampButton androidLink=new CampButton();
        readonly HashSet<Task> crossplayWork=new HashSet<Task>();
        CancellationTokenSource crossplayCancellation;
        CrossplayServer crossplay;

        void PlaceAndroidLink() {
            androidLink.FlatStyle=FlatStyle.Flat;
            androidLink.TabIndex=7;
            androidLink.Click+=async delegate {
                if (crossplay!=null && hosting && session!=null) Copy(address.Text);
                else await ShowCrossplaySetup();
            };
            Place(androidLink,new Rectangle(501,398,202,44),20,true);
        }

        void UpdateAndroidLink() {
            bool ready=crossplay!=null && hosting && session!=null;
            androidLink.Enabled=ready || (host.Enabled && crossplayWork.Count==0 && !closing);
            androidLink.Text=androidLink.AccessibleName=ready ? "复制房主游戏地址" : "跨端建房";
        }

        async Task ShowCrossplaySetup() {
            if (!host.Enabled || crossplayWork.Count!=0 || closing) return;
            var roots=CrossplayServer.DataRoots();
            var savedPaths=new List<string>();
            string existing=null, source=null;
            var workingArea=Screen.FromControl(this).WorkingArea;
            float scale=Math.Min(Math.Max(.85f,canvas.Width/1152f),
                Math.Min((workingArea.Width-24)/540f,(workingArea.Height-24)/386f));
            Func<int,int> px=value=>(int)Math.Round(value*scale);
            var panelColor=Color.FromArgb(37,33,30);
            var muted=Color.FromArgb(214,205,195);
            using(var body=new Font("Microsoft YaHei UI",16*scale,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var strong=new Font("Microsoft YaHei UI",16*scale,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var heading=new Font("Microsoft YaHei UI",27*scale,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var dialog=new Form {
                Text="跨端建房", StartPosition=FormStartPosition.CenterParent,
                FormBorderStyle=FormBorderStyle.None, AutoScaleMode=AutoScaleMode.None,
                ClientSize=new Size(px(540),px(386)), ShowInTaskbar=false,
                Font=body, BackColor=panelColor, ForeColor=White
            }) {
                var controls=new Panel { Dock=DockStyle.Fill, BackColor=panelColor };
                dialog.Controls.Add(controls);
                Action<Control,int,int,int,int> put=(control,x,y,w,h)=>{
                    control.SetBounds(px(x),px(y),px(w),px(h));
                    controls.Controls.Add(control);
                };
                controls.Paint+=delegate(object sender,PaintEventArgs e) {
                    using(var border=new Pen(Color.FromArgb(115,105,82)))
                        e.Graphics.DrawRectangle(border,0,0,controls.Width-1,controls.Height-1);
                    using(var line=new SolidBrush(CampButton.AccentColor))
                        e.Graphics.FillRectangle(line,1,1,controls.Width-2,px(3));
                };

                var title=new Label { Text="跨端建房", Font=heading, ForeColor=White, UseMnemonic=false };
                title.MouseDown+=delegate(object sender,MouseEventArgs e) {
                    if (e.Button!=MouseButtons.Left) return;
                    ReleaseCapture();
                    SendMessage(dialog.Handle,0xA1,new IntPtr(2),null);
                };
                put(title,26,23,423,40);
                put(new Label {
                    Text="电脑 Java "+CrossplayServer.GameVersion+" 开房 · Android 基岩版加入",
                    ForeColor=muted, UseMnemonic=false
                },27,66,486,28);
                var close=new CampButton {
                    Text="×", AccessibleName="关闭", Font=strong, TabStop=false,
                    FlatStyle=FlatStyle.Flat, DialogResult=DialogResult.Cancel
                };
                put(close,474,23,40,34);
                put(new Label { Text="Java 世界", Font=strong, ForeColor=CampButton.AccentColor },27,111,200,26);
                var mode=CrossplayChoice();
                mode.AccessibleName="世界来源";
                mode.Items.AddRange(new object[] {"新建 Java 世界","导入原版 Java 世界","继续联机存档"});
                var saved=CrossplayChoice();
                saved.AccessibleName="联机存档";
                foreach(var root in roots)
                    foreach(string entry in CrossplayServer.ExistingWorlds(root)) {
                        savedPaths.Add(entry);
                        saved.Items.Add(Path.GetFileName(entry));
                    }
                if (saved.Items.Count>0) saved.SelectedIndex=0;
                var pick=new CampButton { Text="选择存档…", Font=strong, FlatStyle=FlatStyle.Flat };
                var picked=new Label {
                    Text="请选择已关闭的原版 Java 存档", AutoEllipsis=true, ForeColor=muted,
                    TextAlign=ContentAlignment.MiddleLeft, UseMnemonic=false
                };
                var help=new Label {
                    Text="首次开房会自动下载所需组件。", ForeColor=muted, TextAlign=ContentAlignment.MiddleLeft
                };
                pick.Click+=delegate {
                    using(var browser=new FolderBrowserDialog {Description="选择含有 level.dat 的已关闭 Java 存档",ShowNewFolderButton=false}) {
                        if (browser.ShowDialog(dialog)!=DialogResult.OK) return;
                        source=browser.SelectedPath;
                        picked.Text=Path.GetFileName(source);
                    }
                };
                var eula=new CheckBox {
                    Text="我同意 Minecraft 最终用户许可协议", ForeColor=White,
                    FlatStyle=FlatStyle.Flat, UseVisualStyleBackColor=false
                };
                eula.FlatAppearance.BorderColor=CampButton.AccentColor;
                var terms=new LinkLabel {
                    Text="查看许可协议", LinkColor=CampButton.AccentColor,
                    ActiveLinkColor=White, VisitedLinkColor=CampButton.AccentColor,
                    TextAlign=ContentAlignment.MiddleRight
                };
                terms.LinkClicked+=delegate {
                    try { Process.Start(CrossplayServer.EulaUrl); }
                    catch (System.ComponentModel.Win32Exception) { MessageBox.Show(dialog,CrossplayServer.EulaUrl); }
                };
                var start=new CampButton {
                    Text="启动 Geyser 并建房", Font=strong, FlatStyle=FlatStyle.Flat, Accent=true
                };
                mode.SelectedIndexChanged+=delegate {
                    pick.Visible=picked.Visible=mode.SelectedIndex==1;
                    saved.Visible=mode.SelectedIndex==2 && saved.Items.Count>0;
                    help.Visible=mode.SelectedIndex==0 || (mode.SelectedIndex==2 && saved.Items.Count==0);
                    help.Text=mode.SelectedIndex==0 ? "首次开房会自动下载所需组件。" : "暂无联机存档，请先新建或导入。";
                };
                start.Click+=delegate {
                    if (!eula.Checked) { MessageBox.Show(dialog,"请先阅读并确认 Minecraft 最终用户许可协议。"); return; }
                    if (mode.SelectedIndex==1 && source==null) { MessageBox.Show(dialog,"请选择一个 Java 存档。"); return; }
                    if (mode.SelectedIndex==2) {
                        if (saved.SelectedItem==null) { MessageBox.Show(dialog,"请先新建或导入联机存档。"); return; }
                        existing=savedPaths[saved.SelectedIndex];
                    }
                    if (mode.SelectedIndex!=1) source=null;
                    dialog.DialogResult=DialogResult.OK;
                };
                put(mode,27,143,486,35);
                put(pick,27,188,144,35);
                put(picked,184,188,329,35);
                put(saved,27,189,486,35);
                put(help,27,188,486,35);
                put(eula,27,250,345,32);
                put(terms,373,250,140,32);
                put(start,27,309,486,48);
                mode.SelectedIndex=saved.Items.Count>0 ? 2 : 0;
                dialog.AcceptButton=start;
                dialog.CancelButton=close;
                if (dialog.ShowDialog(this)!=DialogResult.OK) return;
            }

            int current=++generation;
            var cancellation=new CancellationTokenSource();
            crossplayCancellation=cancellation;
            IProgress<string> progress=new Progress<string>(message => {if (current==generation) ShowConnection(message);});
            host.Enabled=join.Enabled=false;
            port.ReadOnly=ticket.ReadOnly=true;
            leave.Enabled=true;
            UpdateAndroidLink();
            var work=Task.Run(()=> {
                var root=CrossplayServer.WritableRoot(roots);
                var name=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
                var directory=existing ?? Path.Combine(root,"worlds",name);
                var prepared=CrossplayServer.Start(directory,source,progress.Report,cancellation.Token,Path.Combine(root,"components"));
                if (!cancellation.IsCancellationRequested) return prepared;
                prepared.Dispose();
                throw new OperationCanceledException(cancellation.Token);
            });
            var completion=new TaskCompletionSource<bool>();
            crossplayWork.Add(completion.Task);
            try {
                var prepared=await work;
                if (current!=generation) { await Task.Run(()=>prepared.Dispose()); return; }
                crossplay=prepared;
                await StartRoom(true,prepared);
            } catch(Exception ex) {
                if (current==generation) { Stop(); ShowConnection("跨端开房失败。"+ex.Message); }
            } finally {
                crossplayWork.Remove(completion.Task);
                completion.SetResult(true);
                if (crossplayCancellation==cancellation) crossplayCancellation=null;
                cancellation.Dispose();
                if (!IsDisposed) UpdateAndroidLink();
            }
        }

        static ComboBox CrossplayChoice() {
            var choice=new ComboBox {
                BackColor=InputColor, ForeColor=White, FlatStyle=FlatStyle.Flat,
                DropDownStyle=ComboBoxStyle.DropDownList, DrawMode=DrawMode.OwnerDrawFixed
            };
            choice.FontChanged+=delegate { choice.ItemHeight=choice.Font.Height+8; };
            choice.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
                if (e.Index<0) return;
                bool selected=(e.State&DrawItemState.Selected)!=0 && (e.State&DrawItemState.ComboBoxEdit)==0;
                var fill=selected ? CampButton.AccentColor : InputColor;
                using(var brush=new SolidBrush(fill)) e.Graphics.FillRectangle(brush,e.Bounds);
                var text=Rectangle.Inflate(e.Bounds,-6,0);
                TextRenderer.DrawText(e.Graphics,choice.GetItemText(choice.Items[e.Index]),e.Font,text,
                    selected ? StatusColor : White,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|
                    TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            return choice;
        }
    }
}
