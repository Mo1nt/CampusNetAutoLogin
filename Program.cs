using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;

namespace CampusNetAutoLogin
{
    internal static class Program
    {
        private const string Title = "重庆三峡科技大学校园网自动登录";
        private const string IniName = "CampusNetAutoLogin.ini";

        private static string account = "";
        private static string password = "";
        private static string portalHost = "1.1.1.1";
        private static int portalPort = 801;
        private static int loginAttempts = 20;

        private static readonly string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36 Edg/152.0.0.0";

        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls |
                SecurityProtocolType.Tls11 |
                SecurityProtocolType.Tls12;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string exePath = Application.ExecutablePath;
            string exeDir = Path.GetDirectoryName(exePath);
            string logFile = Path.Combine(exeDir, "CampusNetAutoLogin.log");
            LoadConfig(exeDir);

            string startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string startupExe = Path.Combine(startupDir, Path.GetFileName(exePath));

            // Windows started this copy from the Startup folder -> run silently.
            if (String.Equals(exePath, startupExe, StringComparison.OrdinalIgnoreCase))
            {
                RunAutoLogin(logFile, loginAttempts, true);
                return;
            }

            Application.Run(new ControlPanel(exePath, exeDir, logFile));
        }

        private static void LoadConfig(string dir)
        {
            string path = Path.Combine(dir, IniName);
            if (!File.Exists(path))
            {
                return;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch
            {
                return;
            }

            foreach (string rawLine in lines)
            {
                string line = (rawLine ?? String.Empty).Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "account":
                        if (value.Length > 0) { account = value; }
                        break;
                    case "password":
                        password = value;
                        break;
                    case "portal_host":
                        if (value.Length > 0) { portalHost = value; }
                        break;
                    case "portal_port":
                        {
                            int parsed;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                            {
                                portalPort = parsed;
                            }
                        }
                        break;
                    case "login_attempts":
                        {
                            int parsed;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
                            {
                                loginAttempts = parsed;
                            }
                        }
                        break;
                }
            }
        }

        private static void Log(string logFile, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message;
            try
            {
                File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static bool RunAutoLogin(string logFile, int maxAttempts, bool silentFailure)
        {
            Log(logFile, "===== CampusNetAutoLogin start =====");

            bool success = false;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                Log(logFile, "Login attempt " + attempt + "/" + maxAttempts);

                string localIp = FindFirstIPv4();
                if (String.IsNullOrEmpty(localIp))
                {
                    Log(logFile, "No IPv4 address available yet; sending without wlan_user_ip.");
                }

                if (DoLogin(logFile, localIp))
                {
                    success = true;
                    break;
                }

                if (attempt < maxAttempts)
                {
                    Thread.Sleep(2000);
                }
            }

            if (success)
            {
                Log(logFile, "Campus login request succeeded.");
                return true;
            }

            Log(logFile, "Automatic login did not succeed after all attempts.");
            if (!silentFailure)
            {
                OpenPortalPage(logFile);
            }
            return false;
        }

        private static string GetIPv4Address(NetworkInterface ni)
        {
            if (ni == null)
            {
                return null;
            }

            try
            {
                IPInterfaceProperties props = ni.GetIPProperties();
                foreach (UnicastIPAddressInformation info in props.UnicastAddresses)
                {
                    if (info.Address == null || info.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }
                    if (IPAddress.IsLoopback(info.Address))
                    {
                        continue;
                    }

                    byte[] b = info.Address.GetAddressBytes();
                    if (b.Length == 4 && b[0] == 169 && b[1] == 254)
                    {
                        continue;
                    }

                    return info.Address.ToString();
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsVirtual(NetworkInterface ni)
        {
            string name = ni.Name ?? String.Empty;
            string desc = ni.Description ?? String.Empty;

            string[] bad = new string[]
            {
                "virtual", "loopback", "teredo", "bluetooth",
                "vEthernet", "hyper-v", "hyperv", "wfp", "npcap",
                "windivert", "tunnel", "pseudo", "isatap"
            };

            foreach (string token in bad)
            {
                if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    desc.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string FindFirstIPv4()
        {
            try
            {
                NetworkInterface[] all = NetworkInterface.GetAllNetworkInterfaces();

                foreach (NetworkInterfaceType preferredType in new NetworkInterfaceType[]
                {
                    NetworkInterfaceType.Ethernet,
                    NetworkInterfaceType.Wireless80211
                })
                {
                    foreach (NetworkInterface ni in all)
                    {
                        if (ni.NetworkInterfaceType != preferredType ||
                            ni.OperationalStatus != OperationalStatus.Up ||
                            IsVirtual(ni))
                        {
                            continue;
                        }

                        string ip = GetIPv4Address(ni);
                        if (!String.IsNullOrEmpty(ip))
                        {
                            return ip;
                        }
                    }
                }
            }
            catch
            {
            }

            return String.Empty;
        }

        private static bool DoLogin(string logFile, string localIp)
        {
            string accountForServer = ",0," + account;
            int randomV = new Random().Next(1000, 9999);

            string loginUrl =
                "http://" + portalHost + ":" + portalPort +
                "/eportal/portal/login?callback=dr1003" +
                "&login_method=1" +
                "&user_account=" + Uri.EscapeDataString(accountForServer) +
                "&user_password=" + Uri.EscapeDataString(password) +
                "&wlan_user_ip=" + Uri.EscapeDataString(localIp) +
                "&wlan_user_ipv6=" +
                "&wlan_user_mac=000000000000" +
                "&wlan_ac_ip=" +
                "&wlan_ac_name=" +
                "&jsVersion=4.2.1" +
                "&terminal_type=1" +
                "&lang=zh-cn" +
                "&v=" + randomV.ToString(CultureInfo.InvariantCulture) +
                "&lang=zh";

            Log(logFile, "Sending campus portal login request...");

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(loginUrl);
                request.Method = "GET";
                request.Timeout = 3000;
                request.ReadWriteTimeout = 3000;
                request.UserAgent = UserAgent;
                request.Referer = "http://" + portalHost + "/";
                request.Accept = "*/*";
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                string body = String.Empty;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8, true))
                    {
                        body = reader.ReadToEnd();
                    }
                }

                Regex resultOk = new Regex("\"result\"\\s*:\\s*1", RegexOptions.IgnoreCase);
                Regex retCodeOk = new Regex("\"ret_code\"\\s*:\\s*2", RegexOptions.IgnoreCase);

                if (resultOk.IsMatch(body) || retCodeOk.IsMatch(body))
                {
                    Log(logFile, "Auth server accepted the login request.");
                    return true;
                }

                string preview = body;
                if (preview.Length > 400)
                {
                    preview = preview.Substring(0, 400);
                }
                Log(logFile, "Auth server did not report success. Body: " + preview);
                return false;
            }
            catch (Exception ex)
            {
                Log(logFile, "Login request failed: " + ex.Message);
                return false;
            }
        }

        private static void OpenPortalPage(string logFile)
        {
            try
            {
                Process.Start("http://" + portalHost + "/a79.htm");
                Log(logFile, "Opened the portal page in the default browser.");
            }
            catch (Exception ex)
            {
                Log(logFile, "Could not open the portal page: " + ex.Message);
            }
        }

        private sealed class ControlPanel : Form
        {
            private readonly string sourceExe;
            private readonly string exeDir;
            private readonly string logFile;
            private readonly string startupDir;
            private readonly string startupExe;

            private Label statusLabel;
            private Button enableButton;
            private Button disableButton;
            private Button saveButton;
            private Button testButton;
            private TextBox accountBox;
            private TextBox passwordBox;

            private readonly Color PrimaryColor = Color.FromArgb(36, 84, 152);
            private readonly Color SuccessColor = Color.FromArgb(52, 142, 92);
            private readonly Color DangerColor = Color.FromArgb(196, 68, 68);
            private readonly Color AccentColor = Color.FromArgb(232, 157, 45);

            public ControlPanel(string exePath, string exeDirectory, string logPath)
            {
                sourceExe = exePath;
                exeDir = exeDirectory;
                logFile = logPath;
                startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                startupExe = Path.Combine(startupDir, Path.GetFileName(exePath));

                Text = Title;
                BackColor = Color.White;
                ClientSize = new Size(500, 392);
                FormBorderStyle = FormBorderStyle.FixedSingle;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = true;
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);

                BuildBanner();
                BuildAccountSection();
                BuildButtonsAndNotes();

                RefreshStatus();
            }

            private void BuildBanner()
            {
                Panel banner = new Panel();
                banner.BackColor = PrimaryColor;
                banner.Dock = DockStyle.Top;
                banner.Height = 82;

                Label bannerTitle = new Label();
                bannerTitle.Text = Title;
                bannerTitle.ForeColor = Color.White;
                bannerTitle.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
                bannerTitle.Location = new Point(18, 12);
                bannerTitle.Size = new Size(464, 32);
                bannerTitle.TextAlign = ContentAlignment.MiddleLeft;

                Label bannerSub = new Label();
                bannerSub.Text = "开机自动登录校园网  ·  账号密码与开机自启管理";
                bannerSub.ForeColor = Color.FromArgb(222, 230, 242);
                bannerSub.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
                bannerSub.Location = new Point(20, 48);
                bannerSub.Size = new Size(464, 24);
                bannerSub.TextAlign = ContentAlignment.MiddleLeft;

                banner.Controls.Add(bannerTitle);
                banner.Controls.Add(bannerSub);
                Controls.Add(banner);
            }

            private void BuildAccountSection()
            {
                Label sectionTitle = new Label();
                sectionTitle.Text = "登录账号";
                sectionTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
                sectionTitle.ForeColor = PrimaryColor;
                sectionTitle.Location = new Point(20, 98);
                sectionTitle.Size = new Size(180, 24);

                Label accountLabel = new Label();
                accountLabel.Text = "账号：";
                accountLabel.Location = new Point(20, 132);
                accountLabel.Size = new Size(60, 28);
                accountLabel.TextAlign = ContentAlignment.MiddleLeft;

                accountBox = new TextBox();
                accountBox.Location = new Point(82, 132);
                accountBox.Size = new Size(396, 28);
                accountBox.Text = account;
                accountBox.BorderStyle = BorderStyle.FixedSingle;

                Label passwordLabel = new Label();
                passwordLabel.Text = "密码：";
                passwordLabel.Location = new Point(20, 168);
                passwordLabel.Size = new Size(60, 28);
                passwordLabel.TextAlign = ContentAlignment.MiddleLeft;

                passwordBox = new TextBox();
                passwordBox.Location = new Point(82, 168);
                passwordBox.Size = new Size(396, 28);
                passwordBox.Text = password;
                passwordBox.UseSystemPasswordChar = true;
                passwordBox.BorderStyle = BorderStyle.FixedSingle;

                statusLabel = new Label();
                statusLabel.Location = new Point(22, 210);
                statusLabel.Size = new Size(458, 26);
                statusLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
                statusLabel.TextAlign = ContentAlignment.MiddleLeft;

                Controls.Add(sectionTitle);
                Controls.Add(accountLabel);
                Controls.Add(accountBox);
                Controls.Add(passwordLabel);
                Controls.Add(passwordBox);
                Controls.Add(statusLabel);
            }

            private void BuildButtonsAndNotes()
            {
                enableButton = MakeButton("开启开机自启", SuccessColor, new Point(18, 250), new Size(112, 42));
                enableButton.Click += delegate { EnableStartup(); };

                disableButton = MakeButton("关闭开机自启", DangerColor, new Point(136, 250), new Size(112, 42));
                disableButton.Click += delegate { DisableStartup(); };

                saveButton = MakeButton("保存账号密码", AccentColor, new Point(254, 250), new Size(112, 42));
                saveButton.Click += delegate { SaveAccountPassword(); };

                testButton = MakeButton("立即登录测试", PrimaryColor, new Point(372, 250), new Size(112, 42));
                testButton.Click += delegate { TestLogin(); };

                Label note = new Label();
                note.Text = "修改账号密码后请点“保存账号密码”生效。想停用自启就点“关闭开机自启”。";
                note.ForeColor = Color.FromArgb(130, 130, 130);
                note.Location = new Point(22, 312);
                note.Size = new Size(458, 24);
                note.TextAlign = ContentAlignment.MiddleLeft;

                Controls.Add(enableButton);
                Controls.Add(disableButton);
                Controls.Add(saveButton);
                Controls.Add(testButton);
                Controls.Add(note);
            }

            private Button MakeButton(string text, Color color, Point location, Size size)
            {
                Button b = new Button();
                b.Text = text;
                b.BackColor = color;
                b.ForeColor = Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                b.Location = location;
                b.Size = size;
                b.Cursor = Cursors.Hand;
                b.UseVisualStyleBackColor = false;
                return b;
            }

            private void RefreshStatus()
            {
                bool enabled = File.Exists(startupExe);
                statusLabel.Text = enabled
                    ? "●  开机自启：已开启"
                    : "●  开机自启：已关闭";
                statusLabel.ForeColor = enabled ? SuccessColor : DangerColor;
                enableButton.Enabled = !enabled;
                disableButton.Enabled = enabled;
            }

            private void EnableStartup()
            {
                try
                {
                    Directory.CreateDirectory(startupDir);
                    File.Copy(sourceExe, startupExe, true);

                    string sourceIni = Path.Combine(exeDir, IniName);
                    string destIni = Path.Combine(startupDir, IniName);
                    if (File.Exists(sourceIni))
                    {
                        File.Copy(sourceIni, destIni, true);
                    }

                    MessageBox.Show("已开启开机自启。\r\n以后每次开机登录 Windows 都会自动尝试登录校园网。",
                        Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("开启失败：" + ex.Message, Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                }
                RefreshStatus();
            }

            private void DisableStartup()
            {
                try
                {
                    if (File.Exists(startupExe))
                    {
                        File.Delete(startupExe);
                    }

                    MessageBox.Show("已关闭开机自启。\r\n不会再随 Windows 自动登录。",
                        Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("关闭失败：" + ex.Message, Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                }
                RefreshStatus();
            }

            private void SaveAccountPassword()
            {
                string acc = accountBox.Text.Trim();
                string pwd = passwordBox.Text;

                if (acc.Length == 0 || pwd.Length == 0)
                {
                    MessageBox.Show("账号和密码不能为空。", Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                account = acc;
                password = pwd;

                try
                {
                    WriteSettingsFile(Path.Combine(exeDir, IniName));
                    if (File.Exists(startupExe))
                    {
                        WriteSettingsFile(Path.Combine(startupDir, IniName));
                    }

                    MessageBox.Show("账号密码已保存。", Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存失败：" + ex.Message, Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            private static void WriteSettingsFile(string path)
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# CampusNetAutoLogin settings");
                sb.AppendLine();
                sb.AppendLine("account=" + account);
                sb.AppendLine("password=" + password);
                sb.AppendLine();
                sb.AppendLine("portal_host=" + portalHost);
                sb.AppendLine("portal_port=" + portalPort);
                sb.AppendLine();
                sb.AppendLine("# Retry every 2 seconds until the network is ready.");
                sb.AppendLine("login_attempts=" + loginAttempts);

                string dir = Path.GetDirectoryName(path);
                if (!String.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }

            private void TestLogin()
            {
                string acc = accountBox.Text.Trim();
                string pwd = passwordBox.Text;

                if (acc.Length == 0 || pwd.Length == 0)
                {
                    MessageBox.Show("请先填写账号和密码。", Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                account = acc;
                password = pwd;

                Cursor = Cursors.WaitCursor;
                statusLabel.Text = "正在登录，请稍候…";
                Refresh();

                int maxAttempts = Math.Min(loginAttempts, 3);
                bool ok = RunAutoLogin(logFile, maxAttempts, true);

                Cursor = Cursors.Default;
                if (ok)
                {
                    MessageBox.Show("登录成功（或账号已在线）。", Title,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("自动登录失败。\r\n请检查网线、网络或账号密码。",
                        Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                RefreshStatus();
                Close();
            }
        }
    }
}
