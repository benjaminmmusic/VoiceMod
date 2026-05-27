using System.Runtime.InteropServices;
using NAudio.Wave;

namespace VoiceMod.Core;

public sealed class EchoEffect
{
    private readonly int _sampleRate;
    private readonly float[] _buffer;
    private int _writePos;

    /// <summary>
    /// Initializes a new EchoEffect and allocates the internal circular buffer for interleaved samples.
    /// </summary>
    /// <param name="sampleRate">Audio sample rate in hertz used to convert delay milliseconds to sample counts.</param>
    /// <param name="channels">Number of interleaved audio channels; the internal buffer is sized to sampleRate * channels floats.</param>
    public EchoEffect(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _buffer = new float[sampleRate * channels];
    }

    public float DelayMs { get; set; }
    public float EchoVolume { get; set; }

    /// <summary>
    /// Processes interleaved float PCM samples from <paramref name="inputBytes"/>, mixes a delayed (echo) signal scaled by <see cref="EchoVolume"/>, updates the internal circular buffer state, and appends the resulting samples to <paramref name="output"/>.
    /// </summary>
    /// <param name="inputBytes">A span of bytes interpreted as little-endian IEEE 754 float samples (must be aligned to 4 bytes; length should be a multiple of sizeof(float)).</param>
    /// <param name="output">The BufferedWaveProvider that will receive the processed audio; processed samples are added via AddSamples.</param>
    public void Process(ReadOnlySpan<byte> inputBytes, BufferedWaveProvider output)
    {
        var inputFloats = MemoryMarshal.Cast<byte, float>(inputBytes);
        var outputFloats = new float[inputFloats.Length];

        int delaySamples = (int)((DelayMs / 1000f) * _sampleRate);

        for (int i = 0; i < inputFloats.Length; i++)
        {
            int readPos = (_writePos - delaySamples + _buffer.Length) % _buffer.Length ;
            
            float echo = _buffer[readPos];

            outputFloats[i] = inputFloats[i] + echo * EchoVolume;
            _buffer[_writePos] = outputFloats[i];
            _writePos = (_writePos + 1) % _buffer.Length;
        }

        var outBytes = MemoryMarshal.AsBytes(outputFloats.AsSpan()).ToArray();
        output.AddSamples(outBytes, 0, outBytes.Length);
    }
}
