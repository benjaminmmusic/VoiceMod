using System.Buffers;
using System.Runtime.InteropServices;
using NAudio.Wave;
using SoundTouch;

namespace VoiceMod.Core;

/// <summary>
/// Real-time pitch shifter built on SoundTouch. Pitch changes are ramped over multiple capture blocks
/// to avoid the audible discontinuities that direct PitchSemiTones writes produce at SoundTouch's
/// internal block boundaries.
/// </summary>
public sealed class PitchEffect
{
    private readonly SoundTouchProcessor _processor;
    private readonly int _channels;
    private float _semitones;
    private float _appliedSemitones;

    /// <summary>
    /// Maximum semitones the applied pitch may change per capture block (~20 ms at 50 blocks/sec).
    /// Higher = snappier slider response. Lower = smoother glide between values and fewer
    /// parameter-change artifacts at small pitch values.
    /// </summary>
    public float RampSemitonesPerBlock { get; set; } = 0.15f;

    /// <summary>
    /// Creates the effect for a specific capture format.
    /// </summary>
    /// <param name="sampleRate">Capture sample rate in Hz.</param>
    /// <param name="channels">Number of interleaved audio channels (e.g. 2 for stereo).</param>
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

    /// <summary>
    /// Target pitch shift in semitones. The actual applied pitch ramps toward this value over
    /// subsequent capture blocks at <see cref="RampSemitonesPerBlock"/> per block.
    /// </summary>
    public float Semitones
    {
        get => _semitones;
        set => _semitones = value;
    }

    /// <summary>
    /// Advances the pitch ramp, runs the captured samples through SoundTouch, and writes any
    /// produced output to the jitter buffer.
    /// </summary>
    /// <param name="inputBytes">IEEE float 32-bit interleaved samples captured from the input device.</param>
    /// <param name="output">Buffer feeding the render side of the pipeline.</param>
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

    /// <summary>
    /// Steps the applied pitch toward the target by at most <see cref="RampSemitonesPerBlock"/>,
    /// then writes the new value to SoundTouch. Called once per <see cref="Process"/> so pitch
    /// changes happen at block boundaries rather than per-event, avoiding audible discontinuities
    /// from rapid setter calls during slider drag.
    /// </summary>
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
