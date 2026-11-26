"""ZombieTest.ogg -> low, drawn-out, grating zombie moan."""
import sys
import numpy as np
import soundfile as sf
from scipy import signal

src, dst = sys.argv[1], sys.argv[2]
x, sr = sf.read(src, always_2d=True)
x = x.mean(axis=1)
x /= np.max(np.abs(x)) + 1e-9


def resample_speed(y, speed):
    """Tape-style: speed < 1 = slower and lower."""
    n = int(len(y) / speed)
    return signal.resample(y, n)


def stretch(y, factor, n_fft=4096, hop=512):
    """Phase vocoder time stretch (keeps pitch, smears it - which is the point)."""
    f, t, Z = signal.stft(y, sr, nperseg=n_fft, noverlap=n_fft - hop)
    steps = np.arange(0, Z.shape[1] - 1, 1.0 / factor)
    phase = np.angle(Z[:, 0])
    expected = 2 * np.pi * hop * np.arange(Z.shape[0]) / n_fft
    out = np.zeros((Z.shape[0], len(steps)), complex)
    for i, s in enumerate(steps):
        a, frac = int(s), s - int(s)
        mag = (1 - frac) * np.abs(Z[:, a]) + frac * np.abs(Z[:, a + 1])
        out[:, i] = mag * np.exp(1j * phase)
        dphi = np.angle(Z[:, a + 1]) - np.angle(Z[:, a]) - expected
        dphi -= 2 * np.pi * np.round(dphi / (2 * np.pi))
        phase += expected + dphi
    _, y2 = signal.istft(out, sr, nperseg=n_fft, noverlap=n_fft - hop)
    return y2


def band(y, lo, hi, order=4):
    sos = signal.butter(order, [lo, hi], btype="band", fs=sr, output="sos")
    return signal.sosfilt(sos, y)


# 1. down ~9 semitones, slower
y = resample_speed(x, 0.6)
# 2. drag it out further
y = stretch(y, 1.6)
y /= np.max(np.abs(y)) + 1e-9
n = len(y)
t = np.arange(n) / sr

# 3. a minor-second layer a semitone up, plus one a tritone down: constant beating, nothing resolves
up = resample_speed(y, 2 ** (1 / 12))[:n]
up = np.pad(up, (0, n - len(up)))
down = resample_speed(y, 2 ** (-6 / 12))[:n]
y = y + 0.55 * up + 0.4 * down

# 4. roughness: ring-mod at a wobbling ~40 Hz (the band the ear finds roughest)
mod_f = 40 + 12 * np.sin(2 * np.pi * 0.23 * t)
ring = y * np.sin(2 * np.pi * np.cumsum(mod_f) / sr)
y = 0.65 * y + 0.55 * ring

# 5. push the 2.5-4.5 kHz band the ear is most sensitive to, plus a nasal resonance
y = y + 1.8 * band(y, 2500, 4500) + 0.8 * band(y, 900, 1300)

# 6. uneven tremolo so it never settles
trem = 1 - 0.35 * (0.5 + 0.5 * np.sin(2 * np.pi * (6.5 * t + 0.8 * np.sin(2 * np.pi * 0.4 * t))))
y *= trem

# 7. grit
y /= np.max(np.abs(y)) + 1e-9
y = np.tanh(3.2 * y) / np.tanh(3.2)

# 8. a dank room tail
ir_len = int(1.6 * sr)
rng = np.random.default_rng(7)
ir = rng.standard_normal(ir_len) * np.exp(-np.arange(ir_len) / (0.35 * sr))
ir = signal.sosfilt(signal.butter(2, 3500, fs=sr, output="sos"), ir)
ir /= np.sqrt(np.sum(ir ** 2))
wet = signal.fftconvolve(y, ir)
y = np.pad(y, (0, len(wet) - len(y))) + 0.35 * wet

# cut rumble, fades, normalise
y = signal.sosfilt(signal.butter(2, 45, btype="high", fs=sr, output="sos"), y)
fi, fo = int(0.05 * sr), int(0.8 * sr)
y[:fi] *= np.linspace(0, 1, fi)
y[-fo:] *= np.linspace(1, 0, fo) ** 2
y = y / np.max(np.abs(y)) * 0.89
sf.write(dst, y.astype(np.float32), sr, subtype="PCM_16")
print(f"{len(y) / sr:.2f}s -> {dst}")
