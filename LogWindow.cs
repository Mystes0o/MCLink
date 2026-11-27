using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MCLink {
    internal sealed class LogWindow : Form {
        readonly ComboBox level=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=104};
        readonly TextBox search=new TextBox {Width=185,AccessibleName="搜索日志"};
        readonly CheckBox pause=new CheckBox {Text="暂停刷新",AutoSize=true};
        readonly Label count=new Label {AutoSize=true,Padding=new Padding(0,5,0,0)};
        readonly RichTextBox output=new RichTextBox {Dock=DockStyle.Fill,ReadOnly=true,
            BackColor=SystemColors.Window,Font=new Font("Microsoft YaHei UI",10),AccessibleName="连接日志内容"};
        readonly Timer refresh=new Timer {Interval=750};
        long seen=-1;

        internal LogWindow() {
            Text="MCLink · 连接日志";
            ClientSize=new Size(850,470);
            MinimumSize=new Size(570,340);
            StartPosition=FormStartPosition.CenterParent;
            Font=new Font("Microsoft YaHei UI",9);
            level.AccessibleName="日志级别";
            level.Items.AddRange(new object[] {"全部级别","警告和错误","仅错误"});
            level.SelectedIndex=0;
            var tools=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(8)};
            var copy=new Button {Text="复制当前结果",AutoSize=true};
            var save=new Button {Text="导出全部",AutoSize=true};
            tools.Controls.AddRange(new Control[] {level,new Label {Text="搜索",AutoSize=true,
                Padding=new Padding(0,5,0,0)},search,pause,copy,save,count});
            Controls.Add(output);
            Controls.Add(tools);
            level.SelectedIndexChanged+=delegate { Reload(); };
            search.TextChanged+=delegate { Reload(); };
            pause.CheckedChanged+=delegate { if (!pause.Checked) UpdateLog(); };
            copy.Click+=delegate {
                if (output.TextLength==0) return;
                try { Clipboard.SetText(output.Text); }
                catch (System.Runtime.InteropServices.ExternalException ex) {
                    DiagnosticLog.Error("日志","复制失败",ex);
                    MessageBox.Show(this,"剪贴板暂时不可用。");
                }
            };
            save.Click+=delegate { Export(); };
            refresh.Tick+=delegate { if (!pause.Checked) UpdateLog(); };
            FormClosed+=delegate { refresh.Dispose(); };
            UpdateLog();
            refresh.Start();
        }

        void Reload() { seen=-1; UpdateLog(); }

        void UpdateLog() {
            int total;
            var text=DiagnosticLog.Read(level.SelectedIndex,search.Text.Trim(),ref seen,out total);
            if (text==null) return;
            count.Text=total+"/"+DiagnosticLog.Capacity+" 条";
            output.Text=text;
            output.Select(output.TextLength,0);
            output.ScrollToCaret();
        }

        void Export() {
            using (var dialog=new SaveFileDialog {Filter="文本日志 (*.txt)|*.txt",
                FileName="MCLink-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"}) {
                if (dialog.ShowDialog(this)!=DialogResult.OK) return;
                try {
                    long version=-1;
                    int total;
                    File.WriteAllText(dialog.FileName,DiagnosticLog.Read(0,"",ref version,out total));
                } catch (Exception ex) {
                    if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                    DiagnosticLog.Error("日志","导出失败",ex);
                    MessageBox.Show(this,"无法写入日志文件。"+ex.Message);
                }
            }
        }
    }

    partial class Window {
        LogWindow logWindow;
        void OpenLog() {
            if (logWindow==null || logWindow.IsDisposed) {
                logWindow=new LogWindow();
                logWindow.Show(this);
            } else {
                if (logWindow.WindowState==FormWindowState.Minimized) logWindow.WindowState=FormWindowState.Normal;
                logWindow.Activate();
            }
        }
    }
}
