using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceMod.Core;

public enum EffectMode
{
    Pitch,
    RingMod,
    Echo,
}

public sealed class Pipeline : IDisposable
{
    private const int CaptureBufferMs = 20;
    private const int RenderLatencyMs = 30;
    private const int JitterBufferMs = 50;

    private readonly WasapiCapture _capture;
    private readonly BufferedWaveProvider _jitterBuffer;
    private readonly WasapiOut _output;
    private readonly PitchEffect _pitch;
    private readonly RingModEffect _ringMod;
    private readonly EchoEffect _echo;

    public EffectMode Mode { get; set;} = EffectMode.Pitch;
   
    /// <summary>
    /// Initializes a new Pipeline that captures audio from the specified input device, applies selectable processing effects, and routes the processed audio to the specified output device.
    /// </summary>
    /// <param name="input">The capture (input) audio device used for recording.</param>
    /// <param name="output">The render (output) audio device used for playback.</param>
    /// <exception cref="NotSupportedException">Thrown when the capture device's wave format is not 32-bit IEEE float; the capture is disposed before this exception is thrown.</exception>
    public Pipeline(MMDevice input, MMDevice output)
    {
        _capture = new WasapiCapture(input, useEventSync: true, audioBufferMillisecondsLength: CaptureBufferMs);

        var format = _capture.WaveFormat;
        if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
        {
            _capture.Dispose();
            throw new NotSupportedException(
                $"Pipeline currently expects IEEE float 32-bit capture; device reports {format}.");
        }

        _pitch = new PitchEffect(format.SampleRate, format.Channels);

        _ringMod = new RingModEffect(format.SampleRate, format.Channels);

        _echo = new EchoEffect(format.SampleRate, format.Channels);

        _jitterBuffer = new BufferedWaveProvider(format)
        {
            BufferDuration = TimeSpan.FromMilliseconds(JitterBufferMs),
            DiscardOnBufferOverflow = true,
        };

        _output = new WasapiOut(output, AudioClientShareMode.Shared, useEventSync: true, latency: RenderLatencyMs);

        _capture.DataAvailable += OnCaptureDataAvailable;
        _output.Init(_jitterBuffer);
    }

    public WaveFormat Format => _capture.WaveFormat;

    public float PitchSemitones
    {
        get => _pitch.Semitones;
        set => _pitch.Semitones = value;
    }

    public float PitchRampRate
    {
        get => _pitch.RampSemitonesPerBlock;
        set => _pitch.RampSemitonesPerBlock = value;
    }

    public float RingModFrequency
    {
        get => _ringMod.FrequencyHz;
        set => _ringMod.FrequencyHz = value;
    }

    public float EchoDelayMs
    {
        get => _echo.DelayMs;
        set => _echo.DelayMs = value;
    }

    public float EchoVolume
    {
        get => _echo.EchoVolume;
        set => _echo.EchoVolume = value;
    }

    /// <summary>
    /// Begins audio capture from the input device and starts audio output playback.
    /// </summary>
    public void Start()
    {
        _capture.StartRecording();
        _output.Play();
    }

    public void Stop()
    {
        _output.Stop();
        _capture.StopRecording();
    }

    public void Dispose()
    {
        _capture.DataAvailable -= OnCaptureDataAvailable;
        _capture.Dispose();
        _output.Dispose();
    }

    /// <summary>
    /// Handles incoming captured audio by applying the currently selected effect and writing the processed samples into the pipeline's jitter buffer.
    /// </summary>
    /// <param name="sender">The event source; may be null.</param>
    /// <param name="e">Contains the captured audio buffer and the number of bytes recorded; the handler processes the recorded samples and forwards them to the jitter buffer.</param>
    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        switch (Mode)
        {
            case EffectMode.Pitch:
                _pitch.Process(e.Buffer.AsSpan(0, e.BytesRecorded), _jitterBuffer);
                break;
            case EffectMode.RingMod:
                _ringMod.Process(e.Buffer.AsSpan(0, e.BytesRecorded), _jitterBuffer);
                break;
            case EffectMode.Echo:
                _echo.Process(e.Buffer.AsSpan(0, e.BytesRecorded), _jitterBuffer);
                break;
        } 
    }
}
