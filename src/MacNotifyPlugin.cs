namespace Loupedeck.MacNotifyPlugin
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;

    // This class contains the plugin-level logic of the Loupedeck plugin.

    public class MacNotifyPlugin : Plugin
    {
        private const String WaveformSetting = "waveform";
        private const String EnabledSetting = "enabled";
        private const String AppRulesSetting = "appRules";

        // Per-app rule value meaning "do not vibrate for this app".
        public const String MuteRule = "mute";

        private readonly ConcurrentDictionary<String, Byte> _seenApps = new();

        // Gets a value indicating whether this is an API-only plugin.
        public override Boolean UsesApplicationApiOnly => true;

        // Gets a value indicating whether this is a Universal plugin or an Application plugin.
        public override Boolean HasNoApplication => true;

        private NotificationWatcher _watcher;

        // Initializes a new instance of the plugin class.
        public MacNotifyPlugin()
        {
            // Initialize the plugin log.
            PluginLog.Init(this.Log);

            // Initialize the plugin resources.
            PluginResources.Init(this.Assembly);
        }

        // The vibration pattern played when a notification arrives.
        public String CurrentWaveform
        {
            get => this.TryGetPluginSetting(WaveformSetting, out var value) && HapticWaveforms.IsValid(value)
                ? value
                : HapticWaveforms.Default;
            set
            {
                if (HapticWaveforms.IsValid(value))
                {
                    this.SetPluginSetting(WaveformSetting, value, false);
                }
            }
        }

        public Boolean HapticsEnabled
        {
            get => !this.TryGetPluginSetting(EnabledSetting, out var value) || value != "false";
            set => this.SetPluginSetting(EnabledSetting, value ? "true" : "false", false);
        }

        // Per-app pattern overrides, stored as "bundleId=rule;bundleId=rule"
        // where rule is a waveform name or MuteRule.
        public IReadOnlyDictionary<String, String> AppRules
        {
            get
            {
                var rules = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
                if (this.TryGetPluginSetting(AppRulesSetting, out var serialized))
                {
                    foreach (var entry in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = entry.Split('=', 2);
                        if (parts.Length == 2 && (parts[1] == MuteRule || HapticWaveforms.IsValid(parts[1])))
                        {
                            rules[parts[0]] = parts[1];
                        }
                    }
                }
                return rules;
            }
        }

        // Sets or clears (rule == null) the pattern override for an app.
        public void SetAppRule(String bundleId, String rule)
        {
            if (String.IsNullOrEmpty(bundleId))
            {
                return;
            }

            var rules = new Dictionary<String, String>(this.AppRules, StringComparer.OrdinalIgnoreCase);
            if (rule == null)
            {
                rules.Remove(bundleId);
            }
            else if (rule == MuteRule || HapticWaveforms.IsValid(rule))
            {
                rules[bundleId] = rule;
            }
            else
            {
                return;
            }

            this.SetPluginSetting(
                AppRulesSetting,
                String.Join(';', rules.Select(r => $"{r.Key}={r.Value}")),
                false);
        }

        // Apps offered in the per-app settings dropdown: everything that has
        // ever posted a notification (from the Notification Center database),
        // plus apps seen at runtime and apps that already have a rule.
        public IReadOnlyList<String> GetKnownApps() =>
            NotificationWatcher.GetKnownApps()
                .Concat(this._seenApps.Keys)
                .Concat(this.AppRules.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        // Plays the haptic for a notification from the given app, honoring
        // per-app overrides and the mute rule.
        public void PlayHapticFor(String bundleId)
        {
            if (bundleId != null)
            {
                this._seenApps.TryAdd(bundleId, 0);
            }

            var waveform = (String)null;
            if (bundleId != null && this.AppRules.TryGetValue(bundleId, out var rule))
            {
                if (rule == MuteRule)
                {
                    return;
                }
                waveform = rule;
            }
            this.PlayHaptic(waveform);
        }

        // Plays the notification haptic using the configured pattern.
        // Preview calls bypass the enabled toggle so the user always gets
        // feedback while choosing a pattern.
        public void PlayHaptic(String waveform = null, Boolean isPreview = false)
        {
            if (!isPreview && !this.HapticsEnabled)
            {
                return;
            }
            waveform ??= this.CurrentWaveform;
            if (HapticWaveforms.IsValid(waveform))
            {
                this.PluginEvents.RaiseEvent(HapticWaveforms.EventName(waveform));
            }
        }

        // This method is called when the plugin is loaded.
        public override void Load()
        {
            foreach (var (name, displayName) in HapticWaveforms.All)
            {
                this.PluginEvents.AddEvent(
                    HapticWaveforms.EventName(name),
                    $"Notification ({displayName})",
                    $"macOS notification haptic with the {displayName} pattern");
            }

            this._watcher = new NotificationWatcher(
                bundleId => this.PlayHapticFor(bundleId),
                this.OnWatcherStatusChanged);
            this._watcher.Start();
        }

        // This method is called when the plugin is unloaded.
        public override void Unload()
        {
            this._watcher?.Dispose();
            this._watcher = null;
        }

        private void OnWatcherStatusChanged(NotificationWatcher.Status status, String detail)
        {
            switch (status)
            {
                case NotificationWatcher.Status.Streaming:
                    this.OnPluginStatusChanged(Loupedeck.PluginStatus.Normal, null);
                    break;
                case NotificationWatcher.Status.Fallback:
                    this.OnPluginStatusChanged(
                        Loupedeck.PluginStatus.Warning,
                        "Real-time notification detection is unavailable; using database polling instead. " +
                        "Haptics will lag ~5 seconds behind notifications.");
                    break;
                case NotificationWatcher.Status.Failed:
                    this.OnPluginStatusChanged(
                        Loupedeck.PluginStatus.Error,
                        $"Cannot detect macOS notifications: {detail} " +
                        "Grant Full Disk Access to LogiPluginService in " +
                        "System Settings > Privacy & Security > Full Disk Access, then restart the service.");
                    break;
            }
        }
    }
}
