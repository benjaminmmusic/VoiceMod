# Project Plan: Voice Modulation Software

## Goal

A Windows desktop application that captures audio from a physical microphone, applies real-time voice modulation effects, and outputs the result to a virtual microphone so that voice chat applications (Discord, Teams, etc.) receive the modulated audio.

## Target Users

Non-developers who want a free alternative to commercial voice modulation software.

## Tech Stack

C# with .NET 8 (LTS), NAudio for audio I/O, WPF for the GUI, VB-Cable as the virtual microphone driver. Primary development in VS Code with the C# Dev Kit extension. Visual Studio available for WPF designer work and debugging.

## Phase 1: Skeleton [Done]

Establish the audio pipeline with no processing. The application reads samples from a selected input device and writes them unchanged to a selected output device. Success criterion: with VB-Cable installed, run the skeleton, configure a voice chat application to read from VB-Cable's output, and confirm unmodified voice is transmitted on a call. Console application is acceptable at this stage. No GUI required. Includes device enumeration, format negotiation between devices, a ring buffer between capture and render threads, and basic start/stop logic.

## Phase 2: Pilot [Done]

Add a single pitch shift effect to the pipeline. Convert the console application to a minimal WPF window with an input device dropdown, output device dropdown, start/stop button, and one slider for pitch amount. Success criterion: on a voice chat call, the receiving party hears a pitch-shifted voice with acceptable latency (target under 50ms end-to-end). Requires choosing a pitch shift algorithm (PSOLA for simplicity, or a phase vocoder for higher quality).

## Phase 3: Second Effect

Add a second effect with simple math and immediate audible feedback, such as ring modulation (robot voice) or a delay line (echo). Add a corresponding slider to the UI. Focus on extending within the existing architecture rather than refactoring it.

## Phase 4: Effect Chain and Presets

Refactor the pipeline to support multiple simultaneous, ordered effects. Add named presets (Robot, Demon, Chipmunk, etc.) that combine effects with preset parameter values. Expand the UI to show available effects and preset selection.

## Phase 5: Polish and Distribution

Application icon, installer (self-contained executable wrapped with Inno Setup, or MSIX), settings persistence, error handling for edge cases (device disconnection, sample rate mismatches, VB-Cable not installed), and a first-run experience that detects VB-Cable and links to the installer if missing. Goal is a downloadable application usable without technical support.

## Out of Scope

Neural voice conversion (RVC-style voice cloning), custom virtual audio driver (VB-Cable is the assumed dependency), cross-platform support, plugin formats (VST/AU), networking or cloud features.

## Open Questions

Which pitch shift algorithm to use in Phase 2 (revisit after Phase 1 establishes the latency budget). Whether to add a basic noise gate before processing (improves output quality on cheap microphones but adds complexity). How to handle the case where VB-Cable is not installed (detect and prompt, or document as a manual prerequisite).
