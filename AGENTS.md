# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A maze-bombing game in the style of Hudson Soft's Bomberman, with a fifty-stage story and a battle mode for up to
four bombers, people or computer. It is C# on .NET 10 and Avalonia 12, drawn with Avalonia's `DrawingContext`. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code, and
the sounds and tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVABomberman.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVABomberman.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVABomberman          # play it; --no-audio for silence
BOMBER_SHOTS=$PWD/Images dotnet test tests/AVABomberman.Tests --filter ScreenshotTests   # refresh the README's pictures
BOMBER_PROBE=story dotnet test tests/AVABomberman.Tests --filter PilotProbe --logger "console;verbosity=detailed"
                                               # how the computer gets on (story or battle); does nothing unset
python3 tools/make_sounds.py                   # rebuild src/AVABomberman/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVABomberman/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title and menu, demonstration, play, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` has what story and battle share: walking, bombs, flames, blocks and power-ups.
  - `GameEngine.Story.cs` (the fifty stages, the creatures, the door and bonus stages) and `GameEngine.Battle.cs`
    (the arena for up to four bombers).
  - `Board.cs` builds the fields: pillars, blocks and what is hidden under them.
  - `Entities.cs` has `Controls`, directions, the field's measures (`Field`) and the bombers and creatures.
  - `BomberPilot.cs` is the computer bomber, used by battles, the demonstration and the tests.
  - `InputHandler.cs` (keyboard for two players, up to four gamepads) and `ControllerManager.cs` (SDL2) produce
    each player's `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVABomberman/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame, scaled to the
  window, and runs the 60 Hz timer. `GameCanvas.Sprites.cs` draws the characters from shapes.
- `src/AVABomberman/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVABomberman.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `GameplayTests.cs` holds the rules tests;
  `PilotProbe.cs` is a tuning report, not a check.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` turns frame time into fixed 1/60 s steps (`Field.Step`, run by `Tick`),
  so tests can play stages deterministically from a seed. Draw random numbers from the engine's seeded `_rng`
  (a `Random(seed)`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Up/down on keys and sticks must produce the same `Dy` sign the engine
  expects (positive is down the screen, negative is up). Test new controls through `InputHandler`, not by setting
  `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. A test in `GameplayTests.cs` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and updates SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData`, `AVABomberman`); tests load the table from a fresh temporary folder and run
  with `Silent = true`.
- **Line endings.** The sources are LF; `app.manifest` is CRLF. Keep each file's existing line endings so diffs
  show only real changes.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
A release also asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
