"""Eternal / Core boot sound (styles: key, tumbler, vault, arcade), timed to eternal-wake.svg followed by core-logo-wake.svg.
Original synthesis (numpy + scipy). `python make_boot_sound.py tumbler|vault` writes
eternal-core-boot-<style>.wav (48 kHz, 16-bit stereo)."""
import sys, wave
import numpy as np
from scipy.signal import butter, sosfilt

STYLE = sys.argv[1] if len(sys.argv) > 1 else "tumbler"
SR = 48000
T = 9.6
t = np.arange(int(SR * T)) / SR
L = np.zeros_like(t); R = np.zeros_like(t)
rng = np.random.default_rng(11)

def env(start, attack, hold, release, curve=5.0):
    e = np.zeros_like(t)
    a1 = start + attack; h1 = a1 + hold; r1 = h1 + release * 3
    m = (t >= start) & (t < a1); e[m] = (t[m] - start) / attack
    m = (t >= a1) & (t < h1); e[m] = 1
    m = (t >= h1) & (t < r1); e[m] = np.exp(-curve * (t[m] - h1) / release)
    return e

def add(sig, pan=0.0, gain=1.0):
    global L, R
    L += sig * gain * np.sqrt((1 - pan) / 2)
    R += sig * gain * np.sqrt((1 + pan) / 2)

def band(x, lo, hi):
    return sosfilt(butter(2, [lo, hi], btype="band", fs=SR, output="sos"), x)

def low(x, hi):
    return sosfilt(butter(2, hi, btype="low", fs=SR, output="sos"), x)

NOISE = rng.standard_normal(len(t))

def click(at, bright=1.0, gain=0.3, pan=0.0, modes=((2900, 0.035), (4700, 0.02), (6900, 0.012))):
    """A small metal contact: a noise transient plus a few fast-decaying resonances."""
    sig = band(NOISE, 1500, 9000) * env(at, 0.0005, 0.0, 0.004) * 0.8
    for f, d in modes:
        sig = sig + np.sin(2 * np.pi * f * bright * (t - at)) * env(at, 0.0005, 0.0, d)
    add(sig, pan, gain)

def knock(at, f=180, d=0.06, gain=0.35, pan=0.0):
    """The body of a lock taking the load."""
    add(np.sin(2 * np.pi * f * (t - at)) * env(at, 0.001, 0.0, d) + low(NOISE, 900) * env(at, 0.001, 0.0, 0.01) * 0.6, pan, gain)

# ---- Eternal: the charge closing in from both ends (0.5-2.3 s), two rising tones meeting
for f0, pan in ((220, -0.6), (330, 0.6)):
    sweep = f0 * (1 + np.clip((t - 0.5) / 1.8, 0, 1) ** 1.5)
    add(np.sin(2 * np.pi * np.cumsum(sweep) / SR) * env(0.5, 1.6, 0.2, 0.3) * 0.09, pan)
add(low(NOISE, 2500) * env(0.5, 1.8, 0.0, 0.35) * 0.04)

if STYLE == "tumbler":
    # The key slides in as the E is drawn (2.3-2.85 s): a scrape with five pins lifting.
    scrape = band(NOISE, 2500, 7000) * env(2.3, 0.05, 0.4, 0.06) * 0.10
    add(scrape, 0.15)
    for i, at in enumerate(np.linspace(2.36, 2.76, 5)):
        click(at, bright=1.0 + 0.04 * i, gain=0.16, pan=0.1)
    # The glint (2.9 s): the key turns - a crisp latch, then the spring settling.
    click(2.90, bright=0.85, gain=0.55, pan=0.05, modes=((2300, 0.05), (3900, 0.03), (5600, 0.018)))
    knock(2.905, f=210, d=0.05, gain=0.30)
    click(2.955, bright=1.15, gain=0.22, pan=0.1)
elif STYLE == "key":
    # A real key: it scrapes in over the pins, seats, turns with a little friction, and the
    # cam throws the latch (the glint), with the bolt sliding and the spring settling.
    krng = np.random.default_rng(3)
    def metal(at, gain, pan=0.0, base=1.0):
        modes = [(f * base, d) for f, d in ((krng.uniform(2200, 3200), 0.018), (krng.uniform(4200, 5600), 0.010),
                                            (krng.uniform(6500, 8200), 0.006))]
        click(at, gain=gain, pan=pan, modes=modes)
    # insertion 2.30-2.72: scrape with a jittery level, six pins lifting at uneven intervals
    jitter = 0.6 + 0.4 * np.abs(np.sin(2 * np.pi * 23 * t + 3 * np.sin(2 * np.pi * 7 * t)))
    add(band(NOISE, 2000, 8000) * jitter * env(2.30, 0.03, 0.36, 0.03) * 0.07, 0.1)
    for at in np.sort(2.33 + krng.uniform(0, 0.37, 6)):
        metal(at, krng.uniform(0.10, 0.18), 0.1)
    # the key seats against the stop
    knock(2.73, f=320, d=0.02, gain=0.16, pan=0.1)
    metal(2.73, 0.14, 0.1, base=0.8)
    # the turn 2.76-2.89: gritty friction rising, with tiny wafer ticks
    add(band(NOISE, 700, 3200) * env(2.76, 0.10, 0.02, 0.02) * 0.09, 0.05)
    for at in (2.79, 2.83, 2.865):
        metal(at, 0.06, 0.05, base=0.7)
    # the latch throws (2.9 s, the glint): sharp click, the cam's knock, the bolt sliding, the spring
    metal(2.90, 0.50, 0.05, base=0.75)
    knock(2.902, f=190, d=0.05, gain=0.34)
    add(band(NOISE, 500, 2500) * env(2.915, 0.008, 0.05, 0.03) * 0.10, 0.15)
    knock(2.985, f=240, d=0.03, gain=0.16, pan=0.15)
    metal(2.99, 0.14, 0.15, base=1.1)
elif STYLE == "arcade":
    # Arcade (original): the key goes in like a ball rolling over lane switches, and the
    # glint is a bumper hit - a bright square-wave pop that drops in pitch, then two rebounds.
    def blip(at, f0, f1, d, gain, pan=0.0):
        f = f0 + (f1 - f0) * np.clip((t - at) / d, 0, 1)
        ph = 2 * np.pi * np.cumsum(f) / SR
        sq = np.sign(np.sin(ph)) * 0.6 + np.sin(ph) * 0.4
        add(low(sq, 6000) * env(at, 0.002, 0.0, d), pan, gain)
    for i, at in enumerate(np.linspace(2.34, 2.76, 4)):
        blip(at, 880 * 2 ** (i / 12 * 3), 880 * 2 ** (i / 12 * 3), 0.03, 0.10, -0.2 + 0.15 * i)
    blip(2.90, 1568, 523, 0.11, 0.30, 0.1)
    knock(2.90, f=160, d=0.05, gain=0.25)
    blip(3.04, 1175, 392, 0.08, 0.16, -0.2)
    blip(3.14, 988, 330, 0.06, 0.09, 0.2)
else:
    # Vault: a heavier key, a long slide and a bolt throwing back.
    scrape = band(NOISE, 900, 4000) * env(2.3, 0.08, 0.38, 0.08) * 0.12
    add(scrape, 0.1)
    for at in (2.45, 2.62, 2.78):
        click(at, bright=0.6, gain=0.18, modes=((1700, 0.05), (2900, 0.03)))
    knock(2.90, f=95, d=0.14, gain=0.55)
    click(2.90, bright=0.5, gain=0.45, modes=((1100, 0.12), (1900, 0.08), (3100, 0.04)))
    bolt = band(NOISE, 400, 2200) * env(2.93, 0.01, 0.10, 0.05) * 0.14
    add(bolt, 0.2)
    knock(3.08, f=120, d=0.08, gain=0.35)

# ---- Core: power-up hum rising with the ring (4.3-6.6 s)
f = 55 * (1 + np.clip((t - 4.3) / 2.3, 0, 1))
ph = 2 * np.pi * np.cumsum(f) / SR
add((np.sin(ph) + 0.5 * np.sin(2 * ph) + 0.25 * np.sin(3 * ph)) * env(4.3, 2.2, 0.1, 0.4) * 0.11)
# The dial turning: detent clicks, quick then slowing, like a combination dial.
for c in [4.4 + x for x in np.cumsum(0.06 + 0.22 * np.linspace(0, 1, 14) ** 2)]:
    click(c, bright=0.9, gain=0.12, pan=0.3 * np.sin(c * 3))
# The lock (6.6 s): the dial drops into place.
knock(6.6, f=72, d=0.18, gain=0.55)
click(6.6, bright=0.7, gain=0.4, modes=((1500, 0.06), (2600, 0.04), (4200, 0.02)))
click(6.66, bright=1.1, gain=0.18)
# CORE appears: a warm swell (D add9), no bell.
for fq, g, pan in ((73.42, 0.15, 0), (146.83, 0.10, -0.3), (220.0, 0.08, 0.3), (293.66, 0.07, -0.2),
                   (369.99, 0.05, 0.2), (659.25, 0.025, 0.4)):
    det = np.sin(2 * np.pi * fq * t) + np.sin(2 * np.pi * fq * 1.003 * t)
    add(det * 0.5 * env(6.68, 0.4, 0.6, 0.8), pan, g)

ir_n = int(0.25 * SR)
ir = rng.standard_normal(ir_n) * np.exp(-np.arange(ir_n) / (0.05 * SR))
ir = low(ir, 5000); ir /= np.sqrt(np.sum(ir ** 2))
from scipy.signal import fftconvolve
L = L + 0.18 * fftconvolve(L, ir)[:len(t)]
R = R + 0.18 * fftconvolve(R, ir[::-1].copy())[:len(t)]
mix = np.stack([L, R], axis=1)
fade = np.ones(len(t)); n = int(0.5 * SR); fade[-n:] = np.linspace(1, 0, n)
mix *= fade[:, None]
mix /= np.max(np.abs(mix)) / 0.89
with wave.open(f"eternal-core-boot-{STYLE}.wav", "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((mix * 32767).astype("<i2").tobytes())
print("ok", STYLE)
