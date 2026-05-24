using System.Runtime.InteropServices;
using NAudio.Wave;

namespace VoiceMod.Core;

public sealed class RingModEffect
{
    private readonly int _sampleRate;

    public RingModEffect(int sampleRate, int channels)
    {
        _sampleRate = sampleRate;
    }

    public float FrequencyHz { get; set; }

    public void Process(ReadOnlySpan<byte> inputBytes, BufferedWaveProvider output)
    {
        if (FrequencyHz == 0f)
        {
            var passthrough = inputBytes.ToArray();
            output.AddSamples(passthrough, 0, passthrough.Length);
            return;
        }

        var inputFloats = MemoryMarshal.Cast<byte, float>(inputBytes);
        var outputFloats = new float[inputFloats.Length];

        // TODO: ring modulation math — fill outputFloats based on inputFloats
        for (int i = 0; i < inputFloats.Length; i++)
        {
            double time = (double)i / _sampleRate;
            outputFloats[i] = inputFloats[i] * (float)Math.Sin(2 * Math.PI * FrequencyHz * time);
        }

        var outBytes = MemoryMarshal.AsBytes(outputFloats.AsSpan()).ToArray();
        output.AddSamples(outBytes, 0, outBytes.Length);
    }
}
