"""Eternal / Core boot sound, timed to eternal-wake.svg followed by core-logo-wake.svg.
Original synthesis (numpy only). Writes eternal-core-boot.wav (48 kHz, 16-bit stereo)."""
import numpy as np, wave
SR = 48000
T = 9.8
t = np.arange(int(SR * T)) / SR
L = np.zeros_like(t); R = np.zeros_like(t)
rng = np.random.default_rng(7)

def env(start, attack, hold, release):
    e = np.zeros_like(t)
    a0, a1 = start, start + attack
    h1 = a1 + hold
    r1 = h1 + release
    m = (t >= a0) & (t < a1); e[m] = (t[m] - a0) / attack
    m = (t >= a1) & (t < h1); e[m] = 1
    m = (t >= h1) & (t < r1); e[m] = np.exp(-5 * (t[m] - h1) / release)
    return e

def add(sig, pan=0.0, gain=1.0):
    global L, R
    L += sig * gain * (1 - pan) / 2 * 2 ** 0.5
    R += sig * gain * (1 + pan) / 2 * 2 ** 0.5

def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR); y = np.zeros_like(x); s = 0.0
    for i in range(len(x)):
        s = (1 - a) * x[i] + a * s; y[i] = s
    return y

# Eternal 1: the charge closing in from both ends (0.5-2.3 s): two rising tones that meet
for f0, pan in ((220, -0.6), (330, 0.6)):
    sweep = f0 * (1 + 1.0 * np.clip((t - 0.5) / 1.8, 0, 1) ** 1.5)
    phase = 2 * np.pi * np.cumsum(sweep) / SR
    add(np.sin(phase) * env(0.5, 1.6, 0.2, 0.4) * 0.10, pan)
noise = lowpass(rng.standard_normal(len(t)), 2500)
add(noise * env(0.5, 1.8, 0.0, 0.5) * 0.05)

# Eternal 2: the key drops (2.3 s) - short metallic slide
for k, f in enumerate((1800, 2400)):
    add(np.sin(2 * np.pi * f * t) * env(2.3 + k * 0.08, 0.004, 0.0, 0.12) * 0.07, 0.2)
# Eternal 3: the glint (2.9 s) - bell, inharmonic partials
for f, g, d in ((1318.5, 0.22, 1.8), (1975.5, 0.12, 1.3), (3290, 0.06, 0.8), (2637, 0.08, 1.0)):
    add(np.sin(2 * np.pi * f * t) * env(2.9, 0.003, 0.0, d) * g, 0.1)

# Core 1: power-up hum rising with the ring (4.3-6.6 s)
f = 55 * (1 + np.clip((t - 4.3) / 2.3, 0, 1))
ph = 2 * np.pi * np.cumsum(f) / SR
hum = np.sin(ph) + 0.5 * np.sin(2 * ph) + 0.25 * np.sin(3 * ph)
add(hum * env(4.3, 2.2, 0.1, 0.5) * 0.12)
# Core 2: the dial turning - ratchet clicks, quick then slowing, ending in the lock
clicks = [4.4 + x for x in np.cumsum(0.06 + 0.22 * np.linspace(0, 1, 14) ** 2)]
for c in clicks:
    burst = lowpass(rng.standard_normal(len(t)), 5000) * env(c, 0.001, 0.0, 0.025)
    add(burst * 0.10, 0.3 * np.sin(c * 3))
# Core 3: the lock (6.6 s) - low thump plus a solid click
add(np.sin(2 * np.pi * 70 * t * np.exp(-(t - 6.6).clip(0) * 6)) * env(6.6, 0.002, 0.0, 0.35) * 0.45)
add(lowpass(rng.standard_normal(len(t)), 6000) * env(6.6, 0.001, 0.0, 0.04) * 0.25)
# Core 4: the core pulses and CORE appears - a warm swell (D add9)
for fq, g, pan in ((73.42, 0.16, 0), (146.83, 0.10, -0.3), (220.0, 0.08, 0.3), (293.66, 0.07, -0.2),
                   (369.99, 0.05, 0.2), (659.25, 0.03, 0.4)):
    det = np.sin(2 * np.pi * fq * t) + np.sin(2 * np.pi * fq * 1.003 * t)
    add(det * 0.5 * env(6.65, 0.35, 0.6, 2.4) * g, pan)
for f, g, d in ((1174.7, 0.08, 1.6), (1760, 0.05, 1.2)):
    add(np.sin(2 * np.pi * f * t) * env(6.9, 0.004, 0.0, d) * g, 0.15)

mix = np.stack([L, R], axis=1)
fade = np.ones(len(t)); fade[-int(0.4 * SR):] = np.linspace(1, 0, int(0.4 * SR))
mix *= fade[:, None]
mix /= np.max(np.abs(mix)) / 0.89
pcm = (mix * 32767).astype("<i2")
with wave.open("eternal-core-boot.wav", "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
print("ok", len(t) / SR, "s")
