namespace AVABomberman.Engine;

/// <summary>
/// The battle: up to four bombers, people or computer, in a one-screen arena packed with blocks. Last one standing
/// takes the round and a trophy; the first to the trophies needed takes the battle. When the clock runs low the
/// hurry-up drops blocks round the arena, spiralling in.
/// </summary>
public sealed partial class GameEngine
{
    public const double RoundSeconds = 150, AttractRoundSeconds = 80, HurrySeconds = 45, DropInterval = 0.3;
    public const double RoundStartSeconds = 2.2, RoundEndDelay = 1.6, RoundOverSeconds = 4.5, MatchOverSeconds = 6.5;

    public int Round { get; private set; }
    /// <summary>The battle's trophies to win, as it was set up.</summary>
    public int WinsNeededNow { get; private set; } = 3;
    /// <summary>Whether the hurry-up is on: blocks dropping round the arena.</summary>
    public bool Hurry { get; private set; }
    /// <summary>The round's winner (null for a draw), once it's over.</summary>
    public Bomber? Winner { get; private set; }
    /// <summary>How long ago each dropped block landed (for drawing its fall).</summary>
    public double[,] Dropped { get; private set; } = new double[Field.BattleCols, Field.BattleRows];

    private IReadOnlyList<(int C, int R)> _spiral = [];
    private int _dropNext;
    private double _dropIn, _endIn;

    /// <summary>The cells the hurry-up has yet to drop blocks on, in order.</summary>
    public IEnumerable<(int C, int R)> DropsToCome => _spiral.Skip(_dropNext);

    private void NewBattle(IReadOnlyList<Slot> slots)
    {
        _rng = new Random(_seed);
        Mode = Mode.Battle;
        Score = 0;
        Round = 0;
        WinsNeededNow = WinsNeeded;
        Bombers.Clear();
        for (int i = 0; i < slots.Count && i < 4; i++)
            if (slots[i] != Slot.Off)
                Bombers.Add(new Bomber { Index = i, Human = slots[i] == Slot.Human });
    }

    private void BeginRound()
    {
        Round++;
        Board = Board.Battle(_rng);
        ClearField();
        Dropped = new double[Board.Cols, Board.Rows];
        _spiral = Board.Spiral();
        _dropNext = 0;
        _dropIn = 0;
        _endIn = 0;
        Hurry = false;
        Winner = null;
        TimeLeft = State == GameState.Attract ? AttractRoundSeconds : RoundSeconds;
        foreach (var b in Bombers)
        {
            var (c, r) = Board.BattleStarts[b.Index];
            b.ResetPowers();
            b.Place(c, r);
            b.Alive = true;
            b.DeadTime = 0;
            b.BombPrev = b.DetonatePrev = true;
            b.Pilot.Reset();
        }
        SetPhase(Phase.Start);
        Say($"ROUND {Round}", WinsNeededNow == 1 ? "LAST ONE STANDING WINS" : $"FIRST TO {WinsNeededNow} TROPHIES", RoundStartSeconds);
        Music(false);
        Play(Sfx.StageStart);
    }

    private void SimulateBattle(double dt, IReadOnlyList<Controls> humans)
    {
        foreach (var (c, r) in Board.Cells())
            if (Board.T[c, r] == Tile.Dropped) Dropped[c, r] += dt;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > RoundStartSeconds)
                {
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Play:
                if (!Calm) TimeLeft = Math.Max(0, TimeLeft - dt);
                if (!Hurry && TimeLeft <= HurrySeconds && !Calm)
                {
                    Hurry = true;
                    Say("HURRY UP!", null, 1.6);
                    Play(Sfx.HurryUp);
                }
                if (Hurry) DropWalls(dt);
                StepPlay(dt, humans);
                CheckRoundEnd(dt);
                break;
            case Phase.RoundOver:
                if (PhaseTime > RoundOverSeconds)
                {
                    if (Bombers.Any(b => b.Wins >= WinsNeededNow))
                    {
                        SetPhase(Phase.MatchOver);
                        Play(Sfx.BattleWin);
                    }
                    else BeginRound();
                }
                break;
            case Phase.MatchOver:
                if (PhaseTime > MatchOverSeconds)
                {
                    if (State == GameState.Attract) ShowHighScores(-1);
                    else ReturnToTitle();
                }
                break;
        }
    }

    /// <summary>The next block of the hurry-up's spiral: it crushes whoever is under it and anything lying there.</summary>
    private void DropWalls(double dt)
    {
        _dropIn -= dt;
        while (_dropIn <= 0 && _dropNext < _spiral.Count)
        {
            _dropIn += DropInterval;
            var (c, r) = _spiral[_dropNext++];
            DropWall(c, r);
        }
    }

    internal void DropWall(int c, int r)
    {
        Board.T[c, r] = Tile.Dropped;
        Board.Item[c, r] = PowerUp.None;
        Dropped[c, r] = 0;
        Crumble[c, r] = 0;
        Flames[c, r] = default;
        if (BombAt(c, r) is { } bomb) Bombs.Remove(bomb);
        foreach (var b in Bombers.Where(b => b.Alive && b.Cx == c && b.Cy == r))
        {
            b.Invincible = 0;
            Kill(b, "CRUSHED");
        }
        Play(Sfx.WallDrop);
    }

    /// <summary>One bomber left (or none): a moment's grace, in case the last blast takes the survivor too, then the round is over.</summary>
    private void CheckRoundEnd(double dt)
    {
        if (Calm) return;
        int alive = Bombers.Count(b => b.Alive);
        bool timeOut = TimeLeft <= 0;
        if (alive > 1 && !timeOut) { _endIn = 0; return; }
        if (!timeOut)
        {
            if (_endIn <= 0) _endIn = RoundEndDelay;
            _endIn -= dt;
            if (_endIn > 0) return;
        }
        Winner = timeOut ? null : Bombers.FirstOrDefault(b => b.Alive);
        if (Winner is { } w)
        {
            w.Wins++;
            Say($"PLAYER {w.Index + 1} WINS!", null, RoundOverSeconds);
        }
        else Say("DRAW GAME", timeOut ? "TIME UP" : "NOBODY LEFT STANDING", RoundOverSeconds);
        SetPhase(Phase.RoundOver);
        Music(false);
        Play(Winner is null ? Sfx.GameOver : Sfx.StageClear);
        foreach (var b in Bombers) b.Walking = false;
    }

    /// <summary>The battle's champion, once somebody has the trophies.</summary>
    public Bomber? Champion => Bombers.FirstOrDefault(b => b.Wins >= WinsNeededNow);

    // ───────────────────────────── test hooks ─────────────────────────────

    /// <summary>Straight into the play of a battle round between the given corners.</summary>
    internal void GoBattle(params Slot[] slots)
    {
        SetSlots(slots);
        StartBattle();
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal void StartHurry()
    {
        TimeLeft = HurrySeconds + Field.Step / 2;
    }
}
