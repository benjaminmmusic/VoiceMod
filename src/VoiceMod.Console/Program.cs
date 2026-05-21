using NAudio.CoreAudioApi;
using VoiceMod.Core;
using static System.Console;

using var enumerator = new MMDeviceEnumerator();

var inputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
var outputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

var inputDevice = PromptForDevice("Input (microphone)", inputDevices);
var outputDevice = PromptForDevice("Output (e.g. CABLE Input)", outputDevices);

using var pipeline = new Pipeline(inputDevice, outputDevice);

WriteLine();
WriteLine($"Routing  {inputDevice.FriendlyName}");
WriteLine($"     ->  {outputDevice.FriendlyName}");
WriteLine($"Format:  {pipeline.Format}");
Write("Pitch semitones (-12 to +12, blank for 0): ");
if (float.TryParse(ReadLine(), out var semis))
{
    pipeline.PitchSemitones = semis;
}
WriteLine("Press any key to stop...");

pipeline.Start();
ReadKey(intercept: true);
pipeline.Stop();

static MMDevice PromptForDevice(string label, List<MMDevice> devices)
{
    WriteLine();
    WriteLine($"=== {label} ===");
    for (var i = 0; i < devices.Count; i++)
    {
        WriteLine($"  [{i}] {devices[i].FriendlyName}");
    }

    while (true)
    {
        Write("Select index: ");
        var line = ReadLine();
        if (int.TryParse(line, out var idx) && idx >= 0 && idx < devices.Count)
        {
            return devices[idx];
        }
        WriteLine("Invalid index, try again.");
    }
}
