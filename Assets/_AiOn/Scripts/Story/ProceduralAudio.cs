using System;
using System.Collections.Generic;
using UnityEngine;

namespace AiOn.Story
{
    /// <summary>
    /// The original game's sounds had no recordings: audio.js synthesized them with Web Audio. These are the same
    /// recipes rendered offline into AudioClips (mono, 22.05 kHz, made once and cached): the air-raid siren (1 minute of
    /// 5 s rises and 3 s falls between 300 and 640 Hz, sawtooth pair + triangle, low-passed, with reverb), the phone's
    /// emergency two-tone (853 + 960 Hz pulses), safety chime and SMS beeps, a distant explosion (crack + long rumble),
    /// the falling shell whistle, hits (punch, thud, land, glass, swing, pick, collapse) and a fire loop.
    /// Everything is deterministic (seeded noise), so tests can check it.
    /// </summary>
    public static class ProceduralAudio
    {
        public const int Rate = 22050;
        static readonly Dictionary<string, AudioClip> Cache = new();

        // ---------- public clips ----------

        public static AudioClip Siren() => Cached("siren", () => Render(SirenSamples()));
        public static AudioClip PhoneAlert(string kind) => Cached("phone:" + (kind ?? "sms"), () => Render(PhoneSamples(kind)));
        public static AudioClip Boom(double distance)
        {
            float far = Mathf.Clamp((float)distance, 0.3f, 1f);
            float q = Mathf.Round(far * 10f) / 10f; // a handful of distances is plenty
            return Cached("boom:" + q.ToString("0.0"), () => Render(BoomSamples(q)));
        }
        public static AudioClip Hit(string name, double seconds = 2.2) =>
            Cached("hit:" + name + (name == "whistle" ? ":" + seconds.ToString("0.0") : ""), () => Render(HitSamples(name, (float)seconds)));
        /// <summary>A seamless fire loop; its level (0..1) is the AudioSource volume.</summary>
        public static AudioClip FireLoop() => Cached("fire", () => Render(FireSamples()));

        static AudioClip Cached(string key, Func<AudioClip> make)
        {
            if (Cache.TryGetValue(key, out var c) && c) return c;
            c = make();
            c.name = "proc_" + key;
            Cache[key] = c;
            return c;
        }

        static AudioClip Render(float[] s)
        {
            Normalize(s, 0.95f);
            var clip = AudioClip.Create("proc", s.Length, 1, Rate, false);
            clip.SetData(s, 0);
            return clip;
        }

        // ---------- recipes (sample arrays, testable without Unity audio) ----------

        public static float[] SirenSamples()
        {
            const float lo = 300, hi = 640, total = 63.3f;
            int n = (int)(total * Rate);
            var outp = new float[n];
            double p1 = 0, p2 = 0, p3 = 0;
            var lp = Biquad.LowPass(1700, 0.5f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = SirenFreq(t, lo, hi);
                p1 += f / Rate; p2 += f * Mathf.Pow(2, 9f / 1200f) / Rate; p3 += 2 * f / Rate;
                float v = 0.5f * (Saw(p1) + Saw(p2)) + 0.3f * 0.5f * Tri(p3);
                float g = SirenGain(t) * (1 + 0.1f * Mathf.Sin(2 * Mathf.PI * 0.37f * t));
                outp[i] = lp.Run(v) * g;
            }
            return Reverb(outp, 0.9f, 2.6f);
        }

        /// <summary>Exponential ramps: 7 cycles of 5 s up and 3 s down, a last rise to 60 s and a fall to 63 s.</summary>
        public static float SirenFreq(float t, float lo, float hi)
        {
            if (t >= 63f) return lo * 0.55f;
            if (t >= 60f) return ExpLerp(hi, lo * 0.55f, (t - 60f) / 3f);
            if (t >= 56f) return ExpLerp(lo, hi, (t - 56f) / 4f);
            float c = t % 8f;
            return c < 5f ? ExpLerp(lo, hi, c / 5f) : ExpLerp(hi, lo, (c - 5f) / 3f);
        }

        static float SirenGain(float t)
        {
            if (t >= 63f) return 0;
            if (t >= 60f) return Mathf.Lerp(0.3f, 0f, (t - 60f) / 3f);
            if (t >= 56f) return Mathf.Lerp(0.1f, 0.3f, (t - 56f) / 4f);
            float c = t % 8f;
            if (c < 0.05f) return Mathf.Lerp(t < 8 ? 0f : 0.1f, 0.1f, c / 0.05f);
            return c < 5f ? Mathf.Lerp(0.1f, 0.3f, (c - 0.05f) / 4.95f) : Mathf.Lerp(0.3f, 0.1f, (c - 5f) / 3f);
        }

        public static float[] PhoneSamples(string kind)
        {
            switch (kind)
            {
                case "emergency":
                {
                    // Pulses (start, length) of the two-tone attention signal, square waves low-passed at 3.2 kHz.
                    var pulses = new[] { (0f, 1.0f), (1.2f, 0.45f), (1.85f, 0.45f), (2.5f, 1.2f) };
                    var s = new float[(int)(3.8f * Rate)];
                    var lp = Biquad.LowPass(3200, 0.7f);
                    for (int i = 0; i < s.Length; i++)
                    {
                        float t = i / (float)Rate, v = 0;
                        foreach (var (st, d) in pulses)
                        {
                            if (t < st || t > st + d) continue;
                            float env = Mathf.Min(1, (t - st) / 0.01f) * Mathf.Min(1, (st + d - t) / 0.02f);
                            v += 0.05f * env * (Square(853 * t) + Square(960 * t));
                        }
                        s[i] = lp.Run(v);
                    }
                    return s;
                }
                case "safety":
                {
                    var s = new float[(int)(1.1f * Rate)];
                    float[] f = { 1046, 1318, 1568 };
                    for (int k = 0; k < 3; k++) AddTone(s, f[k], f[k], 0.5f, 0.09f, k * 0.22f, Wave.Sine);
                    return s;
                }
                default:
                {
                    var s = new float[(int)(0.45f * Rate)];
                    AddTone(s, 1760, 1760, 0.18f, 0.06f, 0, Wave.Sine);
                    AddTone(s, 1760, 1760, 0.18f, 0.06f, 0.16f, Wave.Sine);
                    return s;
                }
            }
        }

        public static float[] BoomSamples(float far)
        {
            var s = new float[(int)(4.2f * Rate)];
            var rng = new System.Random(4242);
            // the crack: white noise low-passed, 4 ms attack, 0.25 s exponential decay
            var lpc = Biquad.LowPass(1800 - far * 900, 0.6f);
            int crack = (int)(0.3f * Rate);
            for (int i = 0; i < crack; i++)
            {
                float t = i / (float)Rate;
                s[i] += lpc.Run(White(rng)) * (0.9f / far) * Burst(t, 0.004f, 0.25f);
            }
            // the rumble: brown noise low-passed at 100-160 Hz, 50 ms rise, 3.8 s decay
            var lpr = Biquad.LowPass(160 - far * 60, 0.8f);
            float brown = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                brown = (brown + 0.02f * White(rng)) / 1.02f;
                float env = t < 0.05f ? t / 0.05f : Mathf.Exp(-(t - 0.05f) / 3.75f * 9.2f);
                s[i] += lpr.Run(brown * 3.5f) * (1.6f / far) * env;
            }
            return Reverb(s, 0.65f, 2.6f);
        }

        public static float[] HitSamples(string name, float seconds)
        {
            var rng = new System.Random(name?.GetHashCode() ?? 1);
            switch (name)
            {
                case "whistle":
                {
                    var s = new float[(int)((seconds + 0.1f) * Rate)];
                    AddTone(s, 2400, 500, seconds, 0.12f, 0, Wave.Sine);
                    return s;
                }
                case "punch":
                {
                    var s = new float[(int)(0.25f * Rate)];
                    AddBurst(s, rng, 900, 0.8f, 0.06f, 0.5f, 0, FilterKind.Band);
                    AddTone(s, 120, 60, 0.12f, 0.25f, 0, Wave.Sine);
                    return s;
                }
                case "land":
                {
                    var s = new float[(int)(0.35f * Rate)];
                    AddBurst(s, rng, 300, 0.7f, 0.18f, 0.7f, 0, FilterKind.Low);
                    AddTone(s, 70, 40, 0.2f, 0.35f, 0, Wave.Sine);
                    return s;
                }
                case "glass":
                {
                    var s = new float[(int)(0.6f * Rate)];
                    float at = 0;
                    for (int i = 0; i < 6; i++) { AddTone(s, Rnd(rng, 2500, 5200), 0, Rnd(rng, 0.05f, 0.25f), 0.05f, at, Wave.Triangle); at += Rnd(rng, 0.01f, 0.04f); }
                    AddBurst(s, rng, 5000, 0.5f, 0.2f, 0.25f, 0, FilterKind.High);
                    return s;
                }
                case "swing":
                {
                    var s = new float[(int)(0.25f * Rate)];
                    AddBurst(s, rng, 700, 0.5f, 0.16f, 0.12f, 0, FilterKind.Band);
                    return s;
                }
                case "pick":
                {
                    var s = new float[(int)(0.12f * Rate)];
                    AddTone(s, 500, 700, 0.06f, 0.05f, 0, Wave.Sine);
                    return s;
                }
                case "collapse":
                {
                    var s = new float[(int)(3.6f * Rate)];
                    float at = 0;
                    for (int i = 0; i < 14; i++) { AddBurst(s, rng, Rnd(rng, 150, 700), 0.6f, Rnd(rng, 0.2f, 0.6f), Rnd(rng, 0.3f, 0.8f), at, FilterKind.Low); at += Rnd(rng, 0.05f, 0.2f); }
                    return Reverb(s, 0.4f, 2.6f);
                }
                default: // thud
                {
                    var s = new float[(int)(0.2f * Rate)];
                    AddBurst(s, rng, 400, 1f, 0.09f, 0.35f, 0, FilterKind.Band);
                    AddTone(s, 90, 50, 0.1f, 0.18f, 0, Wave.Sine);
                    return s;
                }
            }
        }

        public static float[] FireSamples()
        {
            const float len = 6f, fade = 0.5f;
            int n = (int)(len * Rate), nf = (int)(fade * Rate);
            var raw = new float[n + nf];
            var rng = new System.Random(77);
            var roar = Biquad.LowPass(480, 0.7f);
            var hiss = Biquad.BandPass(2500, 0.5f);
            float brown = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                brown = (brown + 0.02f * White(rng)) / 1.02f;
                raw[i] = roar.Run(brown * 3.5f) * 0.4f + hiss.Run(White(rng)) * 0.06f;
            }
            // crackle: short high-passed bursts every 30-160 ms, most of them at full level
            float at = 0;
            while (at < len + fade - 0.05f)
            {
                if (rng.NextDouble() < 0.85) AddBurst(raw, rng, Rnd(rng, 1500, 4000), 1, Rnd(rng, 0.008f, 0.03f), Rnd(rng, 0.05f, 0.22f), at, FilterKind.High);
                at += Rnd(rng, 0.03f, 0.16f);
            }
            // seamless loop: the tail past the end fades into the head
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = raw[i];
            for (int i = 0; i < nf; i++) { float k = i / (float)nf; s[i] = raw[i] * k + raw[n + i] * (1 - k); }
            return s;
        }

        // ---------- building blocks ----------

        enum Wave { Sine, Triangle }
        enum FilterKind { Low, High, Band }

        static float Saw(double phase) => (float)(2 * (phase - Math.Floor(phase + 0.5)));
        static float Tri(double phase) => 1 - 4 * Mathf.Abs((float)(phase - Math.Floor(phase + 0.5)));
        static float Square(float cycles) => cycles - Mathf.Floor(cycles) < 0.5f ? 1 : -1;
        static float ExpLerp(float a, float b, float k) => a * Mathf.Pow(b / a, Mathf.Clamp01(k));
        static float White(System.Random r) => (float)(r.NextDouble() * 2 - 1);
        static float Rnd(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        /// <summary>The audio.js envelope: linear attack to 1, exponential decay to 0.0001 at dur.</summary>
        static float Burst(float t, float attack, float dur) =>
            t < attack ? t / attack : t > dur ? 0 : Mathf.Exp(Mathf.Log(0.0001f) * (t - attack) / Mathf.Max(0.001f, dur - attack));

        /// <summary>tone(): an oscillator gliding exponentially from f0 to f1 (f1 = 0: no glide), 8 ms attack, exp decay.</summary>
        static void AddTone(float[] s, float f0, float f1, float dur, float vol, float at, Wave wave)
        {
            int start = (int)(at * Rate), len = (int)(dur * Rate);
            double ph = 0;
            for (int i = 0; i < len && start + i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float f = f1 > 0 ? ExpLerp(f0, f1, t / dur) : f0;
                ph += f / Rate;
                float v = wave == Wave.Sine ? Mathf.Sin((float)(2 * Math.PI * ph)) : Tri(ph);
                s[start + i] += v * vol * Burst(t, 0.008f, dur);
            }
        }

        static void AddBurst(float[] s, System.Random rng, float freq, float q, float dur, float vol, float at, FilterKind kind)
        {
            var f = kind == FilterKind.Low ? Biquad.LowPass(freq, q) : kind == FilterKind.High ? Biquad.HighPass(freq, q) : Biquad.BandPass(freq, q);
            int start = (int)(at * Rate), len = (int)((dur + 0.02f) * Rate);
            for (int i = 0; i < len && start + i < s.Length; i++)
                s[start + i] += f.Run(White(rng)) * vol * Burst(i / (float)Rate, 0.004f, dur);
        }

        /// <summary>Schroeder reverb (4 combs, 2 all-passes) standing in for the original's 2.6 s noise convolver.</summary>
        static float[] Reverb(float[] dry, float wet, float seconds)
        {
            int tail = (int)(seconds * Rate * 0.5f);
            var o = new float[dry.Length + tail];
            Array.Copy(dry, o, dry.Length);
            int[] combs = { 1116, 1188, 1277, 1356 };
            float fb = Mathf.Pow(0.001f, combs[0] / (seconds * Rate));
            var acc = new float[o.Length];
            foreach (var d0 in combs)
            {
                int d = d0 * Rate / 44100;
                var buf = new float[d];
                int idx = 0;
                for (int i = 0; i < o.Length; i++)
                {
                    float x = i < dry.Length ? dry[i] : 0;
                    float y = buf[idx];
                    buf[idx] = x + y * fb;
                    idx = (idx + 1) % d;
                    acc[i] += y * 0.25f;
                }
            }
            foreach (var d0 in new[] { 556, 441 })
            {
                int d = d0 * Rate / 44100;
                var buf = new float[d];
                int idx = 0;
                for (int i = 0; i < acc.Length; i++)
                {
                    float b = buf[idx], x = acc[i];
                    buf[idx] = x + b * 0.5f;
                    acc[i] = b - x * 0.5f;
                    idx = (idx + 1) % d;
                }
            }
            for (int i = 0; i < o.Length; i++) o[i] += acc[i] * wet * 0.5f;
            return o;
        }

        static void Normalize(float[] s, float peak)
        {
            float m = 0;
            foreach (var v in s) m = Mathf.Max(m, Mathf.Abs(v));
            if (m > peak) { float k = peak / m; for (int i = 0; i < s.Length; i++) s[i] *= k; }
        }

        /// <summary>RBJ cookbook biquad (the Web Audio BiquadFilterNode shapes).</summary>
        sealed class Biquad
        {
            float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            static Biquad Make(float f, float q, int type)
            {
                float w = 2 * Mathf.PI * Mathf.Clamp(f, 10, Rate * 0.45f) / Rate, cs = Mathf.Cos(w), a = Mathf.Sin(w) / (2 * Mathf.Max(0.05f, q));
                float b0, b1, b2;
                if (type == 0) { b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = b0; }
                else if (type == 1) { b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = b0; }
                else { b0 = a; b1 = 0; b2 = -a; }
                float a0 = 1 + a;
                return new Biquad { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = -2 * cs / a0, _a2 = (1 - a) / a0 };
            }

            public static Biquad LowPass(float f, float q) => Make(f, q, 0);
            public static Biquad HighPass(float f, float q) => Make(f, q, 1);
            public static Biquad BandPass(float f, float q) => Make(f, q, 2);

            public float Run(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return y;
            }
        }
    }
}
