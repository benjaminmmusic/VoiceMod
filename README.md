# VoiceMod

Real-time voice modulation desktop app for Windows. Captures audio from a physical microphone, applies effects, and outputs to a virtual microphone (VB-Cable) so any voice chat or in-game voice channel hears the modulated voice.

**Status:** Phase 2 pilot. Live pitch-shift effect with a minimal WPF UI. Headless console build also available for testing.

## Prerequisites

You need three things installed on Windows before this builds and runs.

### 1. .NET 8 SDK (LTS)

The project targets `net8.0` (Console + Core) and `net8.0-windows` (App).

Download: <https://dotnet.microsoft.com/download/dotnet/8.0>

Pick the **SDK** installer, x64, latest patch (8.0.421 or newer).

Verify:
```
dotnet --list-sdks
```
Output should include a line starting with `8.0.`.

### 2. VB-Cable virtual audio device

Free virtual audio driver — the "fake microphone" we write modulated audio to. Discord, in-game voice chat, OBS etc. then read from VB-Cable as if it were a real mic.

Download: <https://vb-audio.com/Cable/>

Install steps:
1. Extract the ZIP to a real folder (e.g. `C:\Temp\VBCable\`). Do *not* run the installer from inside the ZIP preview — Windows extracts to temp and the driver install fails silently.
2. Right-click `VBCABLE_Setup_x64.exe` → **Properties** → tick **Unblock** at the bottom if present. Apply.
3. Right-click → **Run as administrator**.
4. Click **Install Driver** in the installer GUI. **Click once.** Clicking twice registers the driver twice and creates a duplicate device that fails to start. (Recovery: see [notes.md](notes.md) Phase 1 section.)
5. Reboot when prompted.

Verify (PowerShell):
```powershell
Get-PnpDevice -Class MEDIA | Where-Object { $_.FriendlyName -like '*CABLE*' }
```
You should see exactly one row, `Status: OK`.

### 3. Visual Studio Code with C# Dev Kit (recommended)

Visual Studio Community works too if you prefer.

VS Code: <https://code.visualstudio.com/download>

C# Dev Kit extension:
```
code --install-extension ms-dotnettools.csdevkit
```

## Build and run

Clone the repo, then from the repo root:

```
dotnet build VoiceMod.sln
```

### Run the WPF app (Phase 2 GUI)

```
dotnet run --project src/VoiceMod.App
```

A small window opens with Input / Output device dropdowns, a Pitch slider, and Start / Stop buttons.

### Run the console harness (Phase 1 style)

Headless test path — useful for verifying audio routing when the UI is broken or for scripted testing.

```
dotnet run --project src/VoiceMod.Console
```

It prompts for input device index, output device index, then an optional pitch value, then starts streaming. Press any key to stop.

## Usage

1. Pick your physical microphone in the **Input** dropdown.
2. Pick **CABLE Input (VB-Audio Virtual Cable)** in the **Output** dropdown.
3. Drag the **Pitch** slider where you want it (`-12` to `+12` semitones; `0` is straight passthrough).
4. Click **Start**.
5. In your voice chat app (Discord, in-game voice, etc.), set the microphone input to **CABLE Output (VB-Audio Virtual Cable)**.
6. Talk. The other side hears your modulated voice.
7. Click **Stop** when done.

## Tech stack

- **Language:** C# 12, .NET 8
- **Audio I/O:** [NAudio](https://github.com/naudio/NAudio) using WASAPI shared-mode capture and render
- **Pitch shifting:** [SoundTouch.Net](https://github.com/owencjones/SoundTouch.Net)
- **GUI:** WPF (code-behind, no MVVM yet)
- **Virtual mic:** VB-Cable (system driver, installed separately)

## Project layout

```
VoiceMod/
├── VoiceMod.sln
├── src/
│   ├── VoiceMod.Core/      class library — audio pipeline + effects (shared)
│   ├── VoiceMod.Console/   headless test harness
│   └── VoiceMod.App/       WPF UI
├── voice-modulation-plan.md   roadmap, phase by phase
├── notes.md                   running log of decisions, observations, known quirks
├── CLAUDE.md                  instructions for Claude Code sessions in this repo
└── README.md                  this file
```

## Documentation

- **[voice-modulation-plan.md](voice-modulation-plan.md)** — the phase-by-phase project plan, scope, and out-of-scope items.
- **[notes.md](notes.md)** — running notes file. Every decision, every observation, every "why we did it this way" lives here. Read this if you want context on why the code looks the way it does.
- **[CLAUDE.md](CLAUDE.md)** — instructions for Claude Code sessions. If you use Claude Code, this file is auto-loaded and shapes how the AI assistant behaves in this repo (collaboration style, code conventions, junior-dev guidance).

## Troubleshooting

**App starts but no sound reaches the receiving end of a voice call.**

- In Windows Sound settings (right-click speaker icon → Sound settings), make sure **CABLE Input** is *not* set as your default playback device. It needs to be the **Output** in our app, not the system-wide default. Discord etc. must have **CABLE Output** as their microphone input.

**VB-Cable installer asks for admin, you click yes, then nothing happens.**

- See the installer notes in [Prerequisites → VB-Cable](#2-vb-cable-virtual-audio-device) above. Most common cause: running the installer from inside the ZIP preview without extracting first.

**App throws `NotSupportedException: Pipeline currently expects IEEE float 32-bit capture`.**

- Your selected microphone's Windows shared-mode mix format isn't 32-bit float. Right-click speaker icon → Sound settings → choose the mic under Input devices → Properties → check Default Format. Switch to *24 bit, 48000 Hz* or similar and reopen the app. (A future phase will add automatic format conversion.)

**Trailing audio gets cut off, or pieces of speech go missing mid-sentence on Discord.**

- This is Discord's own noise gate / voice activity detection, not our pipeline. Verify by recording from **CABLE Output** in Audacity — the recording will be continuous. In Discord: Settings → Voice & Video → Input Sensitivity slider. Raising it (less aggressive gate) usually fixes it.

**Popping or clicking sounds during pitch-shifted audio.**

- Should be gone in the current build. If you hear them, file an issue with the pitch value, sample rate, and which device combo you're using. See [notes.md](notes.md) for the SoundTouch tuning history that addresses this.
