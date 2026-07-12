namespace Loupedeck.MacNotifyPlugin
{
    using System;
    using System.Collections.Generic;

    // Per-app vibration rules: pick an application and give it its own
    // pattern, mute it, or send it back to the global default. The app list
    // is built from every app that has ever posted a macOS notification.

    public class AppPatternCommand : ActionEditorCommand
    {
        private const String AppControl = "app";
        private const String PatternControl = "pattern";
        private const String DefaultRule = "default";

        // Friendlier names for common bundle ids; anything else falls back
        // to the last bundle-id segment.
        private static readonly Dictionary<String, String> WellKnownApps = new(StringComparer.OrdinalIgnoreCase)
        {
            ["com.tinyspeck.slackmacgap"] = "Slack",
            ["com.kakao.KakaoTalkMac"] = "KakaoTalk",
            ["com.apple.mail"] = "Mail",
            ["com.apple.MobileSMS"] = "Messages",
            ["com.apple.iCal"] = "Calendar",
            ["com.apple.reminders"] = "Reminders",
            ["com.apple.facetime"] = "FaceTime",
            ["com.google.Chrome"] = "Chrome",
            ["com.apple.Safari"] = "Safari",
            ["com.microsoft.teams2"] = "Microsoft Teams",
            ["us.zoom.xos"] = "Zoom",
            ["com.hnc.Discord"] = "Discord",
            ["notion.id"] = "Notion",
            ["com.apple.scripteditor2"] = "Script Editor",
        };

        private MacNotifyPlugin HapticsPlugin => (MacNotifyPlugin)this.Plugin;

        public AppPatternCommand()
        {
            this.DisplayName = "Per-App Haptic Pattern";
            this.Description = "Give a specific application its own vibration pattern, or mute it";
            this.GroupName = "Settings";

            this.ActionEditor.AddControlEx(new ActionEditorListbox(AppControl, "Application"));
            this.ActionEditor.AddControlEx(new ActionEditorListbox(PatternControl, "Vibration pattern"));
            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;
            this.ActionEditor.ControlValueChanged += this.OnControlValueChanged;
        }

        private static String PrettyAppName(String bundleId)
        {
            if (WellKnownApps.TryGetValue(bundleId, out var known))
            {
                return known;
            }
            var lastSegment = bundleId.Substring(bundleId.LastIndexOf('.') + 1);
            return lastSegment.Length > 1
                ? Char.ToUpperInvariant(lastSegment[0]) + lastSegment.Substring(1)
                : bundleId;
        }

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName.EqualsNoCase(AppControl))
            {
                var rules = this.HapticsPlugin.AppRules;
                foreach (var bundleId in this.HapticsPlugin.GetKnownApps())
                {
                    var ruleHint = rules.TryGetValue(bundleId, out var rule) ? $" — current: {rule}" : "";
                    e.AddItem(bundleId, PrettyAppName(bundleId), $"{bundleId}{ruleHint}");
                }
            }
            else if (e.ControlName.EqualsNoCase(PatternControl))
            {
                e.AddItem(DefaultRule, "Default (global pattern)", "Use the pattern from Haptic Pattern settings");
                e.AddItem(MacNotifyPlugin.MuteRule, "Mute", "No vibration for this application");
                foreach (var (name, displayName) in HapticWaveforms.All)
                {
                    e.AddItem(name, displayName, null);
                }

                var selectedApp = e.ActionEditorState.GetControlValue(AppControl);
                var selected = DefaultRule;
                if (!String.IsNullOrEmpty(selectedApp) &&
                    this.HapticsPlugin.AppRules.TryGetValue(selectedApp, out var rule))
                {
                    selected = rule;
                }
                e.SetSelectedItemName(selected);
            }
        }

        private void OnControlValueChanged(Object sender, ActionEditorControlValueChangedEventArgs e)
        {
            if (e.ControlName.EqualsNoCase(AppControl))
            {
                // Refresh the pattern dropdown to show the selected app's rule.
                this.ActionEditor.ListboxItemsChanged(PatternControl);
                return;
            }

            if (!e.ControlName.EqualsNoCase(PatternControl))
            {
                return;
            }

            var app = e.ActionEditorState.GetControlValue(AppControl);
            var pattern = e.ActionEditorState.GetControlValue(PatternControl);
            this.ApplyRule(app, pattern);
        }

        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            this.ApplyRule(
                actionParameters.GetString(AppControl, null),
                actionParameters.GetString(PatternControl, DefaultRule));
            return true;
        }

        private void ApplyRule(String app, String pattern)
        {
            if (String.IsNullOrEmpty(app) || String.IsNullOrEmpty(pattern))
            {
                return;
            }

            this.HapticsPlugin.SetAppRule(app, pattern == DefaultRule ? null : pattern);

            if (HapticWaveforms.IsValid(pattern))
            {
                this.HapticsPlugin.PlayHaptic(pattern, isPreview: true);
            }
            else if (pattern == DefaultRule)
            {
                this.HapticsPlugin.PlayHaptic(isPreview: true);
            }
        }
    }
}
