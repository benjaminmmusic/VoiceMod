using System.Buffers;
using System.Runtime.InteropServices;
using NAudio.Wave;
using SoundTouch;

namespace VoiceMod.Core;

/// <summary>Ramps pitch changes block-by-block to avoid audible discontinuities at SoundTouch block boundaries.</summary>
public sealed class PitchEffect
{
    private readonly SoundTouchProcessor _processor;
    private readonly int _channels;
    private float _semitones;
    private float _appliedSemitones;

    /// <summary>Max semitones the ramp advances per capture block. Trades response speed for glide smoothness.</summary>
    public float RampSemitonesPerBlock { get; set; } = 0.15f;

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

    /// <summary>Target pitch in semitones. The applied value ramps toward this at <see cref="RampSemitonesPerBlock"/>/block.</summary>
    public float Semitones
    {
        get => _semitones;
        set => _semitones = value;
    }

    public void Process(ReadOnlySpan<byte> inputBytes, BufferedWaveProvider output)
    {
        AdvancePitchRamp();

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

    /// <summary>Per-block ramp step. Decouples slider events from SoundTouch's block boundaries to avoid discontinuity pops.</summary>
    private void AdvancePitchRamp()
    {
        if (_appliedSemitones == _semitones) return;

        var delta = _semitones - _appliedSemitones;
        if (Math.Abs(delta) <= RampSemitonesPerBlock)
        {
            _appliedSemitones = _semitones;
        }
        else
        {
            _appliedSemitones += Math.Sign(delta) * RampSemitonesPerBlock;
        }

        _processor.PitchSemiTones = _appliedSemitones;
    }
}
