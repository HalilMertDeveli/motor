<#
.SYNOPSIS
    Registers a Windows scheduled task that starts the MRT Motors demo store at boot.

.DESCRIPTION
    The demo runs from a built checkout (Release build, SQLite tenant in
    src/Smartstore.Web/App_Data). The task starts bin/Release/Smartstore.Web.exe with the
    project folder as working directory, which is what `dotnet run --no-build` does, so the
    same App_Data and Modules are used. It runs at system startup without a logged-on user
    (S4U) and restarts the process if it exits with an error.

    Public access is provided separately by Tailscale Funnel (`tailscale funnel --bg <port>`),
    which runs as a Windows service and keeps its configuration across reboots.
    See docs/motor/tailscale-funnel.md.

    Must be run from an elevated PowerShell (startup tasks need administrator rights).

.EXAMPLE
    .\register-demo-autostart.ps1 -CheckoutDir E:\motor\.claude\worktrees\nostalgic-faraday-2341ef -Start
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CheckoutDir,
    [int] $Port = 5106,
    [string] $TaskName = 'Prosev Demo',
    [switch] $Start
)

$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell (Run as administrator).'
}

$webDir = Join-Path $CheckoutDir 'src\Smartstore.Web'
$exe = Join-Path $webDir 'bin\Release\Smartstore.Web.exe'
if (-not (Test-Path $exe)) {
    throw "Release build not found: $exe (build with 'dotnet build -c Release' first)."
}
if (-not (Test-Path (Join-Path $webDir 'App_Data\Tenants'))) {
    throw "No installed tenant under $webDir\App_Data\Tenants."
}

$action = New-ScheduledTaskAction -Execute $exe `
    -Argument "--urls http://localhost:$Port --environment Production" `
    -WorkingDirectory $webDir
$trigger = New-ScheduledTaskTrigger -AtStartup
# S4U: runs whether or not the user is logged on, without storing a password.
$taskPrincipal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType S4U -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Principal $taskPrincipal -Settings $settings `
    -Description "Starts the MRT Motors demo store on http://localhost:$Port (public via Tailscale Funnel)." `
    -Force | Out-Null

Write-Host "Registered scheduled task '$TaskName' -> $exe"

if ($Start) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Started '$TaskName'. First start takes about a minute."
}
