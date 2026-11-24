using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AntigravityAssistant
{
    // MARK: - 16-Bit Pixel Art Antigravity Robot Mascot, Tray Icon & Windows .ICO Generator

    public static class PixelRobotSprite
    {
        public const int GridWidth = 31;
        public const int GridHeight = 37;

        public enum Expression
        {
            Normal,
            Blink,
            Working,
            Happy,
            Error
        }

        public struct Rgba32
        {
            public byte R;
            public byte G;
            public byte B;
            public byte A;

            public Rgba32(byte r, byte g, byte b, byte a)
            {
                R = r;
                G = g;
                B = b;
                A = a;
            }
        }

        public struct OrbPalette
        {
            public Rgba32 Main;
            public Rgba32 Glow;
            public Rgba32 Shade;

            public static OrbPalette ForPhase(AgentPhase phase)
            {
                OrbPalette p = new OrbPalette();
                AgentPhaseKind kind = phase != null ? phase.Kind : AgentPhaseKind.Idle;
                switch (kind)
                {
                    case AgentPhaseKind.Thinking:
                        p.Main = new Rgba32(36, 198, 255, 255);
                        p.Glow = new Rgba32(148, 236, 255, 255);
                        p.Shade = new Rgba32(18, 128, 208, 255);
                        return p;
                    case AgentPhaseKind.RunningTool:
                        p.Main = new Rgba32(142, 98, 255, 255);
                        p.Glow = new Rgba32(198, 172, 255, 255);
                        p.Shade = new Rgba32(92, 54, 204, 255);
                        return p;
                    case AgentPhaseKind.WaitingUser:
                        p.Main = new Rgba32(255, 178, 36, 255);
                        p.Glow = new Rgba32(255, 226, 122, 255);
                        p.Shade = new Rgba32(206, 122, 14, 255);
                        return p;
                    case AgentPhaseKind.Error:
                        p.Main = new Rgba32(255, 76, 88, 255);
                        p.Glow = new Rgba32(255, 154, 162, 255);
                        p.Shade = new Rgba32(196, 36, 48, 255);
                        return p;
                    default:
                        p.Main = new Rgba32(46, 138, 255, 255);
                        p.Glow = new Rgba32(122, 194, 255, 255);
                        p.Shade = new Rgba32(26, 92, 204, 255);
                        return p;
                }
            }
        }

        private static readonly string[] BaseRows = new string[]
        {
            "....................KKKK.......", // 00: Antenna orb top
            "...................KWQOOK......", // 01: Orb highlight
            "...................KQQOOK......", // 02: Orb glow
            "..................KKOOooK......", // 03: Wire + orb lower
            ".................KK.KKKK.......", // 04: Wire + orb bottom
            "................KK.............", // 05: Bent antenna stem
            ".............KKKKK.............", // 06: Antenna dome top
            "............KGGgggK............", // 07: Antenna dome base
            ".......KKKKKKKKKKKKKKKKK.......", // 08: CRT head top border
            ".....KKWWWWWWWWWWWWWWWWWKK.....", // 09: CRT top white highlight
            "....KWWLLLLLLLLLLLLLLLLLWWK....", // 10: CRT upper bevel
            "....KWLMKKKKKKKKKKKKKKKMLWK....", // 11: Screen top frame
            "....KLMKbbBABBBBBBBCCCCKLWK....", // 12: Screen + Antigravity 'A' tip
            "....KLMKbBABABBBBBBCCCCKLWK....", // 13: Screen + 'A' mid
            "....KLMKbAAAAABBBBBBCCCKLWK....", // 14: Screen + 'A' base
            "....KLMKbBBBBBBBBBBBBCCKMWK....", // 15: Screen upper blue glow
            "....KLMKbBBEEBBBBBEEBBCKMWK....", // 16: Eyes row 1
            "....KLSKbBBEEBBBBBEEBBCKMSK....", // 17: Eyes row 2
            "....KLSKbBBEEBBBBBEEBBBKMSK....", // 18: Eyes row 3
            "....KLSKbBBBBBmmmBBBBBBKMSK....", // 19: Cute smile
            "....KMSKbBpPpBBcBBpPpBBKSMK....", // 20: Rosy pink blush cheeks + lower lip glint
            "....KMSKbbbbbbbbbbbbbbbKSMK....", // 21: Screen bottom shadow
            "....KMSSKKKKKKKKKKKKKKKSSMK....", // 22: Screen lower frame
            ".....KKSSSSSSSSSSSSSSSSSKK.....", // 23: CRT chin bevel
            ".......KKKKKKKKKKKKKKKKK.......", // 24: CRT bottom border
            ".........KDDDDDDDDDDDK.........", // 25: Neck shadow
            "........KKMLLLLLLLLLSKK........", // 26: Shoulders
            "......KWLKLLLnnnnnLLSKLWK......", // 27: Arms + chest badge top
            ".....KWLLKWLLnzzznLLSKLLWK.....", // 28: Arms + chest badge Antigravity arch top
            ".....KLMSKWLLnznznLLSKSMlK.....", // 29: Arms + chest badge Antigravity arch legs
            "......KKKKMLLnnnnnLMSKKKK......", // 30: Wrists + chest badge bottom
            ".........KMLLLLLLLLMSK.........", // 31: Lower torso
            ".........KSSSSSSSSSSSK.........", // 32: Hips bevel
            ".........KMMSKKKKKMSSK.........", // 33: Upper legs split
            ".........KMMSK...KMSSK.........", // 34: Stubby legs
            ".........KSSDK...KSSDK.........", // 35: Leg ankles
            "..........KKKK...KKKK.........."  // 36: Robot feet
        };

        public static char[][] BuildGrid(Expression expression, bool waveTick)
        {
            char[][] grid = new char[BaseRows.Length][];
            for (int i = 0; i < BaseRows.Length; i++)
            {
                grid[i] = BaseRows[i].ToCharArray();
            }

            switch (expression)
            {
                case Expression.Normal:
                    break;
                case Expression.Blink:
                    foreach (int y in new int[] { 16, 17 })
                    {
                        grid[y][11] = 'B'; grid[y][12] = 'B';
                        grid[y][18] = 'B'; grid[y][19] = 'B';
                    }
                    grid[18][10] = 'E'; grid[18][11] = 'E'; grid[18][12] = 'E';
                    grid[18][18] = 'E'; grid[18][19] = 'E'; grid[18][20] = 'E';
                    break;
                case Expression.Working:
                    grid[16][11] = 'W';
                    grid[16][18] = 'W';
                    if (waveTick)
                    {
                        grid[0][17] = 'Q';
                        grid[1][26] = 'Q';
                        grid[3][27] = 'W';
                    }
                    else
                    {
                        grid[1][17] = 'W';
                        grid[0][25] = 'Q';
                    }
                    break;
                case Expression.Happy:
                    foreach (int y in new int[] { 16, 17, 18 })
                    {
                        grid[y][11] = 'B'; grid[y][12] = 'B';
                        grid[y][18] = 'B'; grid[y][19] = 'B';
                    }
                    grid[16][11] = 'E'; grid[17][10] = 'E'; grid[17][12] = 'E';
                    grid[16][19] = 'E'; grid[17][18] = 'E'; grid[17][20] = 'E';
                    break;
                case Expression.Error:
                    foreach (int y in new int[] { 16, 17, 18 })
                    {
                        grid[y][11] = 'B'; grid[y][12] = 'B';
                        grid[y][18] = 'B'; grid[y][19] = 'B';
                    }
                    grid[16][10] = 'E'; grid[17][11] = 'E'; grid[18][10] = 'E';
                    grid[16][20] = 'E'; grid[17][19] = 'E'; grid[18][20] = 'E';
                    grid[19][14] = 'B'; grid[19][15] = 'm'; grid[19][16] = 'B';
                    break;
            }
            return grid;
        }

        public static bool TryResolveColor(char token, OrbPalette orb, out Rgba32 color)
        {
            switch (token)
            {
                case '.': color = default(Rgba32); return false;
                case 'K': color = new Rgba32(22, 25, 36, 255); return true;
                case 'W': color = new Rgba32(255, 255, 255, 255); return true;
                case 'L':
                case 'l': color = new Rgba32(234, 237, 244, 255); return true;
                case 'M': color = new Rgba32(194, 200, 212, 255); return true;
                case 'S': color = new Rgba32(142, 149, 165, 255); return true;
                case 'D': color = new Rgba32(96, 103, 120, 255); return true;
                case 'G': color = new Rgba32(136, 144, 164, 255); return true;
                case 'g': color = new Rgba32(90, 97, 115, 255); return true;
                case 'O': color = orb.Main; return true;
                case 'Q': color = orb.Glow; return true;
                case 'o': color = orb.Shade; return true;
                case 'b': color = new Rgba32(26, 88, 190, 255); return true;
                case 'B': color = new Rgba32(48, 136, 255, 255); return true;
                case 'C': color = new Rgba32(76, 160, 255, 255); return true;
                case 'c': color = new Rgba32(126, 204, 255, 255); return true;
                case 'A': color = new Rgba32(248, 252, 255, 255); return true;
                case 'E': color = new Rgba32(26, 30, 43, 255); return true;
                case 'm': color = new Rgba32(26, 30, 43, 255); return true;
                case 'p': color = new Rgba32(246, 142, 168, 255); return true;
                case 'P': color = new Rgba32(255, 184, 202, 255); return true;
                case 'n': color = new Rgba32(23, 78, 174, 255); return true;
                case 'z': color = new Rgba32(138, 220, 255, 255); return true;
                default: color = default(Rgba32); return false;
            }
        }

        public static BitmapSource MakeSpriteBitmap(AgentPhase phase, Expression expr, bool waveTick, int startRow, int rowCount)
        {
            char[][] grid = BuildGrid(expr, waveTick);
            OrbPalette orb = OrbPalette.ForPhase(phase);
            int w = GridWidth;
            int h = rowCount;
            byte[] bgra = new byte[w * h * 4];

            for (int outY = 0; outY < h; outY++)
            {
                int srcY = startRow + outY;
                if (srcY < 0 || srcY >= grid.Length) continue;
                char[] row = grid[srcY];
                for (int x = 0; x < w && x < row.Length; x++)
                {
                    Rgba32 c;
                    if (!TryResolveColor(row[x], orb, out c)) continue;
                    int idx = (outY * w + x) * 4;
                    bgra[idx + 0] = c.B;
                    bgra[idx + 1] = c.G;
                    bgra[idx + 2] = c.R;
                    bgra[idx + 3] = c.A;
                }
            }

            BitmapSource bmp = BitmapSource.Create(w, h, 96.0, 96.0, PixelFormats.Bgra32, null, bgra, w * 4);
            bmp.Freeze();
            return bmp;
        }

        // MARK: - Crisp System Tray Icon (`System.Drawing.Icon`, safe HICON cleanup)

        public static System.Drawing.Icon MakeTrayIcon(AgentPhase phase)
        {
            Expression expr = Expression.Normal;
            if (phase != null)
            {
                if (phase.Kind == AgentPhaseKind.Thinking || phase.Kind == AgentPhaseKind.RunningTool)
                    expr = Expression.Working;
                else if (phase.Kind == AgentPhaseKind.Error)
                    expr = Expression.Error;
            }

            char[][] grid = BuildGrid(expr, false);
            OrbPalette orb = OrbPalette.ForPhase(phase);

            // Crop head + antenna rows 0..25 onto a crisp 32x32 bitmap
            const int size = 32;
            using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                const int cropStart = 0;
                const int cropH = 25;
                const int cropXStart = 4;
                const int cropW = 24;

                for (int py = 0; py < size; py++)
                {
                    int srcY = cropStart + (py * cropH) / size;
                    if (srcY < 0 || srcY >= grid.Length) continue;
                    char[] row = grid[srcY];
                    for (int px = 0; px < size; px++)
                    {
                        int srcX = cropXStart + (px * cropW) / size;
                        if (srcX < 0 || srcX >= row.Length) continue;
                        Rgba32 c;
                        if (TryResolveColor(row[srcX], orb, out c))
                        {
                            bmp.SetPixel(px, py, System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B));
                        }
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (System.Drawing.Icon tmp = System.Drawing.Icon.FromHandle(hIcon))
                    {
                        return (System.Drawing.Icon)tmp.Clone();
                    }
                }
                finally
                {
                    Win32Native.DestroyIcon(hIcon);
                }
            }
        }

        // MARK: - Multi-Resolution Windows `.ico` Exporter (`--export-ico <file.ico>`)

        public static void ExportAppIco(string outputIcoPath)
        {
            string dir = Path.GetDirectoryName(outputIcoPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            BitmapSource robotBmp = MakeSpriteBitmap(AgentPhase.Idle, Expression.Normal, true, 0, GridHeight);
            int[] sizes = new int[] { 16, 32, 48, 64, 128, 256 };
            List<byte[]> pngFrames = new List<byte[]>(sizes.Length);

            for (int i = 0; i < sizes.Length; i++)
            {
                pngFrames.Add(RenderSquircleIconPng(sizes[i], robotBmp));
            }

            using (FileStream fs = new FileStream(outputIcoPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                // ICONDIR header
                bw.Write((ushort)0); // Reserved
                bw.Write((ushort)1); // Type = 1 (ICO)
                bw.Write((ushort)pngFrames.Count);

                int dataOffset = 6 + pngFrames.Count * 16;
                for (int i = 0; i < pngFrames.Count; i++)
                {
                    int sz = sizes[i];
                    byte[] data = pngFrames[i];
                    bw.Write((byte)(sz >= 256 ? 0 : sz)); // Width
                    bw.Write((byte)(sz >= 256 ? 0 : sz)); // Height
                    bw.Write((byte)0);                    // Color palette
                    bw.Write((byte)0);                    // Reserved
                    bw.Write((ushort)1);                  // Color planes
                    bw.Write((ushort)32);                 // Bits per pixel
                    bw.Write((uint)data.Length);          // Image byte size
                    bw.Write((uint)dataOffset);           // Offset
                    dataOffset += data.Length;
                }

                for (int i = 0; i < pngFrames.Count; i++)
                {
                    bw.Write(pngFrames[i]);
                }
            }
        }

        private static byte[] RenderSquircleIconPng(int pixelSize, BitmapSource robotBmp)
        {
            double s = pixelSize;
            DrawingVisual dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);

            using (DrawingContext dc = dv.RenderOpen())
            {
                double pad = pixelSize >= 48 ? s * 0.08 : s * 0.04;
                Rect cardRect = new Rect(pad, pad, s - pad * 2.0, s - pad * 2.0);
                double radius = cardRect.Width * 0.225;

                LinearGradientBrush bg = new LinearGradientBrush(
                    Color.FromRgb(255, 255, 255),
                    Color.FromRgb(248, 250, 254),
                    90.0);
                Pen border = new Pen(new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)), Math.Max(1.0, s * 0.012));
                dc.DrawRoundedRectangle(bg, border, cardRect, radius, radius);

                double aspect = (double)GridWidth / (double)GridHeight;
                double robotH = Math.Round(cardRect.Height * 0.80);
                double robotW = Math.Round(robotH * aspect);
                Rect robotRect = new Rect(
                    Math.Round((s - robotW) * 0.5),
                    Math.Round((s - robotH) * 0.5),
                    robotW,
                    robotH);
                dc.DrawImage(robotBmp, robotRect);
            }

            RenderTargetBitmap rtb = new RenderTargetBitmap(pixelSize, pixelSize, 96.0, 96.0, PixelFormats.Pbgra32);
            rtb.Render(dv);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                return ms.ToArray();
            }
        }
    }

    // MARK: - Animated WPF Pixel Robot Mascot Element

    public sealed class PixelRobotMascotControl : FrameworkElement
    {
        private static readonly DateTime EpochStart = DateTime.UtcNow.AddSeconds(-1.2);

        private AgentPhase _phase = AgentPhase.Idle;
        private double _mascotHeight = 74.0;
        private bool _animateFloat = true;
        private DispatcherTimer _animTimer;
        private readonly TranslateTransform _bobTransform = new TranslateTransform(0, 0);

        // Cache brushes by packed RGBA to avoid per-frame allocations
        private static readonly Dictionary<uint, SolidColorBrush> BrushCache = new Dictionary<uint, SolidColorBrush>();

        public PixelRobotMascotControl(AgentPhase phase, double height, bool animateFloat)
        {
            _phase = phase ?? AgentPhase.Idle;
            _mascotHeight = height;
            _animateFloat = animateFloat;
            RenderTransform = _bobTransform;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            double aspect = (double)PixelRobotSprite.GridWidth / (double)PixelRobotSprite.GridHeight;
            Width = Math.Round(height * aspect);
            Height = Math.Round(height);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public AgentPhase Phase
        {
            get { return _phase; }
            set
            {
                AgentPhase next = value ?? AgentPhase.Idle;
                if (_phase != next)
                {
                    _phase = next;
                    UpdateTimerInterval();
                    InvalidateVisual();
                }
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_animateFloat)
            {
                EnsureTimer();
                _animTimer.Start();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_animTimer != null)
            {
                _animTimer.Stop();
            }
        }

        private void EnsureTimer()
        {
            if (_animTimer != null) return;
            _animTimer = new DispatcherTimer(DispatcherPriority.Render);
            UpdateTimerInterval();
            _animTimer.Tick += delegate
            {
                if (_animateFloat)
                {
                    double t = (DateTime.UtcNow - EpochStart).TotalSeconds;
                    bool working = _phase != null && _phase.IsWorking;
                    double bob = Math.Sin(t * (working ? 4.2 : 2.1)) * (working ? 2.2 : 1.4);
                    _bobTransform.Y = bob;
                }
                InvalidateVisual();
            };
        }

        private void UpdateTimerInterval()
        {
            if (_animTimer == null) return;
            bool working = _phase != null && _phase.IsWorking;
            _animTimer.Interval = TimeSpan.FromMilliseconds(working ? 130.0 : 180.0);
        }

        private PixelRobotSprite.Expression CurrentExpression(double t)
        {
            if (!_animateFloat)
            {
                if (_phase.Kind == AgentPhaseKind.Error) return PixelRobotSprite.Expression.Error;
                if (_phase.IsWorking) return PixelRobotSprite.Expression.Working;
                if (_phase.Kind == AgentPhaseKind.WaitingUser) return PixelRobotSprite.Expression.Happy;
                return PixelRobotSprite.Expression.Normal;
            }

            switch (_phase.Kind)
            {
                case AgentPhaseKind.Error:
                    return PixelRobotSprite.Expression.Error;
                case AgentPhaseKind.Thinking:
                case AgentPhaseKind.RunningTool:
                    return (t % 3.6) < 0.16 ? PixelRobotSprite.Expression.Blink : PixelRobotSprite.Expression.Working;
                case AgentPhaseKind.WaitingUser:
                    return (t % 3.0) < 0.16 ? PixelRobotSprite.Expression.Blink : PixelRobotSprite.Expression.Happy;
                default:
                    return (t % 4.4) < 0.15 ? PixelRobotSprite.Expression.Blink : PixelRobotSprite.Expression.Normal;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;
            if (w <= 0 || h <= 0) return;

            double t = _animateFloat ? (DateTime.UtcNow - EpochStart).TotalSeconds : 0.8;
            PixelRobotSprite.Expression expr = CurrentExpression(t);
            bool waveTick = !_animateFloat || (((int)(t * 3.2)) % 2 == 0);

            char[][] grid = PixelRobotSprite.BuildGrid(expr, waveTick);
            PixelRobotSprite.OrbPalette orb = PixelRobotSprite.OrbPalette.ForPhase(_phase);

            int cols = PixelRobotSprite.GridWidth;
            int rows = PixelRobotSprite.GridHeight;
            double cellW = w / cols;
            double cellH = h / rows;

            for (int y = 0; y < rows; y++)
            {
                char[] row = grid[y];
                double y0 = Math.Floor(y * cellH);
                double y1 = Math.Floor((y + 1) * cellH);
                double rh = Math.Max(1.0, y1 - y0);

                for (int x = 0; x < cols; x++)
                {
                    PixelRobotSprite.Rgba32 c;
                    if (!PixelRobotSprite.TryResolveColor(row[x], orb, out c)) continue;
                    double x0 = Math.Floor(x * cellW);
                    double x1 = Math.Floor((x + 1) * cellW);
                    double rw = Math.Max(1.0, x1 - x0);

                    dc.DrawRectangle(GetCachedBrush(c), null, new Rect(x0, y0, rw, rh));
                }
            }
        }

        private static SolidColorBrush GetCachedBrush(PixelRobotSprite.Rgba32 c)
        {
            uint key = ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
            SolidColorBrush brush;
            if (!BrushCache.TryGetValue(key, out brush))
            {
                brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
                brush.Freeze();
                BrushCache[key] = brush;
            }
            return brush;
        }
    }
}
