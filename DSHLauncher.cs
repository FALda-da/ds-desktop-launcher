using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

class DSHLauncher
{
    public const int Port = 3080;
    public static readonly string BaseUrl = "http://127.0.0.1:" + Port;
    public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string LogPath = Path.Combine(Root, "dsh-server.log");

    public static bool PortAlive(int port)
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

    // The engine prints its tokenised URL ("dsh web: http://127.0.0.1:3080/?token=...")
    // into the log; the newest match belongs to the running server.
    static string TokenUrlFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string text;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long len = fs.Length;
                int take = (int)Math.Min(len, 262144);
                fs.Seek(len - take, SeekOrigin.Begin);
                byte[] buf = new byte[take];
                int read = fs.Read(buf, 0, take);
                text = Encoding.UTF8.GetString(buf, 0, read);
            }
            MatchCollection ms = Regex.Matches(text, "https?://[^\\s\"'<>]*[?&]token=[A-Za-z0-9_\\-\\.]+");
            if (ms.Count == 0) return null;
            return ms[ms.Count - 1].Value.TrimEnd('.', ',', ')', ']', ';');
        }
        catch
        {
            return null;
        }
    }

    public static string FindTokenUrl()
    {
        string u = TokenUrlFromFile(LogPath);
        if (u == null) u = TokenUrlFromFile(LogPath + ".old");
        return u;
    }

    // Localhost probe. Proxy = null on purpose: the machine runs a system proxy,
    // and routing 127.0.0.1 through it would fail.
    public static int HttpStatus(string url)
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.AllowAutoRedirect = false;
            req.Timeout = 4000;
            req.Proxy = null;
            req.UserAgent = "DSHLauncher";
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                return (int)resp.StatusCode;
            }
        }
        catch (WebException wex)
        {
            HttpWebResponse r = wex.Response as HttpWebResponse;
            if (r != null) return (int)r.StatusCode;
            return -1;
        }
        catch
        {
            return -1;
        }
    }

    public static bool IsOk(int status)
    {
        return status == 200 || status == 302 || status == 303 || status == 307 || status == 308;
    }

    public static string ReadLogTail(int lines)
    {
        try
        {
            if (!File.Exists(LogPath)) return "(dsh-server.log not found)";
            string[] all;
            using (FileStream fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
            {
                all = sr.ReadToEnd().Split('\n');
            }
            int n = Math.Min(lines, all.Length);
            StringBuilder sb = new StringBuilder();
            for (int i = all.Length - n; i < all.Length; i++)
            {
                sb.AppendLine(all[i].TrimEnd('\r'));
            }
            string s = sb.ToString();
            if (s.Length > 4000) s = s.Substring(s.Length - 4000);
            return s;
        }
        catch (Exception ex)
        {
            return "(cannot read log: " + ex.Message + ")";
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length > 0 && (args[0] == "--diag" || args[0] == "/diag"))
        {
            RunDiag();
            return 0;
        }

        bool stop = args.Length > 0 && (args[0] == "--stop" || args[0] == "-stop" || args[0] == "/stop");
        ProgressForm form = new ProgressForm(stop);
        Application.Run(form);
        return form.ExitCode;
    }

    // Writes dsh-diag.txt (and stdout when attached) - handy when a start fails.
    static void RunDiag()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("DSH diagnostic  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("root        : " + Root);
        sb.AppendLine("port " + Port + "   : " + (PortAlive(Port) ? "listening" : "not listening"));
        string token = FindTokenUrl();
        sb.AppendLine("token url   : " + (token == null ? "(none found in log)" : token));
        sb.AppendLine("base url    : " + BaseUrl + " -> HTTP " + HttpStatus(BaseUrl) + "   (401 = token required)");
        if (token != null) sb.AppendLine("token url   : -> HTTP " + HttpStatus(token) + "   (200/302/303 = usable)");
        sb.AppendLine("log file    : " + LogPath);
        sb.AppendLine("--- log tail ---");
        sb.Append(ReadLogTail(30));
        string text = sb.ToString();
        try { File.WriteAllText(Path.Combine(Root, "dsh-diag.txt"), text, new UTF8Encoding(true)); } catch { }
        try { Console.WriteLine(text); } catch { }
    }
}

class ProgressForm : Form
{
    public int ExitCode = 0;

    Label titleLabel;
    Label statusLabel;
    ProgressBar bar;
    Timer timer;
    TextBox logBox;
    Button logButton;
    Button forceButton;
    Button closeButton;

    readonly bool stopMode;
    int step;                    // start flow: 0 begin, 1 wait for port, 2 resolve url, 3 finished
    DateTime phaseStart;
    DateTime doneAt;
    bool done;
    bool failed;
    bool weStarted;
    int stoppedCount;
    Process serverProc;
    DateTime lastResolve;

    public ProgressForm(bool stopMode)
    {
        this.stopMode = stopMode;

        // Scale layout and fonts with the display DPI (96 = 100% baseline).
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "DSH";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        ClientSize = new Size(420, 142);

        titleLabel = new Label();
        titleLabel.AutoSize = false;
        titleLabel.Location = new Point(18, 14);
        titleLabel.Size = new Size(384, 28);
        titleLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        titleLabel.Text = stopMode ? "DSH 正在停止服务器..." : "DSH 正在启动...";
        Controls.Add(titleLabel);

        bar = new ProgressBar();
        bar.Location = new Point(18, 52);
        bar.Size = new Size(384, 22);
        bar.Style = ProgressBarStyle.Continuous;
        bar.Minimum = 0;
        bar.Maximum = 100;
        Controls.Add(bar);

        statusLabel = new Label();
        statusLabel.AutoSize = false;
        statusLabel.Location = new Point(18, 86);
        statusLabel.Size = new Size(384, 40);
        statusLabel.ForeColor = Color.FromArgb(95, 95, 95);
        statusLabel.Text = stopMode ? "正在查找并停止 DSH 服务器..." : "正在检查服务器状态...";
        Controls.Add(statusLabel);

        // hidden until something goes wrong
        logBox = new TextBox();
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Both;
        logBox.WordWrap = false;
        logBox.Font = new Font("Consolas", 8.5F);
        logBox.Location = new Point(18, 86);
        logBox.Size = new Size(384, 226);
        logBox.Visible = false;
        Controls.Add(logBox);

        logButton = new Button();
        logButton.Text = "打开日志";
        logButton.Location = new Point(18, 320);
        logButton.Size = new Size(110, 30);
        logButton.Visible = false;
        logButton.Click += delegate { OpenPath(DSHLauncher.LogPath); };
        Controls.Add(logButton);

        forceButton = new Button();
        forceButton.Text = "强制打开网页";
        forceButton.Location = new Point(140, 320);
        forceButton.Size = new Size(130, 30);
        forceButton.Visible = false;
        forceButton.Click += delegate { OpenPath(DSHLauncher.BaseUrl); };
        Controls.Add(forceButton);

        closeButton = new Button();
        closeButton.Text = "关闭";
        closeButton.Location = new Point(292, 320);
        closeButton.Size = new Size(110, 30);
        closeButton.Visible = false;
        closeButton.Click += delegate { Close(); };
        Controls.Add(closeButton);

        timer = new Timer();
        timer.Interval = 150;
        timer.Tick += OnTick;

        Shown += delegate { Begin(); };
    }

    void Begin()
    {
        phaseStart = DateTime.Now;
        timer.Start();

        if (stopMode)
        {
            stoppedCount = KillServerProcesses();
            if (stoppedCount == 0) SetStatus("未发现运行中的 DSH 服务器");
            else SetStatus("正在等待服务器关闭...");
            return;
        }

        // Already running: the engine will not open a browser this time, so we must.
        if (DSHLauncher.PortAlive(DSHLauncher.Port))
        {
            step = 2;
            SetStatus("服务器已在运行，正在准备网页链接...");
            return;
        }

        string bat = Path.Combine(DSHLauncher.Root, "start-server.bat");
        if (!File.Exists(bat))
        {
            Fail("找不到 start-server.bat（应与 DSHLauncher.exe 放在同一文件夹）");
            return;
        }

        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c \"" + bat + "\"";
            psi.WorkingDirectory = DSHLauncher.Root;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            serverProc = Process.Start(psi);
            weStarted = true;
        }
        catch (Exception ex)
        {
            Fail("启动服务器失败：" + ex.Message);
            return;
        }

        step = 1;
        SetStatus("正在启动服务器...");
    }

    void OnTick(object sender, EventArgs e)
    {
        if (done)
        {
            if (failed) { EnableClose(); return; }          // leave the details on screen
            if ((DateTime.Now - doneAt).TotalMilliseconds >= 900)
            {
                timer.Stop();
                Close();
            }
            return;
        }

        if (stopMode)
        {
            double sec = (DateTime.Now - phaseStart).TotalSeconds;
            if (DSHLauncher.PortAlive(DSHLauncher.Port) && sec < 8)
            {
                SetStatus("正在等待服务器关闭...");
                return;
            }
            if (stoppedCount > 0) SetStatus("已停止服务器（结束 " + stoppedCount + " 个进程）");
            SetTitle("DSH 已停止");
            Finish();
            return;
        }

        Pulse();

        if (step == 1)
        {
            double sec = (DateTime.Now - phaseStart).TotalSeconds;
            if (DSHLauncher.PortAlive(DSHLauncher.Port))
            {
                step = 2;
                lastResolve = DateTime.MinValue;
                phaseStart = DateTime.Now;
                SetStatus(weStarted ? "服务器已就绪，等待浏览器打开..." : "服务器已就绪，正在打开网页...");
                return;
            }
            if (serverProc != null && serverProc.HasExited)
            {
                Fail("服务器进程已退出（请看下方日志）");
                return;
            }
            if (sec > 90)
            {
                Fail("服务器启动超时（已等待 90 秒）");
                return;
            }
            SetStatus("正在启动服务器...（已等待 " + (int)sec + " 秒）");
            return;
        }

        if (step == 2)
        {
            // Probe at most every ~700 ms, so the log/HTTP checks stay cheap.
            if ((DateTime.Now - lastResolve).TotalMilliseconds < 700 && lastResolve != DateTime.MinValue)
                return;
            lastResolve = DateTime.Now;

            string url = ResolveUrl();
            if (url != null)
            {
                if (weStarted)
                {
                    SetStatus("完成 ✔ 服务器已就绪");
                }
                else
                {
                    OpenPath(url);
                    SetStatus("完成 ✔ 已打开网页");
                }
                Finish();
                return;
            }

            if ((DateTime.Now - phaseStart).TotalSeconds > 12)
            {
                if (weStarted)
                {
                    // The engine opens its own tokenised tab; an unreadable log is not fatal.
                    SetStatus("完成 ✔ 服务器已就绪（网页应由服务器自动打开）");
                    SetTitle("DSH 已就绪");
                    Finish();
                }
                else
                {
                    Fail("服务器在运行，但网页链接无法通过鉴权（可能 token 已失效）");
                }
                return;
            }
            SetStatus("正在准备网页链接...");
            return;
        }
    }

    // Returns a URL that is verified to answer, or null.
    string ResolveUrl()
    {
        string token = DSHLauncher.FindTokenUrl();
        if (token != null)
        {
            if (DSHLauncher.IsOk(DSHLauncher.HttpStatus(token))) return token;
            return null;
        }
        // no token in the log -> older engine without auth, or log rotated away
        if (DSHLauncher.IsOk(DSHLauncher.HttpStatus(DSHLauncher.BaseUrl))) return DSHLauncher.BaseUrl;
        return null;
    }

    void Pulse()
    {
        if (bar.Visible && !done)
        {
            int v = bar.Value + 5;
            if (v > 96) v = 5;
            bar.Value = v;
        }
    }

    void Finish()
    {
        done = true;
        doneAt = DateTime.Now;
        if (!failed) SetTitle("DSH 已就绪");
        bar.Value = 100;
    }

    void Fail(string message)
    {
        failed = true;
        done = true;
        doneAt = DateTime.Now;

        bar.Visible = false;
        logBox.Text = DSHLauncher.ReadLogTail(40);
        logBox.Visible = true;
        logButton.Visible = true;
        forceButton.Visible = true;
        closeButton.Visible = true;
        ControlBox = true;

        SetTitle("DSH 启动失败");
        SetStatus(message);
        ExitCode = 1;

        float f = CurrentAutoScaleDimensions.Width / 96f;
        if (f <= 0f) f = 1f;
        ClientSize = new Size((int)(420 * f), (int)(372 * f));
        PerformLayout();
    }

    void EnableClose()
    {
        ControlBox = true;
    }

    void OpenPath(string target)
    {
        try { Process.Start(target); } catch { }
    }

    void SetStatus(string text) { statusLabel.Text = text; }
    void SetTitle(string text) { titleLabel.Text = text; }

    int KillServerProcesses()
    {
        int killed = 0;
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
                                killed++;
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
        return killed;
    }
}
