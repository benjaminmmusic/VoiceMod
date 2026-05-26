# VoiceMod — Improvements & Fixes

Backlog of identified improvements. Not phase-locked — pick from here whenever there's a moment to tighten. Items get removed once shipped (or moved into [notes.md](notes.md) if the analysis turned out to be wrong).

Categories:
- **Bug** — latent issue, fix it.
- **Polish** — user-facing quality of life.
- **Hygiene** — code quality, low user impact.
- **Arch prep** — sets up cleaner work in future phases.

---

## Phase 2 tightening (identified 2026-05-21)

### 2. Final block of audio lost when stopping — *Bug*

**Problem.** `Pipeline.Stop` calls `WasapiOut.Stop` then `WasapiCapture.StopRecording`, leaving up to one block of samples inside SoundTouch never flushed. Currently not user-visible because we dispose after stop. Becomes a real problem if we ever support restart-without-dispose (e.g. a pause/resume button).

**Fix.** Call `_processor.Flush()` in `Pipeline.Stop` (via a new `PitchEffect.Flush` method that pulls any remaining samples and writes them to the jitter buffer).

**Effort.** ~5 lines.

---

### 3. `Pipeline` constructor doesn't dispose on partial failure — *Bug*

**Problem.** Pipeline constructor creates `_capture` first, then validates the format, then creates `_pitch`, then `_jitterBuffer`, then `_output`. If anything after the first throws (e.g. `WasapiOut` fails because the output device is busy in exclusive mode somewhere else), the earlier objects are left undisposed → leaked WASAPI client.

**Fix.** Wrap construction in try/catch, dispose what was created before re-throwing. Lives in [src/VoiceMod.Core/Pipeline.cs](src/VoiceMod.Core/Pipeline.cs).

**Effort.** ~6 lines.

---

### 4. `Start()` failure swallowed in App leaves pipeline in zombie state — *Bug*

**Problem.** In [src/VoiceMod.App/MainWindow.xaml.cs](src/VoiceMod.App/MainWindow.xaml.cs) `StartButton_Click`, if `WasapiOut.Play` throws *after* the Pipeline is constructed, the catch block shows the error but `_pipeline` is left non-null and partially started. Next click of Start replaces it; the original never gets disposed.

**Fix.** In the catch block, call `_pipeline?.Dispose()` and set to null before showing the error.

**Effort.** ~2 lines.

---

### 5. Status line styling — *Polish*

**Problem.** The single `StatusLabel` shows idle, running, and error states all in the same italic grey text. Users (especially the junior dev) can't tell at a glance if something went wrong.

**Fix.** Bind `StatusLabel.Foreground` to a brush set by the state — e.g. green-ish when running, red when error, default grey on idle. Lives in [src/VoiceMod.App/MainWindow.xaml](src/VoiceMod.App/MainWindow.xaml) + the three places in code-behind that set the status text.

**Effort.** ~10 lines XAML + code.

---

### 6. Device list doesn't refresh on hotplug — *Polish*

**Problem.** If you plug a new mic or USB headset after the App is already open, it doesn't appear in the dropdowns. Annoying — user has to restart the app.

**Fix.** Implement `IMMNotificationClient` and register with `MMDeviceEnumerator.RegisterEndpointNotificationCallback`. On `OnDeviceAdded` / `OnDeviceRemoved` / `OnDeviceStateChanged`, re-enumerate and update the combo boxes on the UI thread.

**Effort.** ~30 lines. Need to be careful about thread marshalling (notifications come from a COM thread, must `Dispatcher.Invoke` to touch UI).

---

### 7. No keyboard shortcut for Start/Stop — *Polish*

**Problem.** Mouse-only. Space or Enter to toggle Start/Stop would be nicer.

**Fix.** Add KeyBindings to the Window in XAML, mapping Space → either StartButton or StopButton's Click handler depending on current state. Or simpler: wire `Window.PreviewKeyDown` in code-behind.

**Effort.** ~5 lines.

---

### 8. GC churn in `PitchEffect.Process` — *Hygiene*

**Problem.** Every captured block allocates a fresh `byte[]` via `.ToArray()` to feed `BufferedWaveProvider.AddSamples`, which only accepts `byte[]` (not Span). Roughly 2 KB allocated per 20 ms capture event = ~100 KB/s of garbage. Not pathological but unnecessary.

**Fix.** Pool the byte arrays with `ArrayPool<byte>.Shared`. Tricky because `BufferedWaveProvider` may keep the reference around until consumed — need to confirm it copies internally (it does in NAudio 2.3.x). If it doesn't, this fix is unsafe.

**Effort.** ~15 lines, plus verification that NAudio copies on AddSamples.

**Verdict.** Only do this if profiling shows allocations are causing GC pauses. Probably never necessary at our throughput.

---

### 9. Float parse uses current culture in Console — *Hygiene*

**Problem.** In [src/VoiceMod.Console/Program.cs](src/VoiceMod.Console/Program.cs), `float.TryParse(line, out var semis)` uses the current culture's decimal separator. On a `,`-decimal locale (German, French, etc.) the user types `5,5` and gets `0` instead of an error, or `5.5` and gets parse failure.

**Fix.** `float.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var semis)`.

**Effort.** 1 line + a using.

---

### 10. Move SoundTouch tuning to settable properties — *Arch prep*

**Problem.** The three magic numbers (60/20/10) live in [PitchEffect.cs](src/VoiceMod.Core/PitchEffect.cs) constructor with `_processor.SetSetting` calls. To do the dynamic-tuning future work (see [notes.md](notes.md) Phase 2 future-options section), we'd need to retune at runtime.

**Fix.** Promote the three values to public properties (`SequenceMs`, `SeekWindowMs`, `OverlapMs`). Setter calls `_processor.SetSetting` immediately. Default initialization in the constructor stays as today (60/20/10). Zero behavior change, but `Pipeline` (or eventually the App) can now adjust them.

**Effort.** ~5 lines added, zero behavior change.

**Why now.** Sets up Option B from the notes (per-pitch-direction tuning) and the eventual UI knob for quality-vs-latency. No reason not to do it while we're already in this file.

---

### 11. Pitch ~+2 transit pop — *Bug (deferred)*

**Problem.** Sliding pitch through or away from ~+2 semitones produces an audible pop. Asymmetric — -2 is clean. Confirmed not caused by ramp size, parameter-write frequency, or any tested SoundTouch tuning. Static pitch values everywhere are clean; only slider motion triggers it. Suspected SoundTouch internal heuristic threshold around pitch ratio ~1.12 (= 2^(2/12)) but unverified. Investigated extensively 2026-05-25; see [notes.md](notes.md) Phase 2 section for the full investigation.

**Two fix paths:**

**11a. De-clicker post-process.** Detect sudden waveform discontinuities in the output stream and smooth them. Roughly 30–100 lines depending on sophistication. Risk: tuned aggressively will muffle plosive consonants (`p`, `t`, `k`). A naive amplitude-derivative threshold is ~30 lines and will catch some legitimate transients; a look-ahead envelope analysis (~100 lines) discriminates pops from plosives by what comes after the spike. Bolts a fix on top of SoundTouch.

**11b. Algorithm swap to NWaves phase vocoder.** Phase vocoder doesn't have SoundTouch's block-stitching artifact model — pops of this kind would not exist. Significantly larger change. See notes.md Phase 2 future-options for the full discussion of this swap.

**Recommendation.** 11b is the right long-term move. 11a is a band-aid that would become dead code once 11b ships. Prefer waiting for 11b unless the +2 pop becomes intolerable in real use.

**Effort.** 11a: 30–100 lines. 11b: substantial — new NuGet dep, replace SoundTouch internals, handle the FFT window/hop API.

---

## How to use this file

1. When the user wants to pick up a tightening session, scan this list and choose items.
2. Discuss the picked items with the user as a batch (per the no-edits-without-go-ahead rule in [CLAUDE.md](CLAUDE.md)).
3. Implement once approved.
4. Delete the items from this file once committed. Don't archive — git history is the archive.
5. If new improvements are identified during normal work, add them here rather than holding them in conversation context.
