using Avalonia.Input;

namespace AVABomberman.Engine;

/// <summary>
/// The keyboard and up to four gamepads boiled down to each player's controls. Down the screen is +1, as it is in
/// the engine. With one person playing, every key set works for them; with two, the keyboard is split: player 1 has
/// the arrows, Space to lay a bomb and Ctrl or Enter to detonate; player 2 has W A S D, Q to lay a bomb and E to
/// detonate. Gamepads go to players 1 to 4 in turn: A lays a bomb, B or X detonates.
/// </summary>
public sealed class InputHandler : IDisposable
{
    public const int Pads = 4;

    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager[] _pads = Enumerable.Range(0, Pads).Select(i => new ControllerManager(i)).ToArray();

    /// <summary>
    /// A key goes down. The key it was pressed as is kept against the physical key, because the release can be named
    /// differently: with the keyboard option shift:both_capslock_cancel (Omarchy's default), letting go of a Shift
    /// arrives as CapsLock, and a key that was never let go would hold its player's stick over for good.
    /// </summary>
    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up: the key it was pressed as is let go too, whatever the release is called.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    /// <summary>Let go of everything (the window lost the focus).</summary>
    public void Clear()
    {
        _held.Clear();
        _pressedAs.Clear();
    }

    public void Update()
    {
        ControllerManager.PollEvents();
        foreach (var p in _pads) p.Update();
    }

    private static readonly Key[] ArrowUp = [Key.Up, Key.NumPad8], ArrowDown = [Key.Down, Key.NumPad2], ArrowLeft = [Key.Left, Key.NumPad4], ArrowRight = [Key.Right, Key.NumPad6];
    private static readonly Key[] ArrowBomb = [Key.Space, Key.NumPad0], ArrowDetonate = [Key.LeftCtrl, Key.RightCtrl, Key.Enter];
    private static readonly Key[] WasdUp = [Key.W], WasdDown = [Key.S], WasdLeft = [Key.A], WasdRight = [Key.D];
    private static readonly Key[] WasdBomb = [Key.Q], WasdDetonate = [Key.E];

    private Controls Keys(Key[] up, Key[] down, Key[] left, Key[] right, Key[] bomb, Key[] detonate) =>
        new((Any(right) ? 1 : 0) - (Any(left) ? 1 : 0), (Any(down) ? 1 : 0) - (Any(up) ? 1 : 0), Any(bomb), Any(detonate));

    private Controls Arrows => Keys(ArrowUp, ArrowDown, ArrowLeft, ArrowRight, ArrowBomb, ArrowDetonate);
    private Controls Wasd => Keys(WasdUp, WasdDown, WasdLeft, WasdRight, WasdBomb, WasdDetonate);

    private Controls Pad(int i)
    {
        var p = _pads[i];
        int dx = p.DPadLeft || p.LeftStickX < -0.4f ? -1 : p.DPadRight || p.LeftStickX > 0.4f ? 1 : 0;
        int dy = p.DPadUp || p.LeftStickY < -0.4f ? -1 : p.DPadDown || p.LeftStickY > 0.4f ? 1 : 0;
        return new Controls(dx, dy, p.ButtonA || p.ButtonY, p.ButtonB || p.ButtonX || p.RightBumper);
    }

    /// <summary>Player <paramref name="player"/>'s controls (0 to 3), with <paramref name="humans"/> people playing.</summary>
    public Controls Player(int player, int humans)
    {
        var pad = Pad(player);
        // Keys first, so a half-pushed stick can't override them; the pad fills in what the keys leave.
        return player switch
        {
            0 when humans <= 1 => (Arrows | Wasd) | pad,
            0 => Arrows | pad,
            1 => Wasd | pad,
            _ => pad,
        };
    }

    /// <summary>Every player's controls, in order.</summary>
    public Controls[] Players(int humans)
    {
        int n = Math.Clamp(humans, 1, Pads);
        var all = new Controls[n];
        for (int i = 0; i < n; i++) all[i] = Player(i, humans);
        return all;
    }

    /// <summary>Enter (or Start on any pad).</summary>
    public bool Start => Any(Key.Enter) || _pads.Any(p => p.ButtonStart);

    public bool PadUp => _pads.Any(p => p.DPadUp || p.LeftStickY < -0.5f);
    public bool PadDown => _pads.Any(p => p.DPadDown || p.LeftStickY > 0.5f);
    public bool PadLeft => _pads.Any(p => p.DPadLeft || p.LeftStickX < -0.5f);
    public bool PadRight => _pads.Any(p => p.DPadRight || p.LeftStickX > 0.5f);
    public bool PadAccept => _pads.Any(p => p.ButtonA || p.ButtonX || p.ButtonB);
    public bool PadDone => _pads.Any(p => p.ButtonStart);
    public bool PadPause => _pads.Any(p => p.ButtonStart);
    public bool PadBack => _pads.Any(p => p.ButtonBack);

    public void Dispose()
    {
        foreach (var p in _pads) p.Dispose();
    }
}
