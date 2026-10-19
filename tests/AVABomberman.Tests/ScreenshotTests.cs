using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVABomberman.Engine;
using AVABomberman.Views;
using Controls = AVABomberman.Engine.Controls;

namespace AVABomberman.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set BOMBER_SHOTS to a folder to save
/// them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avabomberman-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("BOMBER_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVABomberman-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            if (g.Mode == Mode.Story && g.Player.Human) g.Tick(g.Pilot.Decide(g, g.Player));
            else g.Idle(1);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("BOM", 184250, 17);
        g.HighScores.Insert("AVA", 92800, 9);
        g.ReturnToTitle();
        g.Tick(new Controls(0, 1));
        g.Tick(new Controls());
        g.Tick(new Controls(Bomb: true));
        c.Advance(0.5);
        Assert.Equal(GameState.Setup, g.State);
        Shot(w, c, "battle-setup");
        g.ReturnToTitle();
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void A_story_stage_mid_blast()
    {
        var (w, c, g) = Open(3);
        g.StartGame();
        g.SetLives(9);
        g.Idle(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.PhaseTime > 9 && g.Player.Alive
            && g.Board.Cells().Count(p => g.Flames[p.C, p.R].Time is > 0.25 and < 0.42) >= 4
            && g.Board.Cells().Any(p => g.Crumble[p.C, p.R] > 0.2)));
        Shot(w, c, "story-blast");
    }

    [AvaloniaFact]
    public void A_chain_reaction()
    {
        var (w, c, g) = Open(5);
        g.Go(12);
        g.SetLives(9);
        g.Calm = true;
        Assert.True(PlayUntil(g, c, g => g.PhaseTime > 1.5 && g.Player.Alive));
        // A row of bombs across the open floor, each in reach of the next, and creatures about.
        var b = g.Board;
        g.Enemies.Clear();
        g.Bombs.Clear();
        void Clear(int x, int y)
        {
            if (b.Hard(x, y)) return;
            b.T[x, y] = Tile.Floor;
            b.Item[x, y] = PowerUp.None;
        }
        for (int x = 1; x <= 21; x++) Clear(x, 5);
        foreach (int x in new[] { 3, 9, 15 })
            for (int y = 1; y <= 9; y++) Clear(x, y);
        Clear(7, 3);
        Clear(7, 2);
        var bombs = new[] { 3, 6, 9, 12, 15, 18 }.Select(x => g.PutBomb(x, 5, 3, g.Player, fuse: 3)).ToList();
        g.PutEnemy(EnemyKind.Grinner, 5, 5, Dir.Right);
        g.PutEnemy(EnemyKind.Dumpling, 10, 5, Dir.Left);
        g.PutEnemy(EnemyKind.Sprout, 9, 7, Dir.Up);
        g.PutEnemy(EnemyKind.Grinner, 15, 3, Dir.Down);
        g.PutEnemy(EnemyKind.Wisp, 17, 5, Dir.Left);
        g.PutBomber(g.Player, 7, 3, Dir.Down);
        foreach (var e in g.Enemies) e.Spawning = 0.05;
        g.Idle(4);
        c.Advance(0.3);
        bombs[0].Fuse = 0.01;
        g.Idle(4);
        c.Advance(0.12);
        Assert.Empty(g.Bombs);
        Shot(w, c, "chain-reaction");
    }

    [AvaloniaFact]
    public void A_power_up_revealed()
    {
        var (w, c, g) = Open(8);
        g.Go(3);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.PhaseTime > 1.5 && g.Player.Alive));
        g.Calm = true;
        var b = g.Board;
        var item = b.Cells().First(p => b.Item[p.C, p.R] != PowerUp.None);
        // Clear a way to it and stand back while a bomb opens it up.
        var side = Dirs.All.Select(Dirs.Vec).First(v => !b.Hard(item.C + v.X, item.R + v.Y) && !b.Hard(item.C + 2 * v.X, item.R + 2 * v.Y));
        int bx = item.C + side.X, by = item.R + side.Y;
        b.T[bx, by] = Tile.Floor;
        b.T[bx + side.X, by + side.Y] = Tile.Floor;
        b.Item[bx, by] = b.Item[bx + side.X, by + side.Y] = PowerUp.None;
        g.Enemies.RemoveAll(e => Math.Abs(e.Cx - item.C) + Math.Abs(e.Cy - item.R) < 8);
        g.PutBomber(g.Player, bx + side.X, by + side.Y, Dirs.Of(-side.X, -side.Y));
        g.Player.FlamePass = true;
        g.PutBomb(bx, by, 1, g.Player, fuse: 0.01);
        g.Idle((int)(60 * GameEngine.CrumbleSeconds) + 20);
        g.Player.FlamePass = false;
        Assert.NotEqual(PowerUp.None, b.Visible(item.C, item.R));
        // Another lying about nearby, then in to pick the new one up.
        var spare = b.Cells().Where(p => b.Floor(p.C, p.R) && b.Item[p.C, p.R] == PowerUp.None && Math.Abs(p.C - item.C) + Math.Abs(p.R - item.R) is >= 3 and <= 5).First();
        b.Item[spare.C, spare.R] = PowerUp.Speed;
        var (dx, dy) = (-side.X, -side.Y);
        for (int t = 0; t < 40 && b.Item[item.C, item.R] != PowerUp.None; t++)
        {
            g.Tick(new Controls(dx, dy));
            c.Advance(1 / 60.0);
        }
        g.Idle(8);
        c.Advance(0.13);
        Shot(w, c, "power-up");
    }

    [AvaloniaFact]
    public void The_exit_door_open()
    {
        var (w, c, g) = Open(9);
        g.Go(6);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.PhaseTime > 1.5 && g.Player.Alive));
        g.Calm = true;
        var b = g.Board;
        var (ex, ey) = b.Exit!.Value;
        // Every creature gone and the door burnt clear, the bomber on its way in.
        g.Enemies.Clear();
        b.T[ex, ey] = Tile.Floor;
        int px = ex - 2;
        while (b.Hard(px, ey)) px++;
        for (int x = Math.Min(px, ex); x <= ex; x++) b.T[x, ey] = Tile.Floor;
        g.PutBomber(g.Player, px == ex ? ex - 1 : px, ey, Dir.Right);
        g.Player.WalkTime = 0.3;
        g.Idle(3);
        Assert.True(g.ExitOpen);
        g.Player.Walking = true;
        c.Advance(0.4);
        Shot(w, c, "exit-door");
    }

    [AvaloniaFact]
    public void A_four_way_battle_and_its_winner()
    {
        var (w, c, g) = Open(4);
        g.GoBattle(Slot.Computer, Slot.Computer, Slot.Computer, Slot.Computer);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.PhaseTime > 14 && g.Bombers.Count(b => b.Alive) == 4
            && g.Board.Cells().Count(p => g.Flames[p.C, p.R].Time is > 0.22 and < 0.42) >= 5 && g.Bombs.Count >= 2, 90));
        Shot(w, c, "battle");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.RoundOver && g.Winner != null && g.PhaseTime > 1.2, 200));
        Shot(w, c, "battle-winner");
    }

    [AvaloniaFact]
    public void The_hurry_up_closing_in()
    {
        var (w, c, g) = Open(6);
        g.GoBattle(Slot.Computer, Slot.Computer, Slot.Computer, Slot.Computer);
        Assert.True(PlayUntil(g, c, g => g.PhaseTime > 6, 20));
        foreach (var b in g.Bombers) b.Invincible = 0;
        g.StartHurry();
        Assert.True(PlayUntil(g, c, g => g.Hurry && g.Board.Count(Tile.Dropped) > 44 && g.Bombers.Count(b => b.Alive) >= 2
            && g.Board.Cells().Any(p => g.Board.T[p.C, p.R] == Tile.Dropped && g.Dropped[p.C, p.R] < 0.1), 40));
        Shot(w, c, "hurry-up");
    }
}
