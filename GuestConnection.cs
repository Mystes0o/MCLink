using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace MCLink {
    internal sealed class GuestConnection : IDisposable {
        const int ProbeCount = 5;
        readonly List<long> timings = new List<long>();
        internal readonly Session Session;
        internal readonly int Port;
        internal readonly string Description;
        internal readonly RouteMode Mode;

        GuestConnection(Room room, string[] peers, RouteMode mode) {
            Port = Room.FreePort();
            Mode = mode;
            if (mode == RouteMode.DirectOnly) Description = "点对点直连";
            else if (mode == RouteMode.RelayOnly) {
                var uri = new Uri(peers[0]);
                Description = "中继 " + uri.GetLeftPart(UriPartial.Authority);
            } else Description = "自动选路（线路未确认）";
            Session = new Session(room.Config(false, Port, peers, "udp://0.0.0.0:0", mode));
        }

        internal static GuestConnection Automatic(Room room, string[] peers) {
            return new GuestConnection(room, peers, RouteMode.Automatic);
        }

        internal static GuestConnection Select(Room room, string[] peers, IProgress<string> progress,
                                               CancellationToken cancellation) {
            // Check actual game traffic with the engine's direct-only mode before trying relays.
            var direct = new GuestConnection(room, peers, RouteMode.DirectOnly);
            try {
                if (WaitForGame(direct, room.Port, cancellation)) {
                    var result = direct;
                    direct = null;
                    return result;
                }
            } finally { if (direct != null) direct.Dispose(); }

            progress.Report("正在比较中继，直连暂未就绪…");
            var candidates = new GuestConnection[peers.Length];
            GuestConnection winner = null;
            try {
                Parallel.For(0, peers.Length, new ParallelOptions {
                    CancellationToken = cancellation, MaxDegreeOfParallelism = 4
                }, index => {
                    GuestConnection candidate;
                    try { candidate = new GuestConnection(room, new[] { peers[index] }, RouteMode.RelayOnly); }
                    catch (Win32Exception) { return; }
                    candidates[index] = candidate;
                    if (!WaitForGame(candidate, room.Port, cancellation)) return;
                    for (int i = 0; i < ProbeCount && candidate.Session.Running; i++) {
                        Pause(cancellation, 500);
                        long timing;
                        if (TryProbe(candidate.Port, room.Port, out timing)) candidate.timings.Add(timing);
                    }
                });
                // A failed probe ranks below any response. P95 includes the entire status exchange.
                cancellation.ThrowIfCancellationRequested();
                winner = candidates.Where(candidate => candidate != null && candidate.timings.Count > 0 && candidate.Session.Running)
                    .OrderByDescending(candidate => candidate.timings.Count)
                    .ThenBy(candidate => candidate.P95()).FirstOrDefault();
                return winner;
            } finally {
                foreach (var candidate in candidates) {
                    if (candidate != null && candidate != winner) candidate.Dispose();
                }
            }
        }

        static bool WaitForGame(GuestConnection candidate, int gamePort, CancellationToken cancellation) {
            var duration = Stopwatch.StartNew();
            do {
                Pause(cancellation, 400);
                long unused;
                if (TryProbe(candidate.Port, gamePort, out unused)) return true;
            } while (candidate.Session.Running && duration.ElapsedMilliseconds < 7000);
            return false;
        }

        static void Pause(CancellationToken cancellation, int milliseconds) {
            if (cancellation.WaitHandle.WaitOne(milliseconds)) cancellation.ThrowIfCancellationRequested();
        }

        static bool TryProbe(int port, int gamePort, out long elapsed) {
            var clock = Stopwatch.StartNew();
            try {
                GameProbe.Check(port, gamePort);
                elapsed = clock.ElapsedMilliseconds;
                return true;
            } catch (SocketException) {}
              catch (InvalidDataException) {}
              catch (IOException) {}
              catch (TimeoutException) {}
            elapsed = 0;
            return false;
        }

        long P95() {
            return timings.OrderBy(value => value).ElementAt((int)Math.Ceiling(timings.Count * .95) - 1);
        }

        public void Dispose() { Session.Dispose(); }
    }
}
