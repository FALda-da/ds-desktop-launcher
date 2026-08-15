using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

class DSHLauncher
{
    const int Port = 3080;
    static readonly string Url = "http://127.0.0.1:" + Port;

    static bool PortAlive(int port)
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
        if (args.Length > 0 && (args[0] == "--stop" || args[0] == "-stop" || args[0] == "/stop"))
        {
            return StopServer();
        }

        // Already running? Just open the browser.
        if (PortAlive(Port))
        {
            OpenBrowser();
            return 0;
        }

        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string bat = Path.Combine(dir, "start-server.bat");
        if (!File.Exists(bat))
        {
            MessageBox.Show("start-server.bat not found next to DSHLauncher.exe", "DSH",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        // Start the server fully hidden (no console window at all).
        // Output goes to dsh-server.log via the bat's own redirection.
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = "cmd.exe";
        psi.Arguments = "/c \"" + bat + "\"";
        psi.WorkingDirectory = dir;
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to start the server: " + ex.Message, "DSH",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        // Wait for the port to come up (up to 60 seconds).
        for (int i = 0; i < 60; i++)
        {
            Thread.Sleep(1000);
            if (PortAlive(Port)) break;
        }

        OpenBrowser();
        return 0;
    }

    static void OpenBrowser()
    {
        try
        {
            Process.Start(Url);
        }
        catch
        {
        }
    }

    static int StopServer()
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

        if (killed == 0)
        {
            MessageBox.Show("No running DSH server was found.", "DSH",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
        return 0;
    }
}
