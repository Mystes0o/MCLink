# MCLink 好友联机

Windows x64 桌面原型，适用于开启局域网共享的 Minecraft Java 版存档。房主电脑运行世界。两端运行 MCLink，通过 EasyTier 2.6.4 无 TUN 模式组网。好友在游戏内直连本机端口。

## 开始联机

1. 两位玩家都需要 MCLink.exe 和同目录的 runtime 文件夹，并使用兼容的游戏版本与整合包。
2. 房主进入存档，按 Esc，选择「对局域网开放」。将聊天栏显示的**本次端口**填入 MCLink，点击「创建房间」。
3. 房主点击「复制邀请码」，私发给好友。邀请码含随机房间密钥，每次建房重新生成。拿到邀请码的人都能尝试加入。
4. 好友打开 MCLink，粘贴邀请码，点击「加入房间」。等状态显示「游戏可连接」，复制地址，进入游戏的「多人游戏 → 直接连接」。
5. 游戏过程中两端保持 MCLink 运行。关窗或点「退出房间」会结束该端的组网进程。

## 延迟读数

好友端定期经转发通道发送 Java 版服务器状态请求，收到状态后测一次协议 ping/pong 往返时间。P95 来自最近最多 24 次成功响应；探测成功数另算最近最多 24 次尝试。房主端的探测只检查自己的存档。读数包含游戏端应答开销，不能直接推算网络丢包率、服务器 TPS 或玩家实际游戏帧率。

## 连接条件

两端默认使用官方示例中的公共引导节点 tcp://public.easytier.cn:11010。连接能力取决于节点可用性、运营商网络以及双方网络策略。房间配置只放行房主选定的入站 TCP 游戏端口，访客的游戏入口绑定在 127.0.0.1。房主本机游戏需支持服务器列表状态查询。游戏登录、模组兼容性仍由 Minecraft 处理。

## 构建与测试

项目使用 Windows 自带的 .NET Framework 4.x C# 编译器，构建脚本为 build.ps1。依赖已放在 runtime 内。如需重装，setup.ps1 会从 EasyTier 官方 GitHub 下载固定版本的 Windows x64 发行包并校验 SHA-256。

在项目目录中的 PowerShell 运行：

```powershell
.\build.ps1
& "$env:windir/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /codepage:65001 /out:Smoke.exe Smoke.cs Room.cs Session.cs
.\Smoke.exe
```

Smoke 使用本机模拟 Java 状态服务、两份独立配置和两个真实组网进程验证邀请格式、TCP 转发、状态查询及 ping/pong。此测试不包含游戏登录或跨运营商中转验证。

上游组件与组网说明：[EasyTier 2.6.4](https://github.com/EasyTier/EasyTier/releases/tag/v2.6.4)、[无 TUN 模式](https://doc.easytier.cn/en/guide/network/no-root.html)。
