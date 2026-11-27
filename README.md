# MCLink 好友联机

Windows x64 桌面原型，适用于开启局域网共享的 Minecraft Java 版存档。房主电脑运行世界。两端运行 MCLink，通过 EasyTier 2.6.4 无 TUN 模式组网。好友在游戏内直连本机端口。

## 开始联机

1. 两位玩家都需要 `MCLink.exe`、同目录的 `runtime` 文件夹和 `relay.txt`，并使用兼容的游戏版本与整合包。
2. 房主进入存档，按 Esc，选择「对局域网开放」。将聊天栏显示的**本次端口**填入 MCLink，点击「创建房间」。
3. 房主点击「复制邀请码」，私发给好友。邀请码含随机房间密钥，每次建房重新生成。拿到邀请码的人都能尝试加入。
4. 好友打开 MCLink，粘贴邀请码，点击「加入房间」。等状态显示「游戏可连接」，打开 Java 版「多人游戏」，选择「MCLink 房间」。若列表未显示房间，可复制地址并使用「直接连接」。
5. 游戏过程中两端保持 MCLink 运行。关窗或点「退出房间」会结束该端的组网进程。

房主的「连接状态」每 2.5 秒刷新在线人数和昵称，人数包含房主。点击「查看玩家线路」可以查看完整列表。支持线路上报的访客会随游戏连接上报接入时的线路。Minecraft 或整合包可能只返回部分昵称，房主端会标记由登录连接识别、但游戏状态未确认在线的玩家。

纯 Java 房间使用 MCL2 邀请码，跨端房间使用 MCL3 邀请码。新版客户端也能用 MCL1 加入旧房间。

## 延迟读数

Windows 和 Android 主界面的「连接日志」会显示近期建房、加入、线路变化、连通探测、TCP 转发及异常。可按级别筛选、搜索和复制，Windows 还可导出日志并查看 EasyTier 及跨端服务端的进程输出。日志保存在本次运行的内存中，退出前可复制或导出，邀请码和房间密钥会被隐藏。

好友端定期经转发通道发送 Java 版服务器状态请求，收到状态后测一次协议 ping/pong 往返时间。P95 来自最近最多 24 次成功响应；探测成功数另算最近最多 24 次尝试。房主端的探测只检查自己的存档。读数包含游戏端应答开销，不能直接推算网络丢包率、服务器 TPS 或玩家实际游戏帧率。

Windows 和 Android Java 访客使用一个 EasyTier 会话自动选路。可用时先通过中继进入，EasyTier 会继续尝试点对点直连。连接成功后，访客端显示当前的点对点或中继线路。Windows 读取线路需要与 `easytier-core.exe` 配套的 `easytier-cli.exe`。路由变化时 MCLink 保持本地游戏地址。两端启用 TCP 和 UDP 监听，公共 IPv6 可用时 EasyTier 也可以尝试 IPv6 直连。

## 连接条件

随包的 `relay.txt` 填有社区公共节点。更换节点时，房主和好友在 `MCLink.exe` 同目录的 `relay.txt` 中填入相同的 EasyTier 中继列表，每行一个地址，支持 `tcp`、`udp`、`ws`、`wss` 和 `quic`。保存后重新建房或加入。连接能力取决于节点可用性、运营商网络以及双方网络策略。普通房间只放行房主端的入站 TCP 游戏转发端口，启用 Android 联机时会额外放行配置的 Geyser TCP 和 UDP 端口。桌面访客的游戏入口绑定在 127.0.0.1。房主本机游戏需支持服务器列表状态查询。游戏登录、模组兼容性仍由 Minecraft 处理。

## Android Java 版加入电脑 Java 房间

1. 电脑房主在 Minecraft 启动器选择游戏版本，打开自己的存档，再对局域网开放。
2. 在 Windows MCLink 填入游戏显示的局域网端口，点「创建房间」，把邀请码发给手机好友。
3. 好友在 Android MCLink 粘贴邀请码。显示「已连接房主」后，在手机 Java 版的「多人游戏」中选择「MCLink 房间」。若列表未显示房间，可复制地址并使用「直接连接」。连接期间保持 Android MCLink 运行。

Android Java 版支持 MCL1 和 MCL2 邀请码。双方使用兼容的 Java 版游戏版本。Android APK 默认内置项目的 relay.txt。如果房主另用了自定义中继，构建 Android APK 时需打包同一份 relay.txt。

## Windows Java 版与 Android 基岩版外网联机

1. 房主打开 Windows MCLink，点击「跨端建房」。选择新建 Java 世界、导入已关闭的原版 Java 存档，或继续联机存档。首次导入会复制世界。后续进度写入这份联机存档。
2. 阅读并确认 Minecraft 最终用户许可协议，点击「启动 Geyser 并建房」。首次运行会检查 Java 25，缺少时下载可校验的 Temurin 运行环境，并下载和校验 Paper、Geyser、Floodgate。房主电脑运行这些组件。
3. 建房后，房主点击「复制房主游戏地址」，在电脑 Java 版的「多人游戏 → 直接连接」中使用。点「复制邀请码」把 MCL3 房间码私发给好友。电脑好友用新版 Windows MCLink，Android 好友用 MCLink for Android。
4. Android 玩家在 MCLink for Android 点击「粘贴房间码并加入」，首次确认系统 VPN 授权。连通后点「打开 Minecraft」。如果游戏需要手动添加服务器，可以在 MCLink 复制地址，并在游戏的「服务器 → 添加服务器」中填写。地址为 10.144.144.1，端口为 19132。
5. 房主结束时点「离开」。MCLink 会请求 Java 服务端保存并正常结束。

房主服务端固定为 Java 26.3。Android 端首版提供 arm64-v8a APK，使用 Android 8.0 或更新版本，并要求安装 Geyser 兼容的国际基岩版。Floodgate 允许基岩玩家不另外购买 Java 版。好友仍需能访问房间使用的 EasyTier 节点。联机存档通常在 %LOCALAPPDATA%\MCLink\Crossplay\worlds。该位置不可写时，MCLink 会在程序旁的 runtime\crossplay-data\worlds 创建存档。「继续联机存档」会列出两个位置可读取的存档。

### 构建 Android APK

安装 JDK 17 或更新版本及 [Android SDK 命令行工具](https://developer.android.com/tools/releases/cmdline-tools)。在 Windows 执行：

    .\mclinkforAndroid\setup.ps1
    .\mclinkforAndroid\build.ps1

输出为仓库根目录的 MCLinkForAndroid.apk，由本机调试密钥签名。APK 使用 [EasyTierCompose v1.0.10](https://github.com/originalFactor/EasyTierCompose/tree/v1.0.10) 发布的 EasyTier 原生库，所用 [EasyTier 源代码](https://github.com/EasyTier/EasyTier/tree/81487c363a56779e745eae927e130a813535c71e) 与该版本的子模块一致。

## 构建与测试

项目使用 Windows 自带的 .NET Framework 4.x C# 编译器，构建脚本为 build.ps1。依赖已放在 runtime 内。如需重装，setup.ps1 会从 EasyTier 官方 GitHub 下载固定版本的 Windows x64 发行包并校验 SHA-256。

在项目目录中的 PowerShell 运行：

```powershell
.\build.ps1
& "$env:windir/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /codepage:65001 /r:System.Web.Extensions.dll /out:Smoke.exe Smoke.cs Room.cs Session.cs DiagnosticLog.cs
.\Smoke.exe
```

Smoke 使用本机模拟 Java 状态服务、两份独立配置和两个真实组网进程验证邀请格式、TCP 转发、状态查询及 ping/pong。此测试不包含游戏登录或跨运营商中转验证。

上游组件与组网说明：[EasyTier 2.6.4](https://github.com/EasyTier/EasyTier/releases/tag/v2.6.4)、[无 TUN 模式](https://doc.easytier.cn/en/guide/network/no-root.html)。
