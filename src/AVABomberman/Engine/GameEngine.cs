namespace AVABomberman.Engine;

public enum GameState
{
    /// <summary>The title, with its menu: story or battle.</summary>
    Title,
    /// <summary>Choosing who plays a battle (human, computer or nobody in each corner) and how many wins take it.</summary>
    Setup,
    Attract, Playing, Paused, GameOver, EnterInitials, HighScores,
}

public enum Mode { Story, Battle }

/// <summary>Where a stage or a battle round has got to.</summary>
public enum Phase
{
    /// <summary>The stage's number (or the round's), and nothing moves yet.</summary>
    Start,
    Play,
    /// <summary>The story's bomber has been caught: a moment, then the stage again.</summary>
    Dying,
    /// <summary>Out through the door (or the bonus stage's time is up): on to the next.</summary>
    Clear,
    /// <summary>A battle round is over: the winner and everyone's trophies.</summary>
    RoundOver,
    /// <summary>Somebody has won the battle.</summary>
    MatchOver,
}

/// <summary>Who plays in each of the battle's four corners.</summary>
public enum Slot { Human, Computer, Off }

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Step, BombPlace, Fuse, Explosion, Crumble, PowerUp, EnemyDeath, PlayerDeath, ExitOpen, Spawn, WallDrop, Select,
    StageStart, StageClear, HurryUp, BattleWin, GameOver,
    /// <summary>The stage's tune, looping.</summary>
    Music, MusicStop,
    TitleTune,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and each human player's controls; it
/// runs in fixed 60 Hz steps, so it plays the same at any frame rate and a seed always plays the same game.
/// </summary>
public sealed partial class GameEngine
{
    public const double TitleIdleSeconds = 15;
    public const double AttractSeconds = 45;
    public const double HighScoreSeconds = 9;
    public const int MenuItems = 2, SetupRows = 6;

    private Random _rng;
    private readonly int _seed;
    private double _accumulator;
    private bool _startPrev;
    private Controls _menuPrev;
    private bool _titleTunePending = true;
    private bool _music;
    private int _attracts;

    public GameEngine(int seed = 1983, HighScoreTable? table = null)
    {
        _seed = seed;
        _rng = new Random(seed);
        HighScores = table ?? HighScoreTable.Load();
        NewStory();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Mode Mode { get; private set; } = Mode.Story;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }

    /// <summary>The title menu's choice: 0 story, 1 battle.</summary>
    public int MenuIndex { get; private set; }
    /// <summary>The battle setup's row: the four corners, then the wins needed, then "fight".</summary>
    public int SetupRow { get; private set; }
    public Slot[] Slots { get; } = [Slot.Human, Slot.Computer, Slot.Computer, Slot.Computer];
    public int WinsNeeded { get; private set; } = 3;

    /// <summary>How many people are playing now (the keyboard is split between the first two).</summary>
    public int Humans => Bombers.Count(b => b.Human);

    public List<Effect> Effects { get; } = [];

    /// <summary>A word across the screen, and for how much longer.</summary>
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, IReadOnlyList<Controls> humans, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(humans, start);
        }
    }

    public void Tick(Controls c, bool start = false) => Tick([c], start);

    /// <summary>One 1/60 s step. <paramref name="humans"/> holds each person's controls, in the order of the corners they play.</summary>
    public void Tick(IReadOnlyList<Controls> humans, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        var any = humans.Aggregate(default(Controls), (a, b) => a | b);
        var prev = _menuPrev;
        _menuPrev = any;
        bool up = any.Dy < 0 && prev.Dy >= 0, down = any.Dy > 0 && prev.Dy <= 0;
        bool left = any.Dx < 0 && prev.Dx >= 0, right = any.Dx > 0 && prev.Dx <= 0;
        bool accept = startPressed || (any.Bomb && !prev.Bomb);
        foreach (var e in Effects) e.Age += dt;
        Effects.RemoveAll(e => e.Done);
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }

        switch (State)
        {
            case GameState.Title:
                if (_titleTunePending) { _titleTunePending = false; Play(Sfx.TitleTune); }
                if (up || down) { MenuIndex = (MenuIndex + 1) % MenuItems; StateTime = 0; Play(Sfx.Select); }
                if (accept) Choose();
                else if (StateTime > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Setup:
                if (up) { SetupRow = (SetupRow + SetupRows - 1) % SetupRows; Play(Sfx.Select); }
                if (down) { SetupRow = (SetupRow + 1) % SetupRows; Play(Sfx.Select); }
                if (left || right) Adjust(right ? 1 : -1);
                if (startPressed || (accept && SetupRow == SetupRows - 1)) { if (CanFight) StartBattle(); }
                else if (accept && SetupRow < SetupRows - 1) Adjust(1);
                break;
            case GameState.Attract:
                Simulate(dt, humans);
                if (startPressed) { ReturnToTitle(); break; }
                // A story demonstration runs so long; a battle runs until somebody wins it.
                if (State == GameState.Attract && Mode == Mode.Story && StateTime > AttractSeconds) ShowHighScores(-1);
                break;
            case GameState.Playing:
                Simulate(dt, humans);
                break;
            case GameState.GameOver:
                if (StateTime > 3.5)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Stage);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) ReturnToTitle();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    /// <summary>The title menu's choice taken: straight into the story, or on to set up a battle.</summary>
    private void Choose()
    {
        Play(Sfx.Select);
        if (MenuIndex == 0) StartGame();
        else
        {
            State = GameState.Setup;
            StateTime = 0;
            SetupRow = SetupRows - 1;
        }
    }

    /// <summary>A battle needs two in it at least.</summary>
    public bool CanFight => Slots.Count(s => s != Slot.Off) >= 2;

    private void Adjust(int way)
    {
        Play(Sfx.Select);
        if (SetupRow < 4)
        {
            Slots[SetupRow] = (Slot)(((int)Slots[SetupRow] + way + 3) % 3);
        }
        else if (SetupRow == 4) WinsNeeded = Math.Clamp(WinsNeeded + way, 1, 5);
    }

    /// <summary>Chooses a corner's player (for tests and the menu).</summary>
    public void SetSlots(params Slot[] slots)
    {
        for (int i = 0; i < Slots.Length; i++) Slots[i] = i < slots.Length ? slots[i] : Slot.Off;
    }

    public void SetWinsNeeded(int wins) => WinsNeeded = Math.Clamp(wins, 1, 5);

    /// <summary>A story game from the first stage.</summary>
    public void StartGame()
    {
        NewStory();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
        BeginStage(1);
    }

    /// <summary>A battle between the corners as set up.</summary>
    public void StartBattle()
    {
        Music(false);
        NewBattle(Slots);
        State = GameState.Playing;
        StateTime = 0;
        BeginRound();
    }

    /// <summary>The demonstration: the computer plays the story, and next time a battle between four computer bombers.</summary>
    private void StartAttract()
    {
        if (_attracts++ % 2 == 0)
        {
            NewStory();
            Lives = 5;
            State = GameState.Attract;
            StateTime = 0;
            BeginStage(1 + _rng.Next(3));
        }
        else
        {
            NewBattle([Slot.Computer, Slot.Computer, Slot.Computer, Slot.Computer]);
            WinsNeededNow = 1;
            State = GameState.Attract;
            StateTime = 0;
            BeginRound();
        }
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _titleTunePending = true;
        Music(false);
        Effects.Clear();
        Message = SubMessage = null;
        NewStory();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        Music(false);
        Effects.Clear();
        Message = SubMessage = null;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void GameOver()
    {
        Music(false);
        if (State == GameState.Attract)
        {
            ShowHighScores(-1);
            return;
        }
        Play(Sfx.GameOver);
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void Music(bool on)
    {
        // The demonstration plays its effects but not the stage's tune.
        if (on && State == GameState.Attract) return;
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }
}
