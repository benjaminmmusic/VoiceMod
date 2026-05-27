using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using VoiceMod.Core;

namespace VoiceMod.App;

public partial class MainWindow : Window
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private Pipeline? _pipeline;

    public MainWindow()
    {
        InitializeComponent();
        LoadDevices();
        EffectCombo.SelectedIndex = 0;
    }

    private void LoadDevices()
    {
        foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            InputDeviceCombo.Items.Add(new DeviceItem(d));
        }
        foreach (var d in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            OutputDeviceCombo.Items.Add(new DeviceItem(d));
        }
    }

    /// <summary>
    /// Initializes and starts the audio processing pipeline using the currently selected input and output devices and the current UI effect settings, then updates the window controls and status to reflect running state.
    /// </summary>
    /// <param name="sender">The source of the click event.</param>
    /// <param name="e">Event data for the click event.</param>
    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (InputDeviceCombo.SelectedItem is not DeviceItem input ||
            OutputDeviceCombo.SelectedItem is not DeviceItem output)
        {
            StatusLabel.Text = "Status: select input and output devices first";
            return;
        }

        try
        {
            _pipeline = new Pipeline(input.Device, output.Device);
            _pipeline.PitchSemitones = (float)PitchSlider.Value;
            _pipeline.RingModFrequency = (float)RingModSlider.Value;
            _pipeline.EchoDelayMs = (float)EchoDelaySlider.Value;
            _pipeline.EchoVolume = (float)EchoVolumeSlider.Value;
            _pipeline.PitchRampRate = (float)RampSlider.Value;
            _pipeline.Mode = (EffectMode)EffectCombo.SelectedIndex;
            _pipeline.Start();

            InputDeviceCombo.IsEnabled = false;
            OutputDeviceCombo.IsEnabled = false;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusLabel.Text = $"Status: running ({_pipeline.Format})";
        }
        catch (Exception ex)
        {
            _pipeline?.Dispose();
            _pipeline = null;
            StatusLabel.Text = $"Status: error — {ex.Message}";
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _pipeline?.Stop();
        _pipeline?.Dispose();
        _pipeline = null;

        InputDeviceCombo.IsEnabled = true;
        OutputDeviceCombo.IsEnabled = true;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusLabel.Text = "Status: idle";
    }

    private void PitchSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PitchLabel is null) return;

        var value = (int)e.NewValue;
        PitchLabel.Text = value >= 0 ? $"+{value} semitones" : $"{value} semitones";

        if (_pipeline != null)
        {
            _pipeline.PitchSemitones = value;
        }
    }

    /// <summary>
    /// Updates the ring-modulation frequency label and, if a pipeline is active, applies the new frequency to the pipeline when the slider value changes.
    /// </summary>
    /// <remarks>
    /// The label text is set to "{value} Hz" where value is the slider's new integer value. If a pipeline exists, its RingModFrequency is updated to the same value.
    /// </remarks>
    private void RingModSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RingModLabel is null) return;

        var value = (int)e.NewValue;
        RingModLabel.Text = $"{value} Hz";

        if (_pipeline != null)
        {
            _pipeline.RingModFrequency = value;
        }
    }

    /// <summary>
    /// Updates the displayed echo delay and, if a pipeline is active, sets its echo delay when the slider value changes.
    /// </summary>
    private void EchoDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (EchoDelayLabel is null) return;

        var value = (int)e.NewValue;
        EchoDelayLabel.Text = $"{value} ms";

        if (_pipeline != null)
        {
            _pipeline.EchoDelayMs = value;
        }
    }

    /// <summary>
    /// Updates the echo volume label to the slider's value and, if a pipeline exists, applies that value to the pipeline's EchoVolume.
    /// </summary>
    /// <param name="sender">The slider control that raised the event.</param>
    /// <param name="e">Event data containing the new slider value in <c>e.NewValue</c>.</param>
    private void EchoVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (EchoVolumeLabel is null) return;

        var value = (float)e.NewValue;
        EchoVolumeLabel.Text = $"{value:F2}";

        if (_pipeline != null)
        {
            _pipeline.EchoVolume = value;
        }
    }

    /// <summary>
    /// Updates the pipeline's effect mode (if running) and collapses the rows belonging to non-selected effects so only the active effect's sliders are visible.
    /// </summary>
    private void EffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PitchSlider is null || RingModSlider is null || EchoDelaySlider is null || EchoVolumeSlider is null) return;

        if (_pipeline != null)
        {
            _pipeline.Mode = (EffectMode)EffectCombo.SelectedIndex;
        }

        var isPitch = EffectCombo.SelectedIndex == 0;
        var isRobot = EffectCombo.SelectedIndex == 1;
        var isEcho  = EffectCombo.SelectedIndex == 2;

        var pitchVis = isPitch ? Visibility.Visible : Visibility.Collapsed;
        var robotVis = isRobot ? Visibility.Visible : Visibility.Collapsed;
        var echoVis  = isEcho  ? Visibility.Visible : Visibility.Collapsed;

        PitchLabelLeft.Visibility = pitchVis;
        PitchRow.Visibility = pitchVis;
        RampLabelLeft.Visibility = pitchVis;
        RampRow.Visibility = pitchVis;
        RingModLabelLeft.Visibility = robotVis;
        RingModRow.Visibility = robotVis;
        EchoDelayLabelLeft.Visibility = echoVis;
        EchoDelayRow.Visibility = echoVis;
        EchoVolumeLabelLeft.Visibility = echoVis;
        EchoVolumeRow.Visibility = echoVis;
    }

    /// <summary>
    /// Updates the ramp-rate label and applies the new value to the running pipeline's pitch ramp.
    /// </summary>
    private void RampSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RampLabel is null) return;

        var value = (float)e.NewValue;
        RampLabel.Text = $"{value:F2} /block";

        if (_pipeline != null)
        {
            _pipeline.PitchRampRate = value;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _pipeline?.Stop();
        _pipeline?.Dispose();
        _enumerator.Dispose();
        base.OnClosed(e);
    }

    private sealed record DeviceItem(MMDevice Device)
    {
        public override string ToString() => Device.FriendlyName;
    }
}
