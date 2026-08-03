// =====================================================================
//  Cloudflare DDNS Updater for Windows — single standalone .exe
//  Keeps selected Cloudflare DNS records updated with your public IP.
//  Argument -hidden = start minimized straight to the system tray.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Cloudflare DDNS Updater")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace CloudflareDDNS
{
    // ------------------------------------------------------------ Localization
    static class L
    {
        public static string Lang = "sk";   // "sk" | "en"

        static readonly Dictionary<string, string[]> S = new Dictionary<string, string[]>
        {
            // key                         [ sk, en ]
            { "Title",          new[]{ "Cloudflare DDNS Updater", "Cloudflare DDNS Updater" } },
            { "Step1",          new[]{ "1. Cloudflare API token (s právom Zone.DNS – Edit):", "1. Cloudflare API token (with Zone.DNS – Edit permission):" } },
            { "LoadRecords",    new[]{ "Načítať záznamy", "Load records" } },
            { "Step2",          new[]{ "2. Vyber záznamy, ktorým sa má aktualizovať IP adresa:", "2. Select the records that should follow your IP address:" } },
            { "Step3",          new[]{ "3. Kontrolovať IP adresu každých", "3. Check the IP address every" } },
            { "Minutes",        new[]{ "minút", "minutes" } },
            { "AutoStart",      new[]{ "Spúšťať automaticky pri prihlásení do Windows (skryto, do lišty)", "Start automatically at Windows sign-in (hidden, in the tray)" } },
            { "Save",           new[]{ "Uložiť nastavenia", "Save settings" } },
            { "CheckNow",       new[]{ "Skontrolovať a aktualizovať teraz", "Check and update now" } },
            { "HideTray",       new[]{ "Skryť do lišty", "Hide to tray" } },
            { "LangLabel",      new[]{ "Jazyk:", "Language:" } },
            { "Ready",          new[]{ "Pripravené.", "Ready." } },
            { "TokenSaved",     new[]{ "********** (token je uložený)", "********** (token is saved)" } },
            { "MissingToken",   new[]{ "Najprv vlož Cloudflare API token.", "Please enter your Cloudflare API token first." } },
            { "MissingTokenT",  new[]{ "Chýba token", "Missing token" } },
            { "Loading",        new[]{ "Načítavam zóny a záznamy z Cloudflare...", "Loading zones and records from Cloudflare..." } },
            { "LoadedStatus",   new[]{ "Načítaných záznamov: {0}. Zaškrtni tie, ktoré sa majú aktualizovať.", "Records loaded: {0}. Tick the ones that should be updated." } },
            { "LoadedLog",      new[]{ "Načítané záznamy z Cloudflare ({0}).", "Loaded records from Cloudflare ({0})." } },
            { "LoadErrStatus",  new[]{ "Chyba pri komunikácii s Cloudflare.", "Error communicating with Cloudflare." } },
            { "LoadErrMsg",     new[]{ "Nepodarilo sa načítať údaje z Cloudflare.\n\nSkontroluj API token a internetové pripojenie.\n\nDetail: {0}", "Failed to load data from Cloudflare.\n\nCheck your API token and internet connection.\n\nDetails: {0}" } },
            { "ErrTitle",       new[]{ "Chyba", "Error" } },
            { "ZonesApiErr",    new[]{ "Cloudflare API vrátilo chybu pri načítaní zón.", "Cloudflare API returned an error while loading zones." } },
            { "StatusIp",       new[]{ "Verejná IP: {0}  |  posledná kontrola: {1}", "Public IP: {0}  |  last check: {1}" } },
            { "IpFail",         new[]{ "CHYBA: Nepodarilo sa zistiť verejnú IP adresu.", "ERROR: Could not determine the public IP address." } },
            { "IpUnchanged",    new[]{ "IP sa nezmenila ({0}) – nič netreba aktualizovať.", "IP unchanged ({0}) – nothing to update." } },
            { "IpChanged",      new[]{ "Zistená zmena IP: '{0}' -> '{1}'", "IP change detected: '{0}' -> '{1}'" } },
            { "NoRecords",      new[]{ "Nie sú vybrané žiadne záznamy na aktualizáciu.", "No records selected for updating." } },
            { "NoToken",        new[]{ "CHYBA: Chýba API token — otvor nastavenia a vlož ho.", "ERROR: API token missing — open settings and enter it." } },
            { "Updated",        new[]{ "AKTUALIZOVANÉ: {0} -> {1}", "UPDATED: {0} -> {1}" } },
            { "UpdateFail",     new[]{ "CHYBA: {0} sa nepodarilo aktualizovať.", "ERROR: failed to update {0}." } },
            { "ErrAt",          new[]{ "CHYBA pri {0}: {1}", "ERROR at {0}: {1}" } },
            { "BalloonUpdated", new[]{ "DNS záznamy aktualizované na {0}", "DNS records updated to {0}" } },
            { "BalloonHidden",  new[]{ "Program beží ďalej na pozadí. Nájdeš ho v systémovej lište.", "The app keeps running in the background. You'll find it in the system tray." } },
            { "SavedLog",       new[]{ "Nastavenia uložené. Sledovaných záznamov: {0}, interval: {1} min.", "Settings saved. Monitored records: {0}, interval: {1} min." } },
            { "SavedStatus",    new[]{ "Uložené. Sledujem {0} záznam(ov), kontrola každých {1} min.", "Saved. Monitoring {0} record(s), checking every {1} min." } },
            { "AutoOn",         new[]{ "Automatické spúšťanie pri prihlásení: ZAPNUTÉ", "Autostart at sign-in: ENABLED" } },
            { "AutoOff",        new[]{ "Automatické spúšťanie pri prihlásení: VYPNUTÉ", "Autostart at sign-in: DISABLED" } },
            { "Started",        new[]{ "--- Program spustený ---", "--- Program started ---" } },
            { "TrayOpen",       new[]{ "Otvoriť nastavenia", "Open settings" } },
            { "TrayCheck",      new[]{ "Skontrolovať teraz", "Check now" } },
            { "TrayExit",       new[]{ "Ukončiť program", "Exit" } },
            { "AlreadyRun",     new[]{ "Cloudflare DDNS Updater už beží — pozri ikonu v systémovej lište.", "Cloudflare DDNS Updater is already running — check the system tray icon." } },
            { "AlreadyRunT",    new[]{ "Už spustené", "Already running" } },
            { "GenErr",         new[]{ "CHYBA: {0}", "ERROR: {0}" } },
        };

        public static string T(string key) { return S[key][Lang == "en" ? 1 : 0]; }
        public static string F(string key, params object[] args) { return string.Format(T(key), args); }
    }

    // ------------------------------------------------------------ Flag bitmaps
    static class Flags
    {
        public static Bitmap SK(int w, int h)
        {
            var b = new Bitmap(w, h);
            using (var g = Graphics.FromImage(b))
            {
                g.FillRectangle(Brushes.White, 0, 0, w, h);
                using (var blue = new SolidBrush(Color.FromArgb(11, 78, 162)))
                    g.FillRectangle(blue, 0, h / 3f, w, h / 3f);
                using (var red = new SolidBrush(Color.FromArgb(238, 28, 37)))
                    g.FillRectangle(red, 0, 2 * h / 3f, w, h - 2 * h / 3f);
                g.DrawRectangle(Pens.Gray, 0, 0, w - 1, h - 1);
            }
            return b;
        }

        public static Bitmap EN(int w, int h)
        {
            var b = new Bitmap(w, h);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var blue = Color.FromArgb(1, 33, 105);
                var red = Color.FromArgb(200, 16, 46);
                using (var br = new SolidBrush(blue)) g.FillRectangle(br, 0, 0, w, h);
                using (var pw = new Pen(Color.White, h / 3f)) { g.DrawLine(pw, 0, 0, w, h); g.DrawLine(pw, 0, h, w, 0); }
                using (var pr = new Pen(red, h / 9f)) { g.DrawLine(pr, 0, 0, w, h); g.DrawLine(pr, 0, h, w, 0); }
                using (var pw2 = new Pen(Color.White, h / 2.2f)) { g.DrawLine(pw2, w / 2f, 0, w / 2f, h); g.DrawLine(pw2, 0, h / 2f, w, h / 2f); }
                using (var pr2 = new Pen(red, h / 3.6f)) { g.DrawLine(pr2, w / 2f, 0, w / 2f, h); g.DrawLine(pr2, 0, h / 2f, w, h / 2f); }
                g.DrawRectangle(Pens.Gray, 0, 0, w - 1, h - 1);
            }
            return b;
        }
    }

    // ------------------------------------------------------------ Language picker dialog
    public class LangDialog : Form
    {
        public string Chosen = "sk";

        public LangDialog()
        {
            Text = "Vyberte jazyk / Choose your language";
            Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(360, 100);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var bSk = new Button
            {
                Text = "  Slovensky",
                Image = Flags.SK(36, 24),
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(20, 25),
                Size = new Size(155, 50)
            };
            var bEn = new Button
            {
                Text = "  English",
                Image = Flags.EN(36, 24),
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(185, 25),
                Size = new Size(155, 50)
            };
            bSk.Click += delegate { Chosen = "sk"; DialogResult = DialogResult.OK; };
            bEn.Click += delegate { Chosen = "en"; DialogResult = DialogResult.OK; };
            Controls.Add(bSk);
            Controls.Add(bEn);
        }
    }

    // ------------------------------------------------------------ Model
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
        public string Language { get; set; }
        public List<DnsRec> Records { get; set; }

        public Config()
        {
            TokenEnc = "";
            IntervalMinutes = 5;
            LastIP = "";
            Language = "";
            Records = new List<DnsRec>();
        }
    }

    class RecItem
    {
        public DnsRec Rec;
        public string Label;
        public override string ToString() { return Label; }
    }

    // ------------------------------------------------------------ Entry point
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // pre-read language so even the "already running" message is localized
            try
            {
                string cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudflareDDNS", "config.json");
                if (File.Exists(cfg))
                {
                    var c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(cfg, Encoding.UTF8));
                    if (c != null && c.Language == "en") L.Lang = "en";
                }
            }
            catch { }

            bool createdNew;
            using (var mutex = new Mutex(true, "CloudflareDDNS_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(L.T("AlreadyRun"), L.T("AlreadyRunT"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    // ------------------------------------------------------------ Main window
    public class MainForm : Form
    {
        readonly string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CloudflareDDNS");
        string ConfigFile { get { return Path.Combine(configDir, "config.json"); } }
        string LogFile { get { return Path.Combine(configDir, "log.txt"); } }

        Config config = new Config();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();

        Label lblToken, lblRec, lblInt, lblMin, lblLang, statusLabel;
        Button btnLoad, btnSave, btnNow, btnHide;
        TextBox txtToken, logBox;
        CheckedListBox list;
        NumericUpDown numInterval;
        CheckBox chkAuto;
        ComboBox cmbLang;
        NotifyIcon tray;
        ToolStripItem miOpen, miCheck, miExit;
        System.Windows.Forms.Timer timer;

        bool tokenSaved;
        bool suppressTokenDirty;
        bool initializing = true;
        bool reallyExit;
        readonly bool startHidden;

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "CloudflareDDNS";

        public MainForm(bool hidden)
        {
            startHidden = hidden;
            Directory.CreateDirectory(configDir);
            LoadConfig();

            // first run: ask for language (with flags), then remember it
            if (config.Language != "sk" && config.Language != "en")
            {
                using (var d = new LangDialog())
                {
                    d.ShowDialog();
                    config.Language = d.Chosen;
                    SaveConfig();
                }
            }
            L.Lang = config.Language;

            BuildUi();
            BuildTray();
            ApplyLanguage();
            initializing = false;

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
            Log(L.T("Started"));
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
            Font = new Font("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(624, 660);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int y = 15;

            lblToken = new Label { Location = new Point(15, y), AutoSize = true };
            Controls.Add(lblToken); y += 25;

            txtToken = new TextBox { Location = new Point(15, y), Size = new Size(440, 25), UseSystemPasswordChar = true };
            txtToken.TextChanged += delegate { if (!suppressTokenDirty) tokenSaved = false; };
            if (!string.IsNullOrEmpty(config.TokenEnc)) tokenSaved = true;
            Controls.Add(txtToken);

            btnLoad = new Button { Location = new Point(465, y - 1), Size = new Size(145, 27) };
            btnLoad.Click += delegate { LoadRecordsClick(); };
            Controls.Add(btnLoad); y += 40;

            lblRec = new Label { Location = new Point(15, y), AutoSize = true };
            Controls.Add(lblRec); y += 25;

            list = new CheckedListBox { Location = new Point(15, y), Size = new Size(595, 200), CheckOnClick = true };
            Controls.Add(list); y += 210;
            FillListFromConfig();

            lblInt = new Label { Location = new Point(15, y + 4), AutoSize = true };
            Controls.Add(lblInt);
            numInterval = new NumericUpDown { Location = new Point(255, y), Size = new Size(60, 25), Minimum = 1, Maximum = 1440, Value = Math.Max(1, config.IntervalMinutes) };
            Controls.Add(numInterval);
            lblMin = new Label { Location = new Point(320, y + 4), AutoSize = true };
            Controls.Add(lblMin);

            lblLang = new Label { Location = new Point(425, y + 4), AutoSize = true };
            Controls.Add(lblLang);
            cmbLang = new ComboBox { Location = new Point(495, y), Size = new Size(115, 25), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbLang.Items.Add("Slovensky");
            cmbLang.Items.Add("English");
            cmbLang.SelectedIndex = (config.Language == "en") ? 1 : 0;
            cmbLang.SelectedIndexChanged += delegate
            {
                if (initializing) return;
                config.Language = (cmbLang.SelectedIndex == 1) ? "en" : "sk";
                L.Lang = config.Language;
                SaveConfig();
                ApplyLanguage();
            };
            Controls.Add(cmbLang); y += 35;

            chkAuto = new CheckBox { Location = new Point(15, y), AutoSize = true, Checked = GetAutoStart() };
            Controls.Add(chkAuto); y += 35;

            btnSave = new Button { Location = new Point(15, y), Size = new Size(180, 32) };
            btnSave.Click += delegate { SaveClick(); };
            Controls.Add(btnSave);

            btnNow = new Button { Location = new Point(205, y), Size = new Size(230, 32) };
            btnNow.Click += delegate { CheckAndUpdate(true); };
            Controls.Add(btnNow);

            btnHide = new Button { Location = new Point(445, y), Size = new Size(165, 32) };
            btnHide.Click += delegate { HideToTray(); };
            Controls.Add(btnHide); y += 45;

            statusLabel = new Label { Location = new Point(15, y), Size = new Size(595, 20) };
            Controls.Add(statusLabel); y += 25;

            logBox = new TextBox { Location = new Point(15, y), Size = new Size(595, 140), Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            Controls.Add(logBox);
        }

        void BuildTray()
        {
            tray = new NotifyIcon();
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { tray.Icon = SystemIcons.Application; }
            tray.Visible = true;

            var menu = new ContextMenuStrip();
            miOpen = menu.Items.Add("");
            miCheck = menu.Items.Add("");
            menu.Items.Add(new ToolStripSeparator());
            miExit = menu.Items.Add("");
            tray.ContextMenuStrip = menu;

            miOpen.Click += delegate { ShowFromTray(); };
            tray.DoubleClick += delegate { ShowFromTray(); };
            miCheck.Click += delegate { CheckAndUpdate(true); };
            miExit.Click += delegate { reallyExit = true; tray.Visible = false; Close(); };
        }

        void ApplyLanguage()
        {
            Text = L.T("Title");
            lblToken.Text = L.T("Step1");
            btnLoad.Text = L.T("LoadRecords");
            lblRec.Text = L.T("Step2");
            lblInt.Text = L.T("Step3");
            lblMin.Text = L.T("Minutes");
            lblLang.Text = L.T("LangLabel");
            chkAuto.Text = L.T("AutoStart");
            btnSave.Text = L.T("Save");
            btnNow.Text = L.T("CheckNow");
            btnHide.Text = L.T("HideTray");
            statusLabel.Text = L.T("Ready");
            tray.Text = L.T("Title");
            miOpen.Text = L.T("TrayOpen");
            miCheck.Text = L.T("TrayCheck");
            miExit.Text = L.T("TrayExit");

            if (tokenSaved)
            {
                suppressTokenDirty = true;
                txtToken.Text = L.T("TokenSaved");
                suppressTokenDirty = false;
            }
        }

        void ShowFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        void HideToTray()
        {
            Hide();
            tray.ShowBalloonTip(3000, L.T("Title"), L.T("BalloonHidden"), ToolTipIcon.Info);
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
                        if (config.Language == null) config.Language = "";
                        if (config.IntervalMinutes < 1) config.IntervalMinutes = 5;
                        // token from the older PowerShell version has a different format — verify it decrypts
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

        // ------------------------------------------------------------ Public IP
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

        // ------------------------------------------------------------ Main logic
        void CheckAndUpdate(bool force)
        {
            try
            {
                string ip = GetPublicIP();
                if (ip == null) { Log(L.T("IpFail")); return; }

                statusLabel.Text = L.F("StatusIp", ip, DateTime.Now.ToString("HH:mm:ss"));

                if (!force && ip == config.LastIP)
                {
                    Log(L.F("IpUnchanged", ip));
                    return;
                }
                if (ip != config.LastIP)
                    Log(L.F("IpChanged", config.LastIP, ip));

                if (config.Records.Count == 0) { Log(L.T("NoRecords")); return; }
                if (GetToken() == null) { Log(L.T("NoToken")); return; }

                bool allOk = true;
                foreach (var rec in config.Records)
                {
                    try
                    {
                        string body = json.Serialize(new Dictionary<string, object> { { "content", ip } });
                        string url = "https://api.cloudflare.com/client/v4/zones/" + rec.ZoneId + "/dns_records/" + rec.RecordId;
                        var resp = (Dictionary<string, object>)json.DeserializeObject(Http("PATCH", url, body));
                        if (resp.ContainsKey("success") && (bool)resp["success"])
                            Log(L.F("Updated", rec.RecordName, ip));
                        else { Log(L.F("UpdateFail", rec.RecordName)); allOk = false; }
                    }
                    catch (Exception ex)
                    {
                        Log(L.F("ErrAt", rec.RecordName, ex.Message));
                        allOk = false;
                    }
                }

                if (allOk)
                {
                    config.LastIP = ip;
                    SaveConfig();
                    if (!Visible)
                        tray.ShowBalloonTip(4000, L.T("Title"), L.F("BalloonUpdated", ip), ToolTipIcon.Info);
                }
            }
            catch (Exception ex) { Log(L.F("GenErr", ex.Message)); }
        }

        // ------------------------------------------------------------ Loading records
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
                        MessageBox.Show(L.T("MissingToken"), L.T("MissingTokenT"));
                        return;
                    }
                    SetToken(t);
                    SaveConfig();
                    suppressTokenDirty = true;
                    txtToken.Text = L.T("TokenSaved");
                    suppressTokenDirty = false;
                    tokenSaved = true;
                }

                statusLabel.Text = L.T("Loading");
                Refresh();

                var selectedIds = new HashSet<string>();
                foreach (var r in config.Records) selectedIds.Add(r.RecordId);

                list.Items.Clear();

                var zonesResp = ApiGet("https://api.cloudflare.com/client/v4/zones?per_page=50");
                if (!(zonesResp.ContainsKey("success") && (bool)zonesResp["success"]))
                    throw new Exception(L.T("ZonesApiErr"));

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
                            Label = zoneName + "  —  " + rec["name"] + "  (A, " + rec["content"] + ")"
                        };
                        list.Items.Add(item, selectedIds.Contains(item.Rec.RecordId));
                    }
                }

                statusLabel.Text = L.F("LoadedStatus", list.Items.Count);
                Log(L.F("LoadedLog", list.Items.Count));
            }
            catch (Exception ex)
            {
                statusLabel.Text = L.T("LoadErrStatus");
                Log(L.F("GenErr", ex.Message));
                MessageBox.Show(L.F("LoadErrMsg", ex.Message), L.T("ErrTitle"));
            }
        }

        // ------------------------------------------------------------ Save
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

            Log(L.F("SavedLog", sel.Count, config.IntervalMinutes));
            statusLabel.Text = L.F("SavedStatus", sel.Count, config.IntervalMinutes);
        }

        // ------------------------------------------------------------ Autostart
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
                    Log(L.T("AutoOn"));
                }
                else if (k.GetValue(RunName) != null)
                {
                    k.DeleteValue(RunName);
                    Log(L.T("AutoOff"));
                }
            }
        }
    }
}
