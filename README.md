# cloudflare-dyndns-gui

![Build](https://github.com/jookySK/cloudflare-dynddns-gui/actions/workflows/build.yml/badge.svg)

Simple Windows tray app that keeps your Cloudflare DNS records updated with your dynamic IP. GUI setup, no dependencies, single `.exe`.

Most Cloudflare DDNS updaters are command-line scripts that require editing config files by hand. This one is different: you paste your API token, it loads all your zones and A records, you tick the ones you want to keep updated, set the check interval — and it quietly runs in the system tray from then on.

## Features

- ✅ **Single portable `.exe`** — no installer, no runtime to download (uses the .NET Framework built into Windows 10/11)
- 🌍 **English & Slovak UI** — pick your language on first launch (with flags 🙂), switchable anytime in settings
- 🖱️ **Full GUI setup** — loads your zones and DNS records straight from Cloudflare, just tick the ones to update
- ⏱️ **Configurable interval** — checks your public IP every 1–1440 minutes
- 🔔 **Runs in the system tray** — closing the window just hides it; balloon notification when records get updated
- 🚀 **Start with Windows** — one checkbox adds it to autostart (launches hidden, straight to the tray)
- 🔐 **Token stored encrypted** — the API token is protected with Windows DPAPI (tied to your user account), never saved in plain text
- 🪶 **Gentle updates** — only the IP (`content`) field is patched; proxy status (orange cloud) and TTL stay untouched
- 📜 **Log file** — every check and update is logged to `%APPDATA%\CloudflareDDNS\log.txt`
- 🔁 **Resilient IP detection** — tries multiple services (ipify, ifconfig.me, icanhazip) in case one is down
- 1️⃣ **Single-instance guard** — won't accidentally run twice

## Getting started

### 1. Create a Cloudflare API token

1. Log in to the [Cloudflare dashboard](https://dash.cloudflare.com/)
2. Go to **My Profile → API Tokens → Create Token**
3. Use the **Edit zone DNS** template
4. Under *Zone Resources*, select the zones you want to manage (or *All zones*)
5. Create the token and copy it

The token only needs the **Zone.DNS – Edit** permission. Do **not** use your Global API Key.

### 2. Run the app

1. Download `CloudflareDDNS.exe` from [Releases](../../releases) (or build it yourself, see below)
2. Run it — since the binary is unsigned, Windows SmartScreen may warn you on first launch: click **More info → Run anyway**
3. Paste your API token and click **Načítať záznamy** (Load records)
4. Tick the records to keep updated, set the interval, click **Uložiť nastavenia** (Save settings)
5. Optionally tick the autostart checkbox so it launches with Windows

That's it. The app keeps running in the tray; right-click the tray icon to open settings, force a check, or exit.

## Configuration & data

Everything lives in `%APPDATA%\CloudflareDDNS\`:

| File | Purpose |
|---|---|
| `config.json` | Selected records, interval, last known IP, encrypted token |
| `log.txt` | History of checks and updates |

The token in `config.json` is encrypted with DPAPI and can only be decrypted by the same Windows user account on the same machine — it is safe to back the file up, but it won't work if copied to another computer (you'd just re-enter the token there).

## Building from source

No IDE needed — the C# compiler ships with the .NET Framework on every Windows machine. From the repo folder, run in **Command Prompt**:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  /target:winexe /out:CloudflareDDNS.exe /win32icon:app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll ^
  Program.cs
```

On Linux/macOS you can build the same Windows binary with [Mono](https://www.mono-project.com/):

```sh
mcs -target:winexe -out:CloudflareDDNS.exe -win32icon:app.ico -codepage:utf8 \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll \
  -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll -r:System.Security.dll \
  Program.cs
```

The whole app is a single file, [`Program.cs`](Program.cs) (~600 lines), so it's easy to audit before running.

## Automated builds (GitHub Actions)

Every push to `main` is compiled automatically by the [build workflow](.github/workflows/build.yml) on a clean Windows runner — the resulting exe is available as a build artifact on each workflow run, so you can verify the binary really comes from the source in this repo.

To publish a new version: create a tag starting with `v` (e.g. `v1.1.0`) or draft a new Release with such a tag — the workflow compiles the exe and attaches it to the Release automatically.

## How it works

Every *N* minutes the app asks a public "what is my IP" service for your current IPv4 address. If it differs from the last known one, it sends a `PATCH` request to the [Cloudflare API v4](https://developers.cloudflare.com/api/) for each selected DNS record, updating only the `content` field. The new IP is remembered so no unnecessary API calls are made.

## Limitations

- IPv4 (A records) only — AAAA/IPv6 support may come later
- Windows only (WinForms)

## License

[MIT](LICENSE)
