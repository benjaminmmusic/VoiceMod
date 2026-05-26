using System.Runtime.InteropServices;
using NAudio.Wave;

namespace VoiceMod.Core;

public sealed class EchoEffect
{
    private readonly int _sampleRate;
    private readonly float[] _buffer;
    private int _writePos;

    public EchoEffect(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
        _buffer = new float[sampleRate * channels];
    }

    public float DelayMs { get; set; }
    public float EchoVolume { get; set; }

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
