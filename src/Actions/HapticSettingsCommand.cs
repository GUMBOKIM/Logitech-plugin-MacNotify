namespace Loupedeck.MacNotifyPlugin
{
    using System;

    // Lets the user pick the notification vibration pattern from a dropdown
    // in the Options+ action editor. Selecting a pattern saves it globally
    // and plays it once as a preview; pressing the assigned button replays
    // the current pattern.

    public class HapticSettingsCommand : ActionEditorCommand
    {
        private const String WaveformControl = "waveform";

        private MacNotifyPlugin HapticsPlugin => (MacNotifyPlugin)this.Plugin;

        public HapticSettingsCommand()
        {
            this.DisplayName = "Haptic Pattern";
            this.Description = "Choose the vibration pattern played when a macOS notification arrives";
            this.GroupName = "Settings";

            this.ActionEditor.AddControlEx(
                new ActionEditorListbox(WaveformControl, "Vibration pattern"));
            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;
            this.ActionEditor.ControlValueChanged += this.OnControlValueChanged;
        }

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(WaveformControl))
            {
                return;
            }

            foreach (var (name, displayName) in HapticWaveforms.All)
            {
                e.AddItem(name, displayName, null);
            }
            e.SetSelectedItemName(this.HapticsPlugin.CurrentWaveform);
        }

        private void OnControlValueChanged(Object sender, ActionEditorControlValueChangedEventArgs e)
        {
            if (!e.ControlName.EqualsNoCase(WaveformControl))
            {
                return;
            }

            var waveform = e.ActionEditorState.GetControlValue(WaveformControl);
            if (HapticWaveforms.IsValid(waveform))
            {
                this.HapticsPlugin.CurrentWaveform = waveform;
                this.HapticsPlugin.PlayHaptic(waveform, isPreview: true);
            }
        }

        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            var waveform = actionParameters.GetString(WaveformControl, this.HapticsPlugin.CurrentWaveform);
            if (HapticWaveforms.IsValid(waveform))
            {
                this.HapticsPlugin.CurrentWaveform = waveform;
            }
            this.HapticsPlugin.PlayHaptic(isPreview: true);
            return true;
        }
    }
}
