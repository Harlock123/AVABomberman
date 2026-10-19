namespace AVABomberman.Engine;

/// <summary>One player's controls: the stick (four ways, down the screen is +1), the bomb button and the detonator.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Bomb = false, bool Detonate = false)
{
    /// <summary>Two sets of controls at once (for the menus, which anyone can work).</summary>
    public static Controls operator |(Controls a, Controls b) =>
        new(a.Dx != 0 ? a.Dx : b.Dx, a.Dy != 0 ? a.Dy : b.Dy, a.Bomb || b.Bomb, a.Detonate || b.Detonate);
}

public enum Dir { None, Up, Down, Left, Right }

public static class Dirs
{
    public static readonly Dir[] All = [Dir.Up, Dir.Left, Dir.Down, Dir.Right];

    public static (int X, int Y) Vec(Dir d) => d switch { Dir.Up => (0, -1), Dir.Down => (0, 1), Dir.Left => (-1, 0), Dir.Right => (1, 0), _ => (0, 0) };

    public static Dir Opposite(Dir d) => d switch { Dir.Up => Dir.Down, Dir.Down => Dir.Up, Dir.Left => Dir.Right, Dir.Right => Dir.Left, _ => Dir.None };

    public static bool Horizontal(Dir d) => d is Dir.Left or Dir.Right;

    public static Dir Of(int dx, int dy) => dx > 0 ? Dir.Right : dx < 0 ? Dir.Left : dy > 0 ? Dir.Down : dy < 0 ? Dir.Up : Dir.None;

    /// <summary>The bit a flame arm going this way sets in a flame cell's mask.</summary>
    public static int Bit(Dir d) => d switch { Dir.Up => 1, Dir.Down => 2, Dir.Left => 4, Dir.Right => 8, _ => 16 };
}

/// <summary>
/// The screen and the fields on it. Positions are in cells: a cell's centre is at whole numbers, (0, 0) the wall
/// in the top left corner. The story field is 31 cells by 13 and scrolls sideways; the battle arena, 15 by 13, fits
/// the screen. Both have a wall all round and a pillar at every place where the row and column are both even.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double Cell = 50;
    /// <summary>Where the field starts down the screen (the bar with the time and the score is above it).</summary>
    public const double Top = 104;
    public const int StoryCols = 31, StoryRows = 13, BattleCols = 15, BattleRows = 13;

    /// <summary>The cell a position is in (halves round up, the same both ways).</summary>
    public static int CellOf(double v) => (int)Math.Floor(v + 0.5);
}

/// <summary>A bomber: a player, or the computer playing one.</summary>
public sealed class Bomber
{
    public const double BaseSpeed = 3.4, SpeedStep = 0.55;
    public const int MaxSpeedUps = 4, MaxBombs = 8, MaxRange = 8;

    public int Index;
    public double X, Y;
    public Dir Facing = Dir.Down;
    public bool Walking;
    public double WalkTime;
    public bool Alive = true;
    /// <summary>How long since it was caught (for the animation).</summary>
    public double DeadTime;
    public bool Human;
    public BomberPilot Pilot = new();

    public int Bombs = 1, Range = 1, SpeedUps;
    public bool Remote, WallPass, BombPass, FlamePass;
    /// <summary>Seconds of the mystery power-up's invincibility left (or a bonus stage's).</summary>
    public double Invincible;

    public int Wins;
    /// <summary>The buttons as they were last step, so a bomb goes down once per press.</summary>
    public bool BombPrev, DetonatePrev;
    /// <summary>A computer bomber's controls this step (and why), for drawing and the probe.</summary>
    public Controls Last;

    public int Cx => Field.CellOf(X);
    public int Cy => Field.CellOf(Y);
    public double Speed => BaseSpeed + SpeedStep * Math.Min(SpeedUps, MaxSpeedUps);

    public void Place(int c, int r)
    {
        (X, Y) = (c, r);
        Walking = false;
        Facing = Dir.Down;
    }

    /// <summary>Whether any part of it is in the cell (it is a cell wide).</summary>
    public bool Overlaps(int c, int r) => Math.Abs(X - c) < 0.99 && Math.Abs(Y - r) < 0.99;

    /// <summary>Back to how a bomber starts out.</summary>
    public void ResetPowers()
    {
        Bombs = Range = 1;
        SpeedUps = 0;
        Remote = WallPass = BombPass = FlamePass = false;
        Invincible = 0;
    }
}

/// <summary>A bomb, ticking (or waiting for its owner's detonator).</summary>
public sealed class Bomb
{
    public const double FuseSeconds = 2.5;

    public int C, R;
    public Bomber? Owner;
    public int Range = 1;
    public double Fuse = FuseSeconds;
    public bool Remote;
    public double Age;
    /// <summary>Bombers who were standing on it when it went down: they may walk off it (but not back on).</summary>
    public readonly HashSet<Bomber> Inside = [];
}

/// <summary>What a flame cell is: how long it burns on, which way its arms go (Dirs.Bit), and which blast it is.</summary>
public struct Flame
{
    public double Time;
    public int Mask;
    public int Group;
    public bool Burning => Time > 0;
}

public enum Tile : byte
{
    Floor,
    /// <summary>The pillars, and the wall round the field.</summary>
    Pillar,
    /// <summary>A breakable block.</summary>
    Soft,
    /// <summary>A block dropped by the battle's hurry-up: as hard as a pillar.</summary>
    Dropped,
}

public enum PowerUp : byte
{
    None,
    /// <summary>One more bomb at a time.</summary>
    Bombs,
    /// <summary>The flames reach a cell further.</summary>
    Fire,
    /// <summary>Roller skates: walk faster.</summary>
    Speed,
    /// <summary>The bombs wait for the detonator.</summary>
    Remote,
    /// <summary>Walk through the breakable blocks.</summary>
    WallPass,
    /// <summary>Walk over bombs.</summary>
    BombPass,
    /// <summary>The flames don't hurt.</summary>
    FlamePass,
    /// <summary>A while of not being hurt by anything.</summary>
    Mystery,
}

/// <summary>The story's creatures, from the slow drifters of the first stages to the whirlwinds the clock lets in.</summary>
public enum EnemyKind
{
    /// <summary>A pink puff that drifts about at random. Slow.</summary>
    Puffle,
    /// <summary>A green bulb on legs: quicker, and turns more.</summary>
    Sprout,
    /// <summary>A blue dumpling, bouncing about at a steady pace.</summary>
    Dumpling,
    /// <summary>A grinning orange disc: quick, and it comes after you.</summary>
    Grinner,
    /// <summary>A slow, clever ghost that drifts through the blocks.</summary>
    Wisp,
    /// <summary>A purple blob that oozes through the blocks.</summary>
    Gloop,
    /// <summary>A fast, clever dart that hunts you down.</summary>
    Dart,
    /// <summary>A whirlwind: the fastest, through the blocks, and after you. The clock running out lets a flood in.</summary>
    Whirl,
}

/// <summary>How a kind of creature behaves: its speed (cells a second), how keen it is to come after you, whether it goes through blocks, and its points.</summary>
public sealed record EnemyInfo(EnemyKind Kind, double Speed, double Smart, bool WallPass, int Points, uint Colour)
{
    public static readonly EnemyInfo[] All =
    [
        new(EnemyKind.Puffle, 1.6, 0.12, false, 100, 0xFFFF8AC8),
        new(EnemyKind.Sprout, 2.4, 0.25, false, 200, 0xFF62D85A),
        new(EnemyKind.Dumpling, 2.4, 0.15, false, 400, 0xFF5AA8FF),
        new(EnemyKind.Grinner, 3.3, 0.5, false, 800, 0xFFFF9A2A),
        new(EnemyKind.Wisp, 1.3, 0.75, true, 1000, 0xFFD8E8FF),
        new(EnemyKind.Gloop, 1.9, 0.35, true, 2000, 0xFFB25AF0),
        new(EnemyKind.Dart, 3.8, 0.85, false, 4000, 0xFFFFE04A),
        new(EnemyKind.Whirl, 4.2, 0.9, true, 8000, 0xFF40F0E0),
    ];

    public static EnemyInfo Of(EnemyKind k) => All[(int)k];
}

/// <summary>A creature, walking from cell centre to cell centre.</summary>
public sealed class Enemy
{
    public const double SpawnSeconds = 1.2, DeathSeconds = 1.0;

    public EnemyKind Kind;
    public EnemyInfo Info => EnemyInfo.Of(Kind);
    public double X, Y;
    /// <summary>The cell it is heading for: its own cell while it stands.</summary>
    public int Tx, Ty;
    public Dir Dir;
    public Dir Facing = Dir.Left;
    public double Age;
    /// <summary>Just come out of a burnt power-up or door: a moment before it moves, when nothing hurts it and it hurts nothing.</summary>
    public double Spawning;
    public bool Dead;
    public double DeadTime;

    public int Cx => Field.CellOf(X);
    public int Cy => Field.CellOf(Y);
    public bool AtCentre => X == Tx && Y == Ty;

    public void Place(int c, int r) => (X, Y, Tx, Ty, Dir) = (c, r, c, r, Dir.None);

    public void Head(Dir d)
    {
        var (dx, dy) = Dirs.Vec(d);
        (Tx, Ty, Dir) = (Cx + dx, Cy + dy, d);
        Facing = d;
    }

    /// <summary>Moves up to <paramref name="dist"/> cells towards its target; returns what is left over if it gets there.</summary>
    public double Glide(double dist)
    {
        double left = Math.Abs(Tx - X) + Math.Abs(Ty - Y);
        if (dist >= left)
        {
            (X, Y, Dir) = (Tx, Ty, Dir.None);
            return dist - left;
        }
        var (dx, dy) = Dirs.Vec(Dir);
        X += dx * dist;
        Y += dy * dist;
        return 0;
    }
}

public enum EffectKind { Score, Word, Sparkle, Debris, Puff }

/// <summary>Something brief to draw: points floating up, a word, the sparkle of a power-up taken. In cells.</summary>
public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
