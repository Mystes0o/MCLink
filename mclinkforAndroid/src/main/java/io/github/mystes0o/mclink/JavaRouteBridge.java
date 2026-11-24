package io.github.mystes0o.mclink;

import java.io.Closeable;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.util.HashSet;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.function.IntSupplier;

final class JavaRouteBridge implements Closeable {
    private static final int MAX_CONNECTIONS = 16;
    private final ServerSocket listener;
    private final InetSocketAddress target;
    private final IntSupplier route;
    private final ExecutorService workers = Executors.newCachedThreadPool();
    private final Set<Link> active = new HashSet<>();

    JavaRouteBridge(int targetPort, IntSupplier route) throws IOException {
        InetAddress loopback = InetAddress.getByName(RoomTicket.LOOPBACK);
        this.target = new InetSocketAddress(loopback, targetPort);
        this.route = route;
        listener = new ServerSocket(0, MAX_CONNECTIONS, loopback);
        workers.execute(this::accept);
    }

    int port() { return listener.getLocalPort(); }

    private void accept() {
        try {
            while (!listener.isClosed()) {
                Socket client = listener.accept();
                synchronized (active) {
                    if (listener.isClosed() || active.size() >= MAX_CONNECTIONS) {
                        closeSocket(client);
                        continue;
                    }
                    Link link = new Link(client);
                    active.add(link);
                    workers.execute(() -> open(link));
                }
            }
        } catch (IOException ignored) {
            close();
        }
    }

    private void open(Link link) {
        try {
            link.target.connect(target, 2500);
            link.source.setTcpNoDelay(true);
            link.target.setTcpNoDelay(true);
            // This is the same MCL2 header understood by existing Windows hosts.
            link.target.getOutputStream().write(new byte[] {0, (byte) route.getAsInt()});
            workers.execute(() -> copy(link, link.target, link.source));
            copy(link, link.source, link.target);
        } catch (IOException | RejectedExecutionException unavailable) {
            close(link);
        }
    }

    private void copy(Link link, Socket from, Socket to) {
        try {
            InputStream input = from.getInputStream();
            OutputStream output = to.getOutputStream();
            byte[] buffer = new byte[16384];
            for (int length; (length = input.read(buffer)) != -1;) output.write(buffer, 0, length);
            to.shutdownOutput();
            if (link.remaining.decrementAndGet() == 0) close(link);
        } catch (IOException disconnected) {
            close(link);
        }
    }

    private void close(Link link) {
        closeSocket(link.source);
        closeSocket(link.target);
        synchronized (active) { active.remove(link); }
    }

    @Override public void close() {
        try { listener.close(); } catch (IOException ignored) {}
        synchronized (active) {
            for (Link link : active) {
                closeSocket(link.source);
                closeSocket(link.target);
            }
            active.clear();
            workers.shutdown();
        }
    }

    private static void closeSocket(Socket socket) {
        try { socket.close(); } catch (IOException ignored) {}
    }

    private static final class Link {
        final Socket source;
        final Socket target = new Socket();
        final AtomicInteger remaining = new AtomicInteger(2);

        Link(Socket source) { this.source = source; }
    }
}
