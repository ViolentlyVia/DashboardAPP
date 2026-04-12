param(
    [string]$ServerUrl,
    [int]$IntervalSeconds = 30,
    [string]$ReportIp,
    [switch]$DisableAutoStart,
    [string]$AgentExePath = (Join-Path $PSScriptRoot "SysDash.EndpointAgent.exe")
)

$ErrorActionPreference = "Stop"

function Test-ServerUrl {
    param([string]$Value)

    $uri = $null
    if (-not [System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref]$uri)) {
        return $false
    }

    return $uri.Scheme -eq "http" -or $uri.Scheme -eq "https"
}

if (-not $ServerUrl) {
    $ServerUrl = Read-Host "Enter SysDash server URL and port (example: http://192.168.1.10:5000)"
}

while (-not (Test-ServerUrl -Value $ServerUrl)) {
    Write-Host "Invalid URL. Use full http/https URL and include port." -ForegroundColor Yellow
    $ServerUrl = Read-Host "Enter SysDash server URL and port"
}

if ($IntervalSeconds -lt 5) {
    $IntervalSeconds = 5
}

if ($ReportIp -and $ReportIp -ne "auto") {
    $parsed = $null
    if (-not [System.Net.IPAddress]::TryParse($ReportIp, [ref]$parsed) -or $parsed.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw "ReportIp must be an IPv4 address or 'auto'."
    }
}

$configRoot = Join-Path $env:APPDATA "SysDashEndpointAgent"
$configPath = Join-Path $configRoot "config.json"

if (-not (Test-Path $configRoot)) {
    New-Item -ItemType Directory -Path $configRoot | Out-Null
}

$config = @{
    ServerUrl = $ServerUrl
    IntervalSeconds = $IntervalSeconds
    PreferredIp = $(if ($ReportIp -eq "auto") { $null } else { $ReportIp })
}

$config | ConvertTo-Json | Set-Content -Path $configPath -Encoding UTF8

Write-Host "Config saved: $configPath" -ForegroundColor Green
Write-Host "ServerUrl: $ServerUrl"
Write-Host "IntervalSeconds: $IntervalSeconds"
if ($null -eq $config.PreferredIp) {
    Write-Host "PreferredIp: auto"
}
else {
    Write-Host "PreferredIp: $($config.PreferredIp)"
}

if (-not $DisableAutoStart) {
    $runPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    $runValue = $null

    if (Test-Path $AgentExePath) {
        $runValue = '"' + $AgentExePath + '"'
    }
    else {
        $dllPath = Join-Path $PSScriptRoot "bin\Debug\net10.0-windows\SysDash.EndpointAgent.dll"
        if (Test-Path $dllPath) {
            $dotnetPath = (Get-Command dotnet).Source
            $runValue = '"' + $dotnetPath + '" "' + $dllPath + '"'
        }
    }

    if ($runValue) {
        Set-ItemProperty -Path $runPath -Name "SysDashEndpointAgent" -Value $runValue
        Write-Host "Windows startup enabled for SysDashEndpointAgent." -ForegroundColor Green
    }
    else {
        Write-Host "Startup registration skipped. No agent executable or debug DLL found." -ForegroundColor Yellow
    }
}
else {
    Write-Host "Windows startup registration disabled by flag."
}
