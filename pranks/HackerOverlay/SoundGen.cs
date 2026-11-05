using System;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;

namespace HackerOverlay
{
    /// <summary>
    /// Procedurally synthesizes short WAV clips in memory so the exe needs
    /// zero external audio assets. Everything here is raw PCM math.
    /// </summary>
    internal static class SoundGen
    {
        private const int SampleRate = 22050;

        public static short[] Noise(double seconds, double amp = 0.5, Random? rng = null)
        {
            rng ??= new Random();
            int n = (int)(SampleRate * seconds);
            var s = new short[n];
            for (int i = 0; i < n; i++)
                s[i] = (short)((rng.NextDouble() * 2 - 1) * amp * short.MaxValue);
            return s;
        }

        public static short[] Tone(double seconds, double f0, double f1, double amp, string shape = "sine", double decay = 0)
        {
            int n = (int)(SampleRate * seconds);
            var s = new short[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SampleRate;
                double freq = f0 + (f1 - f0) * (i / (double)Math.Max(1, n - 1));
                phase += 2 * Math.PI * freq / SampleRate;

                double v = shape switch
                {
                    "square" => Math.Sign(Math.Sin(phase)),
                    "saw"    => 2 * ((phase / (2 * Math.PI)) % 1) - 1,
                    _        => Math.Sin(phase),
                };

                double env = decay > 0 ? Math.Exp(-t * decay) : 1.0;
                s[i] = Clamp(v * amp * env);
            }
            return s;
        }

        public static short[] Thud(double seconds, double freq, double amp)
        {
            int n = (int)(SampleRate * seconds);
            var s = new short[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)SampleRate;
                double env = Math.Exp(-t * 11);
                s[i] = Clamp(Math.Sin(2 * Math.PI * freq * t) * env * amp);
            }
            return s;
        }

        public static short[] Mix(params short[][] layers)
        {
            int n = layers.Max(l => l.Length);
            var outp = new short[n];
            foreach (var l in layers)
                for (int i = 0; i < l.Length; i++)
                    outp[i] = Clamp((outp[i] + l[i]) / 32768.0);
            return outp;
        }

        public static short[] Bitcrush(short[] src, int bits)
        {
            int levels = 1 << bits;
            double step = 65536.0 / levels;
            var outp = new short[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                double v = src[i] + 32768.0;
                v = Math.Round(v / step) * step - 32768.0;
                outp[i] = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
            }
            return outp;
        }

        private static short Clamp(double normalized)
            => (short)Math.Clamp(normalized * short.MaxValue, short.MinValue, short.MaxValue);

        public static byte[] ToWav(short[] samples)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            int byteRate = SampleRate * 2;
            int dataSize = samples.Length * 2;

            bw.Write(new[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + dataSize);
            bw.Write(new[] { 'W', 'A', 'V', 'E' });
            bw.Write(new[] { 'f', 'm', 't', ' ' });
            bw.Write(16);
            bw.Write((short)1);   // PCM
            bw.Write((short)1);   // mono
            bw.Write(SampleRate);
            bw.Write(byteRate);
            bw.Write((short)2);   // block align
            bw.Write((short)16);  // bits per sample
            bw.Write(new[] { 'd', 'a', 't', 'a' });
            bw.Write(dataSize);
            foreach (var smp in samples) bw.Write(smp);

            return ms.ToArray();
        }
    }

    /// <summary>
    /// Fire-and-forget WAV playback with no external files.
    /// Uses PlaySync() on a background thread so the player/stream can't get
    /// garbage-collected mid-playback, without ever blocking the UI thread.
    /// </summary>
    internal static class Sfx
    {
        private static SoundPlayer? _loopPlayer;

        public static void Play(byte[] wavBytes)
        {
            Task.Run(() =>
            {
                try
                {
                    using var player = new SoundPlayer(new MemoryStream(wavBytes));
                    player.PlaySync();
                }
                catch
                {
                    // No audio device, muted session, etc — fail silently, visuals still run.
                }
            });
        }

        public static void StartLoop(byte[] wavBytes)
        {
            try
            {
                StopLoop();
                _loopPlayer = new SoundPlayer(new MemoryStream(wavBytes));
                _loopPlayer.PlayLooping();
            }
            catch { /* ignore */ }
        }

        public static void StopLoop()
        {
            try { _loopPlayer?.Stop(); _loopPlayer?.Dispose(); } catch { /* ignore */ }
            _loopPlayer = null;
        }
    }

    /// <summary>
    /// Pre-rendered library of harsh, synthesized SFX — all raw PCM math,
    /// zero external assets, generated once at process start.
    /// </summary>
    internal static class SoundBank
    {
        private static readonly Random Rng = new();

        public static readonly byte[] StaticBurstShort = Build(() =>
            SoundGen.Bitcrush(SoundGen.Noise(0.10, 0.55, Rng), 6));

        public static readonly byte[] StaticBurstLong = Build(() =>
            SoundGen.Bitcrush(SoundGen.Noise(0.40, 0.5, Rng), 5));

        public static readonly byte[] Impact = Build(() =>
            SoundGen.Mix(
                SoundGen.Thud(0.5, 55, 0.9),
                SoundGen.Bitcrush(SoundGen.Noise(0.12, 0.45, Rng), 5)));

        public static readonly byte[] BigCrash = Build(() =>
            SoundGen.Mix(
                SoundGen.Thud(0.9, 40, 1.0),
                SoundGen.Tone(0.9, 180, 30, 0.45, "saw", 2.5),
                SoundGen.Bitcrush(SoundGen.Noise(0.35, 0.55, Rng), 5)));

        public static readonly byte[] Blip = Build(() =>
            SoundGen.Tone(0.045, 1800, 900, 0.3, "square", 30));

        public static readonly byte[] BlipLow = Build(() =>
            SoundGen.Tone(0.05, 300, 180, 0.35, "square", 25));

        public static readonly byte[] Riser = Build(() =>
            SoundGen.Mix(
                SoundGen.Tone(1.2, 80, 900, 0.35, "saw"),
                SoundGen.Bitcrush(SoundGen.Noise(1.2, 0.18, Rng), 7)));

        public static readonly byte[] SirenSweep = Build(() =>
            SoundGen.Tone(1.0, 440, 880, 0.26, "square"));

        public static readonly byte[] DroneLoop = Build(() =>
            SoundGen.Mix(
                SoundGen.Tone(2.0, 55, 55, 0.3, "saw"),
                SoundGen.Tone(2.0, 110.5, 110.5, 0.12, "sine")));

        public static readonly byte[] GlitchStutter = Build(() =>
            SoundGen.Bitcrush(
                SoundGen.Mix(
                    SoundGen.Noise(0.08, 0.45, Rng),
                    SoundGen.Tone(0.08, 220, 60, 0.35, "square")), 4));

        public static readonly byte[] HeartbeatThud = Build(() =>
            SoundGen.Thud(0.4, 48, 0.85));

        public static readonly byte[] KeyClick = Build(() =>
            SoundGen.Bitcrush(SoundGen.Tone(0.02, 2200, 1800, 0.18, "square", 60), 5));

        private static byte[] Build(Func<short[]> gen) => SoundGen.ToWav(gen());
    }
}
