# DSH 桌面一键启动

双击桌面图标即可后台启动 **DeepSeek Harness (DSH) Web** 服务器并自动打开浏览器，全程无命令行窗口。

## 快速开始

| 操作 | 说明 |
|---|---|
| 双击 **DSH** 图标 | 后台启动服务器（无窗口）+ 自动打开 http://127.0.0.1:3080 |
| 双击 **DSH 停止** 图标 | 停止服务器 |

> 服务器已运行时再点 DSH 不会重复启动，只会打开浏览器。

## 文件说明

| 文件 | 用途 |
|---|---|
| `DSHLauncher.exe` | 主程序（启动 / `--stop` 停止），C# 编译 |
| `DSHLauncher.cs` | 主程序源码 |
| `start-server.bat` | 服务器启动脚本，输出写入 `dsh-server.log` |
| `start-dsh.ps1` | 备用启动脚本 |
| `stop-dsh.bat` | 备用停止脚本 |
| `setup-desktop.ps1` | 重新生成桌面图标 |
| `update-icon.ps1` | 更换图标：`.\update-icon.ps1 -Source "图片.png"` |
| `DSHClient.cs` / `DSHClient.exe` | WebView2 桌面客户端（可选，替代浏览器窗口） |
| `build-client.ps1` | 编译 DSHClient.exe（无需 SDK，用系统自带 csc） |
| `dsh.ico` | 图标文件 |
| `大肥鱼.png` | 图标原始图片 |
| `使用说明.txt` | 图文使用说明 |

## 构建

主程序（用 Windows 自带 .NET Framework 编译器，无需安装 SDK）：

```powershell
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' -nologo -target:winexe -optimize+ -win32icon:dsh.ico -out:DSHLauncher.exe -r:System.Management.dll -r:System.Windows.Forms.dll DSHLauncher.cs
```

桌面客户端：

```powershell
powershell -ExecutionPolicy Bypass -File .\build-client.ps1
```

## 卸载

删除本文件夹 + 删除桌面两个图标（DSH / DSH 停止）即可。

## 常见问题

- 关机 / 重启无需先停止服务器，无任何影响。
- 服务器不会开机自启。
- 桌面图标失效时重新运行 `setup-desktop.ps1`。
