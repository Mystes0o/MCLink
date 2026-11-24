package io.github.mystes0o.mclink;

import java.net.URI;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.Locale;
import java.util.UUID;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

final class RoomTicket {
    static final String HOST = "10.144.144.1";
    static final String LOOPBACK = "127.0.0.1";
    private static final Pattern JAVA_FORMAT = Pattern.compile("MCL[12]-([a-f0-9]{32})-([0-9]{1,5})");
    private static final Pattern FORMAT = Pattern.compile(
            "MCL3-([a-f0-9]{32})-([0-9]{1,5})-([0-9]{1,5})-([A-Za-z0-9_-]+)");
    private static final Pattern RELAY_SCHEME = Pattern.compile("tcp|udp|ws|wss|quic");

    final String instance = UUID.randomUUID().toString();
    final int port;
    final boolean javaRoom;
    private final String secret;
    private final String[] relays;

    private RoomTicket(String secret, int port, String[] relays) {
        this.secret = secret;
        this.port = port;
        this.relays = relays;
        this.javaRoom = relays == null;
    }

    static RoomTicket parse(String input) {
        String code = input.trim();
        if (code.length() > 8192) throw badTicket();
        Matcher java = JAVA_FORMAT.matcher(code);
        if (java.matches()) {
            int port = Integer.parseInt(java.group(2));
            if (port < 1 || port > 65535) throw badTicket();
            return new RoomTicket(java.group(1), port, null);
        }
        Matcher match = FORMAT.matcher(code);
        if (!match.matches()) throw badTicket();
        int javaPort = Integer.parseInt(match.group(2));
        int bedrockPort = Integer.parseInt(match.group(3));
        if (javaPort < 1 || javaPort > 65535 || bedrockPort < 1 || bedrockPort > 65535
                || javaPort == bedrockPort) throw badTicket();
        try {
            byte[] payload = Base64.getUrlDecoder().decode(match.group(4));
            String peers = StandardCharsets.UTF_8.newDecoder().decode(ByteBuffer.wrap(payload)).toString();
            String[] relays = peers.split("\n", -1);
            for (String relay : relays) {
                URI uri = new URI(relay);
                String scheme = uri.getScheme();
                if (scheme == null || !RELAY_SCHEME.matcher(scheme.toLowerCase(Locale.ROOT)).matches()
                        || uri.getHost() == null || uri.getUserInfo() != null || uri.getFragment() != null
                        || uri.getPort() > 65535 || (uri.getPort() <= 0
                        && !(uri.getPort() == -1 && ("ws".equalsIgnoreCase(scheme) || "wss".equalsIgnoreCase(scheme))))
                        || relay.indexOf('"') >= 0 || relay.indexOf('\\') >= 0) throw badTicket();
            }
            return new RoomTicket(match.group(1), bedrockPort, relays);
        } catch (Exception e) {
            throw badTicket();
        }
    }

    private static IllegalArgumentException badTicket() {
        return new IllegalArgumentException("房间码无效，请复制房主发来的完整邀请码。");
    }

    String config(int localPort, String[] defaultRelays) {
        StringBuilder text = new StringBuilder(
                "instance_name = \"" + instance + "\"\ndhcp = true\n");
        if (javaRoom) text.append("listeners = [\"udp://0.0.0.0:0\"]\n");
        else text.append("manual_routes = [\"").append(HOST).append("/32\"]\n");
        text.append("[network_identity]\nnetwork_name = \"mc-" + secret.substring(0, 12)
                + "\"\nnetwork_secret = \"" + secret + "\"\n");
        for (String relay : javaRoom ? defaultRelays : relays)
            text.append("[[peer]]\nuri = \"").append(relay).append("\"\n");
        return text.append("[[port_forward]]\nbind_addr = \"").append(LOOPBACK).append(':').append(localPort)
                .append("\"\ndst_addr = \"").append(HOST).append(':').append(port)
                .append("\"\nproto = \"tcp\"\n")
                .append("[flags]\nno_tun = ").append(javaRoom)
                .append("\nbind_device = false\nrelay_network_whitelist = \"\"\n")
                .append("[[acl.acl_v1.chains]]\nchain_type = 1\nenabled = true\ndefault_action = 2\n")
                .toString();
    }
}
