namespace Loupedeck.MacNotifyPlugin
{
    using System;
    using System.Collections.Generic;

    // The 15 haptic waveforms supported by the SDK. Each one is registered
    // as a plugin event named "notif_<waveform>" and mapped to its waveform
    // in eventMapping.yaml, so the active vibration pattern can be switched
    // at runtime by raising a different event.
    internal static class HapticWaveforms
    {
        public const String Default = "happy_alert";

        public static readonly IReadOnlyList<(String Name, String DisplayName)> All = new[]
        {
            ("happy_alert", "Happy Alert"),
            ("angry_alert", "Angry Alert"),
            ("completed", "Completed"),
            ("knock", "Knock"),
            ("jingle", "Jingle"),
            ("ringing", "Ringing"),
            ("wave", "Wave"),
            ("firework", "Firework"),
            ("square", "Square"),
            ("mad", "Mad"),
            ("sharp_state_change", "Sharp State Change"),
            ("damp_state_change", "Damp State Change"),
            ("sharp_collision", "Sharp Collision"),
            ("damp_collision", "Damp Collision"),
            ("subtle_collision", "Subtle Collision"),
        };

        public static String EventName(String waveform) => $"notif_{waveform}";

        public static Boolean IsValid(String waveform)
        {
            foreach (var (name, _) in All)
            {
                if (name == waveform)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
