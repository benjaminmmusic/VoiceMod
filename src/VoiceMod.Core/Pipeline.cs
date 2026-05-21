using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceMod.Core;

public sealed class Pipeline : IDisposable
{
    private const int CaptureBufferMs = 20;
    private const int RenderLatencyMs = 30;
    private const int JitterBufferMs = 50;

    private readonly WasapiCapture _capture;
    private readonly BufferedWaveProvider _jitterBuffer;
    private readonly WasapiOut _output;
    private readonly PitchEffect _pitch;

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

    private void OnCaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        _pitch.Process(e.Buffer.AsSpan(0, e.BytesRecorded), _jitterBuffer);
    }
}
