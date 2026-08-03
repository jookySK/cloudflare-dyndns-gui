// =====================================================================
//  Cloudflare DDNS Updater pre Windows — samostatný .exe
//  Automatická aktualizácia DNS záznamov v Cloudflare podľa verejnej IP.
//  Parameter -hidden = spustí sa rovno skryto do systémovej lišty.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CloudflareDDNS
{
    public class DnsRec
    {
        public string ZoneId { get; set; }
        public string ZoneName { get; set; }
        public string RecordId { get; set; }
        public string RecordName { get; set; }
    }

    public class Config
    {
        public string TokenEnc { get; set; }
        public int IntervalMinutes { get; set; }
        public string LastIP { get; set; }
        public List<DnsRec> Records { get; set; }

        public Config()
        {
            TokenEnc = "";
            IntervalMinutes = 5;
            LastIP = "";
            Records = new List<DnsRec>();
        }
    }

    class RecItem
    {
        public DnsRec Rec;
        public string Label;
        public override string ToString() { return Label; }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "CloudflareDDNS_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("Cloudflare DDNS Updater už beží — pozri ikonu v systémovej lište.",
                        "Už spustené", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                bool hidden = false;
                foreach (var a in args)
                    if (a.Equals("-hidden", StringComparison.OrdinalIgnoreCase)) hidden = true;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(hidden));
            }
        }
    }

    public class MainForm : Form
    {
        readonly string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudflareDDNS");
        string ConfigFile { get { return Path.Combine(configDir, "config.json"); } }
        string LogFile { get { return Path.Combine(configDir, "log.txt"); } }

        Config config = new Config();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();

        TextBox txtToken;
        bool tokenSaved;
        CheckedListBox list;
        NumericUpDown numInterval;
        CheckBox chkAuto;
        Label statusLabel;
        TextBox logBox;
        NotifyIcon tray;
        System.Windows.Forms.Timer timer;
        bool reallyExit;
        readonly bool startHidden;

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "CloudflareDDNS";

        public MainForm(bool hidden)
        {
            startHidden = hidden;
            Directory.CreateDirectory(configDir);
            LoadConfig();
            BuildUi();
            BuildTray();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = Math.Max(1, config.IntervalMinutes) * 60000;
            timer.Tick += delegate { CheckAndUpdate(false); };
            timer.Start();

            if (startHidden)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Log("--- Program spustený ---");
            if (startHidden)
            {
                Hide();
                ShowInTaskbar = true;
                WindowState = FormWindowState.Normal;
            }
            if (config.Records.Count > 0) CheckAndUpdate(false);
        }

        // ------------------------------------------------------------ UI
        void BuildUi()
        {
            Text = "Cloudflare DDNS Updater";
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(624, 660);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int y = 15;

            var lblToken = new Label { Text = "1. Cloudflare API token (s právom Zone.DNS – Edit):", Location = new Point(15, y), AutoSize = true };
            Controls.Add(lblToken); y += 25;

            txtToken = new TextBox { Location = new Point(15, y), Size = new Size(440, 25), UseSystemPasswordChar = true };
            if (!string.IsNullOrEmpty(config.TokenEnc)) { txtToken.Text = "********** (token je uložený)"; tokenSaved = true; }
            txtToken.TextChanged += delegate { tokenSaved = false; };
            Controls.Add(txtToken);

            var btnLoad = new Button { Text = "Načítať záznamy", Location = new Point(465, y - 1), Size = new Size(145, 27) };
            btnLoad.Click += delegate { LoadRecordsClick(); };
            Controls.Add(btnLoad); y += 40;

            var lblRec = new Label { Text = "2. Vyber záznamy, ktorým sa má aktualizovať IP adresa:", Location = new Point(15, y), AutoSize = true };
            Controls.Add(lblRec); y += 25;

            list = new CheckedListBox { Location = new Point(15, y), Size = new Size(595, 200), CheckOnClick = true };
            Controls.Add(list); y += 210;
            FillListFromConfig();

            var lblInt = new Label { Text = "3. Kontrolovať IP adresu každých", Location = new Point(15, y + 4), AutoSize = true };
            Controls.Add(lblInt);
            numInterval = new NumericUpDown { Location = new Point(230, y), Size = new Size(60, 25), Minimum = 1, Maximum = 1440, Value = Math.Max(1, config.IntervalMinutes) };
            Controls.Add(numInterval);
            var lblMin = new Label { Text = "minút", Location = new Point(295, y + 4), AutoSize = true };
            Controls.Add(lblMin); y += 35;

            chkAuto = new CheckBox { Text = "Spúšťať automaticky pri prihlásení do Windows (skryto, do lišty)", Location = new Point(15, y), AutoSize = true, Checked = GetAutoStart() };
            Controls.Add(chkAuto); y += 35;

            var btnSave = new Button { Text = "Uložiť nastavenia", Location = new Point(15, y), Size = new Size(180, 32) };
            btnSave.Click += delegate { SaveClick(); };
            Controls.Add(btnSave);

            var btnNow = new Button { Text = "Skontrolovať a aktualizovať teraz", Location = new Point(205, y), Size = new Size(230, 32) };
            btnNow.Click += delegate { CheckAndUpdate(true); };
            Controls.Add(btnNow);

            var btnHide = new Button { Text = "Skryť do lišty", Location = new Point(445, y), Size = new Size(165, 32) };
            btnHide.Click += delegate { HideToTray(); };
            Controls.Add(btnHide); y += 45;

            statusLabel = new Label { Text = "Pripravené.", Location = new Point(15, y), Size = new Size(595, 20) };
            Controls.Add(statusLabel); y += 25;

            logBox = new TextBox { Location = new Point(15, y), Size = new Size(595, 140), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            Controls.Add(logBox);
        }

        void BuildTray()
        {
            tray = new NotifyIcon();
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { tray.Icon = SystemIcons.Application; }
            tray.Text = "Cloudflare DDNS Updater";
            tray.Visible = true;

            var menu = new ContextMenuStrip();
            var miOpen = menu.Items.Add("Otvoriť nastavenia");
            var miCheck = menu.Items.Add("Skontrolovať teraz");
            menu.Items.Add(new ToolStripSeparator());
            var miExit = menu.Items.Add("Ukončiť program");
            tray.ContextMenuStrip = menu;

            miOpen.Click += delegate { ShowFromTray(); };
            tray.DoubleClick += delegate { ShowFromTray(); };
            miCheck.Click += delegate { CheckAndUpdate(true); };
            miExit.Click += delegate { reallyExit = true; tray.Visible = false; Close(); };
        }

        void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        void HideToTray()
        {
            Hide();
            tray.ShowBalloonTip(3000, "Cloudflare DDNS", "Program beží ďalej na pozadí. Nájdeš ho v systémovej lište.", ToolTipIcon.Info);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------ Config
        void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var c = json.Deserialize<Config>(File.ReadAllText(ConfigFile, Encoding.UTF8));
                    if (c != null)
                    {
                        config = c;
                        if (config.Records == null) config.Records = new List<DnsRec>();
                        if (config.IntervalMinutes < 1) config.IntervalMinutes = 5;
                        // token zo staršej PowerShell verzie má iný formát — over, či sa dá dešifrovať
                        if (!string.IsNullOrEmpty(config.TokenEnc) && GetToken() == null) config.TokenEnc = "";
                    }
                }
            }
            catch { config = new Config(); }
        }

        void SaveConfig()
        {
            File.WriteAllText(ConfigFile, json.Serialize(config), Encoding.UTF8);
        }

        string GetToken()
        {
            if (string.IsNullOrEmpty(config.TokenEnc)) return null;
            try
            {
                var dec = ProtectedData.Unprotect(Convert.FromBase64String(config.TokenEnc), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(dec);
            }
            catch { return null; }
        }

        void SetToken(string plain)
        {
            var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            config.TokenEnc = Convert.ToBase64String(enc);
        }

        // ------------------------------------------------------------ Log
        void Log(string msg)
        {
            string line = string.Format("[{0:dd.MM.yyyy HH:mm:ss}] {1}", DateTime.Now, msg);
            try { File.AppendAllText(LogFile, line + Environment.NewLine, Encoding.UTF8); } catch { }
            if (logBox != null) logBox.AppendText(line + Environment.NewLine);
        }

        // ------------------------------------------------------------ HTTP / Cloudflare
        string Http(string method, string url, string body)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = 15000;
            req.Headers["Authorization"] = "Bearer " + GetToken();
            req.ContentType = "application/json";
            req.UserAgent = "CloudflareDDNS-Windows";
            if (body != null)
            {
                var b = Encoding.UTF8.GetBytes(body);
                req.ContentLength = b.Length;
                using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
            }
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        Dictionary<string, object> ApiGet(string url)
        {
            return (Dictionary<string, object>)json.DeserializeObject(Http("GET", url, null));
        }

        // ------------------------------------------------------------ Verejná IP
        string GetPublicIP()
        {
            string[] services = { "https://api.ipify.org", "https://ifconfig.me/ip", "https://icanhazip.com" };
            foreach (var s in services)
            {
                try
                {
                    using (var wc = new WebClient())
                    {
                        wc.Headers["User-Agent"] = "CloudflareDDNS-Windows";
                        string ip = wc.DownloadString(s).Trim();
                        System.Net.IPAddress parsed;
                        if (System.Net.IPAddress.TryParse(ip, out parsed) &&
                            parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            return ip;
                    }
                }
                catch { }
            }
            return null;
        }

        // ------------------------------------------------------------ Hlavná logika
        void CheckAndUpdate(bool force)
        {
            try
            {
                string ip = GetPublicIP();
                if (ip == null) { Log("CHYBA: Nepodarilo sa zistiť verejnú IP adresu."); return; }

                statusLabel.Text = string.Format("Verejná IP: {0}  |  posledná kontrola: {1:HH:mm:ss}", ip, DateTime.Now);

                if (!force && ip == config.LastIP)
                {
                    Log("IP sa nezmenila (" + ip + ") – nič netreba aktualizovať.");
                    return;
                }
                if (ip != config.LastIP)
                    Log("Zistená zmena IP: '" + config.LastIP + "' -> '" + ip + "'");

                if (config.Records.Count == 0) { Log("Nie sú vybrané žiadne záznamy na aktualizáciu."); return; }
                if (GetToken() == null) { Log("CHYBA: Chýba API token — otvor nastavenia a vlož ho."); return; }

                bool allOk = true;
                foreach (var rec in config.Records)
                {
                    try
                    {
                        string body = json.Serialize(new Dictionary<string, object> { { "content", ip } });
                        string url = "https://api.cloudflare.com/client/v4/zones/" + rec.ZoneId + "/dns_records/" + rec.RecordId;
                        var resp = (Dictionary<string, object>)json.DeserializeObject(Http("PATCH", url, body));
                        if (resp.ContainsKey("success") && (bool)resp["success"])
                            Log("AKTUALIZOVANÉ: " + rec.RecordName + " -> " + ip);
                        else { Log("CHYBA: " + rec.RecordName + " sa nepodarilo aktualizovať."); allOk = false; }
                    }
                    catch (Exception ex)
                    {
                        Log("CHYBA pri " + rec.RecordName + ": " + ex.Message);
                        allOk = false;
                    }
                }

                if (allOk)
                {
                    config.LastIP = ip;
                    SaveConfig();
                    if (!Visible)
                        tray.ShowBalloonTip(4000, "Cloudflare DDNS", "DNS záznamy aktualizované na " + ip, ToolTipIcon.Info);
                }
            }
            catch (Exception ex) { Log("CHYBA: " + ex.Message); }
        }

        // ------------------------------------------------------------ Načítanie záznamov
        void FillListFromConfig()
        {
            list.Items.Clear();
            foreach (var r in config.Records)
                list.Items.Add(new RecItem { Rec = r, Label = r.ZoneName + "  —  " + r.RecordName + "  (A)" }, true);
        }

        void LoadRecordsClick()
        {
            try
            {
                if (!tokenSaved)
                {
                    string t = txtToken.Text.Trim();
                    if (t.Length == 0)
                    {
                        MessageBox.Show("Najprv vlož Cloudflare API token.", "Chýba token");
                        return;
                    }
                    SetToken(t);
                    SaveConfig();
                    txtToken.Text = "********** (token je uložený)";
                    tokenSaved = true;
                }

                statusLabel.Text = "Načítavam zóny a záznamy z Cloudflare...";
                Refresh();

                var selectedIds = new HashSet<string>();
                foreach (var r in config.Records) selectedIds.Add(r.RecordId);

                list.Items.Clear();

                var zonesResp = ApiGet("https://api.cloudflare.com/client/v4/zones?per_page=50");
                if (!(zonesResp.ContainsKey("success") && (bool)zonesResp["success"]))
                    throw new Exception("Cloudflare API vrátilo chybu pri načítaní zón.");

                foreach (Dictionary<string, object> zone in (object[])zonesResp["result"])
                {
                    string zoneId = (string)zone["id"];
                    string zoneName = (string)zone["name"];

                    var recResp = ApiGet("https://api.cloudflare.com/client/v4/zones/" + zoneId + "/dns_records?type=A&per_page=100");
                    if (!(recResp.ContainsKey("success") && (bool)recResp["success"])) continue;

                    foreach (Dictionary<string, object> rec in (object[])recResp["result"])
                    {
                        var item = new RecItem
                        {
                            Rec = new DnsRec
                            {
                                ZoneId = zoneId,
                                ZoneName = zoneName,
                                RecordId = (string)rec["id"],
                                RecordName = (string)rec["name"]
                            },
                            Label = zoneName + "  —  " + rec["name"] + "  (A, teraz: " + rec["content"] + ")"
                        };
                        list.Items.Add(item, selectedIds.Contains(item.Rec.RecordId));
                    }
                }

                statusLabel.Text = "Načítaných záznamov: " + list.Items.Count + ". Zaškrtni tie, ktoré sa majú aktualizovať.";
                Log("Načítané záznamy z Cloudflare (" + list.Items.Count + ").");
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Chyba pri komunikácii s Cloudflare.";
                Log("CHYBA: " + ex.Message);
                MessageBox.Show("Nepodarilo sa načítať údaje z Cloudflare.\n\nSkontroluj API token a internetové pripojenie.\n\nDetail: " + ex.Message, "Chyba");
            }
        }

        // ------------------------------------------------------------ Uloženie
        void SaveClick()
        {
            var sel = new List<DnsRec>();
            foreach (object o in list.CheckedItems)
                sel.Add(((RecItem)o).Rec);

            config.Records = sel;
            config.IntervalMinutes = (int)numInterval.Value;
            SaveConfig();
            SetAutoStart(chkAuto.Checked);
            timer.Interval = config.IntervalMinutes * 60000;

            Log("Nastavenia uložené. Sledovaných záznamov: " + sel.Count + ", interval: " + config.IntervalMinutes + " min.");
            statusLabel.Text = "Uložené. Sledujem " + sel.Count + " záznam(ov), kontrola každých " + config.IntervalMinutes + " min.";
        }

        // ------------------------------------------------------------ Autoštart
        bool GetAutoStart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        void SetAutoStart(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on)
                {
                    k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" -hidden");
                    Log("Automatické spúšťanie pri prihlásení: ZAPNUTÉ");
                }
                else if (k.GetValue(RunName) != null)
                {
                    k.DeleteValue(RunName);
                    Log("Automatické spúšťanie pri prihlásení: VYPNUTÉ");
                }
            }
        }
    }
}
