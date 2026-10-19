using Avalonia;
using Avalonia.Media;
using AVABomberman.Engine;

namespace AVABomberman.Views;

/// <summary>The characters and things on the field, all drawn from shapes: the bombers, the creatures, bombs, flames, power-ups, the door and the trophies.</summary>
public sealed partial class GameCanvas
{
    // ───────────────────────────── the bombers ─────────────────────────────

    /// <summary>
    /// A bomber: a round white helmet with a dark visor and two glowing eyes, an antenna with a ball in its colour,
    /// a suit in its colour with a belt, white gloves and boots. Seen from the front facing down, from behind facing
    /// up, side-on facing left or right; bobbing and swinging its arms as it walks.
    /// </summary>
    private void DrawBomber(DrawingContext dc, Point at, uint suit, Dir facing, double walk, bool walking, double scale)
    {
        double k = scale;
        double step = walking ? Math.Sin(walk * 14) : 0;
        double bob = walking ? Math.Abs(Math.Cos(walk * 14)) * 2 * k : 0;
        var c = new Point(at.X, at.Y - bob);
        uint boot = Shade(suit, 0.45), suitDark = Shade(suit, 0.7), outline = 0xFF1A1E30;
        int side = facing == Dir.Left ? -1 : 1;
        bool sideOn = facing is Dir.Left or Dir.Right;

        dc.DrawEllipse(Brush(0x48000000), null, new Point(at.X, at.Y + 21 * k), 15 * k, 5 * k);
        // Boots.
        if (sideOn)
        {
            dc.DrawEllipse(Brush(boot), Pen(outline, 1.4), new Point(c.X + side * 2 * k + step * 5 * k, c.Y + 18 * k), 7 * k, 4.5 * k);
            dc.DrawEllipse(Brush(boot), Pen(outline, 1.4), new Point(c.X + side * 2 * k - step * 5 * k, c.Y + 18 * k), 7 * k, 4.5 * k);
        }
        else
        {
            dc.DrawEllipse(Brush(boot), Pen(outline, 1.4), new Point(c.X - 6.5 * k, c.Y + 18 * k - Math.Max(0, step) * 3 * k), 6.5 * k, 4.5 * k);
            dc.DrawEllipse(Brush(boot), Pen(outline, 1.4), new Point(c.X + 6.5 * k, c.Y + 18 * k - Math.Max(0, -step) * 3 * k), 6.5 * k, 4.5 * k);
        }
        // The suit, with its belt.
        var body = new Rect(c.X - 11 * k, c.Y - 3 * k, 22 * k, 20 * k);
        dc.DrawRectangle(Brush(suit), Pen(outline, 1.6), body, 8 * k);
        dc.FillRectangle(Brush(Shade(suit, 1.35)), new Rect(body.X + 3 * k, body.Y + 2 * k, 5 * k, 9 * k), 2.5 * k);
        dc.FillRectangle(Brush(0xFF262A40), new Rect(body.X + 0.8, c.Y + 8 * k, body.Width - 1.6, 3.6 * k));
        if (facing != Dir.Up) dc.FillRectangle(Brush(0xFFFFD040), new Rect(c.X - 2.5 * k + (sideOn ? side * 5 * k : 0), c.Y + 7.6 * k, 5 * k, 4.4 * k), 1);
        // Gloves, swinging.
        if (sideOn)
            dc.DrawEllipse(Brush(0xFFF6F6FF), Pen(outline, 1.4), new Point(c.X - side * 2 * k + step * 6 * k, c.Y + 5 * k), 4.6 * k, 4.6 * k);
        else
        {
            dc.DrawEllipse(Brush(0xFFF6F6FF), Pen(outline, 1.4), new Point(c.X - 13.5 * k, c.Y + 5 * k + step * 2.5 * k), 4.6 * k, 4.6 * k);
            dc.DrawEllipse(Brush(0xFFF6F6FF), Pen(outline, 1.4), new Point(c.X + 13.5 * k, c.Y + 5 * k - step * 2.5 * k), 4.6 * k, 4.6 * k);
        }
        DrawHead(dc, new Point(c.X, c.Y - 13 * k), suit, suitDark, facing, k, walk);
    }

    private void DrawHead(DrawingContext dc, Point h, uint colour, uint dark, Dir facing, double k, double t)
    {
        uint outline = 0xFF1A1E30;
        int side = facing == Dir.Left ? -1 : 1;
        // The antenna, swaying.
        double sway = Math.Sin(_clock * 4 + t) * 2.5 * k;
        var tip = new Point(h.X + sway + (facing is Dir.Left or Dir.Right ? -side * 3 * k : 0), h.Y - 22 * k);
        dc.DrawLine(Pen(outline, 2.4 * k), new Point(h.X, h.Y - 13 * k), tip);
        dc.DrawEllipse(Brush(colour), Pen(outline, 1.4), tip, 4.6 * k, 4.6 * k);
        dc.DrawEllipse(Brush(0xC0FFFFFF), null, new Point(tip.X - 1.5 * k, tip.Y - 1.5 * k), 1.5 * k, 1.5 * k);
        // The helmet.
        dc.DrawEllipse(Brush(0xFFF4F6FF), Pen(outline, 2), h, 14.5 * k, 13.5 * k);
        dc.DrawEllipse(Brush(0xFFD8DEEE), null, new Point(h.X + 3 * k, h.Y + 5 * k), 10 * k, 7 * k);
        dc.DrawEllipse(Brush(0xFFF4F6FF), null, new Point(h.X - 1 * k, h.Y + 1 * k), 12.5 * k, 10.5 * k);
        dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(h.X - 6 * k, h.Y - 6 * k), 3.2 * k, 2.4 * k);
        bool blink = (int)(_clock * 10 + t * 3) % 37 == 0;
        switch (facing)
        {
            case Dir.Up:
                // The back of the helmet: a stripe in its colour.
                dc.FillRectangle(Brush(colour), new Rect(h.X - 3 * k, h.Y - 12 * k, 6 * k, 22 * k), 3 * k);
                break;
            case Dir.Left or Dir.Right:
            {
                var v = new Rect(h.X + side * 2 * k - 8 * k, h.Y - 4.5 * k, 16 * k, 10 * k);
                dc.DrawRectangle(Brush(0xFF1A2244), null, v.Translate(new Vector(side * 3 * k, 0)), 5 * k);
                if (!blink) dc.FillRectangle(Brush(0xFF7FF6FF), new Rect(h.X + side * 8 * k - 1.6 * k, h.Y - 2.6 * k, 3.2 * k, 5.6 * k), 1.2 * k);
                // The ear-piece in its colour.
                dc.DrawEllipse(Brush(colour), Pen(outline, 1.2), new Point(h.X - side * 6 * k, h.Y + 1 * k), 3.6 * k, 3.6 * k);
                break;
            }
            default:
            {
                var v = new Rect(h.X - 10.5 * k, h.Y - 4.5 * k, 21 * k, 11 * k);
                dc.DrawRectangle(Brush(0xFF1A2244), null, v, 5.5 * k);
                if (!blink)
                {
                    dc.FillRectangle(Brush(0xFF7FF6FF), new Rect(h.X - 6.5 * k, h.Y - 2.4 * k, 3.4 * k, 6 * k), 1.2 * k);
                    dc.FillRectangle(Brush(0xFF7FF6FF), new Rect(h.X + 3.1 * k, h.Y - 2.4 * k, 3.4 * k, 6 * k), 1.2 * k);
                }
                // Cheeks in its colour.
                dc.DrawEllipse(Brush(Alpha(colour, 0.85)), null, new Point(h.X - 12 * k, h.Y + 4 * k), 2.6 * k, 2.2 * k);
                dc.DrawEllipse(Brush(Alpha(colour, 0.85)), null, new Point(h.X + 12 * k, h.Y + 4 * k), 2.6 * k, 2.2 * k);
                break;
            }
        }
    }

    /// <summary>Just a bomber's head, for the lives and the panels.</summary>
    private void DrawBomberHead(DrawingContext dc, Point at, uint colour, double scale) =>
        DrawHead(dc, at, colour, Shade(colour, 0.7), Dir.Down, scale, 0);

    /// <summary>A bomber caught: it spins, flashes, then blackens and crumples into smoke.</summary>
    private void DrawBomberDying(DrawingContext dc, Point at, Bomber b)
    {
        double t = b.DeadTime;
        uint colour = PlayerColours[b.Index];
        if (t < 0.7)
        {
            var f = Dirs.All[(int)(t * 14) % 4];
            DrawBomber(dc, at, (int)(t * 16) % 2 == 0 ? 0xFFFFFFFF : colour, f, 0, false, 1);
            return;
        }
        double u = Math.Min(1, (t - 0.7) / 0.9);
        double squash = 1 - u * 0.75;
        using (dc.PushTransform(Matrix.CreateTranslation(-at.X, -at.Y - 20) * Matrix.CreateScale(1 + u * 0.3, squash) * Matrix.CreateTranslation(at.X, at.Y + 20)))
            DrawBomber(dc, at, Shade(colour, 0.35), Dir.Down, 0, false, 1);
        for (int k = 0; k < 5; k++)
        {
            double a = k * 1.3 + t, rise = (t - 0.7) * 30 + k * 6;
            dc.DrawEllipse(Brush(Alpha(0xFF808090, 0.55 * (1 - u * 0.6))), null, new Point(at.X + Math.Sin(a * 2) * 10, at.Y - rise), 6 + k * 1.5, 6 + k * 1.5);
        }
    }

    // ───────────────────────────── bombs and flames ─────────────────────────────

    /// <summary>A bomb: a dark round shell that swells and shrinks faster as the fuse burns down, its fuse fizzing. A remote one has a red band and a winking light.</summary>
    private void DrawBomb(DrawingContext dc, Point at, double fuse, double age, bool remote, double scale)
    {
        double k = scale;
        double rate = remote ? 3 : 4 + 8 * (1 - Math.Clamp(fuse / Bomb.FuseSeconds, 0, 1));
        double pulse = 1 + 0.08 * Math.Sin(age * rate * 2);
        double r = 16.5 * k * pulse;
        var c = new Point(at.X, at.Y + 3 * k);
        dc.DrawEllipse(Brush(0x50000000), null, new Point(at.X, at.Y + 19 * k), 15 * k, 5 * k);
        dc.DrawEllipse(Brush(0xFF1C2240), Pen(0xFF080A14, 2), c, r, r);
        dc.DrawEllipse(Brush(0xFF2E3864), null, new Point(c.X - r * 0.12, c.Y - r * 0.12), r * 0.78, r * 0.78);
        dc.DrawEllipse(Brush(0xFF1C2240), null, new Point(c.X + r * 0.08, c.Y + r * 0.1), r * 0.7, r * 0.7);
        dc.DrawEllipse(Brush(0xE0FFFFFF), null, new Point(c.X - r * 0.42, c.Y - r * 0.42), r * 0.2, r * 0.14);
        if (remote)
        {
            dc.DrawLine(Pen(0xFFE02A3A, 4 * k), new Point(c.X - r * 0.95, c.Y + r * 0.1), new Point(c.X + r * 0.95, c.Y + r * 0.1));
            bool lit = (int)(age * 3) % 2 == 0;
            dc.DrawEllipse(Brush(lit ? 0xFFFF3040 : 0xFF701820), null, new Point(c.X, c.Y + r * 0.1), 3 * k, 3 * k);
        }
        // The cap and the fuse.
        var cap = new Point(c.X + r * 0.45, c.Y - r * 0.8);
        dc.DrawRectangle(Brush(0xFF8890A8), Pen(0xFF080A14, 1.4), new Rect(cap.X - 4.5 * k, cap.Y - 4 * k, 9 * k, 7 * k), 1.5);
        var f1 = new Point(cap.X + 3 * k, cap.Y - 7 * k);
        var f2 = new Point(cap.X + 8 * k, cap.Y - 9 * k);
        dc.DrawLine(Pen(0xFFD8C090, 2.4 * k), new Point(cap.X, cap.Y - 3 * k), f1);
        dc.DrawLine(Pen(0xFFD8C090, 2.4 * k), f1, f2);
        if (!remote || (int)(age * 6) % 2 == 0)
        {
            double flick = 0.7 + 0.3 * Math.Sin(age * 40);
            DrawSpark(dc, f2, 6.5 * k * flick, 0xFFFFB020);
            DrawSpark(dc, f2, 3.5 * k * flick, 0xFFFFF8C0);
        }
    }

    /// <summary>
    /// A cell of flame: a glowing core and arms out to each side the blast spreads to, in three layers (deep orange,
    /// yellow, white-hot), growing in and dying away.
    /// </summary>
    private void DrawFlame(DrawingContext dc, Point at, Flame f)
    {
        double life = f.Time / GameEngine.FlameSeconds;
        // Bursts out at once, burns full, and dies down at the end.
        double grow = Math.Min(1, (1 - life) * GameEngine.FlameSeconds / 0.06 + 0.3) * Math.Min(1, 0.3 + life * 2.4);
        double h = Field.Cell / 2;
        bool centre = (f.Mask & 16) != 0 || BitCount(f.Mask & 15) != 1;
        (uint Colour, double Width)[] layers = [(0x60FF3000, 54), (0xFFFF4A10, 46), (0xFFFF9A20, 35), (0xFFFFE070, 23), (0xFFFFFFF0, 11)];
        double flick = 1 + 0.06 * Math.Sin(_clock * 40 + at.X * 0.1 + at.Y * 0.07);
        foreach (var (colour, width) in layers)
        {
            double w = width * grow * flick;
            foreach (var d in Dirs.All)
            {
                if ((f.Mask & Dirs.Bit(d)) == 0) continue;
                var (dx, dy) = Dirs.Vec(d);
                double len = h + 1;
                var rect = dx != 0
                    ? new Rect(dx > 0 ? at.X : at.X - len, at.Y - w / 2, len, w)
                    : new Rect(at.X - w / 2, dy > 0 ? at.Y : at.Y - len, w, len);
                dc.FillRectangle(Brush(colour), rect);
            }
            double core = centre ? w * 0.62 : w * 0.5;
            dc.DrawEllipse(Brush(colour), null, at, core, core);
        }
        // Tongues of fire licking off the arms.
        for (int k = 0; k < 4; k++)
        {
            double a = k * Math.PI / 2 + Math.Sin(_clock * 13 + k + at.X * 0.3) * 0.5;
            double len = 10 + 6 * Math.Sin(_clock * 21 + k * 2 + at.Y * 0.2);
            var tip = new Point(at.X + Math.Cos(a + 0.785) * (14 + len) * grow, at.Y + Math.Sin(a + 0.785) * (14 + len) * grow);
            Poly(dc, Alpha(0xFFFFB030, 0.8), null, 0, new Point(at.X + Math.Cos(a) * 9 * grow, at.Y + Math.Sin(a) * 9 * grow), tip, new Point(at.X + Math.Cos(a + 1.57) * 9 * grow, at.Y + Math.Sin(a + 1.57) * 9 * grow));
        }
        if (centre)
        {
            for (int k = 0; k < 6; k++)
            {
                double a = k * Math.PI / 3 + _clock * 5;
                DrawSpark(dc, new Point(at.X + Math.Cos(a) * 14 * grow, at.Y + Math.Sin(a) * 14 * grow), 4 * grow, Alpha(0xFFFFFFFF, 0.7));
            }
        }
    }

    private static int BitCount(int m)
    {
        int n = 0;
        for (; m != 0; m &= m - 1) n++;
        return n;
    }

    // ───────────────────────────── the creatures ─────────────────────────────

    private void DrawEnemy(DrawingContext dc, Enemy e, Point at, double scale)
    {
        double k = scale;
        var info = e.Info;
        uint col = info.Colour;
        if (e.Dead)
        {
            double t = Math.Min(1, e.DeadTime / Enemy.DeathSeconds);
            if (t < 0.35)
            {
                using (dc.PushTransform(Matrix.CreateTranslation(-at.X, -at.Y) * Matrix.CreateScale(1 + t, 1 - t * 1.5) * Matrix.CreateTranslation(at.X, at.Y)))
                    DrawEnemyBody(dc, e, at, (int)(t * 30) % 2 == 0 ? 0xFFFFFFFF : col, k, true);
            }
            for (int i = 0; i < 6; i++)
            {
                double a = i * Math.PI / 3, d = 4 + 22 * t;
                dc.DrawEllipse(Brush(Alpha(Shade(col, 1.3), 0.8 * (1 - t))), null, new Point(at.X + Math.Cos(a) * d, at.Y + Math.Sin(a) * d - 12 * t), 7 * (1 - t) + 1, 7 * (1 - t) + 1);
            }
            return;
        }
        if (e.Spawning > 0)
        {
            double t = 1 - e.Spawning / Enemy.SpawnSeconds;
            if ((int)(_clock * 14) % 2 == 0) return;
            using (dc.PushTransform(Matrix.CreateTranslation(-at.X, -at.Y) * Matrix.CreateScale(0.3 + 0.7 * t, 0.3 + 0.7 * t) * Matrix.CreateTranslation(at.X, at.Y)))
                DrawEnemyBody(dc, e, at, col, k, false);
            return;
        }
        DrawEnemyBody(dc, e, at, col, k, false);
    }

    private void DrawEnemyBody(DrawingContext dc, Enemy e, Point at, uint col, double k, bool hurt)
    {
        uint outline = 0xFF1A1424, dark = Shade(col, 0.6), light = Shade(col, 1.45);
        double t = e.Age;
        var (fx, fy) = Dirs.Vec(e.Facing);
        dc.DrawEllipse(Brush(0x40000000), null, new Point(at.X, at.Y + 19 * k), 14 * k, 4.5 * k);
        switch (e.Kind)
        {
            case EnemyKind.Puffle:
            {
                // A pink balloon, bobbing, its knot and string below.
                var c = new Point(at.X, at.Y - 4 * k + Math.Sin(t * 3) * 2.5 * k);
                dc.DrawLine(Pen(0xFF8A6070, 1.5), new Point(c.X, c.Y + 16 * k), new Point(c.X + Math.Sin(t * 4) * 3 * k, c.Y + 24 * k));
                Poly(dc, dark, outline, 1.2, new Point(c.X - 3 * k, c.Y + 17 * k), new Point(c.X + 3 * k, c.Y + 17 * k), new Point(c.X, c.Y + 13 * k));
                dc.DrawEllipse(Brush(col), Pen(outline, 1.8), c, 16 * k, 16.5 * k);
                dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.55)), null, new Point(c.X - 6 * k, c.Y - 7 * k), 4.5 * k, 3.2 * k);
                Eyes(dc, c, fx, fy, k, 5.5, 4.4, hurt);
                dc.DrawLine(Pen(outline, 1.8), new Point(c.X - 4 * k, c.Y + 7 * k), new Point(c.X, c.Y + 9 * k));
                dc.DrawLine(Pen(outline, 1.8), new Point(c.X, c.Y + 9 * k), new Point(c.X + 4 * k, c.Y + 7 * k));
                break;
            }
            case EnemyKind.Sprout:
            {
                // A green bulb with a leafy sprout and stubby legs, cross eyebrows.
                double step = Math.Sin(t * 12) * 3 * k;
                var c = new Point(at.X, at.Y);
                dc.DrawEllipse(Brush(dark), Pen(outline, 1.4), new Point(c.X - 6 * k, c.Y + 16 * k + step * 0.3), 5 * k, 3.5 * k);
                dc.DrawEllipse(Brush(dark), Pen(outline, 1.4), new Point(c.X + 6 * k, c.Y + 16 * k - step * 0.3), 5 * k, 3.5 * k);
                Poly(dc, col, outline, 1.8, new Point(c.X, c.Y - 18 * k), new Point(c.X + 9 * k, c.Y - 10 * k), new Point(c.X + 15 * k, c.Y + 2 * k),
                    new Point(c.X + 11 * k, c.Y + 13 * k), new Point(c.X - 11 * k, c.Y + 13 * k), new Point(c.X - 15 * k, c.Y + 2 * k), new Point(c.X - 9 * k, c.Y - 10 * k));
                dc.DrawLine(Pen(dark, 1.6), new Point(c.X - 5 * k, c.Y - 12 * k), new Point(c.X - 7 * k, c.Y + 10 * k));
                dc.DrawLine(Pen(dark, 1.6), new Point(c.X + 5 * k, c.Y - 12 * k), new Point(c.X + 7 * k, c.Y + 10 * k));
                double leaf = Math.Sin(t * 5) * 3 * k;
                Poly(dc, 0xFF9AF070, outline, 1.4, new Point(c.X, c.Y - 17 * k), new Point(c.X - 9 * k + leaf, c.Y - 27 * k), new Point(c.X - 2 * k, c.Y - 22 * k));
                Poly(dc, 0xFF9AF070, outline, 1.4, new Point(c.X, c.Y - 17 * k), new Point(c.X + 8 * k + leaf, c.Y - 28 * k), new Point(c.X + 3 * k, c.Y - 21 * k));
                Eyes(dc, new Point(c.X, c.Y - 1 * k), fx, fy, k, 5, 4, hurt);
                dc.DrawLine(Pen(outline, 2), new Point(c.X - 9 * k, c.Y - 8 * k), new Point(c.X - 2 * k, c.Y - 5 * k));
                dc.DrawLine(Pen(outline, 2), new Point(c.X + 9 * k, c.Y - 8 * k), new Point(c.X + 2 * k, c.Y - 5 * k));
                break;
            }
            case EnemyKind.Dumpling:
            {
                // A blue dumpling, squashing as it bounces, with pleats along the top.
                double sq = Math.Sin(t * 8);
                double w = 17 * k * (1 + sq * 0.08), hgt = 14 * k * (1 - sq * 0.08);
                var c = new Point(at.X, at.Y + 4 * k - Math.Max(0, sq) * 4 * k);
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(c.X - w, c.Y + hgt * 0.7), true);
                    ctx.CubicBezierTo(new Point(c.X - w * 1.1, c.Y - hgt * 1.4), new Point(c.X + w * 1.1, c.Y - hgt * 1.4), new Point(c.X + w, c.Y + hgt * 0.7));
                    ctx.LineTo(new Point(c.X - w, c.Y + hgt * 0.7));
                    ctx.EndFigure(true);
                }
                dc.DrawGeometry(Brush(col), Pen(outline, 1.8), g);
                for (int i = -2; i <= 2; i++)
                    dc.DrawLine(Pen(dark, 1.6), new Point(c.X + i * 4 * k, c.Y - hgt * 0.95 + Math.Abs(i) * 1.6 * k), new Point(c.X + i * 5 * k, c.Y - hgt * 0.55 + Math.Abs(i) * 1.6 * k));
                dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.4)), null, new Point(c.X - 8 * k, c.Y - 4 * k), 3.5 * k, 2.5 * k);
                Eyes(dc, new Point(c.X, c.Y + 1 * k), fx, fy, k, 5.5, 3.6, hurt);
                dc.DrawEllipse(Brush(0xFFFF8AB0), null, new Point(c.X - 11 * k, c.Y + 6 * k), 2.6 * k, 1.8 * k);
                dc.DrawEllipse(Brush(0xFFFF8AB0), null, new Point(c.X + 11 * k, c.Y + 6 * k), 2.6 * k, 1.8 * k);
                break;
            }
            case EnemyKind.Grinner:
            {
                // An orange disc with a spinning rim of teeth and a huge grin.
                var c = new Point(at.X, at.Y - 2 * k);
                double spin = t * 4;
                var pts = Enumerable.Range(0, 24).Select(i =>
                {
                    double a = spin + i * Math.PI / 12, r = (i % 2 == 0 ? 19 : 15) * k;
                    return new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
                }).ToArray();
                Poly(dc, dark, outline, 1.4, pts);
                dc.DrawEllipse(Brush(col), Pen(outline, 1.6), c, 15 * k, 15 * k);
                dc.DrawEllipse(Brush(light), null, new Point(c.X - 5 * k, c.Y - 6 * k), 4 * k, 3 * k);
                Eyes(dc, new Point(c.X, c.Y - 4 * k), fx, fy, k, 5, 3.6, hurt);
                var mouth = new Rect(c.X - 9 * k, c.Y + 2 * k, 18 * k, 8 * k);
                dc.DrawRectangle(Brush(0xFF3A0A10), Pen(outline, 1.4), mouth, 4 * k);
                for (int i = 0; i < 4; i++) dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(mouth.X + 2 * k + i * 4 * k, mouth.Y + 1, 3 * k, 3 * k));
                break;
            }
            case EnemyKind.Wisp:
            {
                // A pale ghost, see-through, its tail wavering.
                var c = new Point(at.X, at.Y - 4 * k + Math.Sin(t * 2.5) * 3 * k);
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(c.X - 15 * k, c.Y + 14 * k), true);
                    ctx.LineTo(new Point(c.X - 15 * k, c.Y));
                    ctx.ArcTo(new Point(c.X + 15 * k, c.Y), new Size(15 * k, 15 * k), 0, false, SweepDirection.Clockwise);
                    ctx.LineTo(new Point(c.X + 15 * k, c.Y + 14 * k));
                    for (int i = 0; i < 5; i++)
                    {
                        double x = c.X + 15 * k - (i + 1) * 6 * k, wv = Math.Sin(t * 8 + i) * 3 * k;
                        ctx.LineTo(new Point(x + 3 * k, c.Y + (i % 2 == 0 ? 19 : 13) * k + wv));
                        ctx.LineTo(new Point(x, c.Y + 14 * k));
                    }
                    ctx.EndFigure(true);
                }
                dc.DrawGeometry(Brush(Alpha(col, 0.82)), Pen(Alpha(0xFF6070A0, 0.9), 1.6), g);
                dc.DrawEllipse(Brush(0xFF2A2050), null, new Point(c.X - 5.5 * k + fx * 2 * k, c.Y - 1 * k + fy * 2 * k), 3.4 * k, 5 * k);
                dc.DrawEllipse(Brush(0xFF2A2050), null, new Point(c.X + 5.5 * k + fx * 2 * k, c.Y - 1 * k + fy * 2 * k), 3.4 * k, 5 * k);
                dc.DrawEllipse(Brush(0xFF2A2050), null, new Point(c.X + fx * 2 * k, c.Y + 7 * k), 2.6 * k, 3 * k);
                break;
            }
            case EnemyKind.Gloop:
            {
                // A purple slime with one big eye and drips.
                double sq = Math.Sin(t * 5) * 0.08;
                var c = new Point(at.X, at.Y + 2 * k);
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(c.X - 17 * k * (1 + sq), c.Y + 14 * k), true);
                    ctx.CubicBezierTo(new Point(c.X - 18 * k, c.Y - 8 * k), new Point(c.X - 6 * k, c.Y - 26 * k * (1 - sq)), new Point(c.X, c.Y - 22 * k * (1 - sq)));
                    ctx.CubicBezierTo(new Point(c.X + 6 * k, c.Y - 26 * k * (1 - sq)), new Point(c.X + 18 * k, c.Y - 8 * k), new Point(c.X + 17 * k * (1 + sq), c.Y + 14 * k));
                    ctx.EndFigure(true);
                }
                dc.DrawGeometry(Brush(col), Pen(outline, 1.8), g);
                dc.DrawEllipse(Brush(dark), null, new Point(c.X - 9 * k, c.Y + 16 * k), 4 * k, 2.4 * k + Math.Abs(Math.Sin(t * 3)) * 2 * k);
                dc.DrawEllipse(Brush(dark), null, new Point(c.X + 8 * k, c.Y + 15 * k), 3 * k, 2 * k + Math.Abs(Math.Sin(t * 3 + 1)) * 2 * k);
                dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.35)), null, new Point(c.X - 7 * k, c.Y - 8 * k), 3 * k, 5 * k);
                dc.DrawEllipse(Brush(0xFFFFFFFF), Pen(outline, 1.4), new Point(c.X, c.Y - 3 * k), 8 * k, 8 * k);
                if (hurt) { dc.DrawLine(Pen(outline, 2), new Point(c.X - 4 * k, c.Y - 7 * k), new Point(c.X + 4 * k, c.Y + 1 * k)); dc.DrawLine(Pen(outline, 2), new Point(c.X + 4 * k, c.Y - 7 * k), new Point(c.X - 4 * k, c.Y + 1 * k)); }
                else dc.DrawEllipse(Brush(0xFF201030), null, new Point(c.X + fx * 3 * k, c.Y - 3 * k + fy * 3 * k), 3.6 * k, 3.6 * k);
                break;
            }
            case EnemyKind.Dart:
            {
                // A yellow arrowhead pointing the way it goes, one eye, speed lines behind.
                var c = new Point(at.X, at.Y - 2 * k);
                double ax = fx, ay = fy;
                if (ax == 0 && ay == 0) ax = 1;
                double px = -ay, py = ax;
                var nose = new Point(c.X + ax * 18 * k, c.Y + ay * 18 * k);
                var l = new Point(c.X - ax * 14 * k + px * 15 * k, c.Y - ay * 14 * k + py * 15 * k);
                var r = new Point(c.X - ax * 14 * k - px * 15 * k, c.Y - ay * 14 * k - py * 15 * k);
                var notch = new Point(c.X - ax * 6 * k, c.Y - ay * 6 * k);
                for (int i = 1; i <= 3; i++)
                    dc.DrawLine(Pen(Alpha(col, 0.5), 2), new Point(c.X - ax * (16 + i * 5) * k + px * (i - 2) * 6 * k, c.Y - ay * (16 + i * 5) * k + py * (i - 2) * 6 * k),
                        new Point(c.X - ax * (24 + i * 5) * k + px * (i - 2) * 6 * k, c.Y - ay * (24 + i * 5) * k + py * (i - 2) * 6 * k));
                Poly(dc, col, outline, 1.8, nose, l, notch, r);
                Poly(dc, light, null, 0, nose, new Point((nose.X + l.X) / 2, (nose.Y + l.Y) / 2), c);
                dc.DrawEllipse(Brush(0xFFFFFFFF), Pen(outline, 1.2), new Point(c.X + ax * 3 * k, c.Y + ay * 3 * k), 5 * k, 5 * k);
                dc.DrawEllipse(Brush(0xFF201010), null, new Point(c.X + ax * 5 * k, c.Y + ay * 5 * k), 2.4 * k, 2.4 * k);
                break;
            }
            case EnemyKind.Whirl:
            {
                // A spinning whirlwind with two angry eyes.
                var c = new Point(at.X, at.Y - 3 * k);
                double spin = t * 9;
                for (int i = 0; i < 3; i++)
                {
                    double a = spin + i * Math.PI * 2 / 3;
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(c, true);
                        ctx.QuadraticBezierTo(new Point(c.X + Math.Cos(a) * 22 * k, c.Y + Math.Sin(a) * 22 * k), new Point(c.X + Math.Cos(a + 1.6) * 18 * k, c.Y + Math.Sin(a + 1.6) * 18 * k));
                        ctx.QuadraticBezierTo(new Point(c.X + Math.Cos(a + 0.9) * 10 * k, c.Y + Math.Sin(a + 0.9) * 10 * k), c);
                        ctx.EndFigure(true);
                    }
                    dc.DrawGeometry(Brush(i == 0 ? col : i == 1 ? Shade(col, 0.75) : light), Pen(outline, 1.2), g);
                }
                dc.DrawEllipse(Brush(dark), Pen(outline, 1.4), c, 8 * k, 8 * k);
                Eyes(dc, c, fx, fy, k, 3.6, 2.8, hurt);
                dc.DrawLine(Pen(outline, 1.8), new Point(c.X - 7 * k, c.Y - 6 * k), new Point(c.X - 1 * k, c.Y - 3 * k));
                dc.DrawLine(Pen(outline, 1.8), new Point(c.X + 7 * k, c.Y - 6 * k), new Point(c.X + 1 * k, c.Y - 3 * k));
                break;
            }
        }
    }

    /// <summary>Two eyes looking the way it goes (crosses, when hurt).</summary>
    private static void Eyes(DrawingContext dc, Point c, int fx, int fy, double k, double apart, double size, bool hurt)
    {
        uint outline = 0xFF1A1424;
        foreach (int s in new[] { -1, 1 })
        {
            var e = new Point(c.X + s * apart * k, c.Y - 2 * k);
            if (hurt)
            {
                dc.DrawLine(Pen(outline, 2), new Point(e.X - size * 0.7 * k, e.Y - size * 0.7 * k), new Point(e.X + size * 0.7 * k, e.Y + size * 0.7 * k));
                dc.DrawLine(Pen(outline, 2), new Point(e.X - size * 0.7 * k, e.Y + size * 0.7 * k), new Point(e.X + size * 0.7 * k, e.Y - size * 0.7 * k));
                continue;
            }
            dc.DrawEllipse(Brush(0xFFFFFFFF), Pen(outline, 1.2), e, size * k, size * 1.15 * k);
            dc.DrawEllipse(Brush(0xFF101018), null, new Point(e.X + fx * size * 0.45 * k, e.Y + fy * size * 0.45 * k + 0.5 * k), size * 0.5 * k, size * 0.55 * k);
        }
    }

    // ───────────────────────────── power-ups, the door, trophies ─────────────────────────────

    /// <summary>A power-up: a bevelled tile with its picture, its edge flashing.</summary>
    private void DrawPowerUp(DrawingContext dc, Point at, PowerUp p, double scale)
    {
        double k = scale, s = 21 * k;
        var r = new Rect(at.X - s, at.Y - s, 2 * s, 2 * s);
        uint bg = p switch
        {
            PowerUp.Bombs => 0xFF2A4AC0, PowerUp.Fire => 0xFFC02A1A, PowerUp.Speed => 0xFF1A9A6A, PowerUp.Remote => 0xFF8A2AB0,
            PowerUp.WallPass => 0xFF8A5A2A, PowerUp.BombPass => 0xFF3A6A9A, PowerUp.FlamePass => 0xFFB0602A, _ => 0xFF404858,
        };
        bool flash = scale >= 1 && (int)(_clock * 6) % 2 == 0;
        dc.FillRectangle(Brush(0xFF10121C), r.Inflate(1.5), 6 * k);
        dc.FillRectangle(Brush(bg), r, 5 * k);
        dc.FillRectangle(Brush(Shade(bg, 1.4)), new Rect(r.X + 2, r.Y + 2, r.Width - 4, 5 * k), 3 * k);
        dc.DrawRectangle(null, Pen(flash ? 0xFFFFFFFF : 0xFFFFD040, 2 * Math.Max(0.6, k)), r.Deflate(2 * k), 4 * k);
        var c = r.Center;
        switch (p)
        {
            case PowerUp.Bombs:
                dc.DrawEllipse(Brush(0xFF14182C), Pen(0xFF000000, 1), new Point(c.X - 1 * k, c.Y + 2 * k), 9.5 * k, 9.5 * k);
                dc.DrawEllipse(Brush(0xD0FFFFFF), null, new Point(c.X - 4.5 * k, c.Y - 1.5 * k), 2.4 * k, 1.8 * k);
                dc.DrawLine(Pen(0xFFD8C090, 2 * k), new Point(c.X + 5 * k, c.Y - 6 * k), new Point(c.X + 9 * k, c.Y - 11 * k));
                DrawSpark(dc, new Point(c.X + 9.5 * k, c.Y - 11.5 * k), 4 * k, 0xFFFFE070);
                break;
            case PowerUp.Fire:
                FlameIcon(dc, c, k);
                break;
            case PowerUp.Speed:
            {
                // A winged boot.
                Poly(dc, 0xFFF4F6FF, 0xFF101018, 1.2, new Point(c.X - 6 * k, c.Y - 10 * k), new Point(c.X + 1 * k, c.Y - 10 * k), new Point(c.X + 1 * k, c.Y + 1 * k),
                    new Point(c.X + 10 * k, c.Y + 4 * k), new Point(c.X + 10 * k, c.Y + 9 * k), new Point(c.X - 7 * k, c.Y + 9 * k));
                dc.DrawLine(Pen(0xFFE02A3A, 2.4 * k), new Point(c.X - 6 * k, c.Y + 9 * k), new Point(c.X + 10 * k, c.Y + 9 * k));
                Poly(dc, 0xFFFFE070, 0xFF101018, 1, new Point(c.X - 6 * k, c.Y - 6 * k), new Point(c.X - 14 * k, c.Y - 11 * k), new Point(c.X - 12 * k, c.Y - 6 * k), new Point(c.X - 15 * k, c.Y - 4 * k), new Point(c.X - 6 * k, c.Y - 1 * k));
                break;
            }
            case PowerUp.Remote:
            {
                // A detonator: a box with a T-handled plunger, its wire running off to a spark.
                dc.DrawRectangle(Brush(0xFFE8A030), Pen(0xFF101018, 1.4), new Rect(c.X - 11 * k, c.Y - 1 * k, 17 * k, 13 * k), 2);
                dc.FillRectangle(Brush(0xFFB0701A), new Rect(c.X - 11 * k, c.Y + 8 * k, 17 * k, 4 * k));
                dc.FillRectangle(Brush(0xFF101018), new Rect(c.X - 4 * k, c.Y - 11 * k, 3 * k, 10 * k));
                dc.DrawRectangle(Brush(0xFFF4F6FF), Pen(0xFF101018, 1.2), new Rect(c.X - 10 * k, c.Y - 14 * k, 15 * k, 4.5 * k), 1.5);
                dc.DrawLine(Pen(0xFF101018, 1.6 * k), new Point(c.X + 6 * k, c.Y + 4 * k), new Point(c.X + 11 * k, c.Y - 2 * k));
                DrawSpark(dc, new Point(c.X + 12 * k, c.Y - 4 * k), 5 * k, 0xFFFFE070);
                break;
            }
            case PowerUp.WallPass:
            {
                // A ghostly bomber passing through bricks.
                for (int i = 0; i < 3; i++)
                {
                    double y = c.Y - 9 * k + i * 6.5 * k;
                    dc.FillRectangle(Brush(0xFFC9733A), new Rect(c.X - 12 * k + (i % 2) * 4 * k, y, 9 * k, 5 * k));
                    dc.FillRectangle(Brush(0xFFC9733A), new Rect(c.X + (i % 2) * 4 * k - 1 * k, y, 9 * k, 5 * k));
                }
                dc.DrawEllipse(Brush(0xB0F4F6FF), Pen(0xFFF4F6FF, 1), new Point(c.X, c.Y - 1 * k), 7 * k, 7 * k);
                dc.FillRectangle(Brush(0xB01A2244), new Rect(c.X - 5 * k, c.Y - 3 * k, 10 * k, 4.5 * k), 2);
                break;
            }
            case PowerUp.BombPass:
            {
                dc.DrawEllipse(Brush(0xFF14182C), null, new Point(c.X, c.Y + 4 * k), 8 * k, 8 * k);
                dc.DrawLine(Pen(0xFFFFFFFF, 2.6 * k), new Point(c.X - 11 * k, c.Y - 3 * k), new Point(c.X + 9 * k, c.Y - 3 * k));
                Poly(dc, 0xFFFFFFFF, null, 0, new Point(c.X + 12 * k, c.Y - 3 * k), new Point(c.X + 6 * k, c.Y - 8 * k), new Point(c.X + 6 * k, c.Y + 2 * k));
                break;
            }
            case PowerUp.FlamePass:
            {
                // A shield with a flame on it.
                Poly(dc, 0xFFE8ECF8, 0xFF101018, 1.2, new Point(c.X - 10 * k, c.Y - 11 * k), new Point(c.X + 10 * k, c.Y - 11 * k), new Point(c.X + 9 * k, c.Y + 2 * k), new Point(c.X, c.Y + 12 * k), new Point(c.X - 9 * k, c.Y + 2 * k));
                FlameIcon(dc, new Point(c.X, c.Y + 1 * k), k * 0.6);
                break;
            }
            case PowerUp.Mystery:
            {
                double hue = _clock * 2;
                Text(dc, "?", new Point(c.X, c.Y - 1 * k), 36 * k, RainbowColour(hue));
                break;
            }
        }
    }

    private static void FlameIcon(DrawingContext dc, Point c, double k)
    {
        Poly(dc, 0xFFFF6A10, 0xFF601000, 1, new Point(c.X, c.Y - 13 * k), new Point(c.X + 9 * k, c.Y), new Point(c.X + 7 * k, c.Y + 9 * k), new Point(c.X - 7 * k, c.Y + 9 * k), new Point(c.X - 9 * k, c.Y - 1 * k), new Point(c.X - 4 * k, c.Y - 5 * k));
        Poly(dc, 0xFFFFC030, null, 0, new Point(c.X + 1 * k, c.Y - 5 * k), new Point(c.X + 5 * k, c.Y + 3 * k), new Point(c.X + 3 * k, c.Y + 8 * k), new Point(c.X - 4 * k, c.Y + 8 * k), new Point(c.X - 5 * k, c.Y + 2 * k));
        dc.DrawEllipse(Brush(0xFFFFF4C0), null, new Point(c.X, c.Y + 5 * k), 2.6 * k, 3 * k);
    }

    /// <summary>The exit door: a stone gateway with an arched steel door; once open, light pours out and an arrow beckons.</summary>
    private void DrawDoor(DrawingContext dc, Point at, bool open)
    {
        double s = Field.Cell / 2;
        var r = new Rect(at.X - s + 1, at.Y - s + 1, 2 * s - 2, 2 * s - 2);
        dc.FillRectangle(Brush(0xFF4A5268), r, 5);
        dc.FillRectangle(Brush(0xFF6A748E), new Rect(r.X + 2, r.Y + 2, r.Width - 4, 6), 3);
        dc.DrawRectangle(null, Pen(0xFF1E2230, 1.5), r, 5);
        var arch = new StreamGeometry();
        double w = s - 8;
        using (var ctx = arch.Open())
        {
            ctx.BeginFigure(new Point(at.X - w, r.Bottom - 2), true);
            ctx.LineTo(new Point(at.X - w, at.Y - 3));
            ctx.ArcTo(new Point(at.X + w, at.Y - 3), new Size(w, w), 0, false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(at.X + w, r.Bottom - 2));
            ctx.EndFigure(true);
        }
        if (open)
        {
            double glow = 0.7 + 0.3 * Math.Sin(_clock * 6);
            dc.DrawEllipse(Brush(Alpha(0xFFFFF4A0, 0.3 * glow)), null, at, s + 16, s + 16);
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4 + _clock * 0.8;
                dc.DrawLine(Pen(Alpha(0xFFFFF0A0, 0.35 * glow), 4), new Point(at.X + Math.Cos(a) * (s - 4), at.Y + Math.Sin(a) * (s - 4)), new Point(at.X + Math.Cos(a) * (s + 14), at.Y + Math.Sin(a) * (s + 14)));
            }
            dc.DrawGeometry(Brush(Mix(0xFFFFE070, 0xFFFFFFFF, glow)), Pen(0xFF1E2230, 1.8), arch);
            for (int k = 0; k < 5; k++)
            {
                double a = _clock * 3 + k * Math.PI * 2 / 5;
                DrawSpark(dc, new Point(at.X + Math.Cos(a) * 11, at.Y + 4 + Math.Sin(a) * 11), 3.5, 0xFFFF9A20);
            }
            double bounce = Math.Abs(Math.Sin(_clock * 5)) * 6;
            Poly(dc, 0xFFFF4A5A, 0xFF401010, 1.5, new Point(at.X, r.Top - 4 + bounce), new Point(at.X - 10, r.Top - 16 + bounce), new Point(at.X - 4, r.Top - 16 + bounce),
                new Point(at.X - 4, r.Top - 26 + bounce), new Point(at.X + 4, r.Top - 26 + bounce), new Point(at.X + 4, r.Top - 16 + bounce), new Point(at.X + 10, r.Top - 16 + bounce));
        }
        else
        {
            dc.DrawGeometry(Brush(0xFF2A3A64), Pen(0xFF10141E, 1.8), arch);
            dc.DrawLine(Pen(0xFF4A6AA8, 2), new Point(at.X, at.Y - 16), new Point(at.X, r.Bottom - 3));
            for (int k = 0; k < 3; k++)
                dc.DrawLine(Pen(0xFF4A6AA8, 1.5), new Point(at.X - w + 2, at.Y - 2 + k * 7), new Point(at.X + w - 2, at.Y - 2 + k * 7));
            dc.DrawEllipse(Brush(0xFFFFD040), Pen(0xFF6A4A00, 1), new Point(at.X + 6, at.Y + 8), 2.4, 2.4);
        }
        Text(dc, "EXIT", new Point(at.X, r.Top + 7), 15, open ? 0xFFFF4A5A : 0xFFFFD040, center: true, shadow: false);
    }

    /// <summary>A trophy cup: gold when won, a dim outline when still to win.</summary>
    private static void DrawTrophy(DrawingContext dc, Point at, bool won, double scale)
    {
        double k = scale;
        uint gold = won ? 0xFFFFC830 : 0xFF3A4060, edge = won ? 0xFF8A5A00 : 0xFF2A3048, shine = won ? 0xFFFFF0A0 : 0xFF4A5070;
        dc.DrawEllipse(null, Pen(edge, 3 * k), new Point(at.X - 11 * k, at.Y - 7 * k), 5 * k, 5 * k);
        dc.DrawEllipse(null, Pen(gold, 1.8 * k), new Point(at.X - 11 * k, at.Y - 7 * k), 5 * k, 5 * k);
        dc.DrawEllipse(null, Pen(edge, 3 * k), new Point(at.X + 11 * k, at.Y - 7 * k), 5 * k, 5 * k);
        dc.DrawEllipse(null, Pen(gold, 1.8 * k), new Point(at.X + 11 * k, at.Y - 7 * k), 5 * k, 5 * k);
        Poly(dc, gold, edge, 1.4 * k, new Point(at.X - 12 * k, at.Y - 14 * k), new Point(at.X + 12 * k, at.Y - 14 * k), new Point(at.X + 8 * k, at.Y + 1 * k), new Point(at.X - 8 * k, at.Y + 1 * k));
        dc.DrawRectangle(Brush(gold), Pen(edge, 1.2 * k), new Rect(at.X - 2.5 * k, at.Y + 1 * k, 5 * k, 6 * k));
        dc.DrawRectangle(Brush(gold), Pen(edge, 1.2 * k), new Rect(at.X - 9 * k, at.Y + 7 * k, 18 * k, 5 * k), 1.5 * k);
        dc.FillRectangle(Brush(shine), new Rect(at.X - 8 * k, at.Y - 12 * k, 3 * k, 9 * k), 1.5 * k);
        if (won) dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(at.X + 3 * k, at.Y - 9 * k), 1.6 * k, 1.6 * k);
    }
}
