// ============================================================================
// DSH 客户端 - WebView2 桌面壳
// ----------------------------------------------------------------------------
// 把 DSH 网页端装进一个独立桌面窗口：
//   - 启动时若 3080 端口没有服务，自动拉起 start-server.bat（隐藏运行）
//   - WebView2 加载 http://127.0.0.1:3080，插件（皮肤/贴纸等）100% 兼容
//   - 关闭窗口时可选择：最小化到托盘 / 退出并停止服务 / 退出但保持服务
//   - 托盘右键菜单：显示窗口 / 浏览器打开 / 查看日志 / 退出
//
// 编译方式见 build-client.ps1（.NET Framework 4.8 自带 csc，无需安装 SDK）
// ============================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DSHClient
{
    internal static class Program
    {
        public const int Port = 3080;
        public const string BaseUrl = "http://127.0.0.1:3080";
        private const string MutexName = "DSH_WebView2_Client_SingleInstance";

        [STAThread]
        private static int Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已有实例在运行：把它带到前台，然后退出本次启动
                    ActivateExistingWindow();
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            return 0;
        }

        private static void ActivateExistingWindow()
        {
            IntPtr h = FindWindow(null, "DeepSeek Harness");
            if (h != IntPtr.Zero)
            {
                ShowWindow(h, 9); // SW_RESTORE
                SetForegroundWindow(h);
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }

    internal enum CloseChoice
    {
        MinimizeToTray,
        ExitStopServer,
        ExitKeepServer,
        Cancel
    }

    internal sealed class MainForm : Form
    {
        private readonly WebView2 _web;
        private readonly NotifyIcon _tray;
        private readonly string _rootDir;
        private bool _exitConfirmed;

        public MainForm()
        {
            _rootDir = AppDomain.CurrentDomain.BaseDirectory;

            Text = "DeepSeek Harness";
            ClientSize = new Size(1280, 820);
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = LoadIcon();
            BackColor = Color.FromArgb(16, 18, 24);

            _web = new WebView2();
            _web.Dock = DockStyle.Fill;
            _web.DefaultBackgroundColor = Color.FromArgb(16, 18, 24);
            _web.CoreWebView2InitializationCompleted += OnWebInitCompleted;
            Controls.Add(_web);

            _tray = new NotifyIcon();
            _tray.Icon = LoadIcon();
            _tray.Text = "DSH 客户端";
            _tray.Visible = true;
            _tray.ContextMenuStrip = BuildTrayMenu();
            _tray.DoubleClick += delegate { ShowMainWindow(); };
            _tray.BalloonTipTitle = "DSH 客户端";
            _tray.BalloonTipText = "DSH 仍在后台运行，双击托盘图标可恢复窗口。";

            FormClosing += OnFormClosing;
            Shown += OnShown;
        }

        // ------------------------------------------------------------------
        // 启动流程
        // ------------------------------------------------------------------
        private async void OnShown(object sender, EventArgs e)
        {
            bool serverOk = await EnsureServerAsync();
            if (serverOk)
            {
                Text = "DeepSeek Harness — 正在启动界面…";
                await InitWebViewAsync();
                Text = "DeepSeek Harness";
            }
            else
            {
                MessageBox.Show(this,
                    "DSH 服务器启动失败（60 秒内端口 " + Program.Port + " 未就绪）。\n" +
                    "请检查 " + Path.Combine(_rootDir, "dsh-server.log") + " 或手动运行 start-server.bat。",
                    "DSH 客户端", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task<bool> EnsureServerAsync()
        {
            if (PortAlive(Program.Port)) return true;

            string bat = Path.Combine(_rootDir, "start-server.bat");
            if (!File.Exists(bat))
            {
                MessageBox.Show(this, "未找到服务器启动脚本：" + bat, "DSH 客户端",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c \"" + bat + "\"";
                psi.WorkingDirectory = _rootDir;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动服务器失败：" + ex.Message, "DSH 客户端",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            Text = "DeepSeek Harness — 正在启动服务器…";
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(1000);
                if (PortAlive(Program.Port)) return true;
            }
            return false;
        }

        private async Task InitWebViewAsync()
        {
            try
            {
                string userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DSHClient", "WebView2");
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userData);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                _web.Source = new Uri(Program.BaseUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "WebView2 初始化失败：" + ex.Message + "\n\n" +
                    "请确认已安装 Edge WebView2 运行时（Windows 11 自带）。",
                    "DSH 客户端", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------------
        // WebView2 事件
        // ------------------------------------------------------------------
        private void OnWebInitCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess) return;
            MessageBox.Show(this, "WebView2 初始化失败：" + e.InitializationException.Message,
                "DSH 客户端", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // 网页里打开的链接：本地地址在当前窗口打开，外部地址交给系统浏览器
        private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            string uri = e.Uri;
            Uri parsed;
            if (Uri.TryCreate(uri, UriKind.Absolute, out parsed) && IsExternal(parsed))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(uri);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch
                {
                }
            }
            else
            {
                try
                {
                    _web.CoreWebView2.Navigate(uri);
                }
                catch
                {
                }
            }
        }

        private static bool IsExternal(Uri uri)
        {
            if (uri.Scheme != "http" && uri.Scheme != "https") return false;
            string host = uri.Host.ToLowerInvariant();
            return host != "127.0.0.1" && host != "localhost";
        }

        // ------------------------------------------------------------------
        // 托盘
        // ------------------------------------------------------------------
        private ContextMenuStrip BuildTrayMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, delegate { ShowMainWindow(); });
            menu.Items.Add("在浏览器中打开", null, delegate { OpenInBrowser(); });
            menu.Items.Add("打开服务器日志", null, delegate { OpenLog(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出并停止服务", null, delegate { ExitApp(true); });
            menu.Items.Add("退出（保持服务运行）", null, delegate { ExitApp(false); });
            return menu;
        }

        private void ShowMainWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void OpenInBrowser()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Program.BaseUrl);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch
            {
            }
        }

        private void OpenLog()
        {
            string log = Path.Combine(_rootDir, "dsh-server.log");
            if (File.Exists(log))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(log);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch
                {
                }
            }
            else
            {
                MessageBox.Show(this, "日志文件尚不存在（服务器还没写过日志）：" + log,
                    "DSH 客户端", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ExitApp(bool stopServer)
        {
            _exitConfirmed = true;
            if (stopServer) StopServer();
            _tray.Visible = false;
            Close();
        }

        // ------------------------------------------------------------------
        // 关闭行为
        // ------------------------------------------------------------------
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_exitConfirmed)
            {
                _tray.Visible = false;
                return;
            }
            e.Cancel = true; // 先弹选择框，不直接关闭

            using (CloseChoiceDialog dlg = new CloseChoiceDialog())
            {
                CloseChoice choice = dlg.ShowChoice(this);
                switch (choice)
                {
                    case CloseChoice.MinimizeToTray:
                        Hide();
                        try { _tray.ShowBalloonTip(3000); }
                        catch { }
                        break;
                    case CloseChoice.ExitStopServer:
                        _exitConfirmed = true;
                        StopServer();
                        _tray.Visible = false;
                        Close();
                        break;
                    case CloseChoice.ExitKeepServer:
                        _exitConfirmed = true;
                        _tray.Visible = false;
                        Close();
                        break;
                    case CloseChoice.Cancel:
                        break;
                }
            }
        }

        // ------------------------------------------------------------------
        // 服务器进程管理（与 stop-dsh.bat 相同的逻辑）
        // ------------------------------------------------------------------
        private static void StopServer()
        {
            try
            {
                using (System.Management.ManagementObjectSearcher searcher =
                    new System.Management.ManagementObjectSearcher(
                        "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'node.exe'"))
                {
                    foreach (System.Management.ManagementObject mo in searcher.Get())
                    {
                        object cl = mo["CommandLine"];
                        if (cl != null && cl.ToString().IndexOf("dsh", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            try
                            {
                                int pid = Convert.ToInt32(mo["ProcessId"]);
                                using (Process p = Process.GetProcessById(pid))
                                {
                                    p.Kill();
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        // ------------------------------------------------------------------
        // 工具
        // ------------------------------------------------------------------
        private static bool PortAlive(int port)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    c.Connect("127.0.0.1", port);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private Icon LoadIcon()
        {
            try
            {
                string p = Path.Combine(_rootDir, "dsh.ico");
                if (File.Exists(p)) return new Icon(p);
            }
            catch
            {
            }
            return SystemIcons.Application;
        }
    }

    // ------------------------------------------------------------------------
    // 关闭选择对话框
    // ------------------------------------------------------------------------
    internal sealed class CloseChoiceDialog : Form
    {
        private CloseChoice _result = CloseChoice.Cancel;

        public CloseChoiceDialog()
        {
            Text = "DSH 客户端";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(400, 236);

            Label label = new Label();
            label.Text = "关闭窗口后 DSH 服务器仍在后台运行，请选择：";
            label.AutoSize = false;
            label.Size = new Size(372, 30);
            label.Location = new Point(14, 12);
            label.Font = new Font("Microsoft YaHei UI", 9F);
            Controls.Add(label);

            Button btnTray = AddButton("最小化到托盘（服务继续运行）", 14, 48, CloseChoice.MinimizeToTray);
            Button btnStop = AddButton("退出并停止服务", 14, 88, CloseChoice.ExitStopServer);
            Button btnKeep = AddButton("退出（保持服务在后台运行）", 14, 128, CloseChoice.ExitKeepServer);
            Button btnCancel = AddButton("取消", 296, 196, CloseChoice.Cancel);
            btnCancel.Size = new Size(90, 28);

            AcceptButton = btnCancel;
        }

        private Button AddButton(string text, int x, int y, CloseChoice choice)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(372, 32);
            b.Location = new Point(x, y);
            b.Font = new Font("Microsoft YaHei UI", 9F);
            b.UseVisualStyleBackColor = true;
            b.Click += delegate { _result = choice; DialogResult = DialogResult.OK; };
            Controls.Add(b);
            return b;
        }

        public CloseChoice ShowChoice(IWin32Window owner)
        {
            ShowDialog(owner);
            return _result;
        }
    }
}
