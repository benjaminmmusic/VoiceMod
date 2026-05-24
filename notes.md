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

---

## Phase 2 — Pitch shift + WPF

### Decisions

- **Library: SoundTouch.Net 2.3.2.** Time-domain pitch shifter (hybrid time-stretch + resample). Battle-tested, real-time capable, ~20–40 ms algorithmic delay. Alternative would be a phase vocoder (frequency-domain) — higher quality but +40 ms latency and bigger code change. Revisit if SoundTouch artifacts become unacceptable.
- **Effect runs on the capture thread.** `OnCaptureDataAvailable` → `PitchEffect.Process` → `BufferedWaveProvider`. Render thread reads from the buffer separately. Capture thread is event-driven, not real-time-critical, so a few ms of processing there is fine.
- **Zero-semitone bypass.** When pitch is exactly 0, skip SoundTouch entirely — avoids its algorithmic latency for the most common idle state. Trade-off: at-runtime transitions between bypass and SoundTouch can briefly glitch.
- **Jitter buffer = 50 ms** (`BufferedWaveProvider.BufferDuration`). Down from 200 ms in Phase 1. `DiscardOnBufferOverflow = true` — drop oldest if render stalls, prefer bounded latency over no-loss.

### SoundTouch tuning history (the warble vs. clicks trade-off)

SoundTouch exposes three knobs: `SequenceDurationMs` (size of the chunks the algorithm processes), `SeekWindowDurationMs` (how far it can search for a good cut point inside each chunk), `OverlapDurationMs` (how long the cross-fade between chunks is). All three are in milliseconds. Bigger = smoother sustained tones but audible clicks at block boundaries on transients and longer trailing buffer at the end of speech. Smaller = no clicks, low latency, but the periodic modulation cycle ("robot/warble") becomes audible when the cycle frequency enters the audible range (~20–80 Hz, i.e. block durations under ~50 ms).

- **Phase 1 / pre-tuning**: defaults (~82 / 28 / 12). Clicks on transients, noticeable trailing cut-off at negative semitones.
- **First tuning attempt**: 40 / 15 / 8. Removed clicks entirely but introduced robot/warble at higher positive semitones (overlap cycle around 25 Hz, audibly modulating the carrier).
- **Current**: 60 / 20 / 10. Middle ground, signed off as acceptable for the pilot. At ±6 semitones it sounds clean; at +12 the warble returns (chipmunk territory). At −12 it still sounds OK. Asymmetric quality is expected — shifting pitch *up* requires the algorithm to fit more pitch-shifted material into the same output time, which means more block transitions per second and more chance of audible warble.

### Pilot status

Phase 2 audio quality signed off as good enough for pilot at 60/20/10 (2026-05-21). Trailing/middle cuts in Discord are confirmed to be Discord's noise gate, not our pipeline (Audacity test from CABLE Output came back continuous). No further tuning before Phase 3 unless something regresses.

### Future options for improving extreme-pitch quality

Three escalating paths if/when we want chipmunk-grade quality at +12 (or better behavior across the full range). Listed in order of complexity. All deferred.

**Option A — Bigger uniform windows.**

One-line change: push `SequenceDurationMs` / `SeekWindowDurationMs` / `OverlapDurationMs` to e.g. `100 / 35 / 15` or `140 / 45 / 20`. Larger blocks = smoother sustained tones, less warble, but +30–40 ms latency added to the floor and the trailing cut-off at negative pitch returns.

- Cost: trivial (3 line edits in `PitchEffect`'s constructor).
- Best for: testing whether the warble is genuinely fixable inside SoundTouch before going to harder options.
- Try first if Option A's latency hit (~80–100 ms end-to-end) is tolerable for the use case.

**Option B — Dynamic tuning by pitch direction and magnitude.**

Apply different SoundTouch settings depending on the current `PitchSemitones` value, refreshed whenever the slider moves. Sketch:

- `|pitch| ≤ 3`: 60 / 20 / 10 — low latency, current pilot setting
- positive and `> 3`: 120 / 40 / 18 — chipmunk gets quality, latency goes up only when needed
- negative and `> 3`: 40 / 15 / 8 — minimize trailing artifact on demonic / monster voices

- Cost: ~10 lines added to `PitchEffect`. Move the three settings into a private `RetuneFor(float semitones)` method called from the `Semitones` setter. Need to call `_processor.SetSetting` whenever pitch changes; SoundTouch may need a `Flush()` between regimes to avoid mid-stream glitches.
- Best for: keeping the user-visible UX as a single Pitch slider while getting near-optimal quality at every position. The user gets quality without thinking about it.
- Risk: regime transitions (slider crossing the ±3 boundary while talking) may glitch.

**Option C — Switch algorithm: phase vocoder via NWaves.**

[NWaves](https://github.com/ar1st0crat/NWaves) is a comprehensive C# DSP library with multiple pitch-shift implementations. The phase vocoder family operates in the frequency domain (FFT → modify bins → IFFT) instead of stitching time-domain blocks together. Different artifact character — smearing and "phasiness" instead of warble — and most listeners prefer the result for voice. Some implementations support formant correction, which is closer to "human chipmunk" than "vocoder chipmunk."

- Cost: substantial. New NuGet dependency. Replace SoundTouch usage inside `PitchEffect` (or split into a new `PhaseVocoderEffect` class so both implementations coexist). API is different — need to feed sample-frame chunks of a fixed size, manage FFT window/hop, handle the algorithmic delay.
- Latency: floor goes up to ~40 ms regardless of pitch (FFT window size). End-to-end is comparable to Option A but the quality at extreme pitch is meaningfully better.
- Best for: long-term right answer if extreme pitch quality matters. Also unlocks formant-preserved pitch shifting (sound like yourself but higher/lower, rather than a different voice). NWaves also has WSOLA and SOLA implementations if we want to stay in time domain but try a different algorithm.
- Worth doing in Phase 4 or 5 if the pilot is well-received and we want to polish quality. Until then, Option A or B carries us.

### Other open items / future work

- **Trailing cut-off & mid-speech cuts in Discord** turned out to be Discord's noise gate, not our pipeline. Confirmed via Audacity test (recorded from CABLE Output): continuous audio with no cuts. Discord's voice-activity gate is the cause. No fix on our side.
- **Popping in Audacity at non-zero pitch** turned out to be SoundTouch block-boundary clicks. Fixed by the 60/20/10 tuning (resolved 2026-05-21).
- **Bypass-vs-SoundTouch transition glitch — resolved 2026-05-22.** Removed the zero-semitone bypass entirely; `PitchEffect.Process` now always feeds SoundTouch regardless of pitch value. Trade-off accepted: ~30 ms algorithmic latency at pitch=0 (was ~5 ms passthrough), and a touch of CPU at idle. Reasoning: A/B test confirmed audio quality at pitch=0 is indistinguishable from bypass mode, while sliding through 0 with bypass produced popping that wasn't acceptable. The path-switching itself was the bug; killing the special case kills the bug. SoundTouch's overlap-add machinery handles in-stream pitch changes (e.g. +2 → +5) smoothly, so all slider movement is now glitch-free.

---

## Phase 3 — Ring modulation (robot voice)

### Decisions

- **Effect picker UI: mutually exclusive (B1 from the Phase 3 design discussion).** A ComboBox above the sliders selects which effect is active; the inactive slider greys out. Plan called for "extending within the existing architecture rather than refactoring it," so we deliberately *didn't* build a generic effect-chain. That's Phase 4's job; doing it now would be infrastructure we'd rebuild in a month.
- **Ring mod math: `output = input * sin(2π · f · t)`**, where `f` is `FrequencyHz` and `t = sampleIndex / sampleRate`. One line per sample, no library dependency. Slider range 0–2000 Hz: below ~30 Hz reads as tremolo, 100–500 Hz robotic, >1000 Hz metallic/weird.
- **Zero-frequency bypass on `RingModEffect`.** When `FrequencyHz == 0`, skip the math and pass input through. Different reason from PitchEffect's old bypass: there's no algorithmic latency to dodge, but `sin(2π · 0 · t) = 0` would *mute* the output. Without the bypass the slider's resting position would silence the mic entirely. Note Phase 2 ended up *removing* its bypass; if we ever observe slider-touch-induced clicks on RingMod near 0 Hz, the lesson from Phase 2 says path-switching is the more likely cause than the math itself — try removing the bypass before tuning anything else.
- **EffectMode enum lives in `Pipeline.cs`** alongside the class that uses it, not its own file. 5–10 lines, single consumer, no helper logic — doesn't earn a separate file yet. Move it out in Phase 4 if/when the UI / preset code starts referring to it independently.

### Open items / future work

- **Phase-discontinuity clicks at chunk boundaries (test plan deferred).** Suspected but not yet confirmed audible. `RingModEffect.Process` currently computes `time = i / _sampleRate` starting from `i = 0` on every call. That means the sine wave restarts at phase 0 each ~20 ms capture chunk. At higher carrier frequencies (e.g. 500 Hz, 1000 Hz, 2000 Hz) the wave is mid-cycle when the chunk ends, so restarting from 0 in the next chunk creates a step discontinuity — audible as periodic ticking at the chunk rate (~50 Hz with our 20 ms capture buffer), getting more pronounced as carrier frequency rises.
  - **Test plan**: sustained vowel ("aaaaah") into the mic, drag the RingMod slider slowly 0 → 2000 Hz. Listen for periodic ticking/buzz that emerges and intensifies as frequency increases. A clean recording into Audacity from CABLE Output would let us actually *see* the discontinuities at chunk boundaries. Also worth comparing two consecutive `Process` calls' end-and-start sample values during a sustained tone — should match within rounding if continuous.
  - **Likely fix**: add a `private double _phase` field on `RingModEffect`. In `Process`, use `_phase + 2π * f * (i / sampleRate)` inside `Math.Sin`, and after the loop advance `_phase += 2π * f * frameCount / sampleRate`. Modulo `2π` periodically to keep float precision from drifting over long sessions.
  - Pilot user (2026-05-24) reported it "works good" — junior dev's ear may not have caught it, or it genuinely isn't bad at the frequencies tested. Either way, worth the structured test before declaring it fine.
- **Mono assumption in ring mod math.** Current loop treats every sample as an independent time step. For stereo capture (the realistic case — most mics report stereo to WASAPI even when actually mono), this means left and right samples at the "same" frame use slightly different `time` values (sample-index based, not frame-index based), so L and R drift out of phase. Inaudible at low carrier frequencies, potentially audible at high ones as a stereo wobble. If we hear it, divide `i` by `_channels` (or iterate frames and apply per-channel) before computing time.
