package io.github.mystes0o.mclink;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;

final class JavaGameProbe {
    private JavaGameProbe() {}

    static boolean check(int localPort, int roomPort) {
        try (Socket socket = new Socket()) {
            socket.connect(new InetSocketAddress(RoomTicket.LOOPBACK, localPort), 1800);
            socket.setSoTimeout(2200);
            DataOutputStream output = new DataOutputStream(socket.getOutputStream());
            DataInputStream input = new DataInputStream(socket.getInputStream());

            byte[] address = "localhost".getBytes(StandardCharsets.US_ASCII);
            ByteArrayOutputStream payload = new ByteArrayOutputStream();
            DataOutputStream handshake = new DataOutputStream(payload);
            handshake.writeByte(0);
            handshake.writeByte(47); // Same status probe protocol as Windows MCLink.
            handshake.writeByte(address.length);
            handshake.write(address);
            handshake.writeShort(roomPort);
            handshake.writeByte(1);
            output.writeByte(payload.size());
            payload.writeTo(output);
            output.writeByte(1);
            output.writeByte(0);

            int length = readVarInt(input);
            if (length < 3 || length > 1048576) return false;
            byte[] reply = new byte[length];
            input.readFully(reply);
            DataInputStream status = new DataInputStream(new ByteArrayInputStream(reply));
            if (readVarInt(status) != 0 || readVarInt(status) != status.available() || status.read() != '{')
                return false;

            long nonce = System.nanoTime();
            output.writeByte(9);
            output.writeByte(1);
            output.writeLong(nonce);
            return readVarInt(input) == 9 && input.readUnsignedByte() == 1 && input.readLong() == nonce;
        } catch (IOException unavailable) {
            return false;
        }
    }

    private static int readVarInt(DataInputStream input) throws IOException {
        int value = 0;
        for (int shift = 0; shift <= 28; shift += 7) {
            int part = input.readUnsignedByte();
            if (shift == 28 && part > 7) break;
            value |= (part & 127) << shift;
            if ((part & 128) == 0) return value;
        }
        throw new IOException("Invalid Minecraft packet length");
    }
}
