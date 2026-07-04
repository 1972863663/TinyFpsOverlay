# TinyFpsOverlay

**TinyFpsOverlay** 是一个极简 Windows 游戏帧率 / 硬件状态悬浮条。当前软件名、程序集名和发布 EXE 名均为 `TinyFpsOverlay`，应用图标位于：

```text
Assets\app-icon.ico
```

## 功能

悬浮条横排显示：

```text
FPS --   CPU 3% 51°C   GPU 1% 31°C
```

当前特性：

- 顶部居中显示；
- 黑底紧凑横排，只包住文字；
- 鼠标穿透，不影响游戏点击；
- 不可拖动，避免游戏中误移动；
- `[` 显示悬浮条；
- `]` 隐藏悬浮条；
- 托盘菜单只保留常用设置：热键、开机自启动、字体颜色、透明度滑块、退出；
- FPS 由 `tools\PresentMon-2.5.1-x64.exe` 采集；
- CPU/GPU 占用和 GPU 温度由 LibreHardwareMonitor 采集；
- CPU 温度优先使用 AMD Ryzen Master Monitoring SDK，失败后回退到 LibreHardwareMonitor。

## CPU 温度后端顺序

当前代码中的 CPU 温度 Provider 顺序为：

1. `AmdRyzenMasterTemperatureProvider`
   - 调用 AMD Ryzen Master Monitoring SDK；
   - 通过注册表定位 SDK 安装目录；
   - 加载 SDK 的 `bin\Platform.dll`；
   - 初始化失败时 `IsAvailable = false`；
   - 读取失败返回 `null`；
   - `GetCPUParameters` 读取频率限制为不高于 1Hz，并缓存最近一次有效温度。
2. `RyzenSmuTemperatureProvider`
   - 当前保留占位，尚未接入真实 Ryzen SMU 读取逻辑。
3. `LibreHardwareTemperatureProvider`
   - 作为最后 fallback。

HWiNFO Shared Memory 支持已经从代码中移除，不再需要 HWiNFO 后台运行。

## AMD Ryzen Master Monitoring SDK

AMD SDK 不随项目分发，也不把 AMD SDK 的 DLL/EXE 放进仓库。需要 CPU 温度优先走 AMD SDK 时，请在本机安装：

```text
C:\Program Files\AMD\RyzenMasterMonitoringSDK
```

已经在 AMD Ryzen 7 9800X3D 上用 AMD 官方 sample 验证过 `GetCurrentTemperature` 可以读到 CPU 温度。

如果 SDK 不存在、SDK DLL 缺失、初始化失败或读取失败，程序不会抛异常影响 UI，会自动回退到后续 Provider。

## 运行

开发环境直接运行：

```powershell
dotnet run -c Release
```

也可以双击：

```text
运行.bat
```

管理员启动不是强制要求；只有个别传感器或 PresentMon 在特定系统环境下取不到数据时，才考虑使用：

```text
运行_管理员.bat
```

## 打包 / 发布 EXE

推荐发布命令：

```powershell
dotnet clean
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

发布输出：

```text
bin\Release\net8.0-windows\win-x64\publish\TinyFpsOverlay.exe
```

也可以直接运行项目根目录的脚本：

```text
打包到桌面.bat
```

它会重新 `clean/build/publish`，然后把完整发布目录复制到：

```text
%USERPROFILE%\Desktop\TinyFpsOverlay\TinyFpsOverlay.exe
```

说明：

- 发布模式是 framework-dependent，需要目标机器安装 .NET 8 Desktop Runtime；
- `TinyFpsOverlay.exe` 为单文件主程序；
- `PresentMon-2.5.1-x64.exe` 按项目配置作为外部内容复制到发布目录，未打进单文件；
- AMD Ryzen Master Monitoring SDK 不会被复制到发布目录。

## 配置文件

运行时配置保存在：

```text
%APPDATA%\TinyFpsOverlay\config.json
```

当前保存内容包括窗口状态、透明度等。

## 托盘菜单

右键托盘图标后，当前只保留这些功能：

```text
热键：开启/关闭
开机自启动：开启/关闭
字体颜色...
透明度滑块
退出
```

透明度滑块方向：

- 往左：更透明；
- 往右：更不透明。

## 开机自启动

托盘右键菜单里有：

```text
开机自启动：开启/关闭
```

实现方式是写入当前用户注册表启动项：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TinyFpsOverlay
```

注意：如果你移动了 EXE，Windows 启动项里保存的旧路径会失效。新版程序启动时会做路径自修复：

- 只要配置里仍然是开启自启动；
- 或注册表里已经存在 `TinyFpsOverlay` 启动项；
- 程序启动后都会把启动项更新成当前正在运行的 EXE 路径。

所以移动位置后，手动打开一次新位置的 `TinyFpsOverlay.exe`，保持“开机自启动：开启”，它就会把启动项修正到新路径。

## 项目结构

```text
TinyFpsOverlay.csproj
Program.cs
MainForm.cs
app.manifest
Assets\app-icon.ico
Assets\app-icon.png
Models\
Services\
Services\CpuTemperature\
tools\PresentMon-2.5.1-x64.exe
```

## Git / 仓库注意事项

`.gitignore` 已排除：

- `bin/`
- `obj/`
- 本地验证截图 `verify-*.png`、`desktop*.png`
- IDE 用户状态和临时文件

不要提交 AMD SDK 安装目录、SDK DLL、官方 sample EXE 或本机下载的安装包。
