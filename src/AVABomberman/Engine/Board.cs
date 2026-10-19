namespace AVABomberman.Engine;

/// <summary>
/// A field: the pillars and the wall round it, the breakable blocks, and what is hidden under them (a power-up, the
/// story's exit door). A power-up or the door shows once its block is burnt away.
/// </summary>
public sealed class Board
{
    public int Cols { get; }
    public int Rows { get; }
    public readonly Tile[,] T;
    /// <summary>The power-up in each cell: hidden while a block is on it.</summary>
    public readonly PowerUp[,] Item;
    /// <summary>Where the story's exit door is (hidden until its block burns), or null in a battle.</summary>
    public (int C, int R)? Exit;

    public Board(int cols, int rows)
    {
        Cols = cols;
        Rows = rows;
        T = new Tile[cols, rows];
        Item = new PowerUp[cols, rows];
    }

    public bool Inside(int c, int r) => c >= 0 && c < Cols && r >= 0 && r < Rows;
    public Tile At(int c, int r) => Inside(c, r) ? T[c, r] : Tile.Pillar;
    public bool Floor(int c, int r) => Inside(c, r) && T[c, r] == Tile.Floor;
    public bool Hard(int c, int r) => At(c, r) is Tile.Pillar or Tile.Dropped;

    /// <summary>The power-up lying in the open in a cell (None under a block).</summary>
    public PowerUp Visible(int c, int r) => Floor(c, r) ? Item[c, r] : PowerUp.None;

    public bool ExitShowing => Exit is var (c, r) && Floor(c, r);

    public static bool IsPillar(int c, int r) => c % 2 == 0 && r % 2 == 0;

    public IEnumerable<(int C, int R)> Cells()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                yield return (c, r);
    }

    public int Count(Tile t) => Cells().Count(p => T[p.C, p.R] == t);

    /// <summary>Steps from (c, r) to every cell, through cells <paramref name="pass"/> allows (-1 where it can't get).</summary>
    public int[,] Distances(int c, int r, Func<int, int, bool> pass)
    {
        var d = new int[Cols, Rows];
        for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                d[x, y] = -1;
        if (!Inside(c, r)) return d;
        var q = new Queue<(int, int)>();
        d[c, r] = 0;
        q.Enqueue((c, r));
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var w in Dirs.All)
            {
                var (dx, dy) = Dirs.Vec(w);
                int nx = x + dx, ny = y + dy;
                if (!Inside(nx, ny) || d[nx, ny] >= 0 || !pass(nx, ny)) continue;
                d[nx, ny] = d[x, y] + 1;
                q.Enqueue((nx, ny));
            }
        }
        return d;
    }

    /// <summary>The wall round the edge and the pillars, with open floor between.</summary>
    public static Board Open(int cols, int rows)
    {
        var b = new Board(cols, rows);
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
                b.T[c, r] = c == 0 || r == 0 || c == cols - 1 || r == rows - 1 || IsPillar(c, r) ? Tile.Pillar : Tile.Floor;
        return b;
    }

    /// <summary>
    /// A story stage: blocks strewn over about a third of the open floor (never right by the start in the top left
    /// corner), the stage's one power-up under one of them and the exit door under another, well away from the start.
    /// </summary>
    public static Board Story(Random rng, PowerUp power, double density = 0.3)
    {
        var b = Open(Field.StoryCols, Field.StoryRows);
        var open = b.Cells().Where(p => b.T[p.C, p.R] == Tile.Floor && p.C + p.R > 3).ToList();
        Shuffle(rng, open);
        int blocks = (int)(open.Count * density);
        var soft = open.Take(blocks).ToList();
        foreach (var (c, r) in soft) b.T[c, r] = Tile.Soft;
        var far = soft.Where(p => p.C >= 8).ToList();
        var exit = far[rng.Next(far.Count)];
        b.Exit = exit;
        var others = soft.Where(p => p != exit && p.C >= 3).ToList();
        var item = others[rng.Next(others.Count)];
        b.Item[item.C, item.R] = power;
        return b;
    }

    /// <summary>A bonus stage: just the pillars.</summary>
    public static Board Bonus() => Open(Field.StoryCols, Field.StoryRows);

    /// <summary>Where the battle's bombers start: the four corners.</summary>
    public static readonly (int C, int R)[] BattleStarts = [(1, 1), (Field.BattleCols - 2, Field.BattleRows - 2), (Field.BattleCols - 2, 1), (1, Field.BattleRows - 2)];

    /// <summary>
    /// A battle arena: blocks on most of the floor but the corners each bomber starts in, about a third of them
    /// hiding a power-up (mostly bombs and fire).
    /// </summary>
    public static Board Battle(Random rng)
    {
        var b = Open(Field.BattleCols, Field.BattleRows);
        bool NearStart(int c, int r) => BattleStarts.Any(s => Math.Abs(s.C - c) + Math.Abs(s.R - r) <= 1);
        var soft = new List<(int C, int R)>();
        foreach (var (c, r) in b.Cells().ToList())
        {
            if (b.T[c, r] != Tile.Floor || NearStart(c, r) || rng.NextDouble() > 0.78) continue;
            b.T[c, r] = Tile.Soft;
            soft.Add((c, r));
        }
        Shuffle(rng, soft);
        PowerUp[] pool =
        [
            PowerUp.Bombs, PowerUp.Bombs, PowerUp.Bombs, PowerUp.Bombs, PowerUp.Bombs, PowerUp.Bombs, PowerUp.Bombs,
            PowerUp.Fire, PowerUp.Fire, PowerUp.Fire, PowerUp.Fire, PowerUp.Fire, PowerUp.Fire, PowerUp.Fire,
            PowerUp.Speed, PowerUp.Speed, PowerUp.Speed, PowerUp.Speed, PowerUp.BombPass, PowerUp.Remote, PowerUp.FlamePass,
        ];
        for (int i = 0; i < Math.Min(pool.Length, soft.Count); i++) b.Item[soft[i].C, soft[i].R] = pool[i];
        return b;
    }

    /// <summary>
    /// The order the hurry-up's blocks drop in: round the arena from the top left, clockwise, ring by ring inwards,
    /// missing out the pillars and leaving the middle open.
    /// </summary>
    public IReadOnlyList<(int C, int R)> Spiral(int leave = 7)
    {
        var order = new List<(int C, int R)>();
        int left = 1, top = 1, right = Cols - 2, bottom = Rows - 2;
        while (left <= right && top <= bottom)
        {
            for (int c = left; c <= right; c++) order.Add((c, top));
            for (int r = top + 1; r <= bottom; r++) order.Add((right, r));
            if (top < bottom) for (int c = right - 1; c >= left; c--) order.Add((c, bottom));
            if (left < right) for (int r = bottom - 1; r > top; r--) order.Add((left, r));
            left++; top++; right--; bottom--;
        }
        var cells = order.Where(p => !IsPillar(p.C, p.R)).ToList();
        return cells.Take(Math.Max(0, cells.Count - leave)).ToList();
    }

    /// <summary>
    /// A field drawn as text, for tests: '#' a pillar, '+' a block, '.' floor; a block hiding something is a lower-case
    /// letter and the thing lying in the open its capital: e the exit door, b bombs, f fire, s speed, r remote, w wall
    /// pass, p bomb pass, l flame-proof, m mystery.
    /// </summary>
    public static Board Parse(params string[] rows)
    {
        var b = new Board(rows.Max(r => r.Length), rows.Length);
        for (int r = 0; r < b.Rows; r++)
            for (int c = 0; c < b.Cols; c++)
            {
                char ch = c < rows[r].Length ? rows[r][c] : '.';
                b.T[c, r] = ch switch { '#' => Tile.Pillar, '.' => Tile.Floor, _ when char.IsLower(ch) || ch == '+' => Tile.Soft, _ => Tile.Floor };
                char k = char.ToLowerInvariant(ch);
                if (k == 'e') b.Exit = (c, r);
                b.Item[c, r] = k switch
                {
                    'b' => PowerUp.Bombs, 'f' => PowerUp.Fire, 's' => PowerUp.Speed, 'r' => PowerUp.Remote, 'w' => PowerUp.WallPass,
                    'p' => PowerUp.BombPass, 'l' => PowerUp.FlamePass, 'm' => PowerUp.Mystery, _ => PowerUp.None,
                };
            }
        return b;
    }

    private static void Shuffle<T>(Random rng, List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
