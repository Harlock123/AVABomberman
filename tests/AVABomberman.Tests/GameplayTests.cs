using Avalonia.Input;
using AVABomberman.Engine;

namespace AVABomberman.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avabomberman-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A story game in play on a field of one's own (just the wall and the pillars unless a test adds more), with no creatures and no clock.</summary>
    private static GameEngine Story(int cols = 15, int rows = 11, params (int C, int R, char Ch)[] cells)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(1);
        g.SetBoard(Arena(cols, rows, cells));
        return g;
    }

    /// <summary>The wall, the pillars and the given cells: '+' a block, '#' a pillar, a letter as in <see cref="Board.Parse"/>.</summary>
    private static Board Arena(int cols, int rows, params (int C, int R, char Ch)[] cells)
    {
        var b = Board.Open(cols, rows);
        var text = Enumerable.Range(0, rows).Select(r => Enumerable.Range(0, cols).Select(c => b.T[c, r] == Tile.Pillar ? '#' : '.').ToArray()).ToArray();
        foreach (var (c, r, ch) in cells) text[r][c] = ch;
        return Board.Parse(text.Select(t => new string(t)).ToArray());
    }

    /// <summary>A real story game in play (creatures about), with plenty of lives.</summary>
    private static GameEngine Real(int stage = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(stage);
        g.SetLives(99);
        return g;
    }

    private static Controls Press(Dir d, bool bomb = false, bool detonate = false)
    {
        var (dx, dy) = Dirs.Vec(d);
        return new Controls(dx, dy, bomb, detonate);
    }

    private static readonly Controls Bomb = new(Bomb: true);
    private static readonly Controls Detonate = new(Detonate: true);

    private static int Ticks(double seconds) => (int)Math.Ceiling(seconds * 60);

    // ───────────────────────────── the fields ─────────────────────────────

    [Fact]
    public void Every_story_field_has_its_pillars_blocks_door_and_power_up_and_the_start_is_clear()
    {
        for (int stage = 1; stage <= 50; stage += 7)
        {
            var b = Board.Story(new Random(stage), GameEngine.StagePower(stage));
            Assert.Equal((Field.StoryCols, Field.StoryRows), (b.Cols, b.Rows));
            foreach (var (c, r) in b.Cells())
            {
                bool edge = c == 0 || r == 0 || c == b.Cols - 1 || r == b.Rows - 1;
                if (edge || (c % 2 == 0 && r % 2 == 0)) Assert.Equal(Tile.Pillar, b.T[c, r]);
                else Assert.NotEqual(Tile.Pillar, b.T[c, r]);
            }
            Assert.True(b.Floor(1, 1) && b.Floor(2, 1) && b.Floor(1, 2), "the start is clear");
            int open = b.Cells().Count(p => b.T[p.C, p.R] != Tile.Pillar);
            Assert.InRange(b.Count(Tile.Soft), open / 4, open / 2);
            Assert.True(b.Exit is var (ec, er) && b.T[ec, er] == Tile.Soft && ec >= 8, "the door is hidden well away");
            var items = b.Cells().Where(p => b.Item[p.C, p.R] != PowerUp.None).ToList();
            Assert.Single(items);
            Assert.Equal(Tile.Soft, b.T[items[0].C, items[0].R]);
            Assert.Equal(GameEngine.StagePower(stage), b.Item[items[0].C, items[0].R]);
            Assert.NotEqual(b.Exit, items[0]);
            // Every open cell can be got to, by breaking blocks.
            var d = b.Distances(1, 1, (c, r) => !b.Hard(c, r));
            Assert.All(b.Cells().Where(p => !b.Hard(p.C, p.R)), p => Assert.True(d[p.C, p.R] >= 0));
        }
    }

    [Fact]
    public void A_stage_is_laid_out_the_same_every_time_it_is_played()
    {
        var a = Real(4, seed: 3);
        var b = Real(4, seed: 3);
        Assert.Equal(a.Board.T.Cast<Tile>(), b.Board.T.Cast<Tile>());
        Assert.Equal(a.Board.Exit, b.Board.Exit);
        Assert.Equal(a.Enemies.Select(e => (e.Kind, e.Cx, e.Cy)), b.Enemies.Select(e => (e.Kind, e.Cx, e.Cy)));
    }

    [Fact]
    public void The_stages_get_harder_and_each_hides_its_own_power_up()
    {
        Assert.All(GameEngine.StageEnemies(1), k => Assert.Equal(EnemyKind.Puffle, k));
        Assert.Equal(6, GameEngine.StageEnemies(1).Count);
        Assert.Equal(10, GameEngine.StageEnemies(50).Count);
        Assert.Contains(EnemyKind.Dart, GameEngine.StageEnemies(40));
        for (int s = 2; s <= 50; s++)
            Assert.True(GameEngine.StageEnemies(s).Average(k => (int)k) >= GameEngine.StageEnemies(s - 1).Average(k => (int)k) - 0.1, $"stage {s}");
        Assert.True(GameEngine.StageEnemies(30).Average(k => (int)k) > GameEngine.StageEnemies(5).Average(k => (int)k) + 3);
        Assert.Equal(PowerUp.Fire, GameEngine.StagePower(1));
        Assert.Equal(PowerUp.Bombs, GameEngine.StagePower(2));
        Assert.Equal(PowerUp.Remote, GameEngine.StagePower(3));
        var all = Enumerable.Range(1, 16).Select(GameEngine.StagePower).ToHashSet();
        Assert.Equal(Enum.GetValues<PowerUp>().Where(p => p != PowerUp.None).ToHashSet(), all);
        // The creatures start well away from the bomber.
        var g = Real(9);
        Assert.All(g.Enemies, e => Assert.True(e.Cx + e.Cy >= 7));
    }

    [Fact]
    public void The_battle_arena_keeps_each_corner_clear_and_hides_power_ups_in_its_blocks()
    {
        for (int seed = 1; seed <= 10; seed++)
        {
            var b = Board.Battle(new Random(seed));
            Assert.Equal((Field.BattleCols, Field.BattleRows), (b.Cols, b.Rows));
            foreach (var (c, r) in Board.BattleStarts)
            {
                Assert.True(b.Floor(c, r));
                Assert.Equal(2, Dirs.All.Count(d => { var (dx, dy) = Dirs.Vec(d); return b.Floor(c + dx, r + dy); }));
            }
            Assert.True(b.Count(Tile.Soft) > 60);
            var hidden = b.Cells().Where(p => b.Item[p.C, p.R] != PowerUp.None).ToList();
            Assert.True(hidden.Count >= 15);
            Assert.All(hidden, p => Assert.Equal(Tile.Soft, b.T[p.C, p.R]));
            Assert.Null(b.Exit);
        }
    }

    [Fact]
    public void The_hurry_up_spirals_in_from_the_corner_and_leaves_the_middle_open()
    {
        var b = Board.Battle(new Random(1));
        var spiral = b.Spiral();
        Assert.Equal((1, 1), spiral[0]);
        Assert.Equal((2, 1), spiral[1]);
        Assert.All(spiral, p => Assert.False(Board.IsPillar(p.C, p.R)));
        Assert.Equal(spiral.Count, spiral.Distinct().Count());
        // The outer ring comes first, all the way round.
        int ring = 2 * (Field.BattleCols - 2) + 2 * (Field.BattleRows - 2) - 4;
        Assert.All(spiral.Take(ring), p => Assert.True(p.C == 1 || p.R == 1 || p.C == Field.BattleCols - 2 || p.R == Field.BattleRows - 2));
        Assert.DoesNotContain((Field.BattleCols / 2, Field.BattleRows / 2), spiral);
    }

    // ───────────────────────────── walking ─────────────────────────────

    [Fact]
    public void The_bomber_walks_along_and_a_pillar_stops_it()
    {
        var g = Story();
        var p = g.Player;
        g.PutBomber(p, 1, 1);
        g.Run(30, Press(Dir.Right));
        Assert.InRange(p.X, 1 + 0.4 * Bomber.BaseSpeed, 1 + 0.55 * Bomber.BaseSpeed);
        Assert.Equal(1.0, p.Y);
        Assert.Equal(Dir.Right, p.Facing);
        // Down from row 1 at an even column: a pillar.
        g.PutBomber(p, 2, 1);
        g.Run(30, Press(Dir.Down));
        Assert.Equal((2.0, 1.0), (p.X, p.Y));
        Assert.Equal(Dir.Down, p.Facing);
        // Into the wall.
        g.PutBomber(p, 1, 1);
        g.Run(30, Press(Dir.Up));
        Assert.Equal((1.0, 1.0), (p.X, p.Y));
    }

    [Fact]
    public void Pushing_into_a_pillar_corner_slides_the_bomber_round_it()
    {
        var g = Story();
        var p = g.Player;
        // Just past the column on the right of the pillar below: it slides across to the column and on down.
        g.PutBomber(p, 2.65, 1);
        g.Run(20, Press(Dir.Down));
        Assert.Equal(3.0, p.X);
        Assert.True(p.Y > 1.5, $"y {p.Y}");
        // Short of the column on the left: it slides back on to its own column and on down.
        g.PutBomber(p, 1.3, 1);
        g.Run(20, Press(Dir.Down));
        Assert.Equal(1.0, p.X);
        Assert.True(p.Y > 1.5, $"y {p.Y}");
        // Right in front of the pillar, there's no way round.
        g.PutBomber(p, 2.05, 1);
        g.Run(20, Press(Dir.Down));
        Assert.Equal(1.0, p.Y);
    }

    [Fact]
    public void Holding_a_diagonal_turns_the_corner_at_the_next_opening()
    {
        var g = Story();
        var p = g.Player;
        g.PutBomber(p, 2, 1, Dir.Right);
        g.Run(40, new Controls(1, 1));
        Assert.Equal(3.0, p.X);
        Assert.True(p.Y > 1.8, $"y {p.Y}");
    }

    [Fact]
    public void The_keys_move_the_bomber_the_way_they_say()
    {
        var g = Story();
        var p = g.Player;
        using var input = new InputHandler();
        g.PutBomber(p, 1, 3);
        input.OnKeyDown(Key.Up, PhysicalKey.ArrowUp);
        for (int i = 0; i < 20; i++) g.Tick(input.Players(1));
        Assert.True(p.Y < 3, $"up went to {p.Y}");
        input.OnKeyUp(Key.Up, PhysicalKey.ArrowUp);
        g.PutBomber(p, 1, 3);
        input.OnKeyDown(Key.Down, PhysicalKey.ArrowDown);
        for (int i = 0; i < 20; i++) g.Tick(input.Players(1));
        Assert.True(p.Y > 3, $"down went to {p.Y}");
        input.OnKeyUp(Key.Down, PhysicalKey.ArrowDown);
        // On their own, W A S D work too, and Space lays a bomb.
        g.PutBomber(p, 3, 1);
        input.OnKeyDown(Key.A, PhysicalKey.A);
        for (int i = 0; i < 20; i++) g.Tick(input.Players(1));
        Assert.True(p.X < 3);
        input.OnKeyUp(Key.A, PhysicalKey.A);
        input.OnKeyDown(Key.Space, PhysicalKey.Space);
        g.Tick(input.Players(1));
        Assert.Single(g.Bombs);
    }

    [Fact]
    public void Two_people_split_the_keyboard()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Left, PhysicalKey.ArrowLeft);
        input.OnKeyDown(Key.S, PhysicalKey.S);
        input.OnKeyDown(Key.Q, PhysicalKey.Q);
        input.OnKeyDown(Key.RightCtrl, PhysicalKey.ControlRight);
        var both = input.Players(2);
        Assert.Equal(new Controls(-1, 0, false, true), both[0]);
        Assert.Equal(new Controls(0, 1, true, false), both[1]);
        input.Clear();
        input.OnKeyDown(Key.Space, PhysicalKey.Space);
        input.OnKeyDown(Key.E, PhysicalKey.E);
        input.OnKeyDown(Key.D, PhysicalKey.D);
        input.OnKeyDown(Key.Enter, PhysicalKey.Enter);
        both = input.Players(2);
        Assert.Equal(new Controls(0, 0, true, true), both[0]);
        Assert.Equal(new Controls(1, 0, false, true), both[1]);
        Assert.Equal(4, input.Players(4).Length);
        Assert.Equal(default, input.Players(4)[2]);
    }

    [Fact]
    public void A_key_let_go_under_another_name_is_still_let_go()
    {
        // Here a Shift's release arrives as CapsLock: what matters is the physical key.
        using var input = new InputHandler();
        input.OnKeyDown(Key.LeftShift, PhysicalKey.ShiftLeft);
        input.OnKeyUp(Key.CapsLock, PhysicalKey.ShiftLeft);
        Assert.False(input.Has(Key.LeftShift));
        input.OnKeyDown(Key.W, PhysicalKey.W);
        Assert.Equal(-1, input.Players(2)[1].Dy);
        input.OnKeyUp(Key.Z, PhysicalKey.W);
        Assert.Equal(0, input.Players(2)[1].Dy);
    }

    [Fact]
    public void The_battle_sends_each_persons_keys_to_their_own_bomber()
    {
        var g = new GameEngine(3, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.GoBattle(Slot.Human, Slot.Human);
        g.SetBoard(Arena(Field.BattleCols, Field.BattleRows));
        var (p1, p2) = (g.Bombers[0], g.Bombers[1]);
        g.PutBomber(p1, 1, 1);
        g.PutBomber(p2, 13, 11);
        using var input = new InputHandler();
        input.OnKeyDown(Key.Right, PhysicalKey.ArrowRight);
        input.OnKeyDown(Key.W, PhysicalKey.W);
        for (int i = 0; i < 20; i++) g.Tick(input.Players(g.Humans));
        Assert.True(p1.X > 1 && p1.Y == 1);
        Assert.True(p2.Y < 11 && p2.X == 13);
        input.OnKeyDown(Key.Q, PhysicalKey.Q);
        g.Tick(input.Players(g.Humans));
        Assert.Single(g.Bombs);
        Assert.Same(p2, g.Bombs[0].Owner);
    }

    // ───────────────────────────── bombs and flames ─────────────────────────────

    [Fact]
    public void A_bomb_can_be_walked_off_but_not_back_on_to()
    {
        var g = Story();
        var p = g.Player;
        g.PutBomber(p, 3, 1);
        g.Run(1, Bomb);
        Assert.Single(g.Bombs);
        Assert.Equal((3, 1), (g.Bombs[0].C, g.Bombs[0].R));
        g.Run(25, Press(Dir.Right));
        Assert.True(p.X >= 4);
        g.Run(40, Press(Dir.Left));
        Assert.Equal(4.0, p.X);
    }

    [Fact]
    public void One_bomb_at_a_time_at_first_and_more_with_the_bomb_power_up()
    {
        var g = Story();
        var p = g.Player;
        g.PutBomber(p, 1, 1);
        g.Run(1, Bomb);
        g.Run(20, Press(Dir.Right));
        g.Run(1, Bomb);
        Assert.Single(g.Bombs);
        GameEngine.Give(p, PowerUp.Bombs);
        g.Idle(1);
        g.Run(1, Bomb);
        Assert.Equal(2, g.Bombs.Count);
        // Only one to a cell.
        g.Idle(1);
        g.Run(1, Bomb);
        Assert.Equal(2, g.Bombs.Count);
    }

    [Fact]
    public void A_bomb_bursts_after_its_fuse_in_a_cross_of_flame_that_pillars_stop()
    {
        var g = Story();
        var p = g.Player;
        g.PutBomber(p, 3, 1);
        g.Run(1, Bomb);
        g.PutBomber(p, 9, 9);
        g.Idle(Ticks(Engine.Bomb.FuseSeconds) - 5);
        Assert.Single(g.Bombs);
        g.Idle(6);
        Assert.Empty(g.Bombs);
        Assert.True(g.Flames[3, 1].Burning && g.Flames[2, 1].Burning && g.Flames[4, 1].Burning && g.Flames[3, 2].Burning);
        Assert.False(g.Flames[5, 1].Burning);
        Assert.False(g.Flames[3, 0].Burning);
        g.Idle(Ticks(GameEngine.FlameSeconds) + 1);
        Assert.False(g.Flames[3, 1].Burning);

        // Bigger with fire, and a pillar stops it dead: none past it.
        GameEngine.Give(p, PowerUp.Fire);
        GameEngine.Give(p, PowerUp.Fire);
        g.PutBomber(p, 3, 3);
        g.Run(1, Bomb);
        g.PutBomber(p, 9, 9);
        g.Idle(Ticks(Engine.Bomb.FuseSeconds) + 1);
        Assert.True(g.Flames[6, 3].Burning && g.Flames[0 + 1, 3].Burning && g.Flames[3, 6].Burning);
        Assert.False(g.Flames[7, 3].Burning);
        Assert.False(g.Flames[4, 4].Burning);
        Assert.False(g.Flames[2, 2].Burning);
    }

    [Fact]
    public void Flames_stop_at_the_first_block_which_burns_away_and_shows_what_was_under_it()
    {
        var g = Story(15, 11, (5, 1, '+'), (6, 1, 'b'), (3, 3, 'f'));
        var p = g.Player;
        p.Range = 4;
        g.PutBomber(p, 3, 1);
        g.Run(1, Bomb);
        g.PutBomber(p, 11, 9);
        g.Idle(Ticks(Engine.Bomb.FuseSeconds) + 1);
        Assert.True(g.Flames[4, 1].Burning);
        Assert.False(g.Flames[5, 1].Burning);
        Assert.True(g.Crumble[5, 1] > 0);
        Assert.Equal(Tile.Soft, g.Board.T[5, 1]);
        Assert.Equal(Tile.Soft, g.Board.T[6, 1]);
        Assert.True(g.Crumble[3, 3] > 0);
        Assert.Equal(PowerUp.None, g.Board.Visible(3, 3));
        g.Idle(Ticks(GameEngine.CrumbleSeconds) + 1);
        Assert.Equal(Tile.Floor, g.Board.T[5, 1]);
        Assert.Equal(Tile.Soft, g.Board.T[6, 1]);
        Assert.Equal(PowerUp.Fire, g.Board.Visible(3, 3));
    }

    [Fact]
    public void A_bomb_caught_in_a_blast_goes_off_with_it()
    {
        var g = Story();
        var a = g.PutBomb(3, 1, 2);
        var b = g.PutBomb(5, 1, 2, fuse: 2.0);
        var c = g.PutBomb(5, 3, 1, fuse: 2.0);
        a.Fuse = 0.05;
        g.PutBomber(g.Player, 11, 9);
        g.Idle(5);
        Assert.Empty(g.Bombs);
        Assert.True(g.Flames[6, 1].Burning, "the second bomb's flames");
        Assert.True(g.Flames[5, 4].Burning, "the third, set off by the second");
        Assert.Equal(g.Flames[3, 1].Group, g.Flames[5, 4].Group);
        _ = (b, c);
    }

    [Fact]
    public void The_flames_catch_the_bomber_unless_it_is_flame_proof_or_invincible()
    {
        var g = Story();
        g.SetLives(3);
        var p = g.Player;
        g.PutBomber(p, 3, 1);
        g.Run(1, Bomb);
        g.Idle(Ticks(Engine.Bomb.FuseSeconds) + 1);
        Assert.False(p.Alive);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal(2, g.Lives);

        foreach (var power in new[] { PowerUp.FlamePass, PowerUp.Mystery })
        {
            var h = Story();
            GameEngine.Give(h.Player, power);
            h.PutBomber(h.Player, 3, 1);
            h.Run(1, Bomb);
            h.Idle(Ticks(Engine.Bomb.FuseSeconds) + 5);
            Assert.True(h.Player.Alive, power.ToString());
        }
    }

    [Fact]
    public void Creatures_caught_in_one_blast_are_worth_double_each_time()
    {
        var g = Story();
        g.Player.Range = 3;
        g.PutBomber(g.Player, 9, 9);
        g.PutEnemy(EnemyKind.Puffle, 4, 1);
        g.PutEnemy(EnemyKind.Puffle, 6, 1);
        g.PutEnemy(EnemyKind.Puffle, 3, 2);
        foreach (var e in g.Enemies) e.Spawning = 10; // held still
        g.Idle(1);
        foreach (var e in g.Enemies) e.Spawning = 0;
        var bomb = g.PutBomb(3, 1, 3);
        bomb.Fuse = 0;
        g.Idle(1);
        Assert.All(g.Enemies, e => Assert.True(e.Dead));
        Assert.Equal(100 + 200 + 400, g.Score);
        Assert.Equal(100, GameEngine.ChainPoints(100, 0));
        Assert.Equal(800, GameEngine.ChainPoints(100, 3));
        Assert.Equal(16000, GameEngine.ChainPoints(4000, 2));
    }

    [Fact]
    public void A_chain_reaction_counts_as_one_blast_for_the_bonus()
    {
        var g = Story();
        g.PutBomber(g.Player, 11, 9);
        g.PutEnemy(EnemyKind.Sprout, 1, 2);
        g.PutEnemy(EnemyKind.Sprout, 7, 2);
        foreach (var e in g.Enemies) e.Spawning = 0;
        var a = g.PutBomb(1, 1, 3);
        g.PutBomb(4, 1, 3, fuse: 5);
        g.PutBomb(7, 1, 3, fuse: 5);
        a.Fuse = 0;
        g.Idle(1);
        Assert.All(g.Enemies, e => Assert.True(e.Dead));
        Assert.Equal(200 + 400, g.Score);
    }

    [Fact]
    public void A_remote_bomb_waits_for_the_detonator_oldest_first()
    {
        var g = Story();
        var p = g.Player;
        GameEngine.Give(p, PowerUp.Remote);
        GameEngine.Give(p, PowerUp.Bombs);
        g.PutBomber(p, 3, 1);
        g.Run(1, Bomb);
        g.Run(30, Press(Dir.Right));
        g.Idle(1);
        g.Run(1, Bomb);
        Assert.Equal(2, g.Bombs.Count);
        g.PutBomber(p, 9, 9);
        g.Idle(Ticks(Engine.Bomb.FuseSeconds * 3));
        Assert.Equal(2, g.Bombs.Count);
        g.Run(1, Detonate);
        Assert.Single(g.Bombs);
        Assert.True(g.Flames[3, 1].Burning);
        Assert.NotEqual(3, g.Bombs[0].C);
        g.Idle(1);
        g.Run(1, Detonate);
        Assert.Empty(g.Bombs);
    }

    [Fact]
    public void Bomb_pass_walks_over_bombs_and_wall_pass_through_blocks()
    {
        var g = Story(15, 11, (5, 1, '+'));
        var p = g.Player;
        g.PutBomb(3, 1, fuse: 99);
        g.PutBomber(p, 2, 1);
        g.Run(30, Press(Dir.Right));
        Assert.Equal(2.0, p.X);
        p.BombPass = true;
        g.Run(30, Press(Dir.Right));
        Assert.True(p.X > 3.5 && p.X <= 4.0, $"x {p.X}");
        g.Run(30, Press(Dir.Right));
        Assert.Equal(4.0, p.X);
        p.WallPass = true;
        g.Run(40, Press(Dir.Right));
        Assert.True(p.X > 5.5, $"x {p.X}");
    }

    [Fact]
    public void A_bomber_inside_a_block_burns_with_it()
    {
        var g = Story(15, 11, (5, 1, '+'));
        var p = g.Player;
        p.WallPass = true;
        g.PutBomber(p, 5, 1);
        var bomb = g.PutBomb(3, 1, 3);
        bomb.Fuse = 0;
        g.Idle(1);
        Assert.False(p.Alive);
    }

    [Fact]
    public void Power_ups_are_picked_up_by_walking_on_to_them()
    {
        var g = Story(15, 11, (2, 1, 'B'), (3, 1, 'F'), (4, 1, 'S'), (5, 1, 'R'), (6, 1, 'W'), (7, 1, 'P'), (8, 1, 'L'), (9, 1, 'M'));
        var p = g.Player;
        g.PutBomber(p, 1, 1);
        g.Run(Ticks(4), Press(Dir.Right));
        Assert.Equal((2, 2, 1), (p.Bombs, p.Range, p.SpeedUps));
        Assert.True(p.Remote && p.WallPass && p.BombPass && p.FlamePass && p.Invincible > 0);
        Assert.Equal(8 * GameEngine.PowerUpPoints, g.Score);
        Assert.DoesNotContain(g.Board.Cells(), q => g.Board.Item[q.C, q.R] != PowerUp.None);
        Assert.True(p.Speed > Bomber.BaseSpeed);
    }

    [Fact]
    public void A_power_up_burnt_by_the_flames_is_gone_and_lets_creatures_out()
    {
        var g = Story(15, 11, (5, 1, 'B'));
        g.PutBomber(g.Player, 11, 9);
        var bomb = g.PutBomb(3, 1, 3);
        bomb.Fuse = 0;
        g.Idle(1);
        Assert.Equal(PowerUp.None, g.Board.Item[5, 1]);
        Assert.Equal(GameEngine.SpawnCount, g.Enemies.Count);
        Assert.All(g.Enemies, e => Assert.True(e.Spawning > 0 && e.Cx == 5 && e.Cy == 1));
        Assert.All(g.Enemies, e => Assert.Equal(g.SpawnKind, e.Kind));
        // They come to no harm from the flames that let them out.
        g.Idle(Ticks(GameEngine.FlameSeconds) + 2);
        Assert.All(g.Enemies, e => Assert.False(e.Dead));
    }

    // ───────────────────────────── the door ─────────────────────────────

    [Fact]
    public void The_door_opens_once_every_creature_is_gone_and_takes_the_bomber_to_the_next_stage()
    {
        var g = Story(15, 11, (5, 1, 'e'));
        var p = g.Player;
        p.Range = 2;
        var e = g.PutEnemy(EnemyKind.Puffle, 9, 9);
        g.PutBomber(p, 3, 1);
        g.Run(1, Bomb);
        g.Run(40, Press(Dir.Left));
        g.Run(40, Press(Dir.Down));
        g.Idle(Ticks(Engine.Bomb.FuseSeconds + GameEngine.CrumbleSeconds) + 2);
        Assert.True(p.Alive);
        Assert.True(g.Board.ExitShowing);
        Assert.False(g.ExitOpen);
        e.Dead = true;
        g.Idle(2);
        Assert.True(g.ExitOpen);
        g.PutBomber(p, 4, 1);
        g.Run(30, Press(Dir.Right));
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle(Ticks(GameEngine.ClearSeconds) + 2);
        Assert.Equal(2, g.Stage);
        Assert.Equal(Phase.Start, g.Phase);
    }

    [Fact]
    public void Blasting_the_door_lets_out_a_crowd_of_tougher_creatures()
    {
        var g = Story(15, 11, (5, 1, 'E'));
        g.PutBomber(g.Player, 11, 9);
        Assert.True(g.Board.ExitShowing);
        g.Idle(2);
        Assert.True(g.ExitOpen);
        var bomb = g.PutBomb(3, 1, 3);
        bomb.Fuse = 0;
        g.Idle(1);
        Assert.Equal(GameEngine.SpawnCount, g.Enemies.Count);
        Assert.False(g.ExitOpen);
        Assert.True(g.Board.ExitShowing, "the door survives");
        Assert.True(g.SpawnKind > GameEngine.TopKind(g.Stage));
        // The same blast lets them out only once.
        g.Idle(5);
        Assert.Equal(GameEngine.SpawnCount, g.Enemies.Count);
        g.Idle(Ticks(Enemy.SpawnSeconds) + 2);
        Assert.Contains(g.Enemies, e => e.Cx != 5 || e.Cy != 1 || !e.AtCentre);
    }

    // ───────────────────────────── the clock ─────────────────────────────

    [Fact]
    public void When_the_clock_runs_out_the_whirlwinds_flood_in()
    {
        var g = Real();
        g.PutBomber(g.Player, 1, 1);
        int before = g.Enemies.Count;
        g.SetTimeLeft(0.05);
        g.Idle(5);
        Assert.True(g.TimeUp);
        Assert.Equal(before + GameEngine.FloodCount, g.Enemies.Count);
        Assert.Equal(GameEngine.FloodCount, g.Enemies.Count(e => e.Kind == EnemyKind.Whirl));
        Assert.Equal(GameEngine.StageSeconds, Real().TimeLeft);
    }

    // ───────────────────────────── the creatures ─────────────────────────────

    [Fact]
    public void Creatures_that_cannot_pass_blocks_keep_to_the_open_and_the_ghostly_ones_drift_through()
    {
        var cells = new List<(int, int, char)>();
        for (int c = 1; c <= 13; c++)
            for (int r = 1; r <= 9; r++)
                if (!Board.IsPillar(c, r) && (c + r) % 3 == 0) cells.Add((c, r, '+'));
        var g = Story(15, 11, cells.ToArray());
        g.PutBomber(g.Player, 1, 1);
        g.Player.Invincible = 999;
        var puffle = g.PutEnemy(EnemyKind.Puffle, 5, 5);
        var wisp = g.PutEnemy(EnemyKind.Wisp, 9, 5);
        g.Board.T[5, 5] = g.Board.T[9, 5] = Tile.Floor;
        bool wispInBlock = false;
        for (int t = 0; t < Ticks(40); t++)
        {
            g.Idle(1);
            Assert.NotEqual(Tile.Soft, g.Board.T[puffle.Cx, puffle.Cy]);
            Assert.NotEqual(Tile.Soft, g.Board.T[puffle.Tx, puffle.Ty]);
            if (g.Board.T[wisp.Cx, wisp.Cy] == Tile.Soft) wispInBlock = true;
        }
        Assert.True(wispInBlock);
    }

    [Fact]
    public void A_keen_creature_comes_after_the_bomber_and_a_drifter_does_not()
    {
        double Closest(EnemyKind kind)
        {
            var g = Story();
            g.PutBomber(g.Player, 1, 1);
            g.Player.Invincible = 999;
            var e = g.PutEnemy(kind, 7, 3);
            double near = 99;
            for (int t = 0; t < Ticks(10); t++)
            {
                g.Idle(1);
                near = Math.Min(near, Math.Abs(e.X - 1) + Math.Abs(e.Y - 1));
            }
            return near;
        }
        Assert.True(Closest(EnemyKind.Dart) < 1, "the dart catches up");
        Assert.True(EnemyInfo.Of(EnemyKind.Dart).Speed > 2 * EnemyInfo.Of(EnemyKind.Puffle).Speed);
        Assert.True(EnemyInfo.Of(EnemyKind.Whirl).WallPass && EnemyInfo.Of(EnemyKind.Wisp).WallPass && !EnemyInfo.Of(EnemyKind.Grinner).WallPass);
        Assert.True(EnemyInfo.All.All(i => i.Smart >= EnemyInfo.Of(EnemyKind.Puffle).Smart));
    }

    [Fact]
    public void A_creature_catches_the_bomber_by_touching_it_and_turns_back_from_a_bomb()
    {
        var g = Story();
        g.SetLives(3);
        g.PutBomber(g.Player, 5, 1);
        g.PutEnemy(EnemyKind.Puffle, 6, 1);
        g.Idle(Ticks(1));
        Assert.False(g.Player.Alive);
        Assert.Equal("CAUGHT", g.Cause);

        // Along a corridor with no way off it, a bomb in the way turns it round.
        var h = Story(15, 11, Enumerable.Range(0, 7).Select(i => (1 + 2 * i, 2, '+')).ToArray());
        h.PutBomber(h.Player, 1, 9);
        var f = h.PutEnemy(EnemyKind.Puffle, 10, 1, Dir.Left);
        h.Idle(1);
        Assert.Equal(Dir.Left, f.Dir);
        h.PutBomb(7, 1, fuse: 99);
        h.Idle(Ticks(2));
        Assert.True(f.X > 7.5, $"x {f.X}");
    }

    [Fact]
    public void Losing_a_life_plays_the_stage_again_without_the_special_powers()
    {
        var g = Real(3);
        var p = g.Player;
        GameEngine.Give(p, PowerUp.Bombs);
        GameEngine.Give(p, PowerUp.Fire);
        GameEngine.Give(p, PowerUp.Remote);
        GameEngine.Give(p, PowerUp.WallPass);
        int lives = g.Lives;
        g.PutEnemy(EnemyKind.Puffle, p.Cx, p.Cy);
        g.Idle(2);
        Assert.False(p.Alive);
        Assert.Equal(lives - 1, g.Lives);
        g.Idle(Ticks(GameEngine.DyingSeconds) + 2);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal(3, g.Stage);
        Assert.True(p.Alive);
        Assert.Equal((1.0, 1.0), (p.X, p.Y));
        Assert.Equal((2, 2), (p.Bombs, p.Range));
        Assert.False(p.Remote || p.WallPass);
        Assert.Equal(GameEngine.StageEnemies(3).Count, g.Enemies.Count);
    }

    [Fact]
    public void After_every_fifth_stage_a_bonus_stage_where_nothing_can_hurt_the_bomber()
    {
        var g = Story(15, 11, (5, 1, 'E'));
        g.PutBomber(g.Player, 4, 1);
        g.SetStage(5);
        g.Idle(2);
        g.Run(30, Press(Dir.Right));
        Assert.Equal(Phase.Clear, g.Phase);
        g.Calm = false;
        g.Idle(Ticks(GameEngine.ClearSeconds) + 2);
        Assert.True(g.IsBonus);
        Assert.Equal(5, g.Stage);
        Assert.Equal(0, g.Board.Count(Tile.Soft));
        Assert.Equal(GameEngine.BonusCrowd, g.Enemies.Count);
        g.Idle(Ticks(GameEngine.StartSeconds) + 2);
        // Walk into them: nothing happens to the bomber.
        int lives = g.Lives;
        for (int t = 0; t < Ticks(GameEngine.BonusSeconds) - 300; t++) g.Tick(g.Pilot.Decide(g, g.Player));
        Assert.True(g.Player.Alive);
        Assert.Equal(lives, g.Lives);
        Assert.True(g.Score > 0);
        g.Idle(Ticks(10));
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Equal(6, g.Stage);
        Assert.False(g.IsBonus);
    }

    // ───────────────────────────── the battle ─────────────────────────────

    private static GameEngine Battle(params Slot[] slots)
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.GoBattle(slots);
        return g;
    }

    [Fact]
    public void The_title_menu_sets_up_a_battle_of_people_and_computers()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        using var input = new InputHandler();
        void Key1(Key k, PhysicalKey pk)
        {
            input.OnKeyDown(k, pk);
            g.Tick(input.Players(InputHandler.Pads), input.Start);
            input.OnKeyUp(k, pk);
            g.Tick(input.Players(InputHandler.Pads), input.Start);
        }
        Key1(Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(1, g.MenuIndex);
        Key1(Key.Space, PhysicalKey.Space);
        Assert.Equal(GameState.Setup, g.State);
        // Up to P2's row and make it a person; P3 nobody; P4 stays a computer.
        for (int i = 0; i < 4; i++) Key1(Key.Up, PhysicalKey.ArrowUp);
        Assert.Equal(1, g.SetupRow);
        Key1(Key.Left, PhysicalKey.ArrowLeft);
        Assert.Equal(Slot.Human, g.Slots[1]);
        Key1(Key.Down, PhysicalKey.ArrowDown);
        Key1(Key.Right, PhysicalKey.ArrowRight);
        Key1(Key.Right, PhysicalKey.ArrowRight);
        Assert.Equal(Slot.Human, g.Slots[2]);
        Key1(Key.Right, PhysicalKey.ArrowRight);
        Assert.Equal(Slot.Computer, g.Slots[2]);
        Key1(Key.Right, PhysicalKey.ArrowRight);
        Assert.Equal(Slot.Off, g.Slots[2]);
        Key1(Key.Down, PhysicalKey.ArrowDown);
        Key1(Key.Down, PhysicalKey.ArrowDown);
        Key1(Key.Left, PhysicalKey.ArrowLeft);
        Assert.Equal(2, g.WinsNeeded);
        Key1(Key.Enter, PhysicalKey.Enter);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Mode.Battle, g.Mode);
        Assert.Equal([0, 1, 3], g.Bombers.Select(b => b.Index));
        Assert.Equal([true, true, false], g.Bombers.Select(b => b.Human));
        Assert.Equal(2, g.Humans);
        Assert.Equal(2, g.WinsNeededNow);
        Assert.All(g.Bombers, b => Assert.Equal(Board.BattleStarts[b.Index], (b.Cx, b.Cy)));
    }

    [Fact]
    public void A_battle_needs_two_bombers()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.SetSlots(Slot.Human, Slot.Off, Slot.Off, Slot.Off);
        Assert.False(g.CanFight);
        g.SetSlots(Slot.Off, Slot.Off, Slot.Computer, Slot.Computer);
        Assert.True(g.CanFight);
    }

    [Fact]
    public void The_last_bomber_standing_wins_the_round_and_a_trophy_and_enough_trophies_win_the_battle()
    {
        var g = Battle(Slot.Human, Slot.Human);
        g.SetWinsNeeded(2);
        // A fresh battle takes its trophies from the setup.
        g.StartBattle();
        var (p1, p2) = (g.Bombers[0], g.Bombers[1]);
        g.Idle(Ticks(GameEngine.RoundStartSeconds) + 2);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal(1, g.Round);
        for (int round = 1; round <= 2; round++)
        {
            g.PutBomber(p2, 3, 1);
            var bomb = g.PutBomb(4, 1, 2);
            bomb.Fuse = 0;
            g.Idle(1);
            Assert.False(p2.Alive);
            g.Idle(Ticks(GameEngine.RoundEndDelay) + 2);
            Assert.Equal(Phase.RoundOver, g.Phase);
            Assert.Same(p1, g.Winner);
            Assert.Equal(round, p1.Wins);
            g.Idle(Ticks(GameEngine.RoundOverSeconds) + 2);
            if (round == 1)
            {
                Assert.Equal(Phase.Start, g.Phase);
                Assert.Equal(2, g.Round);
                Assert.True(p2.Alive);
                g.Idle(Ticks(GameEngine.RoundStartSeconds) + 2);
            }
        }
        Assert.Equal(Phase.MatchOver, g.Phase);
        Assert.Same(p1, g.Champion);
        g.Idle(Ticks(GameEngine.MatchOverSeconds) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void Caught_by_the_same_blast_it_is_a_draw()
    {
        var g = Battle(Slot.Human, Slot.Human);
        var (p1, p2) = (g.Bombers[0], g.Bombers[1]);
        g.PutBomber(p1, 3, 1);
        g.PutBomber(p2, 5, 1);
        var bomb = g.PutBomb(4, 1, 1);
        bomb.Fuse = 0;
        g.Idle(Ticks(GameEngine.RoundEndDelay) + 3);
        Assert.Equal(Phase.RoundOver, g.Phase);
        Assert.Null(g.Winner);
        Assert.Equal((0, 0), (p1.Wins, p2.Wins));
    }

    [Fact]
    public void A_blast_finishing_off_the_winner_in_the_moment_after_makes_it_a_draw()
    {
        var g = Battle(Slot.Human, Slot.Human);
        var (p1, p2) = (g.Bombers[0], g.Bombers[1]);
        g.PutBomber(p1, 7, 1);
        g.PutBomber(p2, 3, 1);
        g.PutBomb(4, 1, 1).Fuse = 0;
        g.PutBomb(8, 1, 1).Fuse = 0.5;
        g.Idle(Ticks(GameEngine.RoundEndDelay) + 3);
        Assert.Equal(Phase.RoundOver, g.Phase);
        Assert.Null(g.Winner);
    }

    [Fact]
    public void Power_ups_in_battle_come_out_of_the_blocks()
    {
        var g = Battle(Slot.Human, Slot.Human);
        var b = g.Board;
        var cell = b.Cells().First(p => b.Item[p.C, p.R] != PowerUp.None && b.Floor(p.C - 1, p.R) && !b.Hard(p.C - 2, p.R));
        var kind = b.Item[cell.C, cell.R];
        g.PutBomber(g.Bombers[0], 13, 11);
        g.PutBomber(g.Bombers[1], 13, 11);
        g.Bombers[1].Invincible = g.Bombers[0].Invincible = 99;
        g.PutBomb(cell.C - 1, cell.R, 1).Fuse = 0;
        g.Idle(Ticks(GameEngine.CrumbleSeconds) + 2);
        Assert.Equal(kind, b.Visible(cell.C, cell.R));
        Assert.Empty(g.Enemies);
    }

    [Fact]
    public void When_time_runs_low_the_hurry_up_drops_blocks_round_the_arena_and_they_crush()
    {
        var g = Battle(Slot.Human, Slot.Human);
        var (p1, p2) = (g.Bombers[0], g.Bombers[1]);
        g.SetBoard(Arena(Field.BattleCols, Field.BattleRows));
        g.PutBomber(p1, 7, 5);
        g.PutBomber(p2, 3, 1);
        g.StartHurry();
        g.Idle(2);
        Assert.True(g.Hurry);
        Assert.Equal(Tile.Dropped, g.Board.T[1, 1]);
        g.Idle(Ticks(GameEngine.DropInterval * 2));
        Assert.Equal(Tile.Dropped, g.Board.T[2, 1]);
        Assert.Equal(Tile.Dropped, g.Board.T[3, 1]);
        Assert.False(p2.Alive);
        Assert.Equal("CRUSHED", g.Cause);
        Assert.True(p1.Alive);
        // Dropped blocks are as hard as pillars: no flames get past.
        Assert.True(g.Board.Hard(1, 1));
        Assert.False(g.CanWalk(p1, 2, 1));
    }

    [Fact]
    public void The_hurry_up_fills_the_arena_but_the_middle()
    {
        var g = Battle(Slot.Human, Slot.Human);
        g.Calm = true;
        var (p1, p2) = (g.Bombers[0], g.Bombers[1]);
        g.PutBomber(p1, 7, 6);
        g.PutBomber(p2, 7, 6);
        g.Calm = false;
        g.StartHurry();
        g.Idle(Ticks(GameEngine.HurrySeconds - 5));
        Assert.True(p1.Alive && p2.Alive);
        Assert.Equal(Phase.Play, g.Phase);
        int open = g.Board.Cells().Count(p => g.Board.T[p.C, p.R] != Tile.Pillar && g.Board.T[p.C, p.R] != Tile.Dropped);
        Assert.InRange(open, 1, 12);
        g.Idle(Ticks(8));
        Assert.Equal(Phase.RoundOver, g.Phase);
        Assert.Null(g.Winner);
    }

    // ───────────────────────────── the computer bombers ─────────────────────────────

    [Fact]
    public void A_computer_bomber_gets_out_of_the_way_of_a_bomb()
    {
        for (int seed = 1; seed <= 12; seed++)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
            g.GoBattle(Slot.Computer, Slot.Human);
            g.SetBoard(Arena(Field.BattleCols, Field.BattleRows));
            var cpu = g.Bombers[0];
            var rng = new Random(seed);
            // An open stretch, with a bomb dropped right by it with its fuse half gone.
            var cells = g.Board.Cells().Where(p => g.Board.Floor(p.C, p.R) && Dirs.All.Count(d => { var (dx, dy) = Dirs.Vec(d); return g.Board.Floor(p.C + dx, p.R + dy); }) >= 2).ToList();
            var (c, r) = cells[rng.Next(cells.Count)];
            g.PutBomber(cpu, c, r);
            g.Bombers[1].Alive = false;
            g.PutBomb(c, r, 3, fuse: 1.6).Inside.Add(cpu);
            g.Idle(Ticks(3));
            Assert.True(cpu.Alive, $"seed {seed}: caught at ({c},{r})");
        }
    }

    [Fact]
    public void A_computer_bomber_left_on_its_own_clears_blocks_and_takes_power_ups_without_blowing_itself_up()
    {
        for (int seed = 1; seed <= 4; seed++)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
            g.GoBattle(Slot.Computer, Slot.Human);
            var cpu = g.Bombers[0];
            g.Bombers[1].Alive = false;
            int blocks = g.Board.Count(Tile.Soft);
            g.Idle(Ticks(60));
            Assert.True(cpu.Alive, $"seed {seed}: blew itself up ({cpu.Pilot.Doing})");
            Assert.True(g.Board.Count(Tile.Soft) < blocks - 15, $"seed {seed}: only {blocks - g.Board.Count(Tile.Soft)} blocks");
            Assert.True(cpu.Bombs + cpu.Range + cpu.SpeedUps > 3, $"seed {seed}: no power-ups");
        }
    }

    [Fact]
    public void Computer_bombers_fight_it_out_and_the_round_has_a_result()
    {
        int wins = 0;
        for (int seed = 1; seed <= 3; seed++)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.GoBattle(Slot.Computer, Slot.Computer, Slot.Computer, Slot.Computer);
            for (int t = 0; t < Ticks(GameEngine.RoundSeconds + 5) && g.Phase != Phase.RoundOver; t++) g.Idle(1);
            Assert.Equal(Phase.RoundOver, g.Phase);
            if (g.Winner != null) wins++;
        }
        Assert.True(wins >= 2, $"only {wins} rounds won outright");
    }

    [Fact]
    public void A_computer_bomber_runs_from_the_hurry_up()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.GoBattle(Slot.Computer, Slot.Human);
        g.SetBoard(Arena(Field.BattleCols, Field.BattleRows));
        var cpu = g.Bombers[0];
        g.PutBomber(cpu, 1, 1);
        // The other bomber waits in the middle, where the blocks never drop, and nothing else can hurt it.
        g.PutBomber(g.Bombers[1], 7, 6);
        g.Bombers[1].Invincible = 999;
        g.Calm = false;
        g.StartHurry();
        g.Idle(Ticks(GameEngine.HurrySeconds - 6));
        Assert.True(cpu.Alive, $"crushed at ({cpu.X},{cpu.Y})");
    }

    [Fact]
    public void A_computer_bomber_with_the_detonator_sets_its_bomb_off_once_clear()
    {
        var g = new GameEngine(2, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.GoBattle(Slot.Computer, Slot.Human);
        var cpu = g.Bombers[0];
        GameEngine.Give(cpu, PowerUp.Remote);
        g.Bombers[1].Alive = false;
        int bursts = 0;
        for (int t = 0; t < Ticks(20); t++)
        {
            g.Idle(1);
            if (g.Flames[cpu.Cx, cpu.Cy].Burning) Assert.Fail("caught by its own bomb");
            if (g.Bombs.Count == 0 && g.Board.Cells().Any(p => g.Flames[p.C, p.R].Burning && g.Flames[p.C, p.R].Time > GameEngine.FlameSeconds - 0.02)) bursts++;
        }
        Assert.True(cpu.Alive);
        Assert.True(bursts >= 2, $"{bursts} bursts");
        Assert.All(g.Bombs, b => Assert.True(b.Remote));
    }

    // ───────────────────────────── the story, played ─────────────────────────────

    [Fact]
    public void The_computer_bomber_clears_the_first_stage()
    {
        var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < Ticks(420) && g.State == GameState.Playing && g.Stage < 2; t++) g.Tick(g.Pilot.Decide(g, g.Player));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Stage >= 2, $"stage {g.Stage}, {g.Enemies.Count(e => !e.Dead)} creatures left, doing {g.Pilot.Doing}");
    }

    [Fact]
    public void Running_out_of_lives_ends_the_game_and_a_high_score_takes_initials()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.SetScore(99999);
        g.SetLives(1);
        g.PutEnemy(EnemyKind.Puffle, 1, 1);
        g.Idle(Ticks(GameEngine.DyingSeconds + 5));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "BOM") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("BOM", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }

    [Fact]
    public void The_title_runs_a_story_demonstration_then_a_battle_between_the_computers()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle(Ticks(GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        Assert.Equal(Mode.Story, g.Mode);
        Assert.False(g.Player.Human);
        g.Idle(Ticks(25));
        Assert.True(g.Score > 0 || g.Bombs.Count > 0 || g.Board.Count(Tile.Soft) < 200);
        g.Idle(Ticks(GameEngine.AttractSeconds - 25) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle(Ticks(GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        g.Idle(Ticks(GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        Assert.Equal(Mode.Battle, g.Mode);
        Assert.Equal(4, g.Bombers.Count);
        Assert.All(g.Bombers, b => Assert.False(b.Human));
        for (int t = 0; t < Ticks(GameEngine.AttractRoundSeconds * 3) && g.State == GameState.Attract; t++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void Enter_on_the_title_starts_the_story_and_pause_holds_everything()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(new Controls(), start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(Mode.Story, g.Mode);
        Assert.Equal(1, g.Stage);
        g.Idle(Ticks(GameEngine.StartSeconds) + 2);
        double time = g.TimeLeft;
        g.Pause();
        g.Idle(120);
        Assert.Equal(GameState.Paused, g.State);
        Assert.Equal(time, g.TimeLeft);
        g.Resume();
        g.Idle(60);
        Assert.True(g.TimeLeft < time);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        static (int, double, double, int, int) Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilot.Decide(g, g.Player));
            return (g.Score, g.Player.X, g.Player.Y, g.Stage, g.Board.Count(Tile.Soft));
        }
        Assert.Equal(Play(12), Play(12));

        static string Fight(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.GoBattle(Slot.Computer, Slot.Computer, Slot.Computer);
            g.Idle(60 * 30);
            return string.Join(";", g.Bombers.Select(b => $"{b.Alive}{b.X:0.000},{b.Y:0.000}"));
        }
        Assert.Equal(Fight(4), Fight(4));
    }
}
