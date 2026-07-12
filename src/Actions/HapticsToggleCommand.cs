namespace Loupedeck.MacNotifyPlugin
{
    using System;

    // On/off switch for notification haptics. The button shows the current
    // state, so it can be flipped quickly during meetings or focus time.

    public class HapticsToggleCommand : PluginMultistateDynamicCommand
    {
        private MacNotifyPlugin HapticsPlugin => (MacNotifyPlugin)this.Plugin;

        public HapticsToggleCommand()
            : base("Notification Haptics On/Off", "Toggles haptic feedback for macOS notifications", "Settings")
        {
            this.AddState("Off", "Notification haptics are muted");
            this.AddState("On", "Notification haptics are active");
        }

        protected override Boolean OnLoad()
        {
            this.SetCurrentState(this.HapticsPlugin.HapticsEnabled ? 1 : 0);
            return true;
        }

        protected override void RunCommand(String actionParameter)
        {
            var enabled = this.ToggleCurrentState() == 1;
            this.HapticsPlugin.HapticsEnabled = enabled;
            if (enabled)
            {
                this.HapticsPlugin.PlayHaptic(isPreview: true);
            }
        }

        protected override String GetCommandDisplayName(String actionParameter, Int32 stateIndex, PluginImageSize imageSize) =>
            stateIndex == 1 ? "Haptics\nON" : "Haptics\nOFF";
    }
}
