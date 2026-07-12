namespace Loupedeck.MacNotifyPlugin
{
    using System;

    // Manual trigger for verifying the haptic pipeline without waiting
    // for a real macOS notification. Bind it to a button in Logi Options+.

    public class TestHapticCommand : PluginDynamicCommand
    {
        public TestHapticCommand()
            : base(displayName: "Test Haptic", description: "Plays the configured notification haptic", groupName: "Test")
        {
        }

        protected override void RunCommand(String actionParameter) =>
            ((MacNotifyPlugin)this.Plugin).PlayHaptic(isPreview: true);
    }
}
