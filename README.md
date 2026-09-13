# DSH 桌面一键启动

双击桌面图标即可后台启动 **DeepSeek Harness (DSH) Web** 服务器并自动打开浏览器，全程无命令行窗口。

## 快速开始

| 操作 | 说明 |
|---|---|
| 双击 **DSH** 图标 | 后台启动服务器（无窗口）+ 打开**带鉴权 token 的正确网址** |
| 双击 **DSH 停止** 图标 | 停止服务器 |

> - 服务器已运行时再点 DSH 不会重复启动，只会打开一个可用的网页链接。
> - 启动 / 停止时弹出进度窗口；**失败时会把日志直接显示在窗口里**，可点「打开日志」。
> - 已启用 PerMonitorV2 高 DPI 支持，2K / 4K 高分屏（125% / 150% 缩放）文字清晰锐利。

## 稳定性设计（以前为什么会突然坏掉）

| 曾经的故障 | 真正原因 | 现在的对策 |
|---|---|---|
| 浏览器打开是"未授权 / 打不开" | 新版引擎加入 token 鉴权，**没有 token 的 `http://127.0.0.1:3080` 会返回 401** | 启动器从日志读取最新的 `?token=...` 链接，并**先做 HTTP 验证（303）再打开** |
| 每隔一段时间就突然全部失败 | `npx @deepseek-ai/dsh` 每次都解析 npm 最新版 → **引擎被静默升级**，新版引入新校验 | **锁定版本**（`dsh-engine.txt`），只有主动运行 `update-engine.ps1` 才会升级 |
| `DeepSeek request extension preparation failed` | profile 的 `package.json` 缺 `version` 字段（皮肤 / 市场工具重写配置时会丢） | 每次启动前由 `dsh-preflight.ps1` **自愈**：自动补齐 `name` / `version`（改前先备份、改后校验 JSON） |
| 启动失败却看不出原因 | 旧窗口只会说"等待超时" | 失败时窗口内显示日志尾部，并提供「打开日志」/「强制打开网页」按钮 |

## 文件说明

| 文件 | 用途 |
|---|---|
| `DSHLauncher.exe` | 主程序（启动 / `--stop` 停止 / `--diag` 诊断），C# 编译 |
| `DSHLauncher.cs` | 主程序源码 |
| `dsh-engine.txt` | **引擎版本锁**（改这里即改版本，建议用 `update-engine.ps1`） |
| `start-server.bat` | 启动脚本：读版本锁 → 跑自愈 → 启动引擎（输出写入 `dsh-server.log`） |
| `dsh-preflight.ps1` | 启动前自愈：补齐 `name`/`version`、日志超 1MB 自动轮转 |
| `update-engine.ps1` | 主动升级 / 回滚引擎版本 |
| `app.manifest` | PerMonitorV2 高 DPI 清单（编译时嵌入） |
| `start-dsh.ps1` / `stop-dsh.bat` | 备用启动 / 停止脚本 |
| `setup-desktop.ps1` | 重新生成桌面图标 |
| `update-icon.ps1` | 更换图标：`.\update-icon.ps1 -Source "图片.png"` |
| `DSHClient.cs` / `DSHClient.exe` | WebView2 桌面客户端（可选，替代浏览器窗口） |
| `build-client.ps1` | 编译 DSHClient.exe |
| `dsh.ico` / `大肥鱼.png` | 图标及其原始图片 |
| `dsh-server.log` | 服务器日志（自动生成，超 1MB 轮转为 `.old`） |
| `dsh-repair.log` | 自愈记录（自动生成） |
| `dsh-diag.txt` | `--diag` 诊断报告（自动生成） |
| `使用说明.txt` | 纯文本使用说明 |

## 排查工具

```powershell
.\DSHLauncher.exe --diag
```

生成 `dsh-diag.txt`：端口是否监听、日志里解析到的 token 网址、基础网址 / token 网址的 HTTP 状态、日志尾部。
**401 = 需要 token（正常，启动器会自动加）；200 / 302 / 303 = 可用。**

## 引擎版本管理

```powershell
.\update-engine.ps1                 # 查看当前锁定版本 + npm 上的最新版
.\update-engine.ps1 -Version 0.1.5-rc.1   # 锁定指定版本（也可用旧版本回滚）
.\update-engine.ps1 -Latest         # 跟随 npm 最新版（不推荐，等于放弃稳定性）
```

改完版本后：点桌面「DSH 停止」，再点「DSH」重新启动即可生效。

## 构建

主程序（用 Windows 自带 .NET Framework 编译器，无需安装 SDK）：

```powershell
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' -nologo -target:winexe -optimize+ -codepage:65001 -win32manifest:app.manifest -win32icon:dsh.ico -out:DSHLauncher.exe -r:System.Management.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll DSHLauncher.cs
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
- 升级引擎后出问题：把 `dsh-engine.txt` 改回旧版本号即可回滚（旧版本仍在 npx 缓存中）。
