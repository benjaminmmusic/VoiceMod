using NAudio.CoreAudioApi;
using NAudio.Wave;
using static System.Console;

using var enumerator = new MMDeviceEnumerator();

var inputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
var outputDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

var inputDevice = PromptForDevice("Input (microphone)", inputDevices);
var outputDevice = PromptForDevice("Output (e.g. CABLE Input)", outputDevices);

using var capture = new WasapiCapture(inputDevice, useEventSync: true, audioBufferMillisecondsLength: 20);

var buffer = new BufferedWaveProvider(capture.WaveFormat)
{
    BufferDuration = TimeSpan.FromMilliseconds(200),
    DiscardOnBufferOverflow = true,
};

using var output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, useEventSync: true, latency: 30);

capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

output.Init(buffer);

WriteLine();
WriteLine($"Routing  {inputDevice.FriendlyName}");
WriteLine($"     ->  {outputDevice.FriendlyName}");
WriteLine($"Format:  {capture.WaveFormat}");
WriteLine("Press any key to stop...");

capture.StartRecording();
output.Play();

ReadKey(intercept: true);

capture.StopRecording();
output.Stop();

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
