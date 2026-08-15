# DSH Web launcher: starts the server if needed, then opens the browser.
$ErrorActionPreference = 'Continue'
$port = 3080
$url = "http://127.0.0.1:$port"
$root = $PSScriptRoot
$serverBat = Join-Path $root 'start-server.bat'

function Test-PortAlive([int]$Port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect('127.0.0.1', $Port)
        return $true
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

# Already running? Just open the browser.
if (Test-PortAlive $port) {
    Start-Process $url
    exit 0
}

# Start the server fully hidden (no console window).
$cmdArgs = '/c', ('"' + $serverBat + '"')
Start-Process -FilePath $env:ComSpec -ArgumentList $cmdArgs -WindowStyle Hidden

# Wait for the port to come up (up to 60 seconds).
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 1
    if (Test-PortAlive $port) { break }
}

Start-Process $url
