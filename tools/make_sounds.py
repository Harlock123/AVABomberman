#!/usr/bin/env python3
"""Synthesises AVABomberman's sound effects and tunes into ../src/AVABomberman/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices. The tune
is an arrangement of the galop that ends Rossini's William Tell Overture (1829), which is in the public domain; the jingles
are new. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVABomberman", "Sounds")
RNG = random.Random(1983)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "thin":
        return 1.0 if p < 0.125 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def lowpass(samples, amount):
    """A one-pole low-pass: 0 leaves it alone, nearer 1 muffles it."""
    out, y = [], 0.0
    for s in samples:
        y = y * amount + s * (1 - amount)
        out.append(y)
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def line(bpm, notes, kind="pulse", gain=0.55, release=0.6, legato=1.0):
    """A voice: (note, beats) pairs; '-' is a rest."""
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
        else:
            sound = tone(d * legato, hz(note), kind=kind, release=release, gain=gain)
            out.extend(sound + silence(d - len(sound) / RATE))
    return out


def tune(bpm, lead, bass, kind="pulse", bass_kind="tri", lead_gain=0.55, bass_gain=0.6):
    return mix(line(bpm, lead, kind, lead_gain, 0.6), line(bpm, bass, bass_kind, bass_gain, 0.3, legato=0.9))


# ───────────────────── William Tell Overture, finale (Rossini, 1829) ─────────────────────
# The galop, put into C, 2/4 time: a beat is a quarter note, so .25 is a sixteenth. Its galloping
# da-da-dum hook over an oom-pah left hand: the root low on the beat, the chord answering on the off-beat.

GALLOP = [("G5", .25), ("G5", .25), ("G5", .5), ("G5", .25), ("G5", .25), ("G5", .5)]
STRAIN = (
    GALLOP + [("G5", .25), ("G5", .25), ("C6", .5), ("D6", .5), ("E6", .5)]
    + GALLOP + [("G5", .25), ("G5", .25), ("E6", .5), ("C6", .5), ("E6", .5)]
    + GALLOP + [("G5", .25), ("G5", .25), ("C6", .5), ("D6", .5), ("E6", .5)]
    + [("C6", .5), ("E6", .5), ("G6", 1.0), ("D6", .5), ("B5", .5), ("C6", 1.0)]
)


def oompah(chords):
    """Two beats a bar: root, chord, fifth, chord, in eighths."""
    out = []
    for root, fifth, chord in chords:
        out += [(root, .5), (chord, .5), (fifth, .5), (chord, .5)]
    return out


C, G7 = ("C3", "G2", "E4"), ("G2", "D3", "F4")
STRAIN_BASS = oompah([C, C, C, C, C, C, C, G7])


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))

    # Walking: a soft pat. Laying a bomb: a dull thunk. Its fuse: a sizzle.
    write("Step.wav", lowpass(tone(0.045, 900, 500, "noise", release=1.5), 0.6), 0.25)
    write("BombPlace.wav", mix(tone(0.14, 210, 80, "sine", release=1.2), tone(0.03, 2500, 900, "noise", release=1.0, gain=0.4)), 0.5)
    fuse = tone(1.0, 9000, 6000, "noise", release=0.6, gain=0.5)
    crackle = [s * (1.0 if RNG.random() < 0.04 else 0.25) for s in tone(1.0, 4000, 3000, "noise", release=0.8)]
    write("Fuse.wav", mix(fuse, crackle), 0.22)

    # The blast: a crack, a roar of low noise and a falling boom, with debris pattering after.
    crack = tone(0.05, 7000, 2000, "noise", release=0.5)
    roar = lowpass(tone(1.1, 1600, 120, "noise", release=1.6), 0.55)
    boom = tone(0.7, 110, 32, "sine", release=1.1, gain=1.2)
    patter = [s * (1.0 if RNG.random() < 0.02 else 0.0) for s in tone(1.1, 3000, 800, "noise", release=1.0, gain=0.4)]
    write("Explosion.wav", mix(crack, roar, boom, patter), 0.8)
    rubble = [s * (1.0 if (i // 300) % 3 else 0.3) for i, s in enumerate(lowpass(tone(0.35, 2200, 500, "noise", release=1.0), 0.35))]
    write("Crumble.wav", mix(rubble, tone(0.12, 160, 70, "square", release=1.2, gain=0.25)), 0.4)
    write("WallDrop.wav", mix(tone(0.25, 90, 40, "sine", release=1.0, gain=1.2), lowpass(tone(0.2, 1200, 200, "noise", release=1.2), 0.5),
                              cat(silence(0.02), tone(0.12, 620, 600, "square", release=1.5, gain=0.25))), 0.55)

    write("PowerUp.wav", cat(*[tone(0.055, hz(n), kind="pulse", release=0.3) for n in ["C6", "E6", "G6", "C7", "E6", "G6", "C7", "E7"]],
                             tone(0.25, hz("G7"), kind="tri", release=0.7, vibrato=0.01)), 0.4)
    write("EnemyDeath.wav", cat(tone(0.08, 1400, 2200, "square", release=0.4, gain=0.6), tone(0.32, 1800, 220, "pulse", release=0.8, vibrato=0.06, vib_rate=30)), 0.38)
    write("PlayerDeath.wav", cat(tone(0.25, 1600, 400, "square", release=0.5, vibrato=0.05, vib_rate=24),
                                 *[tone(0.14, hz(n), kind="tri", release=0.4) for n in ["A5", "G#5", "G5", "F#5", "F5", "E5", "D#5"]],
                                 tone(0.6, hz("D5"), kind="tri", release=0.9)), 0.48)
    write("ExitOpen.wav", cat(*[tone(0.07, hz(n), kind="tri", release=0.4) for n in ["G5", "C6", "E6", "G6", "C7", "E7"]],
                              mix(tone(0.7, hz("G7"), kind="tri", release=0.8, vibrato=0.006), tone(0.7, hz("C7"), kind="sine", release=0.8, gain=0.5))), 0.42)
    write("Spawn.wav", mix(tone(0.6, 120, 380, "saw", release=0.6, vibrato=0.15, vib_rate=14), tone(0.6, 240, 760, "square", release=0.8, gain=0.3)), 0.38)
    write("Select.wav", cat(tone(0.04, hz("A5"), kind="pulse", release=0.3), tone(0.06, hz("E6"), kind="pulse", release=0.5)), 0.3)
    write("HurryUp.wav", cat(*[tone(0.12, f, kind="square", release=0.15) for f in [1568, 1175, 1568, 1175, 1568, 1175, 1568, 1175]]), 0.32)

    # Jingles (new): the stage coming up, the stage cleared, a battle won, the game lost.
    write("StageStart.wav", tune(150, [("G5", .5), ("C6", .5), ("E6", .5), ("G6", .5), ("E6", .25), ("G6", .25), ("C7", 1.5)],
                                 [("C3", 1), ("G2", 1), ("C3", .5), ("G2", .5), ("C3", 1)]), 0.45)
    write("StageClear.wav", tune(168, [("C6", .25), ("C6", .25), ("C6", .25), ("C6", .75), ("G#5", .75), ("A#5", .75), ("C6", .5), ("A#5", .25), ("C6", 2)],
                                 [("C3", 1), ("G#2", 1), ("A#2", 1), ("C3", 2)]), 0.45)
    write("BattleWin.wav", tune(150, [("G5", .25), ("C6", .25), ("E6", .25), ("G6", .75), ("E6", .25), ("G6", 1), ("A6", .5), ("G6", .5), ("F6", .5), ("E6", .5),
                                      ("D6", .5), ("G6", .5), ("C7", 2)],
                                [("C3", 1), ("E3", 1), ("F3", 1), ("D3", 1), ("G2", 1), ("C3", 2)]), 0.45)
    write("GameOver.wav", tune(84, [("G5", 1), ("E5", 1), ("C5", 1), ("D5", .5), ("C5", .5), ("B4", 1), ("C5", 2)],
                               [("C3", 2), ("A2", 1), ("G2", 2), ("C3", 2)], kind="tri"), 0.45)

    # The William Tell galop: a brisk chip arrangement loops through each stage (twice round a loop), and the
    # title has it at an easier canter on a softer, music-box voice.
    write("Music.wav", tune(132, STRAIN + STRAIN, STRAIN_BASS + STRAIN_BASS, kind="pulse", lead_gain=0.5), 0.42)
    write("TitleTune.wav", tune(108, STRAIN + STRAIN, STRAIN_BASS + STRAIN_BASS, kind="tri", bass_kind="tri", lead_gain=0.6, bass_gain=0.45), 0.45)


if __name__ == "__main__":
    main()
