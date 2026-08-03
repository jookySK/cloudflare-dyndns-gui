# =====================================================================
#  Cloudflare DDNS Updater pre Windows
#  Jednoduchá aplikácia na automatickú aktualizáciu DNS záznamov
#  v Cloudflare podľa aktuálnej verejnej IP adresy.
#
#  Spustenie:  dvojklik na Spustit.bat  (alebo Run with PowerShell)
#  Parameter -Hidden = spustí sa rovno skryto do systémovej lišty
# =====================================================================
param([switch]$Hidden)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# ---------------------------------------------------------------------
#  Konfigurácia
# ---------------------------------------------------------------------
$script:ConfigDir  = Join-Path $env:APPDATA 'CloudflareDDNS'
$script:ConfigFile = Join-Path $ConfigDir 'config.json'
$script:LogFile    = Join-Path $ConfigDir 'log.txt'
if (-not (Test-Path $ConfigDir)) { New-Item -ItemType Directory -Path $ConfigDir | Out-Null }

$script:Config = [ordered]@{
    TokenEnc        = ''
    IntervalMinutes = 5
    Records         = @()
    LastIP          = ''
}

function Load-Config {
    if (Test-Path $script:ConfigFile) {
        try {
            $j = Get-Content $script:ConfigFile -Raw | ConvertFrom-Json
            if ($j.TokenEnc)        { $script:Config.TokenEnc        = $j.TokenEnc }
            if ($j.IntervalMinutes) { $script:Config.IntervalMinutes = [int]$j.IntervalMinutes }
            if ($j.LastIP)          { $script:Config.LastIP          = $j.LastIP }
            if ($j.Records)         { $script:Config.Records         = @($j.Records) }
        } catch { }
    }
}

function Save-Config {
    $script:Config | ConvertTo-Json -Depth 5 | Set-Content $script:ConfigFile -Encoding UTF8
}

function Get-Token {
    if (-not $script:Config.TokenEnc) { return $null }
    try {
        $sec  = ConvertTo-SecureString $script:Config.TokenEnc
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
        return [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
    } catch { return $null }
}

function Set-Token([string]$plain) {
    $sec = ConvertTo-SecureString $plain -AsPlainText -Force
    $script:Config.TokenEnc = ConvertFrom-SecureString $sec   # šifrované cez Windows DPAPI (len tento používateľ)
}

# ---------------------------------------------------------------------
#  Logovanie
# ---------------------------------------------------------------------
function Log([string]$msg) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'dd.MM.yyyy HH:mm:ss'), $msg
    try { Add-Content -Path $script:LogFile -Value $line -Encoding UTF8 } catch { }
    if ($script:LogBox) {
        $script:LogBox.AppendText($line + [Environment]::NewLine)
    }
}

# ---------------------------------------------------------------------
#  Cloudflare API
# ---------------------------------------------------------------------
function CF-Headers {
    $t = Get-Token
    return @{ 'Authorization' = "Bearer $t"; 'Content-Type' = 'application/json' }
}

function Get-CFZones {
    $r = Invoke-RestMethod -Uri 'https://api.cloudflare.com/client/v4/zones?per_page=50' -Headers (CF-Headers) -Method Get
    if (-not $r.success) { throw "Cloudflare API vrátilo chybu pri načítaní zón." }
    return $r.result
}

function Get-CFRecords([string]$zoneId) {
    $r = Invoke-RestMethod -Uri "https://api.cloudflare.com/client/v4/zones/$zoneId/dns_records?type=A&per_page=100" -Headers (CF-Headers) -Method Get
    if (-not $r.success) { throw "Cloudflare API vrátilo chybu pri načítaní záznamov." }
    return $r.result
}

function Update-CFRecord($rec, [string]$newIP) {
    $body = @{ content = $newIP } | ConvertTo-Json
    $uri  = "https://api.cloudflare.com/client/v4/zones/$($rec.ZoneId)/dns_records/$($rec.RecordId)"
    $r = Invoke-RestMethod -Uri $uri -Headers (CF-Headers) -Method Patch -Body $body
    return $r.success
}

# ---------------------------------------------------------------------
#  Zistenie verejnej IP (skúsi viac služieb)
# ---------------------------------------------------------------------
function Get-PublicIP {
    $services = @('https://api.ipify.org', 'https://ifconfig.me/ip', 'https://icanhazip.com')
    foreach ($s in $services) {
        try {
            $ip = (Invoke-RestMethod -Uri $s -TimeoutSec 10).ToString().Trim()
            if ($ip -match '^\d{1,3}(\.\d{1,3}){3}$') { return $ip }
        } catch { }
    }
    return $null
}

# ---------------------------------------------------------------------
#  Hlavná kontrola + aktualizácia
# ---------------------------------------------------------------------
function Check-AndUpdate([switch]$Force) {
    $ip = Get-PublicIP
    if (-not $ip) { Log "CHYBA: Nepodarilo sa zistiť verejnú IP adresu."; return }

    $script:StatusLabel.Text = "Verejná IP: $ip  |  posledná kontrola: $(Get-Date -Format 'HH:mm:ss')"

    if (-not $Force -and $ip -eq $script:Config.LastIP) {
        Log "IP sa nezmenila ($ip) – nič netreba aktualizovať."
        return
    }

    if ($ip -ne $script:Config.LastIP) { Log "Zistená zmena IP: '$($script:Config.LastIP)' -> '$ip'" }

    $selected = @($script:Config.Records)
    if ($selected.Count -eq 0) { Log "Nie sú vybrané žiadne záznamy na aktualizáciu."; return }

    $allOk = $true
    foreach ($rec in $selected) {
        try {
            $ok = Update-CFRecord $rec $ip
            if ($ok) { Log "AKTUALIZOVANÉ: $($rec.RecordName) -> $ip" }
            else     { Log "CHYBA: $($rec.RecordName) sa nepodarilo aktualizovať."; $allOk = $false }
        } catch {
            Log "CHYBA pri $($rec.RecordName): $($_.Exception.Message)"
            $allOk = $false
        }
    }

    if ($allOk) {
        $script:Config.LastIP = $ip
        Save-Config
        if ($script:Tray -and -not $script:Form.Visible) {
            $script:Tray.ShowBalloonTip(4000, 'Cloudflare DDNS', "DNS záznamy aktualizované na $ip", [System.Windows.Forms.ToolTipIcon]::Info)
        }
    }
}

# ---------------------------------------------------------------------
#  Automatické spúšťanie pri prihlásení
# ---------------------------------------------------------------------
$script:RunKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$script:RunName = 'CloudflareDDNS'
$script:SelfPath = $MyInvocation.MyCommand.Path

function Get-AutoStart { (Get-ItemProperty -Path $script:RunKey -Name $script:RunName -ErrorAction SilentlyContinue) -ne $null }
function Set-AutoStart([bool]$on) {
    if ($on) {
        $cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$($script:SelfPath)`" -Hidden"
        Set-ItemProperty -Path $script:RunKey -Name $script:RunName -Value $cmd
        Log "Automatické spúšťanie pri prihlásení: ZAPNUTÉ"
    } else {
        Remove-ItemProperty -Path $script:RunKey -Name $script:RunName -ErrorAction SilentlyContinue
        Log "Automatické spúšťanie pri prihlásení: VYPNUTÉ"
    }
}

# ---------------------------------------------------------------------
#  GUI
# ---------------------------------------------------------------------
Load-Config

$script:Form = New-Object System.Windows.Forms.Form
$Form.Text          = 'Cloudflare DDNS Updater'
$Form.Size          = New-Object System.Drawing.Size(640, 720)
$Form.StartPosition = 'CenterScreen'
$Form.FormBorderStyle = 'FixedSingle'
$Form.MaximizeBox   = $false

$font = New-Object System.Drawing.Font('Segoe UI', 9.5)
$Form.Font = $font

$y = 15

# --- API token ---
$lblToken = New-Object System.Windows.Forms.Label
$lblToken.Text = '1. Cloudflare API token (s právom Zone.DNS - Edit):'
$lblToken.Location = New-Object System.Drawing.Point(15, $y)
$lblToken.AutoSize = $true
$Form.Controls.Add($lblToken)
$y += 25

$txtToken = New-Object System.Windows.Forms.TextBox
$txtToken.Location = New-Object System.Drawing.Point(15, $y)
$txtToken.Size = New-Object System.Drawing.Size(440, 25)
$txtToken.UseSystemPasswordChar = $true
if ($script:Config.TokenEnc) { $txtToken.Text = '********** (token je uložený)' ; $txtToken.Tag = 'saved' }
$txtToken.add_TextChanged({ $txtToken.Tag = 'dirty' })
$Form.Controls.Add($txtToken)

$btnLoad = New-Object System.Windows.Forms.Button
$btnLoad.Text = 'Načítať záznamy'
$btnLoad.Location = New-Object System.Drawing.Point(465, ($y - 1))
$btnLoad.Size = New-Object System.Drawing.Size(145, 27)
$Form.Controls.Add($btnLoad)
$y += 40

# --- zoznam záznamov ---
$lblRec = New-Object System.Windows.Forms.Label
$lblRec.Text = '2. Vyber záznamy, ktorým sa má aktualizovať IP adresa:'
$lblRec.Location = New-Object System.Drawing.Point(15, $y)
$lblRec.AutoSize = $true
$Form.Controls.Add($lblRec)
$y += 25

$script:List = New-Object System.Windows.Forms.CheckedListBox
$List.Location = New-Object System.Drawing.Point(15, $y)
$List.Size = New-Object System.Drawing.Size(595, 200)
$List.CheckOnClick = $true
$Form.Controls.Add($List)
$y += 210

$script:ListData = @()   # objekty prislúchajúce riadkom v zozname

function Fill-ListFromConfig {
    $List.Items.Clear()
    $script:ListData = @()
    foreach ($r in $script:Config.Records) {
        $idx = $List.Items.Add("$($r.ZoneName)  —  $($r.RecordName)  (A)", $true)
        $script:ListData += ,$r
    }
}
Fill-ListFromConfig

# --- interval ---
$lblInt = New-Object System.Windows.Forms.Label
$lblInt.Text = '3. Kontrolovať IP adresu každých'
$lblInt.Location = New-Object System.Drawing.Point(15, ($y + 4))
$lblInt.AutoSize = $true
$Form.Controls.Add($lblInt)

$numInt = New-Object System.Windows.Forms.NumericUpDown
$numInt.Location = New-Object System.Drawing.Point(230, $y)
$numInt.Size = New-Object System.Drawing.Size(60, 25)
$numInt.Minimum = 1
$numInt.Maximum = 1440
$numInt.Value = [Math]::Max(1, [int]$script:Config.IntervalMinutes)
$Form.Controls.Add($numInt)

$lblMin = New-Object System.Windows.Forms.Label
$lblMin.Text = 'minút'
$lblMin.Location = New-Object System.Drawing.Point(295, ($y + 4))
$lblMin.AutoSize = $true
$Form.Controls.Add($lblMin)
$y += 35

# --- autostart ---
$chkAuto = New-Object System.Windows.Forms.CheckBox
$chkAuto.Text = 'Spúšťať automaticky pri prihlásení do Windows (skryto, do lišty)'
$chkAuto.Location = New-Object System.Drawing.Point(15, $y)
$chkAuto.AutoSize = $true
$chkAuto.Checked = Get-AutoStart
$Form.Controls.Add($chkAuto)
$y += 35

# --- tlačidlá ---
$btnSave = New-Object System.Windows.Forms.Button
$btnSave.Text = 'Uložiť nastavenia'
$btnSave.Location = New-Object System.Drawing.Point(15, $y)
$btnSave.Size = New-Object System.Drawing.Size(180, 32)
$Form.Controls.Add($btnSave)

$btnNow = New-Object System.Windows.Forms.Button
$btnNow.Text = 'Skontrolovať a aktualizovať teraz'
$btnNow.Location = New-Object System.Drawing.Point(205, $y)
$btnNow.Size = New-Object System.Drawing.Size(230, 32)
$Form.Controls.Add($btnNow)

$btnHide = New-Object System.Windows.Forms.Button
$btnHide.Text = 'Skryť do lišty'
$btnHide.Location = New-Object System.Drawing.Point(445, $y)
$btnHide.Size = New-Object System.Drawing.Size(165, 32)
$Form.Controls.Add($btnHide)
$y += 45

# --- stav + log ---
$script:StatusLabel = New-Object System.Windows.Forms.Label
$StatusLabel.Text = 'Pripravené.'
$StatusLabel.Location = New-Object System.Drawing.Point(15, $y)
$StatusLabel.Size = New-Object System.Drawing.Size(595, 20)
$Form.Controls.Add($StatusLabel)
$y += 25

$script:LogBox = New-Object System.Windows.Forms.TextBox
$LogBox.Location = New-Object System.Drawing.Point(15, $y)
$LogBox.Size = New-Object System.Drawing.Size(595, 140)
$LogBox.Multiline = $true
$LogBox.ScrollBars = 'Vertical'
$LogBox.ReadOnly = $true
$Form.Controls.Add($LogBox)

# ---------------------------------------------------------------------
#  Systémová lišta (tray)
# ---------------------------------------------------------------------
$script:Tray = New-Object System.Windows.Forms.NotifyIcon
$Tray.Icon = [System.Drawing.SystemIcons]::Application
$Tray.Text = 'Cloudflare DDNS Updater'
$Tray.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$miOpen  = $menu.Items.Add('Otvoriť nastavenia')
$miCheck = $menu.Items.Add('Skontrolovať teraz')
$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator)) | Out-Null
$miExit  = $menu.Items.Add('Ukončiť program')
$Tray.ContextMenuStrip = $menu

$script:ReallyExit = $false

$miOpen.add_Click({ $Form.Show(); $Form.WindowState = 'Normal'; $Form.Activate() })
$Tray.add_DoubleClick({ $Form.Show(); $Form.WindowState = 'Normal'; $Form.Activate() })
$miCheck.add_Click({ Check-AndUpdate -Force })
$miExit.add_Click({ $script:ReallyExit = $true; $Tray.Visible = $false; $Form.Close(); [System.Windows.Forms.Application]::Exit() })

# ---------------------------------------------------------------------
#  Časovač
# ---------------------------------------------------------------------
$script:Timer = New-Object System.Windows.Forms.Timer
$Timer.Interval = [int]$script:Config.IntervalMinutes * 60000
$Timer.add_Tick({ Check-AndUpdate })
$Timer.Start()

# ---------------------------------------------------------------------
#  Udalosti tlačidiel
# ---------------------------------------------------------------------
$btnLoad.add_Click({
    try {
        if ($txtToken.Tag -ne 'saved') {
            if (-not $txtToken.Text.Trim()) {
                [System.Windows.Forms.MessageBox]::Show('Najprv vlož Cloudflare API token.', 'Chýba token') | Out-Null
                return
            }
            Set-Token $txtToken.Text.Trim()
            Save-Config
            $txtToken.Text = '********** (token je uložený)'   # TextChanged nastaví 'dirty'...
            $txtToken.Tag = 'saved'                             # ...preto Tag nastavujeme až tu
        }

        $StatusLabel.Text = 'Načítavam zóny a záznamy z Cloudflare...'
        $Form.Refresh()

        $List.Items.Clear()
        $script:ListData = @()

        $selectedIds = @($script:Config.Records | ForEach-Object { $_.RecordId })

        foreach ($zone in (Get-CFZones)) {
            foreach ($rec in (Get-CFRecords $zone.id)) {
                $obj = [PSCustomObject]@{
                    ZoneId     = $zone.id
                    ZoneName   = $zone.name
                    RecordId   = $rec.id
                    RecordName = $rec.name
                }
                $checked = $selectedIds -contains $rec.id
                $List.Items.Add("$($zone.name)  —  $($rec.name)  (A, teraz: $($rec.content))", $checked) | Out-Null
                $script:ListData += ,$obj
            }
        }
        $StatusLabel.Text = "Načítaných záznamov: $($List.Items.Count). Zaškrtni tie, ktoré sa majú aktualizovať."
        Log "Načítané záznamy z Cloudflare ($($List.Items.Count))."
    } catch {
        $StatusLabel.Text = 'Chyba pri komunikácii s Cloudflare.'
        Log "CHYBA: $($_.Exception.Message)"
        [System.Windows.Forms.MessageBox]::Show("Nepodarilo sa načítať údaje z Cloudflare.`n`nSkontroluj API token a internetové pripojenie.`n`nDetail: $($_.Exception.Message)", 'Chyba') | Out-Null
    }
})

$btnSave.add_Click({
    $sel = @()
    for ($i = 0; $i -lt $List.Items.Count; $i++) {
        if ($List.GetItemChecked($i)) { $sel += ,$script:ListData[$i] }
    }
    $script:Config.Records = $sel
    $script:Config.IntervalMinutes = [int]$numInt.Value
    Save-Config
    Set-AutoStart $chkAuto.Checked
    $Timer.Interval = [int]$numInt.Value * 60000
    Log "Nastavenia uložené. Sledovaných záznamov: $($sel.Count), interval: $($numInt.Value) min."
    $StatusLabel.Text = "Uložené. Sledujem $($sel.Count) záznam(ov), kontrola každých $($numInt.Value) min."
})

$btnNow.add_Click({ Check-AndUpdate -Force })

$btnHide.add_Click({
    $Form.Hide()
    $Tray.ShowBalloonTip(3000, 'Cloudflare DDNS', 'Program beží ďalej na pozadí. Nájdeš ho v systémovej lište.', [System.Windows.Forms.ToolTipIcon]::Info)
})

$Form.add_FormClosing({
    param($s, $e)
    if (-not $script:ReallyExit) {
        $e.Cancel = $true
        $Form.Hide()
        $Tray.ShowBalloonTip(3000, 'Cloudflare DDNS', 'Program beží ďalej na pozadí. Ukončiť ho môžeš cez pravý klik na ikonu v lište.', [System.Windows.Forms.ToolTipIcon]::Info)
    }
})

# ---------------------------------------------------------------------
#  Štart
# ---------------------------------------------------------------------
Log '--- Program spustený ---'
if ($Hidden) {
    $Form.WindowState = 'Minimized'
    $Form.ShowInTaskbar = $false
    $Form.add_Shown({ $Form.Hide(); $Form.ShowInTaskbar = $true; $Form.WindowState = 'Normal' })
    if ($script:Config.Records.Count -gt 0) { Check-AndUpdate }
} else {
    if ($script:Config.Records.Count -gt 0) { Check-AndUpdate }
}

[System.Windows.Forms.Application]::Run($Form)
$Tray.Visible = $false
