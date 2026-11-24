package io.github.mystes0o.mclink;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;

final class LanRoomAnnouncement {
    private static final int DISCOVERY_PORT = 4445;

    private LanRoomAnnouncement() {}

    static void send(int gamePort) {
        // Java Edition takes the server IP from the packet source. Keep it on this device.
        byte[] announcement = ("[MOTD]MCLink 房间[/MOTD][AD]" + gamePort + "[/AD]")
                .getBytes(StandardCharsets.UTF_8);
        try {
            InetAddress loopback = InetAddress.getByName(RoomTicket.LOOPBACK);
            try (DatagramSocket socket = new DatagramSocket(new InetSocketAddress(loopback, 0))) {
                socket.send(new DatagramPacket(announcement, announcement.length, loopback, DISCOVERY_PORT));
            }
        } catch (IOException unavailable) {
            // Manual connection is still available if local discovery is unavailable.
        }
    }
}
