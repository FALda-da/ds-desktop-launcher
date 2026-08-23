using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Windows.Forms;

class DSHLauncher
{
    public const int Port = 3080;
    public static readonly string Url = "http://127.0.0.1:" + Port;

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

    [STAThread]
    static int Main(string[] args)
    {
        // DPI awareness comes from the embedded app.manifest (PerMonitorV2),
        // so the UI renders sharply on high-DPI (2K/4K) displays.
        bool stop = args.Length > 0 && (args[0] == "--stop" || args[0] == "-stop" || args[0] == "/stop");
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ProgressForm form = new ProgressForm(stop);
        Application.Run(form);
        return form.ExitCode;
    }
}

class ProgressForm : Form
{
    public int ExitCode = 0;

    Label titleLabel;
    Label statusLabel;
    ProgressBar bar;
    Timer timer;

    bool stopMode;
    string root;
    int step;              // start flow: 0 check, 1 start, 2 wait, 3 open browser, 4 done
    DateTime waitStart;
    bool done;
    DateTime doneAt;

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
        ClientSize = new Size(400, 142);

        titleLabel = new Label();
        titleLabel.AutoSize = false;
        titleLabel.Location = new Point(18, 14);
        titleLabel.Size = new Size(364, 28);
        titleLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        titleLabel.Text = stopMode ? "DSH 正在停止服务器..." : "DSH 正在启动...";
        Controls.Add(titleLabel);

        bar = new ProgressBar();
        bar.Location = new Point(18, 52);
        bar.Size = new Size(364, 22);
        bar.Style = ProgressBarStyle.Continuous;
        bar.Minimum = 0;
        bar.Maximum = 100;
        Controls.Add(bar);

        statusLabel = new Label();
        statusLabel.AutoSize = false;
        statusLabel.Location = new Point(18, 86);
        statusLabel.Size = new Size(364, 40);
        statusLabel.Font = new Font("Microsoft YaHei UI", 9F);
        statusLabel.ForeColor = Color.FromArgb(95, 95, 95);
        statusLabel.Text = stopMode ? "正在查找并停止 DSH 服务器..." : "正在检查服务器状态...";
        Controls.Add(statusLabel);

        timer = new Timer();
        timer.Interval = 120;
        timer.Tick += OnTick;

        Shown += delegate { Begin(); };
    }

    void Begin()
    {
        root = AppDomain.CurrentDomain.BaseDirectory;
        timer.Start();

        if (stopMode)
        {
            StopServerNow();
            return;
        }

        // Step 0: already running? Jump straight to opening the browser.
        if (DSHLauncher.PortAlive(DSHLauncher.Port))
        {
            step = 3;
            SetStatus("服务器已在运行，正在打开网页...");
            return;
        }

        string bat = Path.Combine(root, "start-server.bat");
        if (!File.Exists(bat))
        {
            timer.Stop();
            MessageBox.Show("start-server.bat not found next to DSHLauncher.exe", "DSH",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitCode = 1;
            Close();
            return;
        }

        // Step 1: start the server fully hidden (no console window).
        // Output goes to dsh-server.log via the bat's own redirection.
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c \"" + bat + "\"";
            psi.WorkingDirectory = root;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            timer.Stop();
            MessageBox.Show("Failed to start the server: " + ex.Message, "DSH",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitCode = 1;
            Close();
            return;
        }

        waitStart = DateTime.Now;
        step = 2;
        SetStatus("正在启动服务器...");
    }

    void OnTick(object sender, EventArgs e)
    {
        if (stopMode)
        {
            if (done && (DateTime.Now - doneAt).TotalMilliseconds >= 900)
            {
                timer.Stop();
                Close();
            }
            return;
        }

        if (!done)
        {
            if (step < 4)
            {
                int v = bar.Value + 4;
                if (v > 96) v = 4;
                bar.Value = v;
            }

            if (step == 2)
            {
                int sec = (int)(DateTime.Now - waitStart).TotalSeconds;
                if (DSHLauncher.PortAlive(DSHLauncher.Port))
                {
                    step = 3;
                    SetStatus("服务器已就绪，正在打开网页...");
                }
                else if (sec >= 60)
                {
                    step = 3;
                    SetStatus("等待超时，仍然尝试打开网页...");
                }
                else
                {
                    SetStatus("正在启动服务器...（已等待 " + sec + " 秒）");
                }
            }
            else if (step == 3)
            {
                step = 4;
                OpenBrowser();
                SetStatus("完成 ✔");
            }
            else if (step == 4)
            {
                done = true;
                doneAt = DateTime.Now;
                bar.Value = 100;
            }
        }
        else if ((DateTime.Now - doneAt).TotalMilliseconds >= 800)
        {
            timer.Stop();
            Close();
        }
    }

    void StopServerNow()
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

        if (killed > 0)
        {
            SetStatus("已停止服务器（结束 " + killed + " 个进程）");
        }
        else
        {
            SetStatus("未发现运行中的 DSH 服务器");
            ExitCode = 1;
        }
        SetTitle("DSH 已停止");
        done = true;
        doneAt = DateTime.Now;
        bar.Value = 100;
    }

    void OpenBrowser()
    {
        try
        {
            Process.Start(DSHLauncher.Url);
        }
        catch
        {
        }
    }

    void SetStatus(string text)
    {
        statusLabel.Text = text;
    }

    void SetTitle(string text)
    {
        titleLabel.Text = text;
    }
}
