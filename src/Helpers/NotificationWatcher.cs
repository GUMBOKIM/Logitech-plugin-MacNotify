namespace Loupedeck.MacNotifyPlugin
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading;

    // Listens for macOS notification banners in near real-time (~100 ms) by
    // streaming the unified log of the notification daemon (usernoted).
    // If the log stream cannot run, falls back to polling the Notification
    // Center database, which lags ~5 s behind banner display because macOS
    // commits records lazily (and requires Full Disk Access).
    internal sealed class NotificationWatcher : IDisposable
    {
        public enum Status
        {
            Streaming,  // log stream active, ~100 ms latency
            Fallback,   // database polling active, ~5 s latency
            Failed,     // no detection method works (usually missing Full Disk Access)
        }

        // "Presenting" is logged only when a banner is actually shown, so
        // notifications silenced by Focus/Do Not Disturb do not trigger.
        private const String TriggerMarker = "Presenting <NotificationRecord";
        private const String LogPredicate =
            "subsystem == \"com.apple.unc\" AND eventMessage CONTAINS \"" + TriggerMarker + "\"";
        private const Int32 MaxStreamRestarts = 5;
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(1);

        // Extracts the sender's bundle id from a usernoted log line,
        // e.g. ... Presenting <NotificationRecord app:"com.tinyspeck.slackmacgap" ...
        private static readonly Regex AppIdRegex = new Regex("app:\"([^\"]+)\"", RegexOptions.Compiled);

        // The callback receives the bundle id of the app that posted the
        // notification, or null when it could not be determined.
        private readonly Action<String> _onNotification;
        private readonly Action<Status, String> _onStatusChanged;
        private Process _logStream;
        private Timer _restartTimer;
        private Int32 _restartCount;
        private DateTime _lastFireUtc = DateTime.MinValue;
        private Boolean _disposed;

        public NotificationWatcher(Action<String> onNotification, Action<Status, String> onStatusChanged = null)
        {
            this._onNotification = onNotification;
            this._onStatusChanged = onStatusChanged;
        }

        // All apps that have ever posted a notification, straight from the
        // Notification Center database. Requires Full Disk Access; returns
        // an empty list when the database cannot be read.
        public static IReadOnlyList<String> GetKnownApps()
        {
            try
            {
                var output = RunSqlite("SELECT identifier FROM app ORDER BY identifier;");
                return output == null
                    ? Array.Empty<String>()
                    : output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            catch
            {
                return Array.Empty<String>();
            }
        }

        public void Start() => this.StartLogStream();

        public void Dispose()
        {
            this._disposed = true;
            this._restartTimer?.Dispose();
            this._pollTimer?.Dispose();
            try
            {
                if (this._logStream != null && !this._logStream.HasExited)
                {
                    this._logStream.Kill();
                }
            }
            catch
            {
                // The stream process is already gone.
            }
            this._logStream?.Dispose();
        }

        private void StartLogStream()
        {
            if (this._disposed)
            {
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/log",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("stream");
                psi.ArgumentList.Add("--style");
                psi.ArgumentList.Add("compact");
                psi.ArgumentList.Add("--predicate");
                psi.ArgumentList.Add(LogPredicate);

                var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) => this.OnStreamLine(e.Data);
                process.Exited += (_, _) => this.OnStreamExited();
                process.Start();
                process.BeginOutputReadLine();

                this._logStream = process;
                PluginLog.Info("Notification log stream started");
                this._onStatusChanged?.Invoke(Status.Streaming, null);
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Cannot start log stream ({ex.Message}); falling back to database polling");
                this.StartDbPolling();
            }
        }

        private void OnStreamLine(String line)
        {
            // The stream echoes the predicate itself in a header line
            // ("Filtering the log data using ..."), which contains the
            // marker and must not trigger a haptic.
            if (line == null ||
                !line.Contains(TriggerMarker) ||
                line.Contains("Filtering the log data"))
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now - this._lastFireUtc < Cooldown)
            {
                return;
            }
            this._lastFireUtc = now;

            var match = AppIdRegex.Match(line);
            var bundleId = match.Success ? match.Groups[1].Value : null;
            PluginLog.Info($"Notification banner detected (app: {bundleId ?? "unknown"})");
            this._onNotification(bundleId);
        }

        private void OnStreamExited()
        {
            if (this._disposed)
            {
                return;
            }

            if (Interlocked.Increment(ref this._restartCount) > MaxStreamRestarts)
            {
                PluginLog.Warning("Log stream keeps exiting; falling back to database polling");
                this.StartDbPolling();
                return;
            }

            PluginLog.Warning($"Log stream exited; restarting in 3 s (attempt {this._restartCount}/{MaxStreamRestarts})");
            this._restartTimer?.Dispose();
            this._restartTimer = new Timer(_ => this.StartLogStream(), null, 3000, Timeout.Infinite);
        }

        // ==========================================================================================
        // Fallback: Notification Center database polling (~5 s latency, needs Full Disk Access)
        // ==========================================================================================

        private static readonly String DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library/Group Containers/group.com.apple.usernoted/db2/db");

        private Timer _pollTimer;
        private Int32 _polling;
        private Int64 _lastRecId = -1;
        private Boolean _accessErrorLogged;

        private void StartDbPolling()
        {
            if (this._disposed || this._pollTimer != null)
            {
                return;
            }
            this._onStatusChanged?.Invoke(Status.Fallback, null);
            this._pollTimer = new Timer(_ => this.Poll(), null, 0, 1000);
        }

        private void Poll()
        {
            if (Interlocked.Exchange(ref this._polling, 1) == 1)
            {
                return;
            }

            try
            {
                var (maxRecId, bundleId) = this.QueryLatestRecord();
                if (maxRecId < 0)
                {
                    return;
                }

                if (this._lastRecId >= 0 && maxRecId > this._lastRecId)
                {
                    PluginLog.Info($"New notification detected (rec_id {maxRecId}, app: {bundleId ?? "unknown"})");
                    this._onNotification(bundleId);
                }
                this._lastRecId = maxRecId;
            }
            catch (Exception ex)
            {
                this.LogAccessErrorOnce(ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref this._polling, 0);
            }
        }

        private (Int64 RecId, String BundleId) QueryLatestRecord()
        {
            String output;
            try
            {
                output = RunSqlite(
                    "SELECT r.rec_id || '|' || IFNULL(a.identifier, '') " +
                    "FROM record r LEFT JOIN app a ON r.app_id = a.app_id " +
                    "ORDER BY r.rec_id DESC LIMIT 1;");
            }
            catch (Exception ex)
            {
                this.LogAccessErrorOnce(ex.Message);
                return (-1, null);
            }

            if (output == null)
            {
                this.LogAccessErrorOnce("query failed");
                return (-1, null);
            }
            if (output.Length == 0)
            {
                return (0, null); // empty database
            }

            var parts = output.Split('|', 2);
            if (!Int64.TryParse(parts[0], out var recId))
            {
                this.LogAccessErrorOnce(output);
                return (-1, null);
            }
            var bundleId = parts.Length > 1 && !String.IsNullOrEmpty(parts[1]) ? parts[1] : null;
            return (recId, bundleId);
        }

        // Runs a read-only query against the Notification Center database.
        // Returns trimmed stdout, or null when sqlite3 reports an error
        // (typically missing Full Disk Access).
        private static String RunSqlite(String query)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/sqlite3",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-readonly");
            psi.ArgumentList.Add(DbPath);
            psi.ArgumentList.Add(query);

            using var process = Process.Start(psi);
            var stdout = process.StandardOutput.ReadToEnd().Trim();
            process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            return process.ExitCode == 0 ? stdout : null;
        }

        private void LogAccessErrorOnce(String detail)
        {
            if (this._accessErrorLogged)
            {
                return;
            }
            this._accessErrorLogged = true;
            PluginLog.Warning(
                $"Cannot read Notification Center database ({detail}). " +
                "Grant Full Disk Access to LogiPluginService in System Settings > Privacy & Security > Full Disk Access.");
            this._onStatusChanged?.Invoke(Status.Failed, $"{detail}.");
        }
    }
}
