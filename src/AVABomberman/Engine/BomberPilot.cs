namespace AVABomberman.Engine;

/// <summary>
/// A computer bomber, for the battle's computer players and the story's demonstration (and the tests).
/// <para>
/// It works out when the fire will reach every cell and when it will be gone again: each bomb's blast, with the
/// chains they set off, the flames burning now, and (in a hurry-up) when the blocks will drop. In danger it searches
/// through time as well as space (stepping or waiting a moment at a time) for the quickest way to somewhere the fire
/// won't reach, so it can wait for a blast to die down and slip through behind it. Out of danger it weighs up the
/// power-ups lying about, the story's open door, and every cell it could lay a bomb in, for the blocks the blast
/// would break and the creatures or bombers it would catch, but only where it could still get clear of its own
/// bomb; and goes for the best, less the walk there.
/// </para>
/// </summary>
public sealed class BomberPilot
{
    /// <summary>How far ahead (in steps of a cell) it looks for a way out of danger.</summary>
    private const int Horizon = 16;

    /// <summary>What it is up to, for the probe and the tests.</summary>
    public string Doing { get; private set; } = "";
    public (int C, int R) Goal => _goal;

    private (int C, int R) _goal = (-1, -1);
    private bool _bombAtGoal;
    private (int, int) _planCell = (-1, -1);
    private long _plannedAt = -100;
    private int _bombsSeen = -1;
    private int _cols, _rows;
    private double[,] _start = new double[1, 1], _end = new double[1, 1];
    private double[,] _hStart = new double[1, 1], _hEnd = new double[1, 1];
    private int[,] _dist = new int[1, 1];
    private Dir[,] _first = new Dir[1, 1];
    private bool[,] _nearEnemy = new bool[1, 1], _blocked = new bool[1, 1];
    private bool[,,] _seen = new bool[1, 1, 1];
    private readonly Queue<(int C, int R, int T, Dir First)> _queue = new();

    public void Reset()
    {
        _goal = (-1, -1);
        _bombAtGoal = false;
        _planCell = (-1, -1);
        _plannedAt = -100;
        _bombsSeen = -1;
        Doing = "";
    }

    private static Controls Press(Dir d)
    {
        var (dx, dy) = Dirs.Vec(d);
        return new Controls(dx, dy);
    }

    /// <summary>Stand still in the middle of the cell (clear of the next one's fire).</summary>
    private static Controls Centre(Bomber me)
    {
        double ox = me.Cx - me.X, oy = me.Cy - me.Y;
        if (Math.Abs(ox) > 0.06) return Press(Dirs.Of(Math.Sign(ox), 0));
        if (Math.Abs(oy) > 0.06) return Press(Dirs.Of(0, Math.Sign(oy)));
        return default;
    }

    public Controls Decide(GameEngine g, Bomber me)
    {
        if (g.Phase != Phase.Play || !me.Alive) return default;
        Size(g.Board);
        int cx = me.Cx, cy = me.Cy;
        double stepTime = 1.0 / me.Speed;
        Obstacles(g, me);

        // The detonator: set its bombs off once it's out of their way, standing still while they go.
        if (me.Remote && g.Bombs.Any(b => b.Owner == me && b.Remote))
        {
            FireTimes(g, me, null, _start, _end, ownRemoteNow: true);
            if (Rest(cx, cy, _start) && !me.DetonatePrev)
            {
                Doing = "detonate";
                return new Controls(Detonate: true);
            }
        }

        FireTimes(g, me, null, _start, _end);
        bool chased = me.Invincible <= 2 && g.Enemies.Any(e => !e.Dead && e.Spawning <= 0.3 && Math.Abs(e.X - me.X) + Math.Abs(e.Y - me.Y) < 1.9);
        if (!Rest(cx, cy, _start) || chased)
        {
            // In danger: the quickest way out, waiting where that's best.
            _plannedAt = -100;
            double delay = (Math.Abs(me.X - cx) + Math.Abs(me.Y - cy)) * stepTime;
            var (found, first) = Flee(cx, cy, stepTime, delay, _start, _end);
            if (found)
            {
                Doing = "dodge";
                return first == Dir.None ? Centre(me) : Press(first);
            }
            if (chased && CanBomb(g, me, cx, cy) && !me.BombPrev && EscapesAfterBomb(g, me, cx, cy, 0))
            {
                Doing = "cornered";
                return new Controls(Bomb: true);
            }
            // No way out: get as far from it as it can.
            Doing = "brace";
            Dir best = Dir.None;
            double latest = _start[cx, cy];
            foreach (var d in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(d);
                int nx = cx + dx, ny = cy + dy;
                if (_blocked[nx, ny]) continue;
                double s = _start[nx, ny] - (_nearEnemy[nx, ny] ? 0.5 : 0);
                if (s > latest + 0.05) { latest = s; best = d; }
            }
            return best == Dir.None ? Centre(me) : Press(best);
        }

        bool replan = (cx, cy) != _planCell || g.Steps - _plannedAt >= 10 || g.Bombs.Count != _bombsSeen;
        if (replan) Plan(g, me);

        if (_bombAtGoal && (cx, cy) == _goal && Math.Abs(me.X - cx) < 0.3 && Math.Abs(me.Y - cy) < 0.3)
        {
            if (me.BombPrev) return default;
            if (CanBomb(g, me, cx, cy) && EscapesAfterBomb(g, me, cx, cy, 0))
            {
                _plannedAt = -100;
                Doing = "bomb";
                return new Controls(Bomb: true);
            }
            Plan(g, me);
        }

        if (_goal == (cx, cy) || _goal.C < 0) return Centre(me);
        var step = _dist[_goal.C, _goal.R] >= 0 ? _first[_goal.C, _goal.R] : Dir.None;
        return step == Dir.None ? Centre(me) : Press(step);
    }

    private void Size(Board b)
    {
        if (_cols == b.Cols && _rows == b.Rows) return;
        (_cols, _rows) = (b.Cols, b.Rows);
        _start = new double[_cols, _rows];
        _end = new double[_cols, _rows];
        _hStart = new double[_cols, _rows];
        _hEnd = new double[_cols, _rows];
        _dist = new int[_cols, _rows];
        _first = new Dir[_cols, _rows];
        _nearEnemy = new bool[_cols, _rows];
        _blocked = new bool[_cols, _rows];
        _seen = new bool[_cols, _rows, Horizon + 1];
    }

    // ───────────────────────────── where the fire will be ─────────────────────────────

    /// <summary>The cells a bomb's blast would reach on the board as it is: up to a pillar, or onto the first block, bomb, power-up or door.</summary>
    public static List<(int C, int R)> Blast(GameEngine g, int c, int r, int range, (int C, int R)? extraBomb = null)
    {
        var cells = new List<(int C, int R)> { (c, r) };
        var b = g.Board;
        foreach (var d in Dirs.All)
        {
            var (dx, dy) = Dirs.Vec(d);
            for (int k = 1; k <= range; k++)
            {
                int x = c + dx * k, y = r + dy * k;
                if (b.Hard(x, y)) break;
                cells.Add((x, y));
                if (b.T[x, y] == Tile.Soft || g.BombAt(x, y) != null || extraBomb == (x, y) || b.Item[x, y] != PowerUp.None || b.Exit == (x, y)) break;
            }
        }
        return cells;
    }

    /// <summary>
    /// When the fire reaches each cell (seconds from now, or from <paramref name="shift"/> seconds from now; infinity
    /// if it never will) and when it's gone again: every bomb (and <paramref name="extra"/>, one it's thinking of
    /// laying), with the chains they set off; the flames burning now; and the hurry-up's blocks, which never go.
    /// </summary>
    private static void FireTimes(GameEngine g, Bomber me, (int C, int R, int Range)? extra, double[,] start, double[,] end, bool ownRemoteNow = false, double shift = 0)
    {
        var board = g.Board;
        for (int c = 0; c < board.Cols; c++)
            for (int r = 0; r < board.Rows; r++)
            {
                start[c, r] = double.PositiveInfinity;
                end[c, r] = double.NegativeInfinity;
                if (g.Flames[c, r].Burning && !(me.FlamePass || me.Invincible > 3)) { start[c, r] = 0; end[c, r] = g.Flames[c, r].Time - shift; }
                if (g.Crumble[c, r] > 0) { start[c, r] = 0; end[c, r] = Math.Max(end[c, r], g.Crumble[c, r] - shift); }
                if (end[c, r] < 0 && start[c, r] == 0) { start[c, r] = double.PositiveInfinity; end[c, r] = double.NegativeInfinity; }
            }
        // Flame-proof (or invincible for a good while yet), it only has to keep out of the burning blocks and the hurry-up.
        bool immune = me.FlamePass || me.Invincible > 3;
        var bombs = new List<(int C, int R, int Range, double T)>();
        foreach (var b in g.Bombs)
        {
            double t = b.Remote ? (b.Owner == me ? (ownRemoteNow ? 0.05 : 2.0) : 0.3) : Math.Max(0, b.Fuse - shift);
            bombs.Add((b.C, b.R, b.Range, t));
        }
        (int C, int R)? extraCell = null;
        if (extra is var (ec, er, erange))
        {
            bombs.Add((ec, er, erange, me.Remote ? 2.0 : Bomb.FuseSeconds));
            extraCell = (ec, er);
        }

        // Chains: a bomb in an earlier blast goes off with it.
        var blasts = bombs.Select(b => Blast(g, b.C, b.R, b.Range, extraCell)).ToList();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = 0; i < bombs.Count; i++)
                foreach (var (x, y) in blasts[i])
                    for (int j = 0; j < bombs.Count; j++)
                        if (j != i && bombs[j].C == x && bombs[j].R == y && bombs[j].T > bombs[i].T + 1e-9)
                        {
                            bombs[j] = bombs[j] with { T = bombs[i].T };
                            changed = true;
                        }
        }
        for (int i = 0; i < bombs.Count; i++)
            foreach (var (x, y) in blasts[i])
            {
                if (immune && board.T[x, y] != Tile.Soft) continue;
                double t = bombs[i].T;
                if (t < start[x, y]) start[x, y] = t;
                double e = t + GameEngine.FlameSeconds + (board.T[x, y] == Tile.Soft ? GameEngine.CrumbleSeconds : 0);
                if (e > end[x, y]) end[x, y] = e;
            }

        // The creatures: wherever one could get to in the next moment is as good as on fire, for good.
        if (me.Invincible <= 3)
        {
            foreach (var e in g.Enemies)
            {
                if (e.Dead) continue;
                double speed = e.Info.Speed;
                int depth = (int)Math.Ceiling(speed * 0.6) + 1;
                var reach = new Dictionary<(int, int), int> { [(e.Cx, e.Cy)] = 0 };
                if ((e.Tx, e.Ty) != (e.Cx, e.Cy) && g.CanCreep(e, e.Tx, e.Ty)) reach[(e.Tx, e.Ty)] = 0;
                var frontier = new Queue<(int, int)>(reach.Keys);
                while (frontier.Count > 0)
                {
                    var (x, y) = frontier.Dequeue();
                    int k = reach[(x, y)];
                    double t = Math.Max(0, (k - 1) / speed + e.Spawning);
                    if (t < start[x, y]) start[x, y] = t;
                    end[x, y] = double.PositiveInfinity;
                    if (k >= depth) continue;
                    foreach (var d in Dirs.All)
                    {
                        var (dx, dy) = Dirs.Vec(d);
                        int nx = x + dx, ny = y + dy;
                        if (reach.ContainsKey((nx, ny)) || !g.CanCreep(e, nx, ny)) continue;
                        reach[(nx, ny)] = k + 1;
                        frontier.Enqueue((nx, ny));
                    }
                }
            }
        }

        if (g.Hurry)
        {
            int k = 0;
            foreach (var (x, y) in g.DropsToCome)
            {
                double t = k++ * GameEngine.DropInterval - shift;
                if (t > 14) break;
                start[x, y] = Math.Min(start[x, y], Math.Max(0, t - 0.3));
                end[x, y] = double.PositiveInfinity;
            }
        }
    }

    // ───────────────────────────── where it can get ─────────────────────────────

    private bool _strict;

    /// <summary>Whether it can be in a cell from <paramref name="t0"/> to <paramref name="t1"/> without the fire catching it (with more to spare when it's choosing to lay a bomb).</summary>
    private bool SafeSpan(double[,] start, double[,] end, int c, int r, double t0, double t1) =>
        _strict ? t1 < start[c, r] - 0.3 : t1 < start[c, r] - 0.12 || t0 > end[c, r] + 0.1;

    /// <summary>Safe from <paramref name="t"/> on, as far as it can tell (somewhere no fire will reach, when it's choosing to lay a bomb).</summary>
    private bool Settled(double[,] start, double[,] end, int c, int r, double t) =>
        double.IsPositiveInfinity(start[c, r]) || (!_strict && t > end[c, r] + 0.1);

    private void Obstacles(GameEngine g, Bomber me)
    {
        var board = g.Board;
        for (int c = 0; c < board.Cols; c++)
            for (int r = 0; r < board.Rows; r++)
            {
                _blocked[c, r] = !g.CanWalk(me, c, r) || g.Crumble[c, r] > 0;
                _nearEnemy[c, r] = false;
            }
        // Creatures: their cells and those right by them are no way to go; a little further off, no place to stop.
        foreach (var e in g.Enemies.Where(e => !e.Dead && me.Invincible <= 2))
        {
            double fast = e.Info.Speed > 3 ? 1 : 0;
            for (int c = Math.Max(0, e.Cx - 4); c <= Math.Min(board.Cols - 1, e.Cx + 4); c++)
                for (int r = Math.Max(0, e.Cy - 4); r <= Math.Min(board.Rows - 1, e.Cy + 4); r++)
                {
                    double d = Math.Min(Math.Abs(e.X - c) + Math.Abs(e.Y - r), Math.Abs(e.Tx - c) + Math.Abs(e.Ty - r));
                    if (d <= 1.6 + fast) _nearEnemy[c, r] = true;
                    if (d < 0.8 && e.Spawning <= 0.4) _blocked[c, r] = true;
                }
        }
        _blocked[me.Cx, me.Cy] = false;
    }

    /// <summary>Steps from (c, r) to every cell it can get to without being caught by the fire (or a creature) on the way.</summary>
    private void Reach(int sc, int sr, double stepTime, double delay)
    {
        for (int c = 0; c < _cols; c++)
            for (int r = 0; r < _rows; r++)
                _dist[c, r] = -1;
        var q = new Queue<(int, int)>();
        _dist[sc, sr] = 0;
        q.Enqueue((sc, sr));
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var w in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(w);
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= _cols || ny >= _rows || _dist[nx, ny] >= 0 || _blocked[nx, ny]) continue;
                double arrive = delay + (_dist[x, y] + 0.5) * stepTime;
                if (!SafeSpan(_start, _end, nx, ny, arrive, arrive + stepTime)) continue;
                _dist[nx, ny] = _dist[x, y] + 1;
                _first[nx, ny] = _dist[x, y] == 0 ? w : _first[x, y];
                q.Enqueue((nx, ny));
            }
        }
    }

    private bool Rest(int c, int r, double[,] start) => double.IsPositiveInfinity(start[c, r]) && !_nearEnemy[c, r];

    /// <summary>
    /// The quickest way from (sc, sr) to somewhere the fire won't reach, through time as well as space: each moment
    /// it can step to the next cell or wait where it is, so long as the fire isn't there. The first move (None to
    /// wait), if there's a way.
    /// </summary>
    private (bool Found, Dir First) Flee(int sc, int sr, double stepTime, double delay, double[,] start, double[,] end)
    {
        Array.Clear(_seen);
        _queue.Clear();
        _queue.Enqueue((sc, sr, 0, Dir.None));
        _seen[sc, sr, 0] = true;
        while (_queue.Count > 0)
        {
            var (c, r, t, first) = _queue.Dequeue();
            double t0 = delay + t * stepTime;
            if (t > 0 && Settled(start, end, c, r, t0) && !_nearEnemy[c, r]) return (true, first);
            if (t >= Horizon) continue;
            if (SafeSpan(start, end, c, r, t0, t0 + stepTime) && !_seen[c, r, t + 1])
            {
                _seen[c, r, t + 1] = true;
                _queue.Enqueue((c, r, t + 1, first));
            }
            // Leaving: in this cell for the first half of the step (already committed at the start), the next for the second.
            if (t > 0 && !SafeSpan(start, end, c, r, t0, t0 + stepTime * 0.5)) continue;
            foreach (var d in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(d);
                int nx = c + dx, ny = r + dy;
                if (nx < 0 || ny < 0 || nx >= _cols || ny >= _rows || _blocked[nx, ny] || _seen[nx, ny, t + 1]) continue;
                if (!SafeSpan(start, end, nx, ny, t0 + stepTime * 0.5, t0 + stepTime)) continue;
                _seen[nx, ny, t + 1] = true;
                _queue.Enqueue((nx, ny, t + 1, t == 0 ? d : first));
            }
        }
        return (false, Dir.None);
    }

    /// <summary>Whether, having laid a bomb at (c, r) <paramref name="arrive"/> seconds from now, it could get somewhere safe.</summary>
    private bool EscapesAfterBomb(GameEngine g, Bomber me, int c, int r, double arrive)
    {
        FireTimes(g, me, (c, r, me.Range), _hStart, _hEnd, shift: arrive);
        // Not where the fire is already due before it could get away.
        if (_hStart[c, r] < 1.0) return false;
        bool wasBlocked = _blocked[c, r];
        _blocked[c, r] = false;
        _strict = true;
        var (found, _) = Flee(c, r, 1.0 / me.Speed, 0.1, _hStart, _hEnd);
        _strict = false;
        _blocked[c, r] = wasBlocked;
        return found;
    }

    private static bool CanBomb(GameEngine g, Bomber me, int c, int r) =>
        g.BombsOut(me) < me.Bombs && g.Board.Floor(c, r) && g.BombAt(c, r) is null && (!me.Remote || !g.Bombs.Any(b => b.Owner == me));

    // ───────────────────────────── what to do ─────────────────────────────

    private void Plan(GameEngine g, Bomber me)
    {
        int cx = me.Cx, cy = me.Cy;
        _planCell = (cx, cy);
        _plannedAt = g.Steps;
        _bombsSeen = g.Bombs.Count;
        var board = g.Board;
        double stepTime = 1.0 / me.Speed;
        double delay = (Math.Abs(me.X - cx) + Math.Abs(me.Y - cy)) * stepTime;
        Reach(cx, cy, stepTime, delay);

        bool story = g.Mode == Mode.Story;
        bool allGone = story && g.Enemies.All(e => e.Dead);
        double bestScore = 0.5;
        (int C, int R) goal = (cx, cy);
        bool bomb = false;
        string doing = "wait";
        bool canBomb = g.BombsOut(me) < me.Bombs && (!me.Remote || !g.Bombs.Any(b => b.Owner == me));
        var opponents = g.Bombers.Where(o => o != me && o.Alive).ToList();
        var doomed = new HashSet<(int, int)>();
        foreach (var b in g.Bombs)
            foreach (var cell in Blast(g, b.C, b.R, b.Range))
                doomed.Add(cell);

        // Cells in order of the walk, nearest first.
        var order = new List<(int C, int R, int D)>();
        for (int c = 0; c < _cols; c++)
            for (int r = 0; r < _rows; r++)
                if (_dist[c, r] >= 0) order.Add((c, r, _dist[c, r]));
        order.Sort((a, b) => a.D.CompareTo(b.D));

        int checks = 0;
        foreach (var (c, r, d) in order)
        {
            double walk = d * (story ? 1.0 : 2.2);
            // A bomb can go down near a creature (that's the point), so long as it can get away again.
            if (_start[c, r] < delay + d * stepTime + 1.0) continue;
            if (canBomb && checks < 30 && board.Floor(c, r) && g.BombAt(c, r) is null)
            {
                double value = BombValue(g, me, c, r, story, allGone, opponents, doomed, delay + d * stepTime);
                double v = value - walk + ((c, r) == _goal ? 6 : 0);
                if (value > 0 && v > bestScore)
                {
                    checks++;
                    if (EscapesAfterBomb(g, me, c, r, delay + d * stepTime))
                    {
                        bestScore = v;
                        goal = (c, r);
                        bomb = true;
                        doing = value >= 30 ? "attack" : "blocks";
                    }
                }
            }
            if (!Rest(c, r, _start)) continue;
            double stick = (c, r) == _goal ? 6 : 0;
            var item = board.Visible(c, r);
            if (item != PowerUp.None)
            {
                double v = (story ? 90 : 75) - walk + stick;
                if (v > bestScore) { bestScore = v; goal = (c, r); bomb = false; doing = "power-up"; }
            }
            if (story && g.ExitOpen && board.Exit == (c, r))
            {
                double v = 1000 - walk;
                if (v > bestScore) { bestScore = v; goal = (c, r); bomb = false; doing = "exit"; }
            }
        }

        // Nothing worth doing in reach: close in on the nearest opponent or creature (or the door).
        if (doing == "wait")
        {
            var targets = story ? g.Enemies.Where(e => !e.Dead).Select(e => (e.X, e.Y)).ToList() : opponents.Select(o => (o.X, o.Y)).ToList();
            if (story && board.ExitShowing && board.Exit is var (exc, exr)) targets.Add((exc, exr));
            if (targets.Count > 0)
            {
                double bestNear = double.MaxValue;
                foreach (var (c, r, d) in order)
                {
                    if (!Rest(c, r, _start)) continue;
                    double near = targets.Min(t => Math.Abs(t.Item1 - c) + Math.Abs(t.Item2 - r));
                    double v = (near < 2.5 && !allGone ? 40 + near : near) + d * 0.15;
                    if (v < bestNear) { bestNear = v; goal = (c, r); doing = "hunt"; }
                }
            }
        }
        _goal = goal;
        _bombAtGoal = bomb;
        Doing = doing;
    }

    /// <summary>What a bomb laid at (c, r) would be worth: the blocks it would break, the creatures or bombers it would catch, less anything worth keeping that it would burn.</summary>
    private static double BombValue(GameEngine g, Bomber me, int c, int r, bool story, bool allGone, List<Bomber> opponents, HashSet<(int, int)> doomed, double arrive)
    {
        double v = 0;
        var board = g.Board;
        var blast = Blast(g, c, r, me.Range);
        foreach (var (x, y) in blast)
        {
            if (board.T[x, y] == Tile.Soft && g.Crumble[x, y] <= 0 && !doomed.Contains((x, y))) v += allGone ? 22 : 10;
            if (board.Visible(x, y) != PowerUp.None) v -= 30;
            if (story && board.Exit == (x, y) && board.Floor(x, y)) v -= 300;
        }
        if (story)
        {
            foreach (var e in g.Enemies)
            {
                if (e.Dead || e.Spawning > 0) continue;
                // Where it is now means less the longer the walk there: it will have moved on. In the blast, or near
                // enough to wander into it before it goes, is worth a bomb.
                double fade = Math.Max(0, 1 - e.Info.Speed * arrive / 3);
                if (fade <= 0) continue;
                double near = blast.Skip(1).Select(b => Math.Abs(b.C - e.X) + Math.Abs(b.R - e.Y)).DefaultIfEmpty(9).Min();
                // A creature in line with the blast, coming along the corridor, often walks into it.
                bool inLine = Math.Abs(e.X - c) < 0.3 || Math.Abs(e.Y - r) < 0.3;
                v += fade * (near < 0.5 ? 50 : near < 1.5 ? 30 : near < 2.5 && inLine ? 20 : near < 2.5 ? 10 : 0);
            }
        }
        else
        {
            foreach (var o in opponents)
            {
                if (blast.Contains((o.Cx, o.Cy))) v += o.Invincible > 0 ? 0 : 45;
                else if (Math.Abs(o.Cx - c) + Math.Abs(o.Cy - r) <= 2) v += 8;
            }
        }
        return v;
    }
}
