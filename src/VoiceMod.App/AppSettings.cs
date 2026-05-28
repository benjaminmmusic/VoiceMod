namespace VoiceMod.App;

public sealed record AppSettings
{
    public string? InputDeviceId { get; init; }
    public string? OutputDeviceId { get; init; }
    public string EffectMode { get; init; } = "Pitch";
    public int PitchSemitones { get; init; }
    public int RingModFrequencyHz { get; init; }
    public float PitchRampRate { get; init; } = 0.15f;
    public float EchoDelayMs { get; init; }
    public float EchoVolume { get; init; }
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }

}