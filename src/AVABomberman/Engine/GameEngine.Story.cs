namespace AVABomberman.Engine;

/// <summary>
/// The story: fifty stages across a wide field, each with its creatures, one power-up and the exit door hidden under
/// the blocks. Every creature gone, the door opens. The clock lets a flood of whirlwinds in when it runs out, and
/// every fifth stage is followed by a bonus stage.
/// </summary>
public sealed partial class GameEngine
{
    public const int Stages = 50, StartLives = 3;
    public const double StageSeconds = 200, BonusSeconds = 30, StartSeconds = 2.6, DyingSeconds = 2.4, ClearSeconds = 3.2;
    public const int FloodCount = 8, SpawnCount = 5, BonusCrowd = 8;

    public int Stage { get; private set; } = 1;
    public int Lives { get; private set; }
    /// <summary>The stage's clock (or the battle round's).</summary>
    public double TimeLeft { get; private set; }
    /// <summary>Whether this is a bonus stage (after every fifth): no blocks, a crowd of creatures, and the bomber can't be hurt.</summary>
    public bool IsBonus { get; private set; }
    /// <summary>Every creature is gone and the door is showing: in through it to clear the stage.</summary>
    public bool ExitOpen { get; private set; }
    /// <summary>The clock has run out and let the whirlwinds in.</summary>
    public bool TimeUp { get; private set; }
    public bool AllClear { get; private set; }

    private double _crowdIn;

    /// <summary>The power-up hidden on each stage, round and round.</summary>
    private static readonly PowerUp[] StagePowers =
    [
        PowerUp.Fire, PowerUp.Bombs, PowerUp.Remote, PowerUp.Speed, PowerUp.Bombs, PowerUp.Fire, PowerUp.WallPass, PowerUp.Bombs,
        PowerUp.BombPass, PowerUp.Fire, PowerUp.Mystery, PowerUp.Bombs, PowerUp.FlamePass, PowerUp.Fire, PowerUp.Speed, PowerUp.Bombs,
    ];

    public static PowerUp StagePower(int stage) => StagePowers[(stage - 1) % StagePowers.Length];

    /// <summary>The toughest kind about on a stage: the drifters at first, the darts by stage 26.</summary>
    public static EnemyKind TopKind(int stage) => (EnemyKind)Math.Min((int)EnemyKind.Dart, (stage - 1) / 5 + (stage >= 2 ? 1 : 0));

    /// <summary>
    /// A stage's creatures: six to begin with, ten by the last stages. Each new kind arrives as a third of them,
    /// growing to four fifths before the next kind comes; the rest are the two kinds below it.
    /// </summary>
    public static List<EnemyKind> StageEnemies(int stage)
    {
        int count = Math.Min(10, 6 + (stage - 1) / 8);
        int top = (int)TopKind(stage);
        double q = stage switch
        {
            1 => 1,
            <= 5 => (stage - 2) / 3.0,
            < 26 => (stage - 6) % 5 / 4.0,
            _ => Math.Min(1, (stage - 26) / 24.0),
        };
        int nTop = Math.Max(1, (int)Math.Round(count * (0.3 + 0.5 * q)));
        int rest = count - nTop, below = (int)Math.Ceiling(rest * 0.6);
        var list = new List<EnemyKind>();
        for (int i = 0; i < count; i++)
        {
            int k = i < nTop ? top : i < nTop + below ? top - 1 : top - 2;
            list.Add((EnemyKind)Math.Max(0, k));
        }
        return list;
    }

    /// <summary>What comes out of a burnt door or power-up: the kind above the stage's toughest.</summary>
    public EnemyKind SpawnKind => (EnemyKind)Math.Min((int)EnemyKind.Whirl, (int)TopKind(Stage) + 1);

    private void NewStory()
    {
        _rng = new Random(_seed);
        Mode = Mode.Story;
        Score = 0;
        Lives = StartLives;
        Stage = 1;
        IsBonus = false;
        AllClear = false;
        Bombers.Clear();
        Bombers.Add(new Bomber { Index = 0, Human = true });
        LayOutStage();
        SetPhase(Phase.Start);
    }

    private void BeginStage(int stage)
    {
        Stage = stage;
        if (State == GameState.Attract) Player.Human = false;
        LayOutStage();
        SetPhase(Phase.Start);
        if (IsBonus) Say("BONUS STAGE", "CATCH ALL YOU CAN IN 30 SECONDS", StartSeconds);
        else Say($"STAGE {stage}", StageHint(stage), StartSeconds);
        Music(false);
        Play(Sfx.StageStart);
    }

    private static string StageHint(int stage) => stage switch
    {
        1 => "BLOW UP EVERY CREATURE, THEN FIND THE DOOR",
        _ => $"POWER-UP HIDDEN: {Name(StagePower(stage))}",
    };

    /// <summary>The stage's field (the same each time it's played), its creatures well away from the bomber, and the clock.</summary>
    private void LayOutStage()
    {
        var rng = new Random(_seed * 31 + Stage * 101 + (IsBonus ? 7 : 0));
        Board = IsBonus ? Board.Bonus() : Board.Story(rng, StagePower(Stage));
        ClearField();
        var p = Player;
        p.Place(1, 1);
        p.Alive = true;
        p.Walking = false;
        p.BombPrev = p.DetonatePrev = true;
        p.Pilot.Reset();
        p.Invincible = IsBonus ? BonusSeconds + StartSeconds + 1 : 0;
        ExitOpen = TimeUp = false;
        TimeLeft = IsBonus ? BonusSeconds : StageSeconds;
        _crowdIn = 0;
        if (Calm) return;
        if (IsBonus)
        {
            for (int i = 0; i < BonusCrowd; i++) SpawnFar(TopKind(Stage), rng);
            return;
        }
        foreach (var k in StageEnemies(Stage)) SpawnFar(k, rng);
    }

    /// <summary>A creature put down on open floor well away from the bomber (and the others, if it can be).</summary>
    private Enemy? SpawnFar(EnemyKind kind, Random rng, int distance = 7)
    {
        var p = Player;
        var cells = Board.Cells().Where(q => Board.Floor(q.C, q.R) && Math.Abs(q.C - p.Cx) + Math.Abs(q.R - p.Cy) >= distance && BombAt(q.C, q.R) is null).ToList();
        var spread = cells.Where(q => Enemies.All(e => e.Dead || Math.Abs(e.Cx - q.C) + Math.Abs(e.Cy - q.R) >= 3)).ToList();
        if (spread.Count > 0) cells = spread;
        if (cells.Count == 0) return null;
        var (c, r) = cells[rng.Next(cells.Count)];
        var e = new Enemy { Kind = kind, Facing = Dirs.All[rng.Next(4)] };
        e.Place(c, r);
        Enemies.Add(e);
        return e;
    }

    /// <summary>Creatures out of a burnt door or power-up (once a blast): a moment to appear, then off they go.</summary>
    private void SpawnFrom(int c, int r, int group)
    {
        if (!_spawned.Add((c, r, group))) return;
        for (int i = 0; i < SpawnCount; i++)
        {
            var e = new Enemy { Kind = SpawnKind, Spawning = Enemy.SpawnSeconds, Facing = Dirs.All[i % 4] };
            e.Place(c, r);
            Enemies.Add(e);
        }
        ExitOpen = false;
        Play(Sfx.Spawn);
    }

    private void SimulateStory(double dt, IReadOnlyList<Controls> humans)
    {
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Play:
                if (!Calm) TimeLeft = Math.Max(0, TimeLeft - dt);
                if (TimeLeft <= 0 && !TimeUp && !Calm)
                {
                    TimeUp = true;
                    if (IsBonus)
                    {
                        StageClear();
                        return;
                    }
                    for (int i = 0; i < FloodCount; i++) SpawnFar(EnemyKind.Whirl, _rng, 6);
                    Say("TIME UP!", "HERE THEY COME", 2);
                    Play(Sfx.HurryUp);
                }
                StepPlay(dt, humans);
                if (Phase != Phase.Play) break;
                if (IsBonus) KeepCrowd(dt);
                else CheckExit();
                break;
            case Phase.Dying:
                StepPlay(dt, humans, control: false);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Retry();
                }
                break;
            case Phase.Clear:
                if (PhaseTime > ClearSeconds) NextStage();
                break;
        }
    }

    /// <summary>The bonus stage keeps its crowd up: a new creature a moment after each one caught.</summary>
    private void KeepCrowd(double dt)
    {
        if (Calm) return;
        if (Enemies.Count(e => !e.Dead) >= BonusCrowd) { _crowdIn = 0.6; return; }
        _crowdIn -= dt;
        if (_crowdIn > 0) return;
        _crowdIn = 0.6;
        if (SpawnFar(TopKind(Stage), _rng, 5) is { } e) e.Spawning = 0.6;
    }

    private void CheckExit()
    {
        bool open = Board.ExitShowing && Enemies.All(e => e.Dead);
        if (open && !ExitOpen)
        {
            ExitOpen = true;
            Play(Sfx.ExitOpen);
        }
        else if (!open) ExitOpen = false;
        var p = Player;
        if (ExitOpen && p.Alive && Board.Exit is var (c, r) && Math.Abs(p.X - c) < 0.35 && Math.Abs(p.Y - r) < 0.35)
            StageClear();
    }

    private void StageClear()
    {
        SetPhase(Phase.Clear);
        Music(false);
        Play(Sfx.StageClear);
        if (IsBonus) Say("BONUS OVER", null, ClearSeconds);
        else Say("STAGE CLEAR!", Stage == Stages ? "THE LAST STAGE!" : null, ClearSeconds);
        Player.Walking = false;
    }

    private void NextStage()
    {
        if (IsBonus)
        {
            IsBonus = false;
            BeginStage(Stage + 1);
            return;
        }
        if (Stage >= Stages)
        {
            AllClear = true;
            Say("ALL FIFTY STAGES CLEAR!", "WHAT A BOMBER", 4);
            GameOver();
            return;
        }
        if (Stage % 5 == 0)
        {
            IsBonus = true;
            BeginStage(Stage);
            return;
        }
        BeginStage(Stage + 1);
    }

    private void StoryDeath()
    {
        Lives--;
        SetPhase(Phase.Dying);
        Music(false);
    }

    /// <summary>The stage again from the start, without the special powers (the bombs, fire and speed are kept).</summary>
    private void Retry()
    {
        var p = Player;
        p.Remote = p.WallPass = p.BombPass = p.FlamePass = false;
        p.Invincible = 0;
        BeginStage(Stage);
    }

    // ───────────────────────────── the creatures ─────────────────────────────

    public bool CanCreep(Enemy e, int c, int r)
    {
        var t = Board.At(c, r);
        if (t is Tile.Pillar or Tile.Dropped) return false;
        if (t == Tile.Soft && !e.Info.WallPass) return false;
        return BombAt(c, r) is null;
    }

    /// <summary>How near (in cells, along the rows and columns) the bomber must be for a keen creature to come after it.</summary>
    public const double SenseRange = 10;

    private int[,]? _toPlayer;
    private int[,]? _toPlayerThrough;

    private void MoveEnemies(double dt)
    {
        if (Enemies.Count == 0) return;
        _toPlayer = _toPlayerThrough = null;
        foreach (var e in Enemies.Where(e => !e.Dead))
        {
            e.Age += dt;
            if (e.Spawning > 0) { e.Spawning = Math.Max(0, e.Spawning - dt); continue; }
            double dist = e.Info.Speed * dt;
            // A bomb has gone down where it was going: back the way it came.
            if (!e.AtCentre && !CanCreep(e, e.Tx, e.Ty))
            {
                var (bx, by) = Dirs.Vec(e.Dir);
                (e.Tx, e.Ty) = (e.Tx - bx, e.Ty - by);
                e.Dir = e.Facing = Dirs.Opposite(e.Dir);
            }
            for (int guard = 0; guard < 3 && dist > 0; guard++)
            {
                if (e.AtCentre)
                {
                    Choose(e);
                    if (e.AtCentre) break;
                }
                dist = e.Glide(dist);
            }
        }
    }

    /// <summary>
    /// Which way a creature goes from a cell centre. The keen ones, when the bomber is near enough, take the way that
    /// leads to it; otherwise each carries on as it was going, turning now and then where it can and when it must.
    /// </summary>
    private void Choose(Enemy e)
    {
        var info = e.Info;
        var opts = Dirs.All.Where(d => { var (dx, dy) = Dirs.Vec(d); return CanCreep(e, e.Cx + dx, e.Cy + dy); }).ToList();
        if (opts.Count == 0) return;
        var p = Player;
        if (p.Alive && info.Smart > 0 && _rng.NextDouble() < info.Smart && Math.Abs(p.X - e.X) + Math.Abs(p.Y - e.Y) < SenseRange)
        {
            var dist = info.WallPass
                ? _toPlayerThrough ??= Board.Distances(p.Cx, p.Cy, (c, r) => !Board.Hard(c, r) && BombAt(c, r) is null)
                : _toPlayer ??= Board.Distances(p.Cx, p.Cy, (c, r) => Board.Floor(c, r) && BombAt(c, r) is null);
            Dir best = Dir.None;
            int bestD = int.MaxValue;
            foreach (var d in opts)
            {
                var (dx, dy) = Dirs.Vec(d);
                int v = dist[e.Cx + dx, e.Cy + dy];
                if (v >= 0 && v < bestD) { bestD = v; best = d; }
            }
            if (best != Dir.None) { e.Head(best); return; }
        }
        var (fx, fy) = Dirs.Vec(e.Facing);
        bool ahead = opts.Contains(e.Facing);
        double turn = e.Kind == EnemyKind.Puffle ? 0.12 : e.Kind == EnemyKind.Dumpling ? 0.05 : 0.3;
        if (ahead && (opts.Count <= 2 || _rng.NextDouble() > turn) && !(fx == 0 && fy == 0))
        {
            e.Head(e.Facing);
            return;
        }
        var sides = opts.Where(d => d != Dirs.Opposite(e.Facing)).ToList();
        var pick = sides.Count > 0 ? sides : opts;
        e.Head(pick[_rng.Next(pick.Count)]);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    /// <summary>Straight into the play of a story stage.</summary>
    internal void Go(int stage = 1)
    {
        StartGame();
        IsBonus = false;
        BeginStage(stage);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal void GoBonus(int afterStage = 5)
    {
        StartGame();
        IsBonus = true;
        BeginStage(afterStage);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal void SetLives(int n) => Lives = n;
    internal void SetStage(int n) => Stage = n;
    internal void SetTimeLeft(double t) => TimeLeft = t;
}
