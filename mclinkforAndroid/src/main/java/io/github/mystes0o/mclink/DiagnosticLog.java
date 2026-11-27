package io.github.mystes0o.mclink;

import android.os.SystemClock;
import java.io.PrintWriter;
import java.io.StringWriter;
import java.text.SimpleDateFormat;
import java.util.ArrayDeque;
import java.util.Date;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.regex.Pattern;

final class DiagnosticLog {
    private static final int CAPACITY = 600;
    private static final int MAX_CHARACTERS = 150000;
    private static final Pattern SECRETS = Pattern.compile(
            "MCL[123]-[a-f0-9]{32}(?:-[A-Za-z0-9_-]+)?|\\bmc-[a-f0-9]{12}\\b|\\b[a-f0-9]{32}\\b",
            Pattern.CASE_INSENSITIVE);
    private static final Pattern ENDPOINT = Pattern.compile(
            "\\b((?:tcp|udp|quic|wss?)://)(?:[^\\s/?#@\"'<>]+@)?([^\\s/?#@\"'<>]+)(?:[/?#][^\\s\"'<>]*)?",
            Pattern.CASE_INSENSITIVE);
    private static final ArrayDeque<Entry> entries = new ArrayDeque<>();
    private static final LinkedHashMap<String, Long> recentErrors = new LinkedHashMap<>();
    private static final SimpleDateFormat clock = new SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.ROOT);
    private static long version;
    private static int characters;

    private DiagnosticLog() {}

    static void info(String source, String message) { add(0, source, message); }
    static void warn(String source, String message) { add(1, source, message); }
    static void error(String source, String message, Throwable error) {
        StringWriter details = new StringWriter();
        error.printStackTrace(new PrintWriter(details));
        add(2, source, message + "\n" + details);
    }

    static synchronized void repeatedError(String source, String message, Throwable error) {
        String key = sanitize(source + message + error);
        long now = SystemClock.elapsedRealtime();
        Long previous = recentErrors.get(key);
        if (previous != null && now - previous < 15000) return;
        recentErrors.remove(key);
        if (recentErrors.size() >= 40) recentErrors.remove(recentErrors.keySet().iterator().next());
        recentErrors.put(key, now);
        error(source, message, error);
    }

    static String sanitize(String text) {
        return ENDPOINT.matcher(SECRETS.matcher(text).replaceAll("[已隐藏]")).replaceAll("$1$2");
    }

    private static synchronized void add(int level, String source, String message) {
        String label = level == 2 ? "错误" : level == 1 ? "警告" : "信息";
        String safe = sanitize(message);
        if (safe.length() > 4000) safe = safe.substring(0, 4000) + " …";
        String line = clock.format(new Date()) + " [" + label + "] [" + source + "] " + safe;
        while (entries.size() >= CAPACITY || characters + line.length() > MAX_CHARACTERS)
            characters -= entries.removeFirst().text.length();
        entries.addLast(new Entry(level, line));
        characters += line.length();
        version++;
    }

    static synchronized long version() { return version; }

    static synchronized String read(int minimum, String search) {
        String query = search.trim().toLowerCase(Locale.ROOT);
        StringBuilder text = new StringBuilder();
        for (Entry entry : entries) {
            if (entry.level >= minimum && entry.text.toLowerCase(Locale.ROOT).contains(query))
                text.append(entry.text).append('\n');
        }
        return text.toString();
    }

    private static final class Entry {
        final int level;
        final String text;

        Entry(int level, String text) { this.level = level; this.text = text; }
    }
}
