using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVABomberman.Engine;
using Effect = AVABomberman.Engine.Effect;

namespace AVABomberman.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVABomberman/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    private const uint White = 0xFFF4F6FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A5A, Dim = 0xFF9AA8C8, Ink = 0xFF14182A, Orange = 0xFFFF8A1E, Sky = 0xFF7FD8FF;
    private const double BattleX0 = (Field.Width - Field.BattleCols * Field.Cell) / 2;

    /// <summary>The four bombers' colours: blue, red, green and gold.</summary>
    public static readonly uint[] PlayerColours = [0xFF3D8BFF, 0xFFFF4A4A, 0xFF38C858, 0xFFFFB020];

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    /// <summary>Big lettering with a thick outline, for the logo and the headlines.</summary>
    private static void Outlined(DrawingContext dc, string text, Point at, double size, uint fill, uint edge, double thick = 3)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(edge));
        double x = at.X - ft.Width / 2, y = at.Y - ft.Height / 2;
        var sh = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xA0000000));
        dc.DrawText(sh, new Point(x + thick + 3, y + thick + 4));
        for (int k = 0; k < 12; k++)
        {
            double a = k * Math.PI / 6;
            dc.DrawText(ft, new Point(x + Math.Cos(a) * thick, y + Math.Sin(a) * thick));
        }
        dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(fill)), new Point(x, y));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    /// <summary>A colour made lighter (f &gt; 1) or darker (f &lt; 1).</summary>
    private static uint Shade(uint colour, double f)
    {
        uint Ch(int shift)
        {
            double v = (colour >> shift) & 0xFF;
            v = f >= 1 ? v + (255 - v) * (f - 1) : v * f;
            return (uint)Math.Clamp(v, 0, 255) << shift;
        }
        return (colour & 0xFF000000) | Ch(16) | Ch(8) | Ch(0);
    }

    // ───────────────────────────── the camera ─────────────────────────────

    private double _camX;

    /// <summary>Where the field's left edge is on the screen: the story's scrolls with the bomber, the battle's is fixed.</summary>
    private double OriginX => Engine.Mode == Mode.Story ? -_camX : BattleX0;

    private double CamTarget()
    {
        var g = Engine;
        double fieldW = g.Board.Cols * Field.Cell;
        if (g.Mode != Mode.Story || fieldW <= Field.Width) return 0;
        double px = (g.Player.X + 0.5) * Field.Cell;
        return Math.Clamp(px - Field.Width / 2, 0, fieldW - Field.Width);
    }

    private void FollowCamera(double dt)
    {
        double target = CamTarget();
        if (Math.Abs(target - _camX) > 260 || Engine.Phase == Phase.Start) _camX = target;
        else _camX += (target - _camX) * Math.Min(1, dt * 8);
    }

    private Point P(double x, double y) => new(OriginX + (x + 0.5) * Field.Cell, Field.Top + (y + 0.5) * Field.Cell);

    // ───────────────────────────── the screen ─────────────────────────────

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            dc.FillRectangle(Brush(0xFF0C1226), new Rect(0, 0, Field.Width, Field.Height));
            DrawField(dc);
            if (g.Mode == Mode.Battle) DrawBattlePanels(dc);
            DrawHud(dc);
            DrawMessages(dc);
            if (g.Mode == Mode.Battle && g.Phase is Phase.RoundOver or Phase.MatchOver) DrawRoundOver(dc);
        }
        else DrawBackdrop(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.Setup: DrawSetup(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Outlined(dc, "PAUSED", new Point(Field.Width / 2, 340), 90, Yellow, Ink);
            Text(dc, "ANY KEY TO CARRY ON - ESC FOR THE TITLE", new Point(Field.Width / 2, 410), 28, White, center: true);
        }
        if (g.State == GameState.GameOver)
        {
            dc.FillRectangle(Brush(0x80000000), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
            Outlined(dc, g.AllClear ? "ALL CLEAR!" : "GAME OVER", new Point(Field.Width / 2, 400), 110, g.AllClear ? Yellow : Red, Ink, 4);
        }
        if (g.State == GameState.Attract && (int)(_clock * 1.5) % 2 == 0)
        {
            dc.FillRectangle(Brush(0xB0000000), new Rect(Field.Width / 2 - 250, Field.Height - 40, 500, 34), 6);
            Text(dc, "DEMONSTRATION - PRESS ENTER", new Point(Field.Width / 2, Field.Height - 23), 28, Yellow, center: true);
        }
        foreach (var e in g.Effects.Where(e => e.Kind == EffectKind.Word && !inGame)) DrawEffect(dc, e);
    }

    /// <summary>Behind the menus: a field of pillars and blocks, sliding slowly by, darkened.</summary>
    private void DrawBackdrop(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0xFF0C1226), new Rect(0, 0, Field.Width, Field.Height));
        double s = Field.Cell, shift = _clock * 18 % (2 * s);
        for (int c = -2; c < 24; c++)
            for (int r = 0; r < 17; r++)
            {
                var at = new Point(c * s - shift + s / 2, r * s + s / 2 - 16);
                int wc = c + (int)(_clock * 18 / (2 * s)) * 2;
                if (c % 2 == 0 && r % 2 == 0) DrawPillar(dc, at, false);
                else if (Hash(wc, r, 3) % 5 == 0) DrawSoft(dc, at, 0);
                else DrawFloor(dc, at, wc, r);
            }
        dc.FillRectangle(Brush(0xB80A0E1E), new Rect(0, 0, Field.Width, Field.Height));
    }

    // ───────────────────────────── the field ─────────────────────────────

    private void DrawField(DrawingContext dc)
    {
        var g = Engine;
        var b = g.Board;
        double ox = OriginX;
        int c0 = Math.Max(0, (int)Math.Floor(-ox / Field.Cell) - 1), c1 = Math.Min(b.Cols - 1, (int)Math.Ceiling((Field.Width - ox) / Field.Cell));
        using var clip = dc.PushClip(new Rect(0, Field.Top, Field.Width, b.Rows * Field.Cell));

        for (int c = c0; c <= c1; c++)
            for (int r = 0; r < b.Rows; r++)
            {
                var at = P(c, r);
                DrawFloor(dc, at, c, r);
                // The blocks cast a shadow down and to the right.
                if (b.T[c, r] == Tile.Floor && r > 0 && b.T[c, r - 1] != Tile.Floor)
                    dc.FillRectangle(Brush(0x40000000), new Rect(at.X - Field.Cell / 2, at.Y - Field.Cell / 2, Field.Cell, 9));
                if (b.T[c, r] == Tile.Floor && c > 0 && b.T[c - 1, r] != Tile.Floor)
                    dc.FillRectangle(Brush(0x30000000), new Rect(at.X - Field.Cell / 2, at.Y - Field.Cell / 2, 7, Field.Cell));
            }

        // What lies in the open: the door and the power-ups.
        for (int c = c0; c <= c1; c++)
            for (int r = 0; r < b.Rows; r++)
            {
                if (b.T[c, r] != Tile.Floor) continue;
                if (b.Exit == (c, r)) DrawDoor(dc, P(c, r), g.ExitOpen);
                if (b.Item[c, r] != PowerUp.None) DrawPowerUp(dc, P(c, r), b.Item[c, r], 1);
            }

        for (int c = c0; c <= c1; c++)
            for (int r = 0; r < b.Rows; r++)
            {
                var at = P(c, r);
                switch (b.T[c, r])
                {
                    case Tile.Pillar:
                        DrawPillar(dc, at, c == 0 || r == 0 || c == b.Cols - 1 || r == b.Rows - 1);
                        break;
                    case Tile.Soft:
                        DrawSoft(dc, at, g.Crumble[c, r] > 0 ? 1 - g.Crumble[c, r] / GameEngine.CrumbleSeconds : 0);
                        break;
                    case Tile.Dropped:
                        DrawDropped(dc, at, g.Dropped[c, r]);
                        break;
                }
            }

        // The hurry-up's next blocks, as shadows growing where they'll land.
        if (g.Hurry)
        {
            int k = 0;
            foreach (var (c, r) in g.DropsToCome.Take(3))
            {
                var at = P(c, r);
                double a = 0.25 + 0.2 * Math.Sin(_clock * 12) - k++ * 0.06;
                dc.DrawRectangle(Brush(Alpha(0xFF000000, a)), Pen(Alpha(Red, a + 0.3), 2), new Rect(at.X - 22, at.Y - 22, 44, 44), 6);
            }
        }

        foreach (var bomb in g.Bombs) DrawBomb(dc, P(bomb.C, bomb.R), bomb.Fuse, bomb.Age, bomb.Remote, 1);

        for (int c = c0; c <= c1; c++)
            for (int r = 0; r < b.Rows; r++)
                if (g.Flames[c, r].Burning) DrawFlame(dc, P(c, r), g.Flames[c, r]);

        var actors = new List<(double Y, Action Draw)>();
        foreach (var e in g.Enemies) actors.Add((e.Y, () => DrawEnemy(dc, e, P(e.X, e.Y), 1)));
        foreach (var bomber in g.Bombers)
        {
            if (!bomber.Alive && bomber.DeadTime > 2.2) continue;
            actors.Add((bomber.Y + 0.01, () => DrawBomberOnField(dc, bomber)));
        }
        foreach (var (_, draw) in actors.OrderBy(a => a.Y)) draw();

        foreach (var e in g.Effects) DrawEffect(dc, e);
    }

    private void DrawBomberOnField(DrawingContext dc, Bomber b)
    {
        var at = P(b.X, b.Y);
        if (!b.Alive)
        {
            DrawBomberDying(dc, at, b);
            return;
        }
        bool flicker = b.Invincible > 0 && (b.Invincible > 2.5 || (int)(_clock * 12) % 2 == 0);
        uint suit = PlayerColours[b.Index];
        if (flicker) suit = RainbowColour(_clock * 3 + b.Index);
        DrawBomber(dc, at, suit, b.Facing, b.Walking ? b.WalkTime : 0, b.Walking, 1);
        if (b.Invincible > 0 && flicker) dc.DrawEllipse(null, Pen(Alpha(White, 0.5), 2), new Point(at.X, at.Y - 4), 27, 31);
        if (Engine.Mode == Mode.Battle && Engine.Phase == Phase.Start)
        {
            // Who's who, at the start of a round.
            Text(dc, $"P{b.Index + 1}", new Point(at.X, at.Y - 46), 26, PlayerColours[b.Index], center: true);
        }
    }

    private static uint RainbowColour(double t)
    {
        uint[] rainbow = [0xFFFF4A4A, 0xFFFFB020, 0xFFFFE14A, 0xFF38C858, 0xFF3DD8FF, 0xFF3D8BFF, 0xFFB25AF0];
        return rainbow[(int)(t * 6) % rainbow.Length];
    }

    private static void DrawFloor(DrawingContext dc, Point at, int c, int r)
    {
        double s = Field.Cell / 2;
        uint baseC = (c + r) % 2 == 0 ? 0xFF3E9C3E : 0xFF389238;
        dc.FillRectangle(Brush(baseC), new Rect(at.X - s, at.Y - s, 2 * s, 2 * s));
        // Tufts of grass.
        uint h = Hash(c, r, 11);
        for (int k = 0; k < 3; k++)
        {
            double x = at.X - s + 6 + (h >> (k * 5)) % 38, y = at.Y - s + 8 + (h >> (k * 7 + 3)) % 36;
            dc.DrawLine(Pen(0xFF52B44E, 2), new Point(x, y), new Point(x - 2, y - 5));
            dc.DrawLine(Pen(0xFF52B44E, 2), new Point(x + 3, y), new Point(x + 4, y - 6));
        }
    }

    /// <summary>A pillar (or the wall round the edge): a bevelled block of grey stone.</summary>
    private static void DrawPillar(DrawingContext dc, Point at, bool wall)
    {
        double s = Field.Cell / 2;
        var r = new Rect(at.X - s, at.Y - s, 2 * s, 2 * s);
        uint face = wall ? 0xFF6C7488 : 0xFF8E98AE, light = wall ? 0xFFA6AFC2 : 0xFFD0D8E8, dark = wall ? 0xFF3C4254 : 0xFF525A70;
        dc.FillRectangle(Brush(dark), r);
        Poly(dc, light, null, 0, r.TopLeft, r.TopRight, new Point(r.Right - 7, r.Top + 7), new Point(r.Left + 7, r.Top + 7), new Point(r.Left + 7, r.Bottom - 7), r.BottomLeft);
        dc.FillRectangle(Brush(face), new Rect(r.Left + 7, r.Top + 7, r.Width - 14, r.Height - 14));
        if (wall)
        {
            // The outer wall: two courses of big stones.
            dc.DrawLine(Pen(0xFF4A5064, 2), new Point(r.Left + 7, at.Y), new Point(r.Right - 7, at.Y));
            dc.DrawLine(Pen(0xFF4A5064, 2), new Point(at.X, r.Top + 7), new Point(at.X, at.Y));
        }
        else
        {
            dc.DrawRectangle(null, Pen(0xFF6E7890, 1.5), new Rect(r.Left + 12, r.Top + 12, r.Width - 24, r.Height - 24), 2);
            dc.DrawEllipse(Brush(0xFFE8EEF8), null, new Point(r.Left + 11, r.Top + 11), 1.8, 1.8);
        }
        dc.DrawRectangle(null, Pen(0xFF262A38, 1), r);
    }

    /// <summary>A breakable block: warm bricks. Hit, it glows, cracks and falls apart in the flames.</summary>
    private void DrawSoft(DrawingContext dc, Point at, double burn)
    {
        double s = Field.Cell / 2 - 1;
        double shrink = burn > 0.55 ? (burn - 0.55) / 0.45 * 6 : 0;
        var r = new Rect(at.X - s + shrink, at.Y - s + shrink, 2 * (s - shrink), 2 * (s - shrink));
        uint mortar = 0xFF6A3A1E;
        uint brick = burn > 0 ? Mix(0xFFC9733A, 0xFFFF5A10, Math.Min(1, burn * 1.6)) : 0xFFC9733A;
        uint top = burn > 0 ? Mix(0xFFE89A5A, 0xFFFFD040, Math.Min(1, burn * 1.6)) : 0xFFE89A5A;
        dc.FillRectangle(Brush(mortar), r, 3);
        double rh = r.Height / 3;
        for (int row = 0; row < 3; row++)
        {
            double y = r.Top + row * rh;
            double off = row % 2 == 0 ? 0 : r.Width / 4;
            for (double x = r.Left - off; x < r.Right; x += r.Width / 2)
            {
                double x0 = Math.Max(r.Left + 1.5, x + 1.5), x1 = Math.Min(r.Right - 1.5, x + r.Width / 2 - 1.5);
                if (x1 - x0 < 2) continue;
                var br = new Rect(x0, y + 1.5, x1 - x0, rh - 3);
                dc.FillRectangle(Brush(brick), br, 2);
                dc.FillRectangle(Brush(top), new Rect(br.X, br.Y, br.Width, 3), 1.5);
            }
        }
        if (burn > 0)
        {
            // Charring, and flames roaring up over it.
            dc.FillRectangle(Brush(Alpha(0xFF200800, burn * 0.55)), r, 3);
            double fade = burn < 0.8 ? 1 : (1 - burn) / 0.2;
            for (int k = 0; k < 5; k++)
            {
                double fx = r.Left + 4 + k * (r.Width - 8) / 4, h = 16 + 12 * Math.Abs(Math.Sin(_clock * 17 + k * 1.7)) + burn * 10;
                Poly(dc, Alpha(0xFFFF5A10, 0.9 * fade), null, 0, new Point(fx - 8, r.Bottom - 2), new Point(fx + Math.Sin(_clock * 11 + k) * 3, r.Bottom - 2 - h), new Point(fx + 8, r.Bottom - 2));
                Poly(dc, Alpha(0xFFFFD040, 0.95 * fade), null, 0, new Point(fx - 4, r.Bottom - 2), new Point(fx + Math.Sin(_clock * 11 + k) * 2, r.Bottom - 2 - h * 0.6), new Point(fx + 4, r.Bottom - 2));
            }
        }
    }

    private static uint Mix(uint a, uint b, double t)
    {
        uint Ch(int shift) => (uint)Math.Clamp(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t, 0, 255) << shift;
        return 0xFF000000 | Ch(16) | Ch(8) | Ch(0);
    }

    /// <summary>A hurry-up block: steel with warning stripes, thumping down from above.</summary>
    private static void DrawDropped(DrawingContext dc, Point at, double age)
    {
        double fall = age < 0.18 ? (1 - age / 0.18) : 0;
        double s = Field.Cell / 2;
        if (fall > 0) dc.DrawEllipse(Brush(Alpha(0xFF000000, 0.4 * (1 - fall))), null, new Point(at.X, at.Y + s - 6), s * (1 - fall * 0.5), 6);
        var r = new Rect(at.X - s, at.Y - s - fall * 80, 2 * s, 2 * s);
        dc.FillRectangle(Brush(0xFF2C3446), r);
        dc.FillRectangle(Brush(0xFF55627E), new Rect(r.Left + 3, r.Top + 3, r.Width - 6, r.Height - 6), 3);
        dc.FillRectangle(Brush(0xFF1A2030), new Rect(r.Left + 6, r.Top + 6, r.Width - 12, 10));
        using (dc.PushClip(new Rect(r.Left + 6, r.Top + 6, r.Width - 12, 10)))
        {
            for (double x = r.Left - 10; x < r.Right; x += 12)
                Poly(dc, 0xFFFFC020, null, 0, new Point(x, r.Top + 16), new Point(x + 6, r.Top + 16), new Point(x + 16, r.Top + 6), new Point(x + 10, r.Top + 6));
        }
        foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            dc.DrawEllipse(Brush(0xFFA8B4CC), null, new Point(at.X + dx * (s - 9), r.Center.Y + dy * (s - 9) + 4), 2.2, 2.2);
        dc.DrawRectangle(null, Pen(0xFF10141E, 1.5), r);
        if (age < 0.35)
        {
            double t = age / 0.35;
            for (int k = 0; k < 6; k++)
            {
                double a = k * Math.PI / 3 + 0.4;
                dc.DrawEllipse(Brush(Alpha(0xFFD8D0C0, 0.6 * (1 - t))), null, new Point(at.X + Math.Cos(a) * (s + t * 14), at.Y + s * 0.6 + Math.Sin(a) * 6 * t), 5 * (1 - t) + 2, 4 * (1 - t) + 2);
            }
        }
    }

    // ───────────────────────────── the bar along the top ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF161C34), new Rect(0, 0, Field.Width, Field.Top - 4));
        dc.FillRectangle(Brush(0xFF2A3458), new Rect(0, Field.Top - 8, Field.Width, 4));
        if (g.Mode == Mode.Story)
        {
            Text(dc, g.IsBonus ? "BONUS" : "STAGE", new Point(26, 30), 26, Dim);
            Text(dc, g.IsBonus ? "STAGE" : g.Stage.ToString(), new Point(26, 64), 44, White);
            int secs = (int)Math.Ceiling(g.TimeLeft);
            bool low = secs <= 30 && !g.TimeUp;
            Text(dc, "TIME", new Point(170, 30), 26, Dim);
            Text(dc, g.TimeUp ? "UP!" : secs.ToString(), new Point(170, 64), 44, low && (int)(_clock * 4) % 2 == 0 ? Red : g.TimeUp ? Red : Yellow);
            Text(dc, "SCORE", new Point(330, 30), 26, Dim);
            Text(dc, g.Score.ToString("D7"), new Point(330, 64), 44, White);
            Text(dc, "HI", new Point(520, 30), 26, Dim);
            Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D7"), new Point(520, 64), 36, Dim);
            DrawBomberHead(dc, new Point(712, 52), PlayerColours[0], 0.9);
            Text(dc, $"x{Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0))}", new Point(734, 56), 40, White);
            DrawPowers(dc, g.Player, 800, 52);
        }
        else
        {
            int secs = (int)Math.Ceiling(g.TimeLeft);
            Text(dc, $"ROUND {g.Round}", new Point(30, 52), 40, White);
            dc.FillRectangle(Brush(0xFF0A0E1E), new Rect(Field.Width / 2 - 80, 14, 160, 74), 8);
            dc.DrawRectangle(null, Pen(g.Hurry ? Red : 0xFF3A4670, 2), new Rect(Field.Width / 2 - 80, 14, 160, 74), 8);
            Text(dc, $"{secs / 60}:{secs % 60:D2}", new Point(Field.Width / 2, 50), 60, g.Hurry && (int)(_clock * 4) % 2 == 0 ? Red : Yellow, center: true);
            Text(dc, g.WinsNeededNow == 1 ? "ONE ROUND" : $"FIRST TO {g.WinsNeededNow}", new Point(Field.Width - 30, 52), 34, Dim, right: true);
        }
    }

    /// <summary>A bomber's power-ups, in a row: bombs, fire and speed with their counts, then the special ones.</summary>
    private void DrawPowers(DrawingContext dc, Bomber b, double x, double y)
    {
        void Icon(PowerUp p, string? n)
        {
            DrawPowerUp(dc, new Point(x + 16, y), p, 0.66);
            if (n != null) Text(dc, n, new Point(x + 36, y + 8), 26, White);
            x += n != null ? 52 : 36;
        }
        Icon(PowerUp.Bombs, b.Bombs.ToString());
        Icon(PowerUp.Fire, b.Range.ToString());
        if (b.SpeedUps > 0) Icon(PowerUp.Speed, b.SpeedUps.ToString());
        if (b.Remote) Icon(PowerUp.Remote, null);
        if (b.WallPass) Icon(PowerUp.WallPass, null);
        if (b.BombPass) Icon(PowerUp.BombPass, null);
        if (b.FlamePass) Icon(PowerUp.FlamePass, null);
        if (b.Invincible > 0 && !Engine.IsBonus) Icon(PowerUp.Mystery, null);
    }

    /// <summary>The battle's players down the sides, by the corners they start in: their colours, trophies and power-ups.</summary>
    private void DrawBattlePanels(DrawingContext dc)
    {
        var g = Engine;
        double w = BattleX0 - 8;
        foreach (var b in g.Bombers)
        {
            bool left = b.Index is 0 or 3, top = b.Index is 0 or 2;
            double x = left ? 4 : Field.Width - w - 4, y = top ? Field.Top + 6 : Field.Top + 330;
            uint col = PlayerColours[b.Index];
            dc.FillRectangle(Brush(0xFF161C34), new Rect(x, y, w, 314), 8);
            dc.DrawRectangle(null, Pen(b.Alive ? col : 0xFF3A4060, 2), new Rect(x, y, w, 314), 8);
            Text(dc, $"P{b.Index + 1}", new Point(x + w / 2, y + 22), 34, col, center: true);
            Text(dc, b.Human ? "PLAYER" : "COMPUTER", new Point(x + w / 2, y + 50), 22, Dim, center: true);
            if (b.Alive) DrawBomber(dc, new Point(x + w / 2, y + 108), col, Dir.Down, b.Walking ? b.WalkTime : 0, b.Walking, 1.15);
            else
            {
                DrawBomberHead(dc, new Point(x + w / 2, y + 104), 0xFF505870, 1.3);
                Text(dc, "OUT", new Point(x + w / 2, y + 140), 30, Red, center: true);
            }
            for (int i = 0; i < g.WinsNeededNow; i++)
            {
                double tx = x + w / 2 + (i - (g.WinsNeededNow - 1) / 2.0) * 24;
                DrawTrophy(dc, new Point(tx, y + 182), i < b.Wins, 0.7);
            }
            Text(dc, $"{b.Bombs}", new Point(x + 44, y + 228), 26, White);
            DrawPowerUp(dc, new Point(x + 26, y + 228), PowerUp.Bombs, 0.6);
            DrawPowerUp(dc, new Point(x + 86, y + 228), PowerUp.Fire, 0.6);
            Text(dc, $"{b.Range}", new Point(x + 104, y + 228), 26, White);
            DrawPowerUp(dc, new Point(x + 26, y + 268), PowerUp.Speed, 0.6);
            Text(dc, $"{b.SpeedUps}", new Point(x + 44, y + 268), 26, White);
            var special = b.Remote ? PowerUp.Remote : b.BombPass ? PowerUp.BombPass : b.FlamePass ? PowerUp.FlamePass : PowerUp.None;
            if (special != PowerUp.None) DrawPowerUp(dc, new Point(x + 92, y + 268), special, 0.6);
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        if (g.Mode == Mode.Battle && g.Phase is Phase.RoundOver or Phase.MatchOver) return;
        double cx = Field.Width / 2, cy = Field.Top + 5.5 * Field.Cell;
        double w = Math.Min(900, Math.Max(msg.Length * 34, (g.SubMessage?.Length ?? 0) * 15) + 80), h = g.SubMessage != null ? 122.0 : 86.0;
        dc.FillRectangle(Brush(0xD8101830), new Rect(cx - w / 2, cy - 50, w, h), 10);
        dc.DrawRectangle(null, Pen(Alpha(Orange, 0.8), 3), new Rect(cx - w / 2, cy - 50, w, h), 10);
        Outlined(dc, msg, new Point(cx, cy - 8), 64, g.Phase == Phase.Dying ? Red : Yellow, Ink, 3);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 46), 28, White, center: true);
    }

    /// <summary>The end of a battle round: the winner (or a draw) and everyone's trophies; or the battle's champion.</summary>
    private void DrawRoundOver(DrawingContext dc)
    {
        var g = Engine;
        bool match = g.Phase == Phase.MatchOver;
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top));
        double cx = Field.Width / 2;
        var r = new Rect(cx - 340, Field.Top + 40, 680, 560);
        dc.FillRectangle(Brush(0xF0141A30), r, 14);
        dc.DrawRectangle(null, Pen(Yellow, 3), r, 14);
        var champ = g.Champion;
        if (match && champ != null)
        {
            // Confetti.
            for (int k = 0; k < 60; k++)
            {
                double x = r.Left + Hash(k, 1, 5) % (uint)r.Width, y = r.Top + (Hash(k, 2, 5) % 560 + _clock * (60 + Hash(k, 3, 5) % 60)) % 560;
                dc.FillRectangle(Brush(PlayerColours[k % 4]), new Rect(x, y, 6, 3 + Math.Abs(Math.Sin(_clock * 6 + k)) * 6));
            }
            Outlined(dc, "CHAMPION!", new Point(cx, r.Top + 70), 84, Yellow, Ink, 4);
            double bounce = Math.Abs(Math.Sin(_clock * 5)) * 18;
            DrawTrophy(dc, new Point(cx, r.Top + 300), true, 3.2);
            DrawBomber(dc, new Point(cx, r.Top + 190 - bounce), PlayerColours[champ.Index], Dir.Down, 0, false, 2.2);
            Text(dc, $"PLAYER {champ.Index + 1} TAKES THE BATTLE", new Point(cx, r.Top + 420), 38, PlayerColours[champ.Index], center: true);
            Text(dc, $"{champ.Wins} TROPHIES", new Point(cx, r.Top + 466), 30, White, center: true);
            return;
        }
        var w = g.Winner;
        Outlined(dc, w is null ? "DRAW GAME" : $"PLAYER {w.Index + 1} WINS!", new Point(cx, r.Top + 64), 72, w is null ? Dim : PlayerColours[w.Index], Ink, 4);
        if (g.SubMessage is { } sub && w is null) Text(dc, sub, new Point(cx, r.Top + 118), 28, White, center: true);
        int i = 0;
        foreach (var b in g.Bombers)
        {
            double y = r.Top + 180 + i++ * 92;
            bool won = b == w;
            double hop = won ? Math.Abs(Math.Sin(_clock * 6)) * 10 : 0;
            dc.FillRectangle(Brush(won ? 0x40FFE14Au : 0x20FFFFFFu), new Rect(r.Left + 30, y - 38, r.Width - 60, 80), 10);
            DrawBomber(dc, new Point(r.Left + 90, y - hop), PlayerColours[b.Index], Dir.Down, won ? _clock : 0, won, 1.1);
            Text(dc, $"P{b.Index + 1}", new Point(r.Left + 140, y), 38, PlayerColours[b.Index]);
            for (int t = 0; t < g.WinsNeededNow; t++)
            {
                bool lit = t < b.Wins;
                bool fresh = won && t == b.Wins - 1;
                double s = fresh ? 1.1 + 0.12 * Math.Sin(_clock * 8) : 1.0;
                DrawTrophy(dc, new Point(r.Left + 260 + t * 80, y), lit, s * 1.7);
            }
        }
    }

    // ───────────────────────────── the menus ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        double wob = Math.Sin(_clock * 2) * 4;
        Outlined(dc, "AVA", new Point(cx, 82 + wob * 0.5), 70, Sky, Ink, 3);
        Outlined(dc, "BOMBERMAN", new Point(cx, 162 + wob), 150, Yellow, 0xFFB03A10, 6);
        Text(dc, "AFTER HUDSON SOFT'S 1983 CLASSIC", new Point(cx, 238), 28, Dim, center: true);

        // A bomber with a bomb, its fuse fizzing, and a creature to blow up.
        DrawBomber(dc, new Point(cx - 300, 360), PlayerColours[0], Dir.Right, _clock, true, 2.2);
        DrawBomb(dc, new Point(cx - 190, 382), Bomb.FuseSeconds - (_clock % 2.5), _clock, false, 1.6);
        DrawEnemy(dc, new Enemy { Kind = EnemyKind.Puffle, Facing = Dir.Left, Age = _clock }, new Point(cx + 300, 362), 2.0);
        DrawBomber(dc, new Point(cx + 210, 372), PlayerColours[1], Dir.Left, 0, false, 1.4);

        dc.FillRectangle(Brush(0xB80C1226), new Rect(cx - 400, 470, 800, 222), 12);
        string[] items = ["STORY MODE", "BATTLE MODE"];
        string[] about = ["FIFTY STAGES - ONE BOMBER - FIND THE DOOR", "UP TO FOUR BOMBERS - PEOPLE OR COMPUTER"];
        for (int i = 0; i < items.Length; i++)
        {
            bool sel = i == g.MenuIndex;
            double y = 330 + i * 76;
            if (sel)
            {
                dc.FillRectangle(Brush(0x60FF8A1E), new Rect(cx - 170, y - 30, 340, 58), 10);
                dc.DrawRectangle(null, Pen(Orange, 3), new Rect(cx - 170, y - 30, 340, 58), 10);
                DrawBomb(dc, new Point(cx - 140, y), 1.2, _clock, false, 0.8);
            }
            Text(dc, items[i], new Point(cx + 12, y - 2), 48, sel ? Yellow : White, center: true);
        }
        Text(dc, about[g.MenuIndex], new Point(cx, 490), 28, Sky, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "UP / DOWN TO CHOOSE - ENTER OR SPACE TO START", new Point(cx, 548), 30, Yellow, center: true);
        Text(dc, "P1: ARROWS, SPACE TO BOMB, CTRL OR ENTER TO DETONATE", new Point(cx, 612), 26, Dim, center: true);
        Text(dc, "P2: W A S D, Q TO BOMB, E TO DETONATE  -  GAMEPADS FOR P1 TO P4", new Point(cx, 642), 26, Dim, center: true);
        Text(dc, "P PAUSE   M MUTE   CTRL+S SCREENSHOT   ESC QUIT", new Point(cx, 672), 26, Dim, center: true);

        // The creatures and what they're worth.
        for (int i = 0; i < EnemyInfo.All.Length; i++)
        {
            var info = EnemyInfo.All[i];
            double x = 96 + i * 119;
            DrawEnemy(dc, new Enemy { Kind = info.Kind, Facing = Dir.Down, Age = _clock + i }, new Point(x, 714), 0.8);
            Text(dc, info.Points.ToString(), new Point(x, 750), 22, Yellow, center: true);
        }
    }

    private void DrawSetup(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Outlined(dc, "BATTLE", new Point(cx, 86), 100, Yellow, 0xFFB03A10, 5);
        Text(dc, "WHO'S IN EACH CORNER?", new Point(cx, 150), 30, Dim, center: true);
        string[] keys = ["ARROWS + SPACE / PAD 1", "W A S D + Q / PAD 2", "GAMEPAD 3", "GAMEPAD 4"];
        int human = 0;
        for (int i = 0; i < 4; i++)
        {
            double y = 214 + i * 82;
            bool sel = g.SetupRow == i;
            var slot = g.Slots[i];
            uint col = PlayerColours[i];
            dc.FillRectangle(Brush(sel ? 0x50FF8A1Eu : 0x30FFFFFFu), new Rect(cx - 330, y - 34, 660, 70), 10);
            if (sel) dc.DrawRectangle(null, Pen(Orange, 3), new Rect(cx - 330, y - 34, 660, 70), 10);
            if (slot != Slot.Off) DrawBomber(dc, new Point(cx - 280, y + 4), col, Dir.Down, slot == Slot.Human ? _clock : 0, slot == Slot.Human && sel, 1.0);
            else DrawBomberHead(dc, new Point(cx - 280, y), 0xFF505870, 1.0);
            Text(dc, $"P{i + 1}", new Point(cx - 236, y), 40, slot == Slot.Off ? 0xFF505870 : col);
            string what = slot switch { Slot.Human => "PLAYER", Slot.Computer => "COMPUTER", _ => "- NOBODY -" };
            Text(dc, (sel ? "< " : "  ") + what + (sel ? " >" : "  "), new Point(cx + 10, y - 4), 40, slot == Slot.Off ? Dim : White, center: true);
            string hint = slot == Slot.Human ? (g.Slots.Count(s => s == Slot.Human) <= 1 ? "KEYBOARD / PAD 1" : human < keys.Length ? keys[human] : "") : "";
            if (slot == Slot.Human) human++;
            Text(dc, hint, new Point(cx + 320, y + 22), 22, Sky, right: true);
        }
        {
            double y = 214 + 4 * 82;
            bool sel = g.SetupRow == 4;
            dc.FillRectangle(Brush(sel ? 0x50FF8A1Eu : 0x30FFFFFFu), new Rect(cx - 330, y - 34, 660, 70), 10);
            if (sel) dc.DrawRectangle(null, Pen(Orange, 3), new Rect(cx - 330, y - 34, 660, 70), 10);
            Text(dc, (sel ? "< " : "") + "TROPHIES TO WIN" + (sel ? " >" : ""), new Point(cx - 300, y - 2), 36, White);
            for (int t = 0; t < g.WinsNeeded; t++) DrawTrophy(dc, new Point(cx + 70 + t * 52, y), true, 0.9);
        }
        {
            double y = 214 + 5 * 82 + 10;
            bool sel = g.SetupRow == 5;
            uint col = g.CanFight ? (sel ? Yellow : White) : Dim;
            if (sel) dc.FillRectangle(Brush(0x60FF4A1E), new Rect(cx - 140, y - 36, 280, 72), 12);
            if (sel) dc.DrawRectangle(null, Pen(Orange, 3), new Rect(cx - 140, y - 36, 280, 72), 12);
            Outlined(dc, "FIGHT!", new Point(cx, y - 2), 64, col, Ink, 3);
        }
        Text(dc, g.CanFight ? "UP / DOWN: CHOOSE   LEFT / RIGHT: CHANGE   ENTER: FIGHT   ESC: BACK" : "A BATTLE NEEDS TWO BOMBERS AT LEAST", new Point(cx, 742), 26, g.CanFight ? Dim : Red, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Outlined(dc, "TOP BOMBERS", new Point(cx, 110), 92, Yellow, 0xFFB03A10, 5);
        dc.FillRectangle(Brush(0xC80C1226), new Rect(cx - 260, 168, 520, 486), 12);
        dc.DrawRectangle(null, Pen(Alpha(Orange, 0.7), 2), new Rect(cx - 260, 168, 520, 486), 12);
        Text(dc, "RANK NAME    SCORE   STAGE", new Point(cx, 192), 32, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4} ", new Point(cx, 240 + i * 44), 38, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        DrawBomber(dc, new Point(110, 440), PlayerColours[0], Dir.Right, _clock, true, 1.8);
        DrawBomb(dc, new Point(Field.Width - 110, 460), 1.5, _clock, false, 1.6);
        Text(dc, "PRESS ENTER", new Point(cx, 712), 32, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Outlined(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 76, Yellow, 0xFFB03A10, 4);
        Text(dc, g.Score.ToString(), new Point(cx, 280), 56, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 36, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 84, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 482, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 28, White, center: true);
    }

    // ───────────────────────────── effects ─────────────────────────────

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = P(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Debris:
                for (int k = 0; k < 9; k++)
                {
                    double a = k * Math.PI * 2 / 9 + Hash(k, (int)e.X, (int)e.Y) % 10 * 0.05, d = (4 + 30 * t) * (0.6 + Hash(k, (int)e.Y, 3) % 10 * 0.06);
                    double s = 7 * (1 - t);
                    var at = new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d * 0.7 + 26 * t * t);
                    dc.FillRectangle(Brush(Alpha(k % 3 == 0 ? 0xFF6A3A1E : e.Colour, 1 - t)), new Rect(at.X - s / 2, at.Y - s / 2, s, s * 0.7));
                }
                break;
            case EffectKind.Puff:
                for (int k = 0; k < 6; k++)
                {
                    double a = k * Math.PI / 3, d = 6 + 18 * t;
                    dc.DrawEllipse(Brush(Alpha(0xFFE0E0E8, 0.7 * (1 - t))), null, new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d - 10 * t), 9 * (1 - t * 0.5), 9 * (1 - t * 0.5));
                }
                break;
            case EffectKind.Sparkle:
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4 + t * 2, d = 8 + 30 * t;
                    DrawSpark(dc, new Point(c.X + Math.Cos(a) * d, c.Y + Math.Sin(a) * d), 6 * (1 - t) + 2, Alpha(k % 2 == 0 ? Yellow : White, 1 - t));
                }
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 18 - t * 26), 30, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - 20 - t * 30), 30 * e.Size + 6, e.Colour, center: true);
                break;
        }
    }

    private static void DrawSpark(DrawingContext dc, Point at, double r, uint colour)
    {
        var pts = Enumerable.Range(0, 8).Select(k =>
        {
            double a = k * Math.PI / 4, rad = k % 2 == 0 ? r : r * 0.4;
            return new Point(at.X + Math.Cos(a) * rad, at.Y + Math.Sin(a) * rad);
        }).ToArray();
        Poly(dc, colour, null, 0, pts);
    }
}

/// <summary>Rounded rectangles with a double for the corner radius, as everything else here is.</summary>
internal static class DrawingExtensions
{
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) =>
        dc.DrawRectangle(brush, null, rect, radius, radius);

    public static void DrawRectangle(this DrawingContext dc, IBrush? brush, IPen? pen, Rect rect, double radius) =>
        dc.DrawRectangle(brush, pen, rect, radius, radius);
}
