namespace AVABomberman.Engine;

/// <summary>What story and battle share: walking (sliding round the pillars' corners), bombs, flames, burning blocks and power-ups.</summary>
public sealed partial class GameEngine
{
    public const double FlameSeconds = 0.55, CrumbleSeconds = 0.55;
    public const int PowerUpPoints = 1000;

    public Board Board { get; private set; } = Board.Open(Field.StoryCols, Field.StoryRows);
    public List<Bomber> Bombers { get; } = [];
    public List<Enemy> Enemies { get; } = [];
    public List<Bomb> Bombs { get; } = [];
    /// <summary>The flames burning in each cell.</summary>
    public Flame[,] Flames { get; private set; } = new Flame[Field.StoryCols, Field.StoryRows];
    /// <summary>How much longer each block that's been hit burns before it's gone.</summary>
    public double[,] Crumble { get; private set; } = new double[Field.StoryCols, Field.StoryRows];
    /// <summary>The story's bomber (or the battle's first).</summary>
    public Bomber Player => Bombers[0];
    /// <summary>The computer player for the story's bomber: it plays the demonstration (and the tests).</summary>
    public BomberPilot Pilot => Bombers[0].Pilot;
    public int Score { get; private set; }
    /// <summary>For tests: no creatures, no clock running out, and the round never ends.</summary>
    internal bool Calm { get; set; }
    /// <summary>Counts up every step of play (the pilots use it to share their sums).</summary>
    public long Steps { get; private set; }

    private int _group;
    private readonly Dictionary<int, int> _groupKills = [];
    private readonly HashSet<(int, int, int)> _spawned = [];

    private void ClearField()
    {
        Bombs.Clear();
        Enemies.Clear();
        Effects.Clear();
        Flames = new Flame[Board.Cols, Board.Rows];
        Crumble = new double[Board.Cols, Board.Rows];
        _groupKills.Clear();
        _spawned.Clear();
    }

    private void Simulate(double dt, IReadOnlyList<Controls> humans)
    {
        PhaseTime += dt;
        if (Mode == Mode.Story) SimulateStory(dt, humans);
        else SimulateBattle(dt, humans);
    }

    /// <summary>A step of the action: the bombers (when <paramref name="control"/>), the bombs, the fire, the creatures, and who's been hit.</summary>
    private void StepPlay(double dt, IReadOnlyList<Controls> humans, bool control = true)
    {
        Steps++;
        if (control) MoveBombers(dt, humans);
        UpdateBombs(dt);
        UpdateFire(dt);
        MoveEnemies(dt);
        Hits();
        Pickups();
        foreach (var b in Bombers.Where(b => !b.Alive)) b.DeadTime += dt;
        foreach (var e in Enemies.Where(e => e.Dead)) e.DeadTime += dt;
        Enemies.RemoveAll(e => e.Dead && e.DeadTime > Enemy.DeathSeconds);
    }

    // ───────────────────────────── the bombers ─────────────────────────────

    private void MoveBombers(double dt, IReadOnlyList<Controls> humans)
    {
        int h = 0;
        foreach (var b in Bombers)
        {
            Controls c = default;
            if (b.Human)
            {
                if (h < humans.Count) c = humans[h];
                h++;
            }
            else if (b.Alive) c = b.Pilot.Decide(this, b);
            b.Last = c;
            if (!b.Alive) continue;
            if (b.Invincible > 0) b.Invincible = Math.Max(0, b.Invincible - dt);
            bool bomb = c.Bomb && !b.BombPrev, detonate = c.Detonate && !b.DetonatePrev;
            b.BombPrev = c.Bomb;
            b.DetonatePrev = c.Detonate;
            Walk(b, c, dt);
            if (bomb) PlaceBomb(b);
            if (detonate) Detonate(b);
        }
    }

    /// <summary>Whether a bomber may walk into a cell: open floor (or a block, with wall pass), and no bomb unless it can pass bombs or was on this one when it went down.</summary>
    public bool CanWalk(Bomber b, int c, int r)
    {
        var t = Board.At(c, r);
        if (t is Tile.Pillar or Tile.Dropped) return false;
        if (t == Tile.Soft && !b.WallPass) return false;
        var bomb = BombAt(c, r);
        return bomb is null || b.BombPass || bomb.Inside.Contains(b);
    }

    /// <summary>
    /// The stick: walks along the row or column; pushed into a pillar's corner while partly past it, slides round it
    /// (as the original does), so the bomber needn't be lined up exactly to turn a corner. With two ways held, it tries
    /// the one across the way it faces first, so holding a diagonal turns at the next opening.
    /// </summary>
    private void Walk(Bomber b, Controls c, double dt)
    {
        Dir first, second = Dir.None;
        if (c.Dx != 0 && c.Dy != 0)
        {
            // Carry on the way it's going, and turn on reaching the middle of a cell (the corner assist does the
            // rest), so a held diagonal walks a staircase through the pillars rather than jittering on the spot.
            var h = Dirs.Of(c.Dx, 0);
            var v = Dirs.Of(0, c.Dy);
            bool across = Dirs.Horizontal(b.Facing);
            Dir along = across ? h : v, turn = across ? v : h;
            double a = across ? b.X : b.Y;
            int s = across ? c.Dx : c.Dy;
            double ahead = (Field.CellOf(a) - a) * s;
            bool atMiddle = ahead >= -1e-9 && ahead <= b.Speed * dt + 1e-9;
            (first, second) = atMiddle ? (turn, along) : (along, turn);
        }
        else first = Dirs.Of(c.Dx, c.Dy);
        b.Walking = false;
        if (first == Dir.None) return;
        double dist = b.Speed * dt;
        if (Step(b, first, dist)) b.Facing = first;
        else if (second != Dir.None && Step(b, second, dist)) b.Facing = second;
        else
        {
            b.Facing = first;
            return;
        }
        b.Walking = true;
        b.WalkTime += dt;
        if (b.Human && (int)(b.WalkTime * 4) != (int)((b.WalkTime - dt) * 4)) Play(Sfx.Step);
    }

    /// <summary>Moves a bomber up to <paramref name="dist"/> cells one way; true if it moved at all.</summary>
    internal bool Step(Bomber b, Dir d, double dist)
    {
        const double Eps = 1e-6;
        var (dx, dy) = Dirs.Vec(d);
        bool horiz = dx != 0;
        int s = horiz ? dx : dy;
        double a = horiz ? b.X : b.Y, cross = horiz ? b.Y : b.X;
        bool Open(int along, int across) => horiz ? CanWalk(b, along, across) : CanWalk(b, across, along);
        int lane = Field.CellOf(cross);
        int ca = Field.CellOf(a);
        double start = a + cross;

        // Off the line: slide on to it (round the corner, if this lane is blocked but the next one is open).
        double off = cross - lane;
        if (Math.Abs(off) > Eps)
        {
            int other = lane + Math.Sign(off);
            int target;
            if (Open(ca + s, lane)) target = lane;
            else if (Math.Abs(off) >= 0.2 && Open(ca + s, other) && Open(ca, other)) target = other;
            else return false;
            double need = target - cross, mv = Math.Min(dist, Math.Abs(need));
            cross += Math.Sign(need) * mv;
            dist -= mv;
            if (Math.Abs(target - cross) < Eps) cross = target;
            lane = Field.CellOf(cross);
            ca = Field.CellOf(a);
        }
        if (dist > Eps && Math.Abs(cross - lane) < Eps)
        {
            cross = lane;
            double rel = (a - ca) * s;
            if (Open(ca + s, lane)) a += s * dist;
            else if (rel < 0) a += s * Math.Min(dist, -rel);
        }
        if (horiz) (b.X, b.Y) = (a, cross);
        else (b.X, b.Y) = (cross, a);
        return Math.Abs(a + cross - start) > Eps;
    }

    // ───────────────────────────── bombs ─────────────────────────────

    public Bomb? BombAt(int c, int r)
    {
        foreach (var b in Bombs)
            if (b.C == c && b.R == r) return b;
        return null;
    }

    public int BombsOut(Bomber b) => Bombs.Count(x => x.Owner == b);

    /// <summary>Lays a bomb where the bomber stands, if it has one to spare and there isn't one there already.</summary>
    internal bool PlaceBomb(Bomber b)
    {
        int c = b.Cx, r = b.Cy;
        if (!Board.Floor(c, r) || BombAt(c, r) != null || BombsOut(b) >= b.Bombs || Flames[c, r].Burning) return false;
        var bomb = new Bomb { C = c, R = r, Owner = b, Range = b.Range, Remote = b.Remote, Fuse = b.Remote ? double.PositiveInfinity : Bomb.FuseSeconds };
        foreach (var o in Bombers.Where(o => o.Alive && o.Overlaps(c, r))) bomb.Inside.Add(o);
        Bombs.Add(bomb);
        Play(Sfx.BombPlace);
        Play(Sfx.Fuse);
        return true;
    }

    /// <summary>The detonator: sets off the bomber's oldest bomb that's waiting for it.</summary>
    internal bool Detonate(Bomber b)
    {
        var bomb = Bombs.FirstOrDefault(x => x.Owner == b && x.Remote);
        if (bomb is null) return false;
        Explode(bomb);
        return true;
    }

    private void UpdateBombs(double dt)
    {
        foreach (var b in Bombs)
        {
            b.Age += dt;
            b.Inside.RemoveWhere(o => !o.Alive || !o.Overlaps(b.C, b.R));
            if (!b.Remote) b.Fuse -= dt;
        }
        foreach (var b in Bombs.Where(b => b.Fuse <= 0).ToList())
            if (Bombs.Contains(b)) Explode(b);
    }

    /// <summary>
    /// A bomb bursts: flames out four ways as far as its range, stopped by pillars and by the first block (which burns
    /// away), or by a power-up lying in the open (which burns too). Any bomb the flames reach goes off at once, as one
    /// blast with it, so creatures caught by a chain count up the bonus together.
    /// </summary>
    internal void Explode(Bomb first, int group = 0)
    {
        if (group == 0) group = ++_group;
        var queue = new Queue<Bomb>();
        Bombs.Remove(first);
        queue.Enqueue(first);
        while (queue.Count > 0)
        {
            var bomb = queue.Dequeue();
            SetFlame(bomb.C, bomb.R, 0, group);
            foreach (var d in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(d);
                int px = bomb.C, py = bomb.R;
                for (int k = 1; k <= bomb.Range; k++)
                {
                    int x = bomb.C + dx * k, y = bomb.R + dy * k;
                    if (Board.Hard(x, y)) break;
                    if (Board.T[x, y] == Tile.Soft)
                    {
                        if (Crumble[x, y] <= 0)
                        {
                            Crumble[x, y] = CrumbleSeconds;
                            BlockHit(x, y, group);
                        }
                        break;
                    }
                    Flames[px, py].Mask |= Dirs.Bit(d);
                    SetFlame(x, y, Dirs.Bit(Dirs.Opposite(d)), group);
                    (px, py) = (x, y);
                    if (BombAt(x, y) is { } other)
                    {
                        Bombs.Remove(other);
                        queue.Enqueue(other);
                        break;
                    }
                    if (Board.Item[x, y] != PowerUp.None)
                    {
                        BurnItem(x, y, group);
                        break;
                    }
                    if (Board.Exit == (x, y))
                    {
                        ExitHit(x, y, group);
                        break;
                    }
                }
            }
        }
        Play(Sfx.Explosion);
    }

    private void SetFlame(int c, int r, int mask, int group)
    {
        ref var f = ref Flames[c, r];
        if (!f.Burning) f.Mask = 0;
        f.Time = FlameSeconds;
        f.Mask |= mask;
        f.Group = group;
        if (Board.Exit == (c, r) && Board.Floor(c, r) && mask == 0) ExitHit(c, r, group);
    }

    private void BlockHit(int c, int r, int group)
    {
        Play(Sfx.Crumble);
        // Anything inside the block (with wall pass, or a creature that drifts through blocks) burns with it.
        Flames[c, r].Group = group;
    }

    private void UpdateFire(double dt)
    {
        for (int c = 0; c < Board.Cols; c++)
            for (int r = 0; r < Board.Rows; r++)
            {
                if (Flames[c, r].Time > 0) Flames[c, r].Time = Math.Max(0, Flames[c, r].Time - dt);
                if (Crumble[c, r] <= 0) continue;
                Crumble[c, r] -= dt;
                if (Crumble[c, r] > 0) continue;
                Crumble[c, r] = 0;
                if (Board.T[c, r] == Tile.Soft) Board.T[c, r] = Tile.Floor;
                Effects.Add(new Effect { Kind = EffectKind.Debris, X = c, Y = r, Life = 0.45, Colour = 0xFFB86A3A });
            }
    }

    /// <summary>A power-up lying in the open caught by the flames burns up (and, in the story, lets creatures out).</summary>
    private void BurnItem(int c, int r, int group)
    {
        Board.Item[c, r] = PowerUp.None;
        Effects.Add(new Effect { Kind = EffectKind.Puff, X = c, Y = r, Life = 0.6 });
        if (Mode == Mode.Story) SpawnFrom(c, r, group);
    }

    private void ExitHit(int c, int r, int group)
    {
        if (Mode == Mode.Story && Board.Floor(c, r)) SpawnFrom(c, r, group);
    }

    // ───────────────────────────── who's been hit ─────────────────────────────

    /// <summary>Whether a cell is deadly now: flames in it, or a block burning (with anyone inside).</summary>
    public bool Deadly(int c, int r) => Board.Inside(c, r) && (Flames[c, r].Burning || Crumble[c, r] > 0);

    private void Hits()
    {
        foreach (var b in Bombers.Where(b => b.Alive))
        {
            if (b.Invincible > 0) continue;
            int c = b.Cx, r = b.Cy;
            bool burnt = (Flames[c, r].Burning && !b.FlamePass) || Crumble[c, r] > 0;
            bool caught = Enemies.Any(e => !e.Dead && e.Spawning <= 0 && Math.Abs(e.X - b.X) < 0.58 && Math.Abs(e.Y - b.Y) < 0.58);
            if (burnt || caught) Kill(b, burnt ? "BURNT" : "CAUGHT");
        }
        foreach (var e in Enemies.Where(e => !e.Dead && e.Spawning <= 0))
        {
            int c = e.Cx, r = e.Cy;
            if (Deadly(c, r)) KillEnemy(e, Flames[c, r].Group);
        }
    }

    private void Kill(Bomber b, string cause)
    {
        b.Alive = false;
        b.DeadTime = 0;
        b.Walking = false;
        Cause = cause;
        // Bombs waiting for a lost detonator go off by themselves.
        foreach (var bomb in Bombs.Where(x => x.Owner == b && x.Remote)) { bomb.Remote = false; bomb.Fuse = Bomb.FuseSeconds; }
        Play(Sfx.PlayerDeath);
        if (Mode == Mode.Story) StoryDeath();
    }

    /// <summary>A creature caught in a blast: its points, doubled for each one the same blast (chain and all) has caught before it.</summary>
    private void KillEnemy(Enemy e, int group)
    {
        e.Dead = true;
        e.DeadTime = 0;
        int n = _groupKills.GetValueOrDefault(group);
        _groupKills[group] = n + 1;
        int points = ChainPoints(e.Info.Points, n);
        AddScore(points, e.X, e.Y);
        Play(Sfx.EnemyDeath);
    }

    /// <summary>What the <paramref name="earlier"/>+1th creature caught by one blast is worth: each doubles the last.</summary>
    public static int ChainPoints(int points, int earlier) => points << Math.Min(earlier, 7);

    public string Cause { get; private set; } = "";

    private void Pickups()
    {
        foreach (var b in Bombers.Where(b => b.Alive))
        {
            int c = b.Cx, r = b.Cy;
            var p = Board.Visible(c, r);
            if (p == PowerUp.None) continue;
            Board.Item[c, r] = PowerUp.None;
            Give(b, p);
            Effects.Add(new Effect { Kind = EffectKind.Sparkle, X = c, Y = r, Life = 0.7 });
            Effects.Add(new Effect { Kind = EffectKind.Word, X = c, Y = r - 0.3, Text = Name(p), Life = 1.1, Colour = 0xFFFFE14A, Size = 0.8 });
            if (Mode == Mode.Story) AddScore(PowerUpPoints);
            Play(Sfx.PowerUp);
        }
    }

    public static string Name(PowerUp p) => p switch
    {
        PowerUp.Bombs => "BOMB UP", PowerUp.Fire => "FIRE UP", PowerUp.Speed => "SPEED UP", PowerUp.Remote => "DETONATOR",
        PowerUp.WallPass => "WALL PASS", PowerUp.BombPass => "BOMB PASS", PowerUp.FlamePass => "FLAME PROOF", PowerUp.Mystery => "INVINCIBLE!",
        _ => "",
    };

    public const double MysterySeconds = 12;

    internal static void Give(Bomber b, PowerUp p)
    {
        switch (p)
        {
            case PowerUp.Bombs: b.Bombs = Math.Min(Bomber.MaxBombs, b.Bombs + 1); break;
            case PowerUp.Fire: b.Range = Math.Min(Bomber.MaxRange, b.Range + 1); break;
            case PowerUp.Speed: b.SpeedUps = Math.Min(Bomber.MaxSpeedUps, b.SpeedUps + 1); break;
            case PowerUp.Remote: b.Remote = true; break;
            case PowerUp.WallPass: b.WallPass = true; break;
            case PowerUp.BombPass: b.BombPass = true; break;
            case PowerUp.FlamePass: b.FlamePass = true; break;
            case PowerUp.Mystery: b.Invincible = MysterySeconds; break;
        }
    }

    private void AddScore(int points, double? x = null, double? y = null)
    {
        Score += points;
        if (x is { } px && y is { } py)
            Effects.Add(new Effect { Kind = EffectKind.Score, X = px, Y = py, Text = points.ToString(), Colour = points >= 1600 ? 0xFFFFE14A : 0xFFFFFFFF, Life = 1.1 });
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>A field of one's own, with nothing on it.</summary>
    internal void SetBoard(Board b)
    {
        Board = b;
        ClearField();
    }

    internal void SetBoard(params string[] rows) => SetBoard(Board.Parse(rows));

    internal void PutBomber(Bomber b, double x, double y, Dir facing = Dir.Down)
    {
        (b.X, b.Y) = (x, y);
        b.Facing = facing;
        b.Alive = true;
        b.Walking = false;
        b.BombPrev = b.DetonatePrev = false;
    }

    internal Enemy PutEnemy(EnemyKind kind, int c, int r, Dir facing = Dir.Left)
    {
        var e = new Enemy { Kind = kind, Facing = facing };
        e.Place(c, r);
        Enemies.Add(e);
        return e;
    }

    internal Bomb PutBomb(int c, int r, int range = 1, Bomber? owner = null, double fuse = Bomb.FuseSeconds)
    {
        var b = new Bomb { C = c, R = r, Range = range, Owner = owner, Fuse = fuse };
        Bombs.Add(b);
        return b;
    }

    internal void SetScore(int s) => Score = s;
}
