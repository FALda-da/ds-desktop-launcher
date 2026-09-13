# DSH 手机模式启动器：以局域网监听（0.0.0.0）启动 DSH Web 服务器，
# 让同一 Wi-Fi 下的安卓手机可以直接访问，然后打印访问地址。
# 对应桌面快捷方式「DSH 手机模式」，也可用「手机模式-启动.bat」运行。
$ErrorActionPreference = 'Continue'
$port = 3080
$root = $PSScriptRoot
$log = Join-Path $root 'mobile-dsh-server.log'

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

function Stop-DshProcesses {
    Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'node.exe' -and $_.CommandLine -match 'dsh' } | ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force
        Write-Host ("已停止进程 PID " + $_.ProcessId) -ForegroundColor Yellow
    }
}

function Show-LanUrls {
    Write-Host ""
    Write-Host "手机访问地址（手机和电脑需在同一 Wi-Fi 下）：" -ForegroundColor Green
    $ips = @()
    try {
        $ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop | Where-Object {
            $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' -and $_.AddressState -eq 'Preferred'
        } | Select-Object -ExpandProperty IPAddress)
    } catch {}
    if ($ips.Count -eq 0) {
        # 兜底：解析 ipconfig 输出
        $ips = @((ipconfig | Select-String -Pattern 'IPv4') | ForEach-Object { (($_.Line -split ':')[-1]).Trim() } | Where-Object { $_ -ne '' -and $_ -notlike '127.*' })
    }
    foreach ($ip in ($ips | Select-Object -Unique)) {
        Write-Host ("  http://" + $ip + ":" + $port) -ForegroundColor Green
    }
    Write-Host ""
    $lanLine = Select-String -Path $log -Pattern '\(LAN:' -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($lanLine) { Write-Host ("服务器日志提示：" + $lanLine.Line.Trim()) -ForegroundColor Cyan }
    Write-Host ("服务器日志文件：" + $log)
    Write-Host ""
    Write-Host "手机操作：用 Chrome 打开上面的地址 → 菜单「添加到主屏幕」，即可像 App 一样全屏使用。" -ForegroundColor White
    Write-Host "安全提醒：手机模式会把 DSH（含远程执行能力）暴露给整个局域网！" -ForegroundColor Red
    Write-Host "请务必先运行「手机模式-防火墙开启.bat」，用完后运行「DSH 停止」关闭服务器。" -ForegroundColor Red
}

Write-Host "══════════════ DSH 手机模式 ══════════════" -ForegroundColor Cyan

# 端口已被占用：询问是否切换
if (Test-PortAlive $port) {
    Write-Host "端口 $port 已有 DSH 服务器在运行（可能是普通桌面模式）。" -ForegroundColor Yellow
    $answer = Read-Host "要停止它并切换为手机模式吗？(Y/N，默认 N)"
    if ($answer -match '^[Yy]') {
        Stop-DshProcesses
        Start-Sleep -Seconds 2
    } else {
        Write-Host "未做任何改动。若它本来就是手机模式启动的，直接用手机访问即可；否则请先停止再重试。"
        Show-LanUrls
        exit
    }
}

# 启动服务器（后台、无窗口，日志写入 mobile-dsh-server.log）
$cmdArgs = '/c', ('cd /d "' + $root + '" && npx --yes @deepseek-ai/dsh web --patch lan-access.yml --no-open >> "' + $log + '" 2>&1')
Start-Process -FilePath $env:ComSpec -ArgumentList $cmdArgs -WindowStyle Hidden
Write-Host "正在后台启动 DSH（手机模式）..." -ForegroundColor Cyan

# 等待端口就绪（最多 90 秒）
for ($i = 0; $i -lt 90; $i++) {
    Start-Sleep -Seconds 1
    if (Test-PortAlive $port) { break }
    if ($i % 10 -eq 9) { Write-Host ("  ...等待服务器启动中 (" + ($i + 1) + "s)") }
}

if (-not (Test-PortAlive $port)) {
    Write-Host "服务器 90 秒内未启动成功，请查看日志：" -ForegroundColor Red
    Get-Content $log -Tail 30 -ErrorAction SilentlyContinue
    Read-Host "按回车退出"
    exit 1
}

Write-Host "服务器已就绪 ✔" -ForegroundColor Green
Show-LanUrls
