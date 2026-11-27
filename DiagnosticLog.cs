using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MCLink {
    internal static class DiagnosticLog {
        internal enum Level { Info, Warning, Error }

        internal const int Capacity=1200;
        sealed class Entry {
            internal readonly DateTime Time=DateTime.Now;
            internal readonly Level Severity;
            internal readonly string Source, Message;

            internal Entry(Level severity, string source, string message) {
                Severity=severity; Source=source; Message=message;
            }

            public override string ToString() {
                string label=Severity==Level.Error ? "错误" : Severity==Level.Warning ? "警告" : "信息";
                return Time.ToString("yyyy-MM-dd HH:mm:ss.fff")+" ["+label+"] ["+Source+"] "+Message;
            }
        }

        static readonly Queue<Entry> entries=new Queue<Entry>();
        static readonly Regex ansi=new Regex(@"\x1b\[[0-?]*[ -/]*[@-~]");
        static readonly Regex secrets=new Regex(
            @"MCL[123]-[a-f0-9]{32}(?:-[A-Za-z0-9_-]+)?|\bmc-[a-f0-9]{12}\b|\b[a-f0-9]{32}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex endpointSuffix=new Regex(@"\b(?:tcp|udp|quic|wss?)://[^\s""'<>]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static long revision;

        internal static void Info(string source,string message) { Write(Level.Info,source,message); }
        internal static void Warn(string source,string message) { Write(Level.Warning,source,message); }
        internal static void Error(string source,string message,Exception error) {
            Write(Level.Error,source,message+Environment.NewLine+error);
        }

        internal static void ProcessLine(string source,string line,bool stderr) {
            if (String.IsNullOrWhiteSpace(line)) return;
            var message=ansi.Replace(line,"");
            var level=stderr || message.IndexOf("ERROR",StringComparison.OrdinalIgnoreCase)>=0
                || message.IndexOf("Exception",StringComparison.OrdinalIgnoreCase)>=0 ? Level.Error :
                message.IndexOf("WARN",StringComparison.OrdinalIgnoreCase)>=0 ? Level.Warning : Level.Info;
            Write(level,source,message);
        }

        static void Write(Level level,string source,string message) {
            message=secrets.Replace(ansi.Replace(message,""),"[已隐藏]");
            // A WebSocket relay can include an access token in its path or query.
            message=endpointSuffix.Replace(message,match=> {
                Uri uri;
                if (!Uri.TryCreate(match.Value,UriKind.Absolute,out uri)) return "[节点地址已隐藏]";
                return uri.GetComponents(UriComponents.SchemeAndServer,UriFormat.UriEscaped);
            });
            if (message.Length>4000) message=message.Substring(0,4000)+" …";
            lock (entries) {
                if (entries.Count==Capacity) entries.Dequeue();
                entries.Enqueue(new Entry(level,source,message));
                revision++;
            }
        }

        internal static string Read(int minimum,string search,ref long seen,out int count) {
            Entry[] snapshot;
            lock (entries) {
                count=entries.Count;
                if (seen==revision) return null;
                seen=revision;
                snapshot=entries.ToArray();
            }
            var result=new StringBuilder();
            foreach (var entry in snapshot) {
                if ((int)entry.Severity<minimum) continue;
                string line=entry.ToString();
                if (line.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0) result.AppendLine(line);
            }
            return result.ToString();
        }
    }
}
