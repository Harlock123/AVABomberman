using AVABomberman.Engine;

namespace AVABomberman.Tests;

/// <summary>
/// How the computer bombers get on, for tuning: BOMBER_PROBE=story plays the story stage by stage, BOMBER_PROBE=battle
/// runs four-way computer battles. Set it to run (it does nothing otherwise).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("BOMBER_PROBE");
        if (mode is null) return;
        if (mode == "battle") Battles();
        else Story();
    }

    private static GameEngine NewGame(int seed) =>
        new(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "bomber-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };

    private void Story()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = NewGame(seed);
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var stages = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int stage = 1, t = 0;
            double start = 0;
            for (; t < 60 * 1200 && g.Stage <= 10 && g.State == GameState.Playing; t++)
            {
                g.Tick(g.Pilot.Decide(g, g.Player));
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"S{g.Stage}:{g.Cause}"] = deaths.GetValueOrDefault($"S{g.Stage}:{g.Cause}") + 1;
                if (g.Stage != stage) { stages.Add($"S{stage}:{(t - start) / 60:0}s"); stage = g.Stage; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: stage {g.Stage}{(g.IsBonus ? " bonus" : "")} after {t / 60:0}s score {g.Score} | {string.Join(" ", stages)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }

    private void Battles()
    {
        var wins = new int[4];
        int draws = 0;
        double total = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var g = NewGame(seed);
            g.GoBattle(Slot.Computer, Slot.Computer, Slot.Computer, Slot.Computer);
            int t = 0;
            var died = new List<string>();
            var alive = g.Bombers.ToDictionary(b => b, _ => true);
            for (; t < 60 * 200 && g.Phase != Phase.RoundOver; t++)
            {
                g.Idle(1);
                foreach (var b in g.Bombers.Where(b => alive[b] && !b.Alive))
                {
                    alive[b] = false;
                    died.Add($"P{b.Index + 1}@{t / 60}s:{g.Cause}:{b.Pilot.Doing}");
                }
            }
            total += t / 60.0;
            if (g.Winner is { } w) wins[w.Index]++;
            else draws++;
            output.WriteLine($"seed {seed}: {(g.Winner is { } x ? $"P{x.Index + 1}" : "draw")} after {t / 60}s, hurry {g.Hurry} | {string.Join(", ", died)}" +
                             $" | powers {string.Join(" ", g.Bombers.Select(b => $"{b.Bombs}/{b.Range}/{b.SpeedUps}"))}");
        }
        output.WriteLine($"wins {string.Join(" ", wins)} draws {draws}, average {total / 12:0}s");
    }
}
