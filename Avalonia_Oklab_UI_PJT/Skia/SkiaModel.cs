using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;


namespace Avalonia_Oklab_UI_PJT.Skia
{
    public sealed class SkiaModel : Control
    {
        // --- Styled props (렌더 영향) ---
        public static readonly StyledProperty<double> BaseLightnessProperty =
            AvaloniaProperty.Register<SkiaModel, double>(nameof(BaseLightness), 0.70);
        public static readonly StyledProperty<double> HueOffsetDegProperty =
            AvaloniaProperty.Register<SkiaModel, double>(nameof(HueOffsetDeg), 0.0);
        public static readonly StyledProperty<double> SaturationScaleProperty =
            AvaloniaProperty.Register<SkiaModel, double>(nameof(SaturationScale), 1.0);
        public static readonly StyledProperty<double> LightnessOffsetProperty =
            AvaloniaProperty.Register<SkiaModel, double>(nameof(LightnessOffset), 0.0);


        //계산 해상도 스케일(0.5이면 1/2 해상도로 계산 후 업스케일)
        public static readonly StyledProperty<double> QualityScaleProperty =
            AvaloniaProperty.Register<SkiaModel, double>(nameof(QualityScale), 1.0);
        public double QualityScale
        {
            get => GetValue(QualityScaleProperty); 
            set => SetValue(QualityScaleProperty, value);
        }

        //도넛 안쪽 반지름 비율(0~0.95 기본 고정값 0.30)
        public static readonly StyledProperty<double> InnerHoleRatioProperty =
            AvaloniaProperty.Register<SkiaModel, double>(nameof(InnerHoleRatio), 0.30);
        public double InnerHoleRatio
        {
            get => GetValue(InnerHoleRatioProperty);
            set => SetValue(InnerHoleRatioProperty, value);
        }

        public double BaseLightness { get => GetValue(BaseLightnessProperty); set => SetValue(BaseLightnessProperty, value); }
        public double HueOffsetDeg { get => GetValue(HueOffsetDegProperty); set => SetValue(HueOffsetDegProperty, value); }
        public double SaturationScale { get => GetValue(SaturationScaleProperty); set => SetValue(SaturationScaleProperty, value); }
        public double LightnessOffset { get => GetValue(LightnessOffsetProperty); set => SetValue(LightnessOffsetProperty, value); }


        // --- Direct props (선택 결과 바인딩) ---
        public static readonly DirectProperty<SkiaModel, double> SelectedLProperty =
            AvaloniaProperty.RegisterDirect<SkiaModel, double>(nameof(SelectedL), o => o.SelectedL);
        public static readonly DirectProperty<SkiaModel, double> SelectedCProperty =
            AvaloniaProperty.RegisterDirect<SkiaModel, double>(nameof(SelectedC), o => o.SelectedC);
        public static readonly DirectProperty<SkiaModel, double> SelectedHProperty =
            AvaloniaProperty.RegisterDirect<SkiaModel, double>(nameof(SelectedH), o => o.SelectedH);
        public static readonly DirectProperty<SkiaModel, Color> SelectedColorProperty =
            AvaloniaProperty.RegisterDirect<SkiaModel, Color>(nameof(SelectedColor), o => o.SelectedColor);
        public static readonly DirectProperty<SkiaModel, string> SelectedHexProperty =
            AvaloniaProperty.RegisterDirect<SkiaModel, string>(nameof(SelectedHex), o => o.SelectedHex);
        public static readonly DirectProperty<SkiaModel, string> SelectedInfoProperty =
            AvaloniaProperty.RegisterDirect<SkiaModel, string>(nameof(SelectedInfo), o => o.SelectedInfo);

        private double _selL, _selC, _selH;
        private Color _selColor;
        private string _selHex, _selInfo;

        public double SelectedL
        {
            get => _selL; private set => SetAndRaise(SelectedLProperty, ref _selL, value);
        }

        public double SelectedC
        {
            get => _selC; private set => SetAndRaise(SelectedCProperty, ref _selC, value);
        }

        public double SelectedH
        {
            get => _selH; private set => SetAndRaise(SelectedHProperty, ref _selH, value);
        }

        public Color SelectedColor
        {
            get => _selColor; private set => SetAndRaise(SelectedColorProperty, ref _selColor, value);
        }

        public string SelectedHex
        {
            get => _selHex; private set => SetAndRaise(SelectedHexProperty, ref _selHex, value);
        }

        public string SelectedInfo
        {
            get => _selInfo; private set => SetAndRaise(SelectedInfoProperty, ref _selInfo, value);
        }


        // 내부 캐시
        private SKImage? _cacheImg;
        private PieCacheKey _cacheKey;
        private double[]? _cmax;  // 360개(H별 Cmax)
        double _lastEffL = -1;
        private Point? _pickedDIP; // 마지막 선택 위치(DIP)

        static SkiaModel()
        {
             AffectsRender<SkiaModel>(
            BaseLightnessProperty, HueOffsetDegProperty, SaturationScaleProperty,
            LightnessOffsetProperty, InnerHoleRatioProperty, QualityScaleProperty);
        }

        // 포인터 처리에서 _pickedDIP만 바뀌면 캐시 재계산 없이 마커만 갱신
        public SkiaModel()
        {
            //클릭관련 이벤트.
            this.PointerPressed += OnPointerPressed;
            this.PointerMoved += OnPointerMoved;
            //this.PointerReleased += OnPointerReleased;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            if (pt.Properties.IsLeftButtonPressed)
            {
                _pickedDIP = pt.Position;
                UpdateSelection(_pickedDIP.Value);
                InvalidateVisual();
            }
        }
        //private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        //{
        //    isDragging = false; // 드래그 종료
        //}

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _pickedDIP = e.GetPosition(this); 
            UpdateSelection(_pickedDIP.Value); 
            InvalidateVisual();
        }


        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var root = this.VisualRoot as IRenderRoot;
            double scale = root?.RenderScaling ?? 1.0;

            // 실제 픽셀 크기
            int pw = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
            int ph = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale));

            // 품질 스케일 적용: 계산 해상도
            double q = Math.Clamp(QualityScale, 0.25, 1.0);
            int cw = Math.Max(1, (int)Math.Round(pw * q));
            int ch = Math.Max(1, (int)Math.Round(ph * q));

            double effL = Clamp01(BaseLightness + LightnessOffset);
            if (_cmax == null || Math.Abs(effL - _lastEffL) > 1e-6)
            { _cmax = PrecomputeCmax(effL); _lastEffL = effL; }

            var key = new PieCacheKey(cw, ch, (float)scale, effL,
                HueOffsetDeg, Math.Max(0, SaturationScale),
                Math.Clamp(InnerHoleRatio, 0, 0.95));

            if (!key.Equals(_cacheKey) || _cacheImg is null)
            {
                _cacheImg?.Dispose();
                _cacheImg = BuildPieImage(cw, ch, key, _cmax!); // ✨ 무거운 계산은 여기서 한 번만
                _cacheKey = key;
            }

            //경량 DrawOp: 캐시 이미지를 화면에 스케일링해서 그리기만
            context.Custom(new DrawImageOp(Bounds, (float)scale, _cacheImg));

            // 선택 마커만 오버레이
            if (_pickedDIP is Point p)
                context.DrawGeometry(null, new Pen(Brushes.White, 1),
                    new EllipseGeometry(new Rect(p.X - 6, p.Y - 6, 12, 12)));

        }

        private SKImage BuildPieImage(int w, int h, PieCacheKey key, double[] cmax)
        {
            using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            unsafe
            {
                byte* ptr = (byte*)bmp.GetPixels(out _);
                int stride = bmp.Info.RowBytes;
                double cx = (w - 1) * 0.5, cy = (h - 1) * 0.5;
                double R = Math.Min(cx, cy);
                for (int y = 0; y < h; y++)
                {
                    byte* row = ptr + y * stride;
                    double dy = (y - cy) / R;
                    for (int x = 0; x < w; x++)
                    {
                        double dx = (x - cx) / R;
                        double rr = Math.Sqrt(dx * dx + dy * dy);
                        byte* p = row + (x << 2);

                        //경계를 부드럽게 처리.
                        double feather = 1.0 / R; // 1px 두께
                        double a = 1.0;

                        if (rr < key.Inner) { a = Math.Clamp((rr - (key.Inner - feather)) / feather, 0, 1); }
                        else if (rr > 1.0) { a = Math.Clamp((1.0 + feather - rr) / feather, 0, 1); }

                        if (a <= 0) { p[0] = p[1] = p[2] = 0; p[3] = 0; continue; }

                        double hue = Math.Atan2(dy, dx) * 180.0 / Math.PI; if (hue < 0) hue += 360.0;
                        hue = WrapDeg(hue + key.HueOff);
                        int hi = (int)hue;

                        double t = (rr - key.Inner) / (1 - key.Inner);
                        double c = Math.Min(t * cmax[hi] * key.SatScale, cmax[hi]);

                        var col = OklchToColor(new Oklch(key.L, c, hue));
                        p[0] = col.B; p[1] = col.G; p[2] = col.R; p[3] = (byte)Math.Round(255 * a);
                        
                    }
                }
            }
            return SKImage.FromBitmap(bmp);
        }

        private void UpdateSelection(Point dipPoint)
        {
            double w = Math.Max(1, Bounds.Width), h = Math.Max(1, Bounds.Height);
            double cx = (w - 1) * 0.5, cy = (h - 1) * 0.5, R = Math.Min(cx, cy);
            double dx = (dipPoint.X - cx) / R, dy = (dipPoint.Y - cy) / R;
            double r = Math.Sqrt(dx * dx + dy * dy);
            double inner = Math.Clamp(InnerHoleRatio, 0, 0.95);
            if (r < inner || r > 1.0 || _cmax == null) return;

            double effL = Clamp01(BaseLightness + LightnessOffset);
            double hue = Math.Atan2(dy, dx) * 180 / Math.PI; if (hue < 0) hue += 360; hue = WrapDeg(hue + HueOffsetDeg);
            int hi = (int)hue;
            double t = (r - inner) / (1 - inner);
            double c = Math.Min(t * _cmax[hi] * Math.Max(0, SaturationScale), _cmax[hi]);
            var col = OklchToColor(new Oklch(effL, c, hue));

            SelectedL = effL; SelectedC = c; SelectedH = hue; SelectedColor = col;
            SelectedHex = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            SelectedInfo = $"L={effL:F3}  C={c:F3}  H={hue:F1}°  RGB=({col.R},{col.G},{col.B}) {SelectedHex}";
        }

        // --- 키/유틸 ---
        private readonly struct PieCacheKey : IEquatable<PieCacheKey>
        {
            public readonly int W, H; public readonly float Scale;
            public readonly double L, HueOff, SatScale, Inner;
            public PieCacheKey(int w, int h, float scale, double l, double hue, double sat, double inner)
            { W = w; H = h; Scale = scale; L = l; HueOff = hue; SatScale = sat; Inner = inner; }
            public bool Equals(PieCacheKey o) =>
                W == o.W && H == o.H && Scale.Equals(o.Scale) &&
                Math.Abs(L - o.L) < 1e-6 && Math.Abs(HueOff - o.HueOff) < 1e-6 &&
                Math.Abs(SatScale - o.SatScale) < 1e-6 && Math.Abs(Inner - o.Inner) < 1e-6;
        }


        /*
        private void PickFromPoint(Point dipPoint, bool fromPress)
        {
            // DIP 공간에서 휠 좌표 계산
            double w = Math.Max(1, Bounds.Width);
            double h = Math.Max(1, Bounds.Height);
            double cx = (w - 1) * 0.5;
            double cy = (h - 1) * 0.5;
            double radius = Math.Min(cx, cy);

            double dx = (dipPoint.X - cx) / radius;
            double dy = (dipPoint.Y - cy) / radius;
            double r = Math.Sqrt(dx * dx + dy * dy);
            if (r > 1.0 || _cmax == null) return;

            double inner = InnerHoleRatio; if (inner < 0) inner = 0; if (inner > 0.95) inner = 0.95;
            if (r < inner || r > 1.0) return;

            double effL = Clamp01(BaseLightness + LightnessOffset);

            double hue = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (hue < 0) hue += 360.0;
            hue = WrapDeg(hue + HueOffsetDeg);

            //반지름 정규화.
            double t = (r - inner) / (1 - inner);
            int hi = (int)Math.Floor(hue);
            double c = Math.Min(t * _cmax[hi] * Math.Max(0, SaturationScale), _cmax[hi]);

            var col = OklchToColor(new Oklch(effL, c, hue));
            SelectedL = effL; SelectedC = c; SelectedH = hue; SelectedColor = col;
            SelectedHex = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
            SelectedInfo = $"L={effL:F3}  C={c:F3}  H={hue:F1}°  RGB=({col.R},{col.G},{col.B}) {SelectedHex}";
            InvalidateVisual();
        }
        */

        // ---------- OKLab/OKLCH & sRGB ----------
        private struct Oklab { public double L, a, b; public Oklab(double l, double a, double b) { L = l; this.a = a; this.b = b; } }
        private struct Oklch { public double L, C, H; public Oklch(double l, double c, double h) { L = l; C = c; H = h; } }

        private static Oklab LchToLab(Oklch lch)
        {
            double hr = lch.H * Math.PI / 180.0;
            return new Oklab(lch.L, lch.C * Math.Cos(hr), lch.C * Math.Sin(hr));
        }

        private static Color OklchToColor(Oklch lch)
        {
            var lab = LchToLab(lch);
            OklabToLinearSrgb(lab, out double r, out double g, out double b);
            r = LinearToSrgb(Clamp01(r)); g = LinearToSrgb(Clamp01(g)); b = LinearToSrgb(Clamp01(b));
            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }

        private static void OklabToLinearSrgb(Oklab lab, out double r, out double g, out double b)
        {
            double l_ = lab.L + 0.3963377774 * lab.a + 0.2158037573 * lab.b;
            double m_ = lab.L - 0.1055613458 * lab.a - 0.0638541728 * lab.b;
            double s_ = lab.L - 0.0894841775 * lab.a - 1.2914855480 * lab.b;

            double l = l_ * l_ * l_;
            double m = m_ * m_ * m_;
            double s = s_ * s_ * s_;

            r = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
            g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
            b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        }

        private static double LinearToSrgb(double c) => (c <= 0.0031308) ? (12.92 * c) : (1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055);
        private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);
        private static double WrapDeg(double d) { d %= 360.0; if (d < 0) d += 360.0; return d; }

        // ---------- Cmax(L,H) LUT ----------
        private static double[] PrecomputeCmax(double L)
        {
            var arr = new double[360];
            for (int h = 0; h < 360; h++)
            {
                double hue = h;
                double lo = 0.0, hi = 0.4;
                while (InGamut(L, hi, hue) && hi < 1.5) hi *= 1.35; // 상한 확장
                for (int i = 0; i < 16; i++)
                {
                    double mid = (lo + hi) * 0.5;
                    if (InGamut(L, mid, hue)) lo = mid; else hi = mid;
                }
                arr[h] = lo;
            }
            return arr;
        }

        private static bool InGamut(double L, double C, double H)
        {
            var lab = LchToLab(new Oklch(L, C, H));
            OklabToLinearSrgb(lab, out double r, out double g, out double b);
            return r >= 0 && r <= 1 && g >= 0 && g <= 1 && b >= 0 && b <= 1;
        }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _cacheImg?.Dispose();
            _cacheImg = null;
        }
    }

    sealed class DrawImageOp : ICustomDrawOperation
    {
        public Rect Bounds { get; }
        private readonly float _scale;
        private readonly SKImage _img;
        public DrawImageOp(Rect bounds, float scale, SKImage img) { Bounds = bounds; _scale = scale; _img = img; }
        public void Dispose() { }
        public bool HitTest(Point p) => Bounds.Contains(p);
        public bool Equals(ICustomDrawOperation? other) =>
            other is DrawImageOp o && Bounds.Equals(o.Bounds) && _scale.Equals(o._scale) && ReferenceEquals(_img, o._img);
        public void Render(ImmediateDrawingContext ctx)
        {
            var lease = (ctx.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) as ISkiaSharpApiLeaseFeature)?.Lease();
            if (lease == null) return;
            using (lease)
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true }) // Low=빠름, High=미려
            {
                var canvas = lease.SkCanvas;
                canvas.Save(); canvas.Scale(1f / _scale, 1f / _scale);
                var dst = new SKRect(
                    (float)(Bounds.X * _scale), (float)(Bounds.Y * _scale),
                    (float)(Bounds.Right * _scale), (float)(Bounds.Bottom * _scale));
                canvas.DrawImage(_img, dst, paint);
                canvas.Restore();
            }
        }

    }



}
