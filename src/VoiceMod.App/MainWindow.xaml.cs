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


    private void EffectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PitchSlider is null || RingModSlider is null || EchoDelaySlider is null || EchoVolumeSlider is null) return;

        if (_pipeline != null)
        {
            _pipeline.Mode = (EffectMode)EffectCombo.SelectedIndex;
        }

        if (EffectCombo.SelectedIndex == 0)
        {
            PitchSlider.IsEnabled = true;
            RingModSlider.IsEnabled = false;
            EchoDelaySlider.IsEnabled = false;
            EchoVolumeSlider.IsEnabled = false;
        }
        else if (EffectCombo.SelectedIndex == 1)
        {
            PitchSlider.IsEnabled = false;
            RingModSlider.IsEnabled = true;
            EchoDelaySlider.IsEnabled = false;
            EchoVolumeSlider.IsEnabled = false;
        }
        else
        {
            PitchSlider.IsEnabled = false;
            RingModSlider.IsEnabled = false;
            EchoDelaySlider.IsEnabled = true;
            EchoVolumeSlider.IsEnabled = true;
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
