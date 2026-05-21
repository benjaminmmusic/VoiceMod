# VoiceMod

Real-time voice modulation desktop app for Windows. Captures audio from a physical microphone, applies effects, outputs to VB-Cable so voice chat apps (Discord, in-game voice, etc.) receive the modulated voice.

## Project context

**Dual purpose.** This is a real product *and* a learning vehicle for a junior developer (the project owner's sister) who joins from Phase 3 onward. Her background: primitive types and `for` loops in Java, no C#, no prior real-project experience.

**Tech stack.** C# / .NET 8 LTS. NAudio for WASAPI capture and render. WPF for the GUI (Phase 2 onward). SoundTouch.Net for pitch shifting. VB-Cable as the virtual mic driver.

**Solution layout** (from Phase 2 onward):

```
src/
├── VoiceMod.Core/      class library — audio pipeline + effects (shared)
├── VoiceMod.Console/   headless test harness
└── VoiceMod.App/       WPF UI
```

- See [voice-modulation-plan.md](voice-modulation-plan.md) for phase-by-phase scope.
- See [notes.md](notes.md) for ongoing observations, decisions, and known quirks.

## Code style

- Code first. Names and structure carry the meaning.
- Comment only when the *why* is non-obvious AND short. No tutorial comments. No narration of what the code does.
- If a reader wants explanation, they ask Claude in chat. Real codebases don't have prose; reading code as it is is the skill we're building.
- Prefer small, self-contained additions over premature abstraction. Effect-chain interfaces, MVVM, settings persistence — all deferred until they earn their keep.

## Working with the junior dev

When the junior dev is driving the session:

- Default to short, beginner-friendly answers. Define technical terms briefly on first use; skip the deep dive unless she asks.
- Do not lecture. Do not produce walls of text.
- Expand technical depth only when she explicitly asks.
- When she asks Claude to fix a bug, **first guide her to debug it herself**: suggest where to set a breakpoint *before* the suspected issue (not at the issue itself), or what to print/log to narrow it down. Only fix outright after she's tried.
- She learns by hitting walls. Lean toward "here's a hint" rather than "here's the answer".

## Collaboration rules

- No code edits without explicit go-ahead. Discuss first, present a plan, wait for "yes" / "go ahead".
- If unsure whether approval was given, ask.
- Config files (`.gitignore`, `.claude/settings*.json`, memory) and exploratory reads/searches do not require approval.

## Git workflow

- **Never run `git commit` yourself.** Committing is the developer's job. When work is ready to commit, *propose* it: list the files, suggest a commit message, give the exact commands. Wait for the developer to run them.
- **Split into logical chunks, not mega-commits.** Group by intent — e.g. "Core library + console refactor", "WPF app", "docs update", "repo infrastructure". Each chunk should build cleanly and tell one story. Aim for 2–4 commits at natural seams over the course of a session, not one catch-all dump at the end.
- **Proactively remind the developer when a good commit point arrives.** Natural moments: a phase finished, a feature works end-to-end, a refactor is complete, a bugfix verified, a scaffold step that you'd want to revert cleanly later. Suggest once — *"this is a good place to commit"* — and provide the commands. Don't nag. Don't suggest commits mid-feature or after trivial single-file edits.
- **For the junior dev specifically**, name *what changed* and *why now is a good moment* in the suggestion. She is learning git alongside everything else; the reminder is part of the lesson.
