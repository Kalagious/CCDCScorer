using System;
using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HackerOverlay
{
    public partial class OverlayWindow : Window
    {
        private readonly Random _rng = new();
        private DispatcherTimer? _rainTimer;
        private DispatcherTimer? _glitchTimer;
        private DispatcherTimer? _blinkTimer;
        private DispatcherTimer? _staticTimer;
        private DispatcherTimer? _shakeTimer;
        private readonly ObservableCollection<string> _bootLog = new();
        private readonly ObservableCollection<string> _takeLog = new();
        private bool _blinkState;
        private WriteableBitmap? _noiseBmp;
        private double _shakeIntensity;

        private DispatcherTimer? _skullShakeTimer;

        public OverlayWindow()
        {
            InitializeComponent();
            LogList.ItemsSource = _bootLog;
            TakeLog.ItemsSource  = _takeLog;
            Loaded += async (_, _) => await RunSequence();
        }

        // ── Trigger gate — wait until this host:port is reachable before firing ──
        private const string TriggerHost = "192.168.88.6";
        private const int TriggerPort = 8000;

        private static async Task<bool> CanConnect(string host, int port, int timeoutMs = 1000)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync(host, port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                return completed == connectTask && client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private async Task WaitForTrigger()
        {
            while (!await CanConnect(TriggerHost, TriggerPort))
            {
                await Task.Delay(2000);
            }
        }

        // ── Top-level sequence ────────────────────────────────────────────────
        private async Task RunSequence()
        {
            await WaitForTrigger();

            SetClickThrough();
            InitNoiseBitmap();
            StartShakeLoop();

            await Phase1_SubtleGlitch();
            await Phase2_WatchDogsBoot();
            await Phase3_Takeover();

            KeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) Application.Current.Shutdown();
            };
            IsHitTestVisible = true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // PHASE 1 — subtle corner brackets + harsh scan-line glitch stutters
        // ═════════════════════════════════════════════════════════════════════
        private async Task Phase1_SubtleGlitch()
        {
            FadeElement(GlitchCanvas, 0, 1, 900);
            Sfx.Play(SoundBank.BlipLow);
            await Delay(1000);

            var glitchEls = new[] { Glitch1, Glitch2, Glitch3 };
            var end = DateTime.UtcNow.AddSeconds(2.6);
            int tick = 0;
            while (DateTime.UtcNow < end)
            {
                var el = glitchEls[_rng.Next(glitchEls.Length)];
                Canvas.SetTop(el, _rng.Next(50, (int)ActualHeight - 50));
                el.Width = _rng.Next(300, (int)ActualWidth);
                Canvas.SetLeft(el, _rng.Next(0, 300) - 300);
                el.Opacity = _rng.NextDouble() * 0.7 + 0.15;

                if (tick % 3 == 0)
                {
                    PulseStatic(0.12, 0.08);
                    if (_rng.NextDouble() > 0.5) Sfx.Play(SoundBank.GlitchStutter);
                    SetShake(6);
                }
                tick++;

                await Delay(_rng.Next(50, 180));
                el.Opacity = 0;
                await Delay(_rng.Next(40, 120));
            }

            SetShake(0);
            FadeElement(GlitchCanvas, 1, 0, 300);
            await Delay(350);
        }

        // ═════════════════════════════════════════════════════════════════════
        // PHASE 2 — ctOS / breach boot sequence (harsh slam-ins, RGB split)
        // ═════════════════════════════════════════════════════════════════════
        private async Task Phase2_WatchDogsBoot()
        {
            Sfx.Play(SoundBank.BigCrash);
            SetShake(18);
            PulseStatic(0.4, 0.15);
            FadeElement(BootGrid, 0, 1, 120);
            await Delay(160);
            SetShake(4);

            FadeElement(HudTL, 0, 1, 300); FadeElement(HudTR, 0, 1, 300);
            FadeElement(HudBL, 0, 1, 300); FadeElement(HudBR, 0, 1, 300);

            await FlickerInHarsh(LogoText, 5);
            Sfx.Play(SoundBank.Blip);
            await Delay(400);

            AnimateDouble(SepLine, WidthProperty, 0, 640, 350);
            await Delay(400);

            await RgbGlitchSlam(BootTitle, BootTitleGhostR, BootTitleGhostB, 900);
            Sfx.Play(SoundBank.Impact);
            SetShake(14);
            await Delay(250);
            SetShake(3);

            FadeElement(BootSub, 0, 1, 300);
            await Delay(500);

            var scanLines = new[]
            {
                "[+] Enumerating network topology...",
                "[+] Brute-forcing firewall ACLs...",
                "[+] Injecting raw shell payload...",
                "[!] Antivirus process TERMINATED (PID 4812)",
                "[+] Kernel privilege escalation: SUCCESS",
                "[+] Root access CONFIRMED — system is ours",
            };

            double step = 640.0 / scanLines.Length;
            for (int i = 0; i < scanLines.Length; i++)
            {
                double targetW = (i + 1) * step;
                AnimateDouble(ScanBar, WidthProperty, ScanBar.ActualWidth, targetW, 200);
                ScanLabel.Text = $"BREACHING...  {(int)((i + 1) * 100.0 / scanLines.Length)}%";
                _bootLog.Add(scanLines[i]);
                Sfx.Play(i % 2 == 0 ? SoundBank.Blip : SoundBank.BlipLow);
                SetShake(5);
                if (i == 3) { Sfx.Play(SoundBank.StaticBurstShort); PulseStatic(0.3, 0.1); SetShake(10); }
                await Delay(300);
                SetShake(2);
            }
            ScanLabel.Text = "BREACH COMPLETE";
            Sfx.Play(SoundBank.Impact);
            SetShake(12);
            await Delay(450);
            SetShake(0);

            FadeElement(BootGrid, 1, 0, 150);
            await Delay(200);
        }

        // ═════════════════════════════════════════════════════════════════════
        // PHASE 3 — full takeover: matrix rain, skull slam, strobing, drone
        // ═════════════════════════════════════════════════════════════════════
        private async Task Phase3_Takeover()
        {
            StartCodeRain();
            StartDrone();

            Sfx.Play(SoundBank.BigCrash);
            SetShake(22);
            PulseStatic(0.55, 0.2);
            FadeElement(TakeoverGrid, 0, 1, 100);
            await Delay(150);

            await Strobe(4, 60, 90);
            SetShake(6);

            PixelSkull.Build(SkullCanvas, new SolidColorBrush(Colors.Lime));
            ScaleBounceIn(SkullCanvas, 0.2, 1.0, 420);
            FadeElement(SkullCanvas, 0, 1, 180);
            Sfx.Play(SoundBank.Impact);
            SetShake(16);
            StartSkullShake();
            await Delay(550);
            SetShake(3);

            await RgbGlitchSlam(TakeTitle, TakeTitleGhostR, TakeTitleGhostB, 700);
            Sfx.Play(SoundBank.BigCrash);
            SetShake(20);
            await Delay(250);
            SetShake(4);

            await FlickerInHarsh(TakeSub, 6);
            Sfx.Play(SoundBank.SirenSweep);
            await Delay(350);

            var takeLines = new[]
            {
                "[!!!] /etc/shadow — EXFILTRATED (3,291 hashes)",
                "[!!!] SSH private keys — EXFILTRATED",
                "[!!!] Browser passwords — EXFILTRATED",
                "[!!!] LSASS dumped — credentials extracted",
                "[!!!] Webcam feed LIVE — streaming to C2",
                "[!!!] Keylogger installed — capturing input",
                "[!!!] Persistence established — see you at reboot ;)",
            };

            double barMaxW = 700;
            for (int i = 0; i < takeLines.Length; i++)
            {
                double pct = (i + 1) * barMaxW / takeLines.Length;
                AnimateDouble(ExfilBar, WidthProperty, ExfilBar.ActualWidth, pct, 260);
                ExfilLabel.Text = $"EXFILTRATING...  {(int)((i + 1) * 100.0 / takeLines.Length)}%";
                _takeLog.Add(takeLines[i]);
                Sfx.Play(SoundBank.Impact);
                SetShake(14);
                PulseStatic(0.22, 0.09);
                await Delay(80);
                SetShake(3);
                await Delay(280);
            }

            ExfilLabel.Text = "✓  EXFILTRATION COMPLETE";
            await Strobe(6, 50, 70);
            Sfx.Play(SoundBank.BigCrash);
            SetShake(25);
            await Delay(200);
            SetShake(0);

            // Settle into a stable aftermath — calm black background,
            // code rain keeps streaming, no pulsing/heartbeat effects.
            FadeElement(Vignette, Vignette.Opacity, 0, 700);
            TakeoverGrid.Background = new SolidColorBrush(Colors.Black);
            Sfx.StopLoop();

            StartGlitchTitle();
            BlinkMsg.Opacity = 1;
            StartBlinkTimer();
            StartSkullSteadyGlow();
        }

        // ═════════════════════════════════════════════════════════════════════
        // Visual helpers
        // ═════════════════════════════════════════════════════════════════════

        private void SetClickThrough()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, -20);
            SetWindowLong(hwnd, -20, exStyle | 0x00000020 | 0x00080000);
        }

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        private static Task Delay(int ms) => Task.Delay(ms);

        private static void FadeElement(UIElement el, double from, double to, int ms)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms));
            el.BeginAnimation(OpacityProperty, anim);
        }

        private static void AnimateDouble(UIElement el, DependencyProperty prop, double from, double to, int ms)
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms));
            el.BeginAnimation(prop, anim);
        }

        private static void ScaleBounceIn(UIElement el, double from, double to, int ms)
        {
            var st = new ScaleTransform(from, from);
            el.RenderTransform = st;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            var ease = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 3, Springiness = 6 };
            var ax = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            var ay = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, ax);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, ay);
        }

        /// <summary>Harsh on/off flicker — not a smooth fade, true digital stutter.</summary>
        private async Task FlickerInHarsh(UIElement el, int flickers)
        {
            for (int i = 0; i < flickers; i++)
            {
                el.Opacity = _rng.NextDouble() > 0.4 ? 1 : 0;
                await Delay(_rng.Next(25, 90));
            }
            el.Opacity = 1;
        }

        /// <summary>
        /// Slams the main title in with jittering red/cyan ghost copies
        /// (chromatic-aberration glitch look) that settle into place.
        /// </summary>
        private async Task RgbGlitchSlam(TextBlock main, TextBlock ghostR, TextBlock ghostB, int ms)
        {
            main.Opacity = 1; ghostR.Opacity = 1; ghostB.Opacity = 1;
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            int step = 0;
            while (DateTime.UtcNow < end)
            {
                double settle = 1.0 - Math.Min(1.0, step / 14.0); // jitter shrinks over time
                ghostR.RenderTransform = new TranslateTransform(_rng.Next(-14, 14) * settle, _rng.Next(-5, 5) * settle);
                ghostB.RenderTransform = new TranslateTransform(_rng.Next(-14, 14) * settle, _rng.Next(-5, 5) * settle);
                main.RenderTransform   = new TranslateTransform(_rng.Next(-4, 4) * settle, _rng.Next(-2, 2) * settle);
                step++;
                await Delay(35);
            }
            ghostR.RenderTransform = new TranslateTransform(2, 1);
            ghostB.RenderTransform = new TranslateTransform(-2, -1);
            main.RenderTransform   = new TranslateTransform(0, 0);
        }

        /// <summary>Rapid white/black strobe — the "camera flash" shock beat.</summary>
        private async Task Strobe(int count, int onMs, int offMs)
        {
            for (int i = 0; i < count; i++)
            {
                WhiteFlash.Opacity = 0.85;
                await Delay(onMs);
                WhiteFlash.Opacity = 0;
                BlackoutFlash.Opacity = 0.9;
                await Delay(offMs / 2);
                BlackoutFlash.Opacity = 0;
                await Delay(offMs / 2);
            }
        }

        private void SetShake(double intensity) => _shakeIntensity = intensity;

        private void StartShakeLoop()
        {
            _shakeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _shakeTimer.Tick += (_, _) =>
            {
                if (_shakeIntensity <= 0.01) { ShakeTransform.X = 0; ShakeTransform.Y = 0; return; }
                ShakeTransform.X = (_rng.NextDouble() * 2 - 1) * _shakeIntensity;
                ShakeTransform.Y = (_rng.NextDouble() * 2 - 1) * _shakeIntensity;
            };
            _shakeTimer.Start();
        }

        // ── Full-screen static noise (real per-pixel random bitmap) ─────────────
        private void InitNoiseBitmap()
        {
            int w = 192, h = 108; // low-res, scaled up by the Image control — authentic static look
            _noiseBmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            StaticNoiseImage.Source = _noiseBmp;
            RenderOptions.SetBitmapScalingMode(StaticNoiseImage, BitmapScalingMode.NearestNeighbor);
        }

        private void RenderNoiseFrame()
        {
            if (_noiseBmp == null) return;
            int w = _noiseBmp.PixelWidth, h = _noiseBmp.PixelHeight;
            int stride = w * 4;
            var pixels = new byte[stride * h];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte v = (byte)_rng.Next(0, 256);
                pixels[i] = v; pixels[i + 1] = v; pixels[i + 2] = v; pixels[i + 3] = 255;
            }
            _noiseBmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        }

        /// <summary>Flashes real TV static over the whole screen for a beat.</summary>
        private void PulseStatic(double peakOpacity, double seconds)
        {
            if (_staticTimer != null) { _staticTimer.Stop(); _staticTimer = null; }

            StaticNoiseImage.Opacity = peakOpacity;
            _staticTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _staticTimer.Tick += (_, _) => RenderNoiseFrame();
            _staticTimer.Start();

            Task.Run(async () =>
            {
                await Delay((int)(seconds * 1000));
                Dispatcher.Invoke(() =>
                {
                    _staticTimer?.Stop();
                    _staticTimer = null;
                    StaticNoiseImage.Opacity = 0;
                });
            });
        }

        // ── Typewriter code columns — fixed position, characters type in top-to-bottom,
        //    then the column clears and starts typing again from the top. Nothing scrolls. ──
        private sealed class CodeColumn
        {
            public TextBlock Block = null!;
            public int LinesTyped;
            public int MaxLines;
            public string CurrentLine = "";
            public Brush[] CurrentBrushes = Array.Empty<Brush>();
            public int CharIndex;
            public int TicksUntilNextChar;
        }

        private CodeColumn[]? _codeCols;

        private void StartCodeRain()
        {
            const double colW = 220;
            const double lineH = 17;
            int cols = (int)(SystemParameters.PrimaryScreenWidth / colW) + 1;
            double screenH = SystemParameters.PrimaryScreenHeight;
            int maxLines = (int)(screenH / lineH);

            _codeCols = new CodeColumn[cols];
            for (int i = 0; i < cols; i++)
            {
                var tb = new TextBlock
                {
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    LineHeight = lineH,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    Opacity = 0.55,
                    Width = colW - 6,
                    Height = screenH,
                    TextWrapping = TextWrapping.NoWrap,
                };
                Canvas.SetLeft(tb, i * colW);
                Canvas.SetTop(tb, 0);
                CodeRainCanvas.Children.Add(tb);

                var col = new CodeColumn
                {
                    Block = tb,
                    MaxLines = maxLines,
                    TicksUntilNextChar = _rng.Next(0, 6),
                };
                StartNextLine(col);
                _codeCols[i] = col;
            }

            _rainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(28) };
            _rainTimer.Tick += CodeRainTick;
            _rainTimer.Start();
        }

        private void StartNextLine(CodeColumn col)
        {
            col.CurrentLine = CodeRain.RandomLine();
            col.CurrentBrushes = CodeRain.GetCharBrushes(col.CurrentLine);
            col.CharIndex = 0;
        }

        private void CodeRainTick(object? s, EventArgs e)
        {
            if (_codeCols == null) return;
            foreach (var col in _codeCols)
            {
                if (col.TicksUntilNextChar > 0) { col.TicksUntilNextChar--; continue; }

                if (col.CharIndex >= col.CurrentLine.Length)
                {
                    // finished typing this line
                    col.LinesTyped++;
                    if (col.LinesTyped >= col.MaxLines)
                    {
                        // column is full — clear and start fresh from the top
                        col.Block.Inlines.Clear();
                        col.LinesTyped = 0;
                    }
                    else
                    {
                        col.Block.Inlines.Add(new LineBreak());
                    }
                    StartNextLine(col);
                    col.TicksUntilNextChar = _rng.Next(0, 2);
                    continue;
                }

                char ch = col.CurrentLine[col.CharIndex];
                col.Block.Inlines.Add(new Run(ch.ToString()) { Foreground = col.CurrentBrushes[col.CharIndex] });
                col.CharIndex++;
                col.TicksUntilNextChar = _rng.Next(0, 2); // slight per-character stagger, typewriter feel
            }
        }

        // ── Title glitch loop ─────────────────────────────────────────────────
        private void StartGlitchTitle()
        {
            _glitchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            int frame = 0;
            var colors = new[] { Colors.Red, Colors.Cyan, Colors.Magenta, Colors.Yellow, Colors.Red, Colors.White };
            _glitchTimer.Tick += (_, _) =>
            {
                var c = colors[frame % colors.Length];
                TakeTitle.Foreground = new SolidColorBrush(c);
                TakeTitle.RenderTransform = new TranslateTransform(_rng.Next(-8, 8), _rng.Next(-4, 4));
                TakeTitleGhostR.RenderTransform = new TranslateTransform(_rng.Next(-16, 16), _rng.Next(-5, 5));
                TakeTitleGhostB.RenderTransform = new TranslateTransform(_rng.Next(-16, 16), _rng.Next(-5, 5));

                frame++;
                if (frame % 10 == 0)
                {
                    TakeTitle.RenderTransform = new TranslateTransform(0, 0);
                    if (_rng.NextDouble() > 0.5) Sfx.Play(SoundBank.GlitchStutter);
                }
            };
            _glitchTimer.Start();
        }

        private void StartBlinkTimer()
        {
            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _blinkTimer.Tick += (_, _) =>
            {
                _blinkState = !_blinkState;
                BlinkMsg.Opacity = _blinkState ? 1 : 0;
                if (_blinkState) Sfx.Play(SoundBank.Blip);
            };
            _blinkTimer.Start();
        }

        /// <summary>Calm, steady green glow on the skull — no pulsing, no red flashing.</summary>
        private void StartSkullSteadyGlow()
        {
            var effect = (DropShadowEffect)SkullCanvas.Effect;
            effect.Color = Colors.Lime;
            effect.BlurRadius = 30;
        }

        /// <summary>Continuous independent jitter on the skull — separate from the global screen shake.</summary>
        private void StartSkullShake()
        {
            _skullShakeTimer?.Stop();
            _skullShakeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _skullShakeTimer.Tick += (_, _) =>
            {
                SkullShakeTransform.X = (_rng.NextDouble() * 2 - 1) * 4;
                SkullShakeTransform.Y = (_rng.NextDouble() * 2 - 1) * 4;
            };
            _skullShakeTimer.Start();
        }

        // ── Sub-bass atmosphere drone — no periodic thud, just a held tone ────
        private void StartDrone()
        {
            Sfx.StartLoop(SoundBank.DroneLoop);
        }
    }
}
