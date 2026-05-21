using System.Buffers;
using System.Runtime.InteropServices;
using NAudio.Wave;
using SoundTouch;

namespace VoiceMod.Core;

public sealed class PitchEffect
{
    private readonly SoundTouchProcessor _processor;
    private readonly int _channels;
    private float _semitones;

    public PitchEffect(int sampleRate, int channels)
    {
        _channels = channels;
        _processor = new SoundTouchProcessor
        {
            SampleRate = sampleRate,
            Channels = channels,
        };
        _processor.SetSetting(SettingId.SequenceDurationMs, 60);
        _processor.SetSetting(SettingId.SeekWindowDurationMs, 20);
        _processor.SetSetting(SettingId.OverlapDurationMs, 10);
    }

    public float Semitones
    {
        get => _semitones;
        set
        {
            _semitones = value;
            _processor.PitchSemiTones = value;
        }
    }

    public void Process(ReadOnlySpan<byte> inputBytes, BufferedWaveProvider output)
    {
        if (_semitones == 0f)
        {
            // bypass: SoundTouch adds algorithmic latency even at zero shift
            var passthrough = inputBytes.ToArray();
            output.AddSamples(passthrough, 0, passthrough.Length);
            return;
        }

        var inputFloats = MemoryMarshal.Cast<byte, float>(inputBytes);
        var frameCount = inputFloats.Length / _channels;

        var inBuf = ArrayPool<float>.Shared.Rent(inputFloats.Length);
        var outBuf = ArrayPool<float>.Shared.Rent(inputFloats.Length * 2);

        try
        {
            inputFloats.CopyTo(inBuf);
            _processor.PutSamples(inBuf, frameCount);

            var produced = _processor.ReceiveSamples(outBuf, outBuf.Length / _channels);
            if (produced > 0)
            {
                var producedSamples = produced * _channels;
                var outBytes = MemoryMarshal.AsBytes(outBuf.AsSpan(0, producedSamples)).ToArray();
                output.AddSamples(outBytes, 0, outBytes.Length);
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(inBuf);
            ArrayPool<float>.Shared.Return(outBuf);
        }
    }
}
