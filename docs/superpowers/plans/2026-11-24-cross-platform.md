# Java 与 Android 跨端联机实施计划

> 在当前开发分支实施，保留纯 Java 建房和旧邀请码读取路径。

**目标** Windows 房主在 MCLink 管理本地 Java 世界，Android 玩家粘贴同一个房间码加入。

**实现** 使用 Paper、Geyser、Floodgate 和 EasyTier。跨端邀请码附带基岩版端口和房间使用的 EasyTier 节点。

- [x] 在 Room.cs 实现跨端邀请码编码与解析，检查旧邀请码和无效输入。
- [x] 在 Windows 端实现新建、导入和继续世界，管理官方依赖、Paper 进程、就绪检测和正常保存退出。
- [x] 在 mclinkforAndroid 实现邀请码、EasyTier VPN、连通状态和进入 Minecraft 的入口，构建可安装 APK。
- [x] 构建两个客户端，检查原 Java 流程、跨端配置、真实 EasyTier TCP 和 UDP 转发，以及模拟服务端的保存退出。
- [ ] 使用真实 Paper、Geyser 和 Floodgate 启动世界，退出后继续联机存档。
- [ ] 使用 Android 移动数据和 Windows Java 客户端同进世界，验证只持有基岩版的玩家可登录及关房后进度保留。

Android 16 ARM64 真机已完成 APK 安装、冷启动、错误邀请码提示和 EasyTier JNI 启停。进服验收需要手机安装国际基岩版。
