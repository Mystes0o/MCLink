package io.github.mystes0o.mclink;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Intent;
import android.net.VpnService;
import android.os.ParcelFileDescriptor;
import android.os.SystemClock;
import com.easytier.jni.EasyTierJNI;
import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import org.json.JSONArray;
import org.json.JSONObject;

public final class RoomVpn extends VpnService {
    static final String GAME_PACKAGE = "com.mojang.minecraftpe";
    static final String EXTRA_TICKET = "roomCode";
    private static final String CHANNEL = "room";
    // One JNI runtime per app process. Serialize teardown and subsequent reconnects.
    private static final ExecutorService ENGINE = Executors.newSingleThreadExecutor();
    static volatile State state = new State("等待加入房间", 0, false);
    private volatile boolean alive;
    private volatile int route;
    private volatile String endMessage = "已断开房间";
    private boolean joined;

    static final class State {
        final String message;
        final int port;
        final boolean running;
        final boolean javaRoom;

        State(String message, int port, boolean running) {
            this(message, port, running, false);
        }

        State(String message, int port, boolean running, boolean javaRoom) {
            this.message = message;
            this.port = port;
            this.running = running;
            this.javaRoom = javaRoom;
        }

        String address() { return (javaRoom ? RoomTicket.LOOPBACK : RoomTicket.HOST) + ":" + port; }
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        if (joined) return START_NOT_STICKY;
        NotificationManager notifications = getSystemService(NotificationManager.class);
        notifications.createNotificationChannel(new NotificationChannel(CHANNEL, "MCLink 房间", NotificationManager.IMPORTANCE_LOW));
        PendingIntent open = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class),
                PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        startForeground(1, new Notification.Builder(this, CHANNEL)
                .setSmallIcon(android.R.drawable.stat_sys_warning)
                .setContentTitle("MCLink 房间")
                .setContentText("点击查看房间或断开连接")
                .setContentIntent(open).setOngoing(true).build());

        final RoomTicket room;
        try {
            room = RoomTicket.parse(intent == null ? "" : intent.getStringExtra(EXTRA_TICKET));
        } catch (RuntimeException invalid) {
            DiagnosticLog.error("房间", "服务无法读取邀请码", invalid);
            state = new State("房间码无效，请重新粘贴。", 0, false);
            stopSelf(startId);
            return START_NOT_STICKY;
        }
        joined = alive = true;
        DiagnosticLog.info("房间", room.javaRoom ? "加入 Java 房间" : "加入基岩跨端房间");
        state = new State("正在连接房主…", 0, true, room.javaRoom);
        ENGINE.execute(() -> connect(room, startId));
        return START_NOT_STICKY;
    }

    private void connect(RoomTicket room, int startId) {
        ParcelFileDescriptor tunnel = null;
        JavaRouteBridge bridge = null;
        try {
            if (!alive) return;
            final int probePort;
            try (ServerSocket reservation = new ServerSocket(0, 1, InetAddress.getByName(RoomTicket.LOOPBACK))) {
                probePort = reservation.getLocalPort();
            }
            String[] relays = room.javaRoom ? bundledRelays() : null;
            int result = EasyTierJNI.runNetworkInstance(room.config(probePort, relays));
            if (result != 0) throw nativeFailure("EasyTier 启动失败", result);
            DiagnosticLog.info("EasyTier", "组网已启动，通向房主 " + RoomTicket.HOST + ":" + room.port);
            if (room.reportsPlayerRoutes) bridge = new JavaRouteBridge(probePort, () -> route);
            int gamePort = bridge == null ? probePort : bridge.port();
            if (room.javaRoom) DiagnosticLog.info("TCP 转发", "游戏入口 " + RoomTicket.LOOPBACK + ":"
                    + gamePort + " → 本机代理端口 " + probePort + " → 房主端口 " + room.port);
            long deadline = SystemClock.elapsedRealtime() + 45000;
            while (!room.javaRoom && alive && SystemClock.elapsedRealtime() < deadline && tunnel == null) {
                JSONObject info = networkInfo(room.instance);
                JSONObject node = info == null ? null : info.optJSONObject("my_node_info");
                JSONObject ipv4 = node == null ? null : node.optJSONObject("virtual_ipv4");
                JSONObject address = ipv4 == null ? null : ipv4.optJSONObject("address");
                long value = address == null ? 0 : address.optLong("addr", 0);
                // Ignore a temporary DHCP address before the room's subnet is discovered.
                if ((value & 0xffffff00L) == 0x0a909000L && (value & 255) >= 2 && (value & 255) <= 254) {
                    String ip = ((value >>> 24) & 255) + "." + ((value >>> 16) & 255)
                            + "." + ((value >>> 8) & 255) + "." + (value & 255);
                    tunnel = new Builder().setSession("MCLink 房间").setMtu(1380)
                            .addAddress(ip, 24).addRoute(RoomTicket.HOST, 32)
                            .addAllowedApplication(GAME_PACKAGE).establish();
                    if (tunnel == null)
                        throw new IllegalStateException("无法启用 MCLink VPN，请重新授权。");
                    result = EasyTierJNI.setTunFd(room.instance, tunnel.getFd());
                    if (result != 0) throw nativeFailure("EasyTier 绑定 VPN 失败", result);
                    DiagnosticLog.info("VPN", "已分配虚拟地址 " + ip + "，仅 Minecraft 流量路由到 " + RoomTicket.HOST);
                } else Thread.sleep(400);
            }
            if (!room.javaRoom && alive && tunnel == null)
                throw new IllegalStateException("等待房间地址超时，请确认房主在线。");
            boolean connected = false;
            boolean reachable = false;
            int previousRoute = -1;
            int unsuccessful = 0;
            while (alive) {
                boolean available = room.javaRoom ? JavaGameProbe.check(probePort, room.port) : probe(probePort);
                if (!alive) break;
                route = available ? hostRoute(networkInfo(room.instance)) : 0;
                if (available && previousRoute != route) {
                    DiagnosticLog.info("线路", route == 1 ? "点对点直连" : route == 2 ? "中继" : "线路未确认");
                    previousRoute = route;
                }
                if (available) {
                    if (!reachable) DiagnosticLog.info(room.javaRoom ? "Java 探测" : "Geyser 信令",
                            "房主已响应，端口 " + room.port);
                    reachable = true;
                    connected = true;
                    unsuccessful = 0;
                    if (room.javaRoom) LanRoomAnnouncement.send(gamePort);
                    state = room.javaRoom
                            ? new State("已连接房主 · " + (route == 1 ? "点对点直连" :
                                    route == 2 ? "中继（仍在尝试直连）" : "线路未确认") +
                                    "\n在 Java 版「多人游戏」中选择 MCLink 房间。", gamePort, true, true)
                            : new State("已连接房主，可以打开 Minecraft", room.port, true);
                } else if (++unsuccessful >= 4) {
                    if (unsuccessful == 4) DiagnosticLog.warn("房间", connected ? "与房主暂时断开，正在重连"
                            : "还未收到房主响应，正在重试");
                    reachable = false;
                    previousRoute = -1;
                    state = new State(connected ? "与房主暂时断开，正在重连…" :
                            "还未收到房主响应，正在重试…", 0, true, room.javaRoom);
                }
                Thread.sleep(2000);
            }
        } catch (android.content.pm.PackageManager.NameNotFoundException missing) {
            DiagnosticLog.error("VPN", "找不到 Minecraft 国际基岩版", missing);
            if (alive) endMessage = "请先安装 Minecraft 国际基岩版。";
        } catch (Exception | LinkageError error) {
            if (alive) {
                DiagnosticLog.error("房间", "连接流程失败", error);
                endMessage = error instanceof IllegalStateException && error.getMessage() != null
                        ? DiagnosticLog.sanitize(error.getMessage().split("\\r?\\n", 2)[0]) : "连接失败，请重试。";
            }
        } finally {
            if (bridge != null) bridge.close();
            try { if (tunnel != null) tunnel.close(); }
            catch (Exception error) { DiagnosticLog.error("VPN", "关闭 VPN 失败", error); }
            try {
                int result = EasyTierJNI.retainNetworkInstance(new String[0]);
                if (result != 0) throw nativeFailure("停止 EasyTier 失败", result);
            } catch (RuntimeException | LinkageError error) {
                DiagnosticLog.error("EasyTier", "停止组网失败", error);
            }
            stopSelf(startId);
        }
    }

    private static IllegalStateException nativeFailure(String operation, int code) {
        String detail = EasyTierJNI.getLastError();
        return new IllegalStateException(operation + "，返回码 " + code + (detail == null ? "" : "\n" + detail));
    }

    private String[] bundledRelays() throws java.io.IOException {
        ArrayList<String> peers = new ArrayList<>();
        try (BufferedReader reader = new BufferedReader(new InputStreamReader(
                getAssets().open("relay.txt"), StandardCharsets.UTF_8))) {
            for (String line; (line = reader.readLine()) != null;) {
                line = line.replace("\uFEFF", "").trim();
                if (!line.isEmpty() && !line.startsWith("#")) peers.add(line);
            }
        }
        if (peers.isEmpty()) throw new IllegalStateException("应用未配置 Java 联机中继。");
        return peers.toArray(new String[0]);
    }

    private static JSONObject networkInfo(String instance) {
        try {
            String json = EasyTierJNI.collectNetworkInfos(1);
            if (json == null) throw nativeFailure("读取 EasyTier 线路信息失败", -1);
            JSONObject map = new JSONObject(json).optJSONObject("map");
            return map == null ? null : map.optJSONObject(instance);
        } catch (org.json.JSONException | RuntimeException unavailable) {
            DiagnosticLog.repeatedError("EasyTier", "读取线路信息失败", unavailable);
            return null;
        }
    }

    static int hostRoute(JSONObject info) {
        JSONArray routes = info == null ? null : info.optJSONArray("routes");
        if (routes == null) return 0;
        for (int i = 0; i < routes.length(); i++) {
            JSONObject entry = routes.optJSONObject(i);
            JSONObject ipv4 = entry == null ? null : entry.optJSONObject("ipv4_addr");
            JSONObject address = ipv4 == null ? null : ipv4.optJSONObject("address");
            if (address == null || address.optLong("addr", 0) != 0x0a909001L) continue;
            int hops = entry.optInt("cost");
            if (hops > 0) return hops == 1 ? 1 : 2;
        }
        return 0;
    }

    private static boolean probe(int port) {
        try (Socket socket = new Socket()) {
            socket.connect(new InetSocketAddress("127.0.0.1", port), 1200);
            socket.setSoTimeout(1500);
            socket.getOutputStream().write(("GET / HTTP/1.1\r\nHost: " + RoomTicket.HOST
                    + "\r\nConnection: close\r\n\r\n").getBytes(StandardCharsets.US_ASCII));
            InputStream input = socket.getInputStream();
            for (char expected : "HTTP/".toCharArray())
                if (input.read() != expected) throw new java.io.IOException("房主未返回有效 HTTP 信令");
            return true;
        } catch (java.io.IOException unavailable) {
            DiagnosticLog.repeatedError("Geyser 信令", "查询房主失败，代理端口 " + port, unavailable);
            return false;
        }
    }

    @Override public void onRevoke() {
        endMessage = "Android 已撤销 VPN 授权。";
        DiagnosticLog.warn("VPN", endMessage);
        stopSelf();
    }

    @Override public void onDestroy() {
        alive = false;
        if (joined) DiagnosticLog.info("房间", "房间连接已关闭");
        if (state.running) state = new State(endMessage, 0, false);
        stopForeground(STOP_FOREGROUND_REMOVE);
        super.onDestroy();
    }
}
