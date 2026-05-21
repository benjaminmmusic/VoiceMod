# VoiceMod Notes

Running log of observations, decisions, side quests, and known issues. Not curated. The plan file (`voice-modulation-plan.md`) stays untouched; this is the messy companion.

---

## Phase 1 — Skeleton (passthrough)

### Setup quirks worth remembering

- **VB-Cable double-install** (2026-05-21). The installer GUI was clicked twice during install, which registered the driver twice. Result: two `VB-Audio Virtual Cable` devices in `Get-PnpDevice -Class MEDIA`, one `Status: OK` (`ROOT\MEDIA\0000`) and one `Status: Error` with `Problem: CM_PROB_FAILED_START` (`ROOT\MEDIA\0001`). Fix was `pnputil /remove-device "ROOT\MEDIA\0001"` from an elevated PowerShell. Watch for this if VB-Cable ever needs reinstalling.
- **VB-Cable Control Panel "not installed" error**. Immediately after removing the duplicate device, `VBCABLE_ControlPanel.exe` refused to launch with a popup saying *"VB-Audio Virtual Cable is not installed"*. A reboot fixed it — driver registration apparently needed a clean cycle after the pnputil removal. Worth knowing if the control panel ever flakes again.
- **VB-Cable internal config confirmed**: Internal SR = 48000 Hz; channel labels exposed are FL/FR/FC/LF/RL/RR/SL/SR (driver supports up to 8 active for 7.1, adapts to whatever the app actually writes). Shared-mode mix advertised to Windows is 16-bit 48 kHz stereo by default.
- **Win11 Sound Settings UI hides channel count for VB-Cable's Default Format dropdown.** Both new Settings and the classic `mmsys.cpl` Advanced tab show only bit-depth + sample-rate (e.g. *"16 bit, 48000 Hz (DVD Quality)"*) — no "2 channel" prefix like real hardware mics show. Don't waste time trying to find a channel selector; VB-Cable's own control panel is the authoritative source for its internal channel config.

### Design decisions locked for Phase 1

- **Audio API: WASAPI shared mode** (NAudio's `WasapiCapture` + `WasapiOut`).
  - Why: WaveIn/MME alone burns 50–200 ms before any effect runs, blowing the Phase 2 <50 ms budget. WASAPI shared lands at 10–30 ms and still lets other apps use the mic. Exclusive mode would lock out Discord etc.
- **Format: read each device's WASAPI mix format; don't try to force**.
  - Why: in shared mode the capture format is fixed to the device's mix format. NAudio's `WasapiCapture.WaveFormat` is read-after-construction. `WasapiOut.Init(IWaveProvider)` internally resamples if the source format doesn't match the render device's mix format, so we pass the captured format straight through and let WasapiOut handle conversion. All three endpoints (mic, CABLE Input, CABLE Output) are 16-bit 48 kHz stereo at the Default Format level, so conversion should be a no-op or near-no-op.
- **Buffer: `BufferedWaveProvider`, 200 ms duration, `DiscardOnBufferOverflow = true`**.
  - Why: NAudio's built-in producer/consumer between the capture event thread and the render thread. 200 ms is way more than we need for steady-state (~10 ms of headroom would suffice) but tolerates jitter without underruns. Drop-oldest on overflow prevents drift if render ever stalls. Revisit this number in Phase 2 once we know the effect-processing CPU cost.
- **`WasapiCapture` constructed with `useEventSync: true` and `audioBufferMillisecondsLength: 20`** for low-latency event-driven capture.
- **`WasapiOut` constructed with `useEventSync: true` and `latency: 30`** (30 ms requested render latency; actual is driver-dependent but usually close).
- **UI: console with index prompt + any-key-stop**. List devices with indices, prompt for input idx then output idx, start streaming immediately, `Console.ReadKey` blocks until stopped. Will be replaced by WPF in Phase 2 — no point investing in CLI args until then.
- **Project structure**: `VoiceMod.sln` + `src/VoiceMod.Console/` (net8.0, NAudio 2.3.0). Solution file is technically overkill for one project but pays off in Phase 4+ when we add effect projects.

### Observations during testing

#### 2026-05-21 — Audio tail through VB-Cable feels worse than direct mic into Discord

When Discord's input device is the physical mic, the trailing edge of speech "tapers off" cleanly into silence. When piping the mic through our app → VB-Cable → Discord, the trailing edge feels less clean — speech seems to linger slightly or the silence afterwards is noisier. Phase 1 success criterion is met (audio is transmitted) but quality is subjectively lower than baseline.

Likely contributing factors, in rough order of suspicion:

1. **Buffer drain delay (most likely cause of the "lingering" tail).** Our 200 ms `BufferedWaveProvider` means at the instant you stop speaking, there are still up to 200 ms of buffered samples queued for render. Discord-direct sees your real silence the moment your mouth stops; Discord-via-cable sees ~200 ms of "speech still arriving". Reducing the buffer in Phase 2 (target maybe 30–50 ms once we measure effect CPU cost) should remove most of this. Cannot drop it below the WASAPI capture/render buffer sums (~50 ms combined here) or we'll get underruns.
2. **Discord's noise suppression interacts differently with cable audio than with a raw mic.** Discord's mic processing pipeline (Krisp / RNNoise / built-in suppression — depends on settings) is tuned for real microphone signal characteristics: room tone, breath noise, mic self-noise, DC offset, etc. The VB-Cable signal we feed it is a perfectly clean stream — no room tone, no analog floor. Discord's noise gate / suppressor calibrates against the noise floor it observes, and a zero-noise input can confuse it, causing it to gate more aggressively or release more slowly. Worth testing with Discord noise suppression turned OFF on both the direct and cable paths to see if the difference shrinks.
3. **No noise gate in our own pipeline.** Direct path: Discord gates raw mic before transmission. Cable path: we pass the raw mic through unchanged, then Discord still gates (but gates the cable signal — see #2). If we added a simple gate before writing to VB-Cable, Discord's gate might behave more like the direct case. This is exactly the open question on the plan's list — adding a noise gate before processing. Not a Phase 1 task, but the perceived quality difference is the first concrete reason to add one.
4. **Sample format conversion artifacts.** If the mic's mix format and VB-Cable Input's mix format aren't byte-for-byte identical, `WasapiOut` is internally resampling/converting. Per the device dropdowns both endpoints look like 16-bit 48 kHz, but Windows' shared-mode mix format is often IEEE float 32-bit internally even when the user-visible "Default Format" says 16-bit. So a float-32 → int-16 conversion may be happening twice (engine → driver at each end). Should be inaudible but worth logging the actual `WaveFormat` strings the app prints and confirming both are float-32 (or both int-16) before chasing this further.
5. **WASAPI shared-mode latency stack adds at every stage**: mic driver buffer → Windows engine → our capture event → our 200 ms buffer → WasapiOut buffer → VB-Cable driver buffer → Windows engine again → Discord's input buffer. Each adds a few ms and a touch of jitter. Reducing #1 is the biggest lever; the rest are baked into the OS and we can't really shrink them.

What to test next (deferred to Phase 2 or earlier if it bugs us):

- Print the exact `WaveFormat.ToString()` for capture and render and confirm they're identical (or both float-32) — rules out factor #4.
- Try shrinking `BufferDuration` from 200 ms to 50 ms and see if the tail tightens — confirms factor #1.
- Disable Discord's noise suppression on both paths and compare — isolates factor #2.

Not blocking Phase 1 sign-off. User explicitly said it works and is good enough for now.

### Open items / things to revisit

- Noise gate before processing — plan's open question, now has a concrete justification (the audio tail observation).
- Buffer size tuning — revisit once effect-processing CPU cost is measured in Phase 2.
- Device disconnection handling — deferred to Phase 5 per plan, but if either device drops mid-session right now the app probably crashes ugly. Not worth fixing yet.
- Sample rate mismatch handling — if any device ever defaults to 44.1 kHz instead of 48 kHz, `WasapiOut.Init` should still work (it resamples), but Phase 1 hasn't been tested against that case.
- `VBCABLE_ControlPanel.exe` reliability — opened fine after reboot but the install-time error suggests the registration is fragile. Note for the Phase 5 "first-run experience" that detects VB-Cable: don't rely on the control panel binary as the detection check.
