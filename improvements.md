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

### 12. File organization as the project grows — *Arch prep (planned)*

**Problem.** All `*Effect.cs` files sit flat in `src/VoiceMod.Core/` next to `Pipeline.cs`. Same story for App: settings + window + future dialogs will all live flat in `src/VoiceMod.App/`. Fine for 2–3 effects; gets crowded fast as Phase 4 (effect chain, presets) and beyond land.

**Plan (when triggered).** Organize by responsibility, not by class type. Proposed layout:

```
src/
├── VoiceMod.Core/
│   ├── Audio/             pipeline, format helpers, future audio plumbing
│   │   └── Pipeline.cs
│   ├── CoreEffects/       atomic / built-in effects
│   │   ├── PitchEffect.cs
│   │   └── RingModEffect.cs
│   ├── (future) Presets/  named combos like "Robot", "Chipmunk", "Demon"
│   └── (future) Chains/   ordered multi-effect orchestration (Phase 4 work)
└── VoiceMod.App/
    ├── Settings/          AppSettings.cs, SettingsStore.cs
    ├── Views/             MainWindow.xaml(.cs), future windows/dialogs
    └── (root)             App.xaml(.cs) — entry point stays at root
```

**Why `CoreEffects/` and not just `Effects/`.** Phase 4 introduces *presets* (named combinations of effects with preset parameter values) and an *effect chain* (multiple effects running in series). Both are "effect-related" concepts but conceptually distinct from the atomic effect implementations themselves. Reserving `Effects/` would force a confusing nested structure later (`Effects/Effects/PitchEffect.cs`?). `CoreEffects/` for the atomic primitives leaves `Presets/` and `Chains/` (or similar) as clean siblings when they arrive.

C# namespace can stay flat (e.g. `VoiceMod.Core` for everything) regardless of folders, or mirror the folder structure (`VoiceMod.Core.Effects`) — folder reshuffling alone doesn't force namespace changes.

**Trigger.** Pull this in when:
- `VoiceMod.Core/` has 5+ effect files, OR
- We start adding effect-related supporting files (effect-base interfaces, common DSP helpers), OR
- Phase 4 begins (effect chain + presets land — natural moment to reorganize anyway).

**Cost.** ~30 min: move files into folders, update namespaces if we want them mirrored, fix any using-statements. Project references and csproj need no changes — folders inside a project are implicit.

**Why now-ish but not now.** Premature reorganization is its own waste. The current 2 effects don't justify subfolders. But pre-agreeing on the structure means when the trigger fires, we just do it without re-debating layout.

---

### 13. Persistent settings — *Polish (planned, design-approved 2026-05-25)*

**Problem.** Every time the app starts, devices and slider values reset to defaults. Annoying for daily use — picking the right mic + VB-Cable + pitch level every session is friction.

**Goal.** Save user's last-used settings on close. Restore them on next launch.

#### Concepts to know first (skim, don't memorize)

**What "persistent" means.** While the app runs, the user's choices live in memory — the slider knows its current value, the combo knows the selected device. When the app closes, all of that is gone. "Persistent" = written to disk so it survives a close. We write to a file, read it back next launch.

**What a schema is.** A schema is the *shape* of your data — what fields exist, what types they hold, what's required vs optional. In our case the schema is the `AppSettings` record below: `InputDeviceId` is a nullable string, `PitchSemitones` is an int, and so on. JSON is "schemaless" in the sense that the file itself doesn't enforce a shape — but our C# record acts as the schema. When we deserialize, JSON values are mapped into the record's fields. Mismatches are tolerated (extra JSON fields are ignored, missing JSON fields get default values).

**Why JSON, not some custom format.** JSON is built into .NET — `System.Text.Json` handles read/write in one line each. Universal format, every tool understands it, easy to hand-edit if you ever need to debug a stuck setting.

**Why we save to `%APPDATA%`, not next to the .exe.** The Windows convention is:
- Program files (`.exe`, `.dll`) live in `C:\Program Files\...` — read-only for normal users, shared across all users on the machine.
- User-specific data (settings, caches, save files) lives in `%APPDATA%` (`C:\Users\<name>\AppData\Roaming\...`) — writable, per-user, doesn't get wiped when the app is reinstalled.

Writing to `Program Files` requires admin and would clobber other users on the same machine. `AppData` is the right place.

**Why we save device *IDs*, not names or indexes.**
- Names ("Microphone (Logitech G733)") can change if the user renames or the driver updates.
- Indexes (the position in the dropdown) change every time devices are plugged in/out or re-enumerated by Windows.
- IDs are opaque strings assigned by Windows that stay the same across reboots and reinstalls.

Picking the wrong identifier means your saved settings silently restore the wrong device after a reboot. IDs avoid this.

**Why fields are nullable (`string?`, `double?`).** First-time runs have no settings file yet. We return an empty `AppSettings` with default values. `null` for "we don't know yet" is meaningful — for example, if `WindowLeft` is null, we let the OS decide where to put the window; if it has a value, we restore that position. Forcing a non-nullable default (like 0) would push every first-run window into the top-left corner. Not what you want.

**Why we tolerate missing fields when loading.** Forward compatibility. When you add a `DelayMs` field for a future "Delay" effect, old settings files won't have it. `JsonSerializer` fills missing fields with the C# default (`0` for `int`, `null` for nullable types). No migration code, no errors. Just works.

#### What to persist

A single JSON file with these fields:

- `InputDeviceId` — the selected input device's `MMDevice.ID` (string, stable across reboots)
- `OutputDeviceId` — same for output
- `EffectMode` — name of the selected effect ("Pitch" or "RingMod")
- `PitchSemitones` — last pitch slider value (int)
- `RingModFrequencyHz` — last ring-mod slider value (int)
- `WindowLeft`, `WindowTop`, `WindowWidth`, `WindowHeight` — window position and size (doubles, nullable)

Save **all** effect values regardless of which is active. If the user dials in pitch=+5, switches to robot for a session, then comes back to pitch — they expect +5 still set.

#### Where the file lives

`%APPDATA%\VoiceMod\settings.json`. Get the path:

```csharp
var dir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "VoiceMod");
Directory.CreateDirectory(dir); // safe if it already exists
var path = Path.Combine(dir, "settings.json");
```

#### When to save

**Option A (start here): save on app close.** Hook into `MainWindow.OnClosed`. Simple, one call site.

**Option B (later, if needed): save on every change with a debounce.** Hook into slider `ValueChanged`, combo `SelectionChanged`, etc., and call Save through a short (~500 ms) timer that resets each event so rapid changes coalesce into one write. Same `Save()` method, just called from more places.

Starting with A makes B a small additive change later — no refactor.

#### When to load

App startup, in `MainWindow` constructor, *after* `LoadDevices()` populates the dropdowns. Reason: we need the device combo items to exist before we can preselect them by ID.

#### Files to create (both in `src/VoiceMod.App/`)

**`AppSettings.cs`** — POCO record holding the persisted fields:

```csharp
namespace VoiceMod.App;

public sealed record AppSettings
{
    public string? InputDeviceId { get; init; }
    public string? OutputDeviceId { get; init; }
    public string EffectMode { get; init; } = "Pitch";
    public int PitchSemitones { get; init; }
    public int RingModFrequencyHz { get; init; }
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }
}
```

`record` = like a class but immutable. `init` = settable only at construction. `?` after type = nullable (allowed to be null). All defaults are sensible for first run.

**`SettingsStore.cs`** — Load and Save static methods. Sketch:

```csharp
using System.IO;
using System.Text.Json;

namespace VoiceMod.App;

public static class SettingsStore
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceMod",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new AppSettings();
            var json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // log to console; carry on with defaults rather than crashing
            System.Diagnostics.Debug.WriteLine($"SettingsStore.Load failed: {ex}");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsStore.Save failed: {ex}");
            // silent failure — settings just don't persist this session
        }
    }
}
```

#### Wiring into MainWindow.xaml.cs

In the constructor, after `LoadDevices()`:

```csharp
var settings = SettingsStore.Load();
ApplySettings(settings);
```

`ApplySettings` reads the record and restores UI:
- Find the device item in `InputDeviceCombo.Items` whose `Device.ID` matches `settings.InputDeviceId` and select it (or leave unselected if not found)
- Same for output
- Set `EffectCombo.SelectedIndex` based on the effect name
- Set `PitchSlider.Value` and `RingModSlider.Value`
- Apply window position/size to the `Window` (`this.Left = settings.WindowLeft.Value;` etc., guarded by null checks)

In `OnClosed`, *before* the dispose calls:

```csharp
SettingsStore.Save(new AppSettings
{
    InputDeviceId = (InputDeviceCombo.SelectedItem as DeviceItem)?.Device.ID,
    OutputDeviceId = (OutputDeviceCombo.SelectedItem as DeviceItem)?.Device.ID,
    EffectMode = ((EffectMode)EffectCombo.SelectedIndex).ToString(),
    PitchSemitones = (int)PitchSlider.Value,
    RingModFrequencyHz = (int)RingModSlider.Value,
    WindowLeft = this.Left,
    WindowTop = this.Top,
    WindowWidth = this.Width,
    WindowHeight = this.Height,
});
```

#### Bug surface (handle these)

- **First run** — settings.json doesn't exist. `Load()` returns a default `AppSettings`. Everything renders with defaults. ✓ handled in sketch above.
- **Corrupted JSON** — `JsonSerializer.Deserialize` throws. Caught, defaults returned. ✓
- **Saved device unplugged** — `InputDeviceId` doesn't match any current device. `ApplySettings` should just leave that dropdown unselected; user picks manually. No error popup needed.
- **Disk full / no write perms** — `Save()` throws on file write. Caught and swallowed (the only "loss" is settings not persisting this session). ✓

#### Future-proofing for new effects

When a new effect lands (say "Delay"):
1. Add a `DelayMs` field to `AppSettings.cs`. JSON deserialization handles missing fields gracefully — old settings files still load.
2. Add one line each in `ApplySettings` and `OnClosed` to read/write the new field.

Two files touched, ~3 lines added. Nothing else.

#### Effort

~80 lines total across the two new files + ~15 lines hooked into MainWindow. About 90 minutes including testing.

---

## How to use this file

1. When the user wants to pick up a tightening session, scan this list and choose items.
2. Discuss the picked items with the user as a batch (per the no-edits-without-go-ahead rule in [CLAUDE.md](CLAUDE.md)).
3. Implement once approved.
4. Delete the items from this file once committed. Don't archive — git history is the archive.
5. If new improvements are identified during normal work, add them here rather than holding them in conversation context.
