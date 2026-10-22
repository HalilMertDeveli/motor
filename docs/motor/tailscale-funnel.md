# Demo on a permanent address (Tailscale Funnel)

Owner decision (2026-10-22): until the store has its own domain or paid hosting, the customer
demo keeps running on the owner's PC and is published through **Tailscale Funnel** on a fixed
address:

**https://prosev.tail41fdd8.ts.net**

It replaces the Cloudflare quick tunnel (`*.trycloudflare.com`), whose hostname changed on
every restart and broke Google sign-in each time. Funnel is free, needs no card and no domain,
and the address stays the same as long as the device name (`prosev`) and the tailnet name
(`tail41fdd8`) are unchanged.

| Part | Where | Survives a reboot because |
|---|---|---|
| Store | Release build in the demo checkout, `http://localhost:5106` | Scheduled task **Prosev Demo** (at startup, no logon needed) |
| Public HTTPS | Tailscale Funnel `https://prosev.tail41fdd8.ts.net` → `http://127.0.0.1:5106` | Tailscale Windows service (automatic) keeps the Funnel config |
| Certificate | Let's Encrypt, issued and renewed by Tailscale | — |

The PC must be on and online. Sleep or hibernation takes the demo offline.

## 1. One-time setup

1. Install Tailscale (`winget install --id Tailscale.Tailscale --exact`). It runs as a service.
2. `tailscale login --hostname=prosev --unattended`. The owner opens the printed URL and signs
   in. `--unattended` keeps the node connected when nobody is logged on to Windows.
3. `tailscale funnel --bg --yes 5106`. The first time, it prints a link to enable HTTPS
   certificates and Funnel for the tailnet; the owner opens it and enables both. The command
   then finishes by itself. Check with `tailscale funnel status`.
4. From an **elevated** PowerShell, register the startup task:

   ```powershell
   deploy\windows\register-demo-autostart.ps1 -CheckoutDir <demo checkout>
   ```

   It starts `src\Smartstore.Web\bin\Release\Smartstore.Web.exe --urls http://localhost:5106
   --environment Production` with `src\Smartstore.Web` as working directory, the same as
   `dotnet run --no-build`. The app finds `Smartstore.sln` above that folder, so App_Data and
   Modules come from the checkout. Add `-Start` to start it right away.
5. Store settings (Admin → Configuration → Stores → Prosev): **Store URL**
   `https://prosev.tail41fdd8.ts.net/`. Leave "SSL enabled" off: Funnel only serves HTTPS, and
   the app learns the scheme from `X-Forwarded-Proto` (`Smartstore:ReverseProxy` is enabled and
   loopback is trusted by default).
6. Google sign-in (see [google-login.md](google-login.md)): in Google Cloud project `motor-512311`
   → APIs & Services → Credentials → the OAuth client, add
   * Authorized JavaScript origin: `https://prosev.tail41fdd8.ts.net`
   * Authorized redirect URI: `https://prosev.tail41fdd8.ts.net/signin-google`

## 2. Daily operation

The task was registered by an elevated shell, so a normal (non-elevated) PowerShell cannot
see it: `Get-ScheduledTask` and `schtasks /query` report "not found" even though it exists and
runs. Use an elevated PowerShell for the commands below. To check without elevation, look at
who owns the port: `Get-Process -Id (Get-NetTCPConnection -LocalPort 5106 -State Listen).OwningProcess`.

```powershell
Get-ScheduledTask -TaskName 'Prosev Demo' | Select-Object State   # Running?
Stop-ScheduledTask  -TaskName 'Prosev Demo'
Start-ScheduledTask -TaskName 'Prosev Demo'
& "C:\Program Files\Tailscale\tailscale.exe" funnel status
```

Redeploying a new version of the demo checkout:

1. `Stop-ScheduledTask -TaskName 'Prosev Demo'` (the running exe locks module DLLs).
2. Fast-forward the checkout, then `dotnet build Smartstore.sln -c Release`.
3. Delete `src\Smartstore.Web\App_Data\Tenants\Default\BundleCache`.
4. `Start-ScheduledTask -TaskName 'Prosev Demo'`; the first request takes about a minute.

Turning the public address off: `tailscale funnel --https=443 off`. Turning it back on:
`tailscale funnel --bg --yes 5106`.

## 3. Limits and next step

* Depends on the PC being on. Funnel traffic is relayed through Tailscale and has
  undocumented bandwidth limits: fine for a demo, not for production.
* The address is not branded. Changing the tailnet name in the Tailscale admin console
  (DNS → Tailnet name) changes the address, the Store URL and the Google redirect URI.
* For production, move to the store's own domain on real hosting ([fly.md](fly.md),
  [oracle.md](oracle.md) or [vps-operations.md](vps-operations.md)). Then remove the task
  (`Unregister-ScheduledTask -TaskName 'Prosev Demo'`), turn Funnel off and add the new
  redirect URI in Google Cloud.
