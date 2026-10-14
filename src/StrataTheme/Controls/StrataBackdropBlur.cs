using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace StrataTheme.Controls;

/// <summary>Edge toward which a <see cref="StrataBackdropBlur"/> fades its frosted backdrop out.</summary>
public enum StrataBackdropFade
{
    /// <summary>The frost is uniform across the whole surface.</summary>
    None,

    /// <summary>Full frost at the bottom, fading out toward the top edge.</summary>
    Top,

    /// <summary>Full frost at the top, fading out toward the bottom edge.</summary>
    Bottom
}

/// <summary>
/// Frosted-glass backdrop. Renders a blurred, optionally saturated copy of whatever was drawn beneath
/// its bounds earlier in the same frame, clipped to <see cref="CornerRadius"/>.
/// </summary>
/// <remarks>
/// <para>The control is a leaf that draws no tint, border or content of its own and never takes hit
/// tests. Place it directly underneath a translucent surface: the surface supplies the tint and the
/// content, this control supplies the frost, and content can then flow behind the chrome while staying
/// legible. The frost replaces what lies beneath it, so the sharp backdrop stays hidden even where the
/// window itself is translucent (Mica, acrylic).</para>
/// <para>Requires the Skia renderer. On other renderers, or when the drawing surface cannot be read,
/// it draws nothing and the translucent surface above it remains the fallback.</para>
/// <para>Only pixels redrawn in the current frame are sampled. The unblurred backdrop is remembered, so
/// when a redraw covers just part of the glass (a caret blink, button feedback, or an animation beneath
/// it), only that part is replaced before the frost is blurred again. Once such redraws settle, every
/// glass is repainted together, so a change beneath also reaches the frost just outside its redrawn area.
/// The glass never samples its own foreground, and overlapping glasses never keep each other rendering.</para>
/// </remarks>
public class StrataBackdropBlur : Control
{
    /// <summary>Gaussian blur standard deviation, in device-independent pixels. Zero disables the frost.</summary>
    public static readonly StyledProperty<double> BlurRadiusProperty =
        AvaloniaProperty.Register<StrataBackdropBlur, double>(nameof(BlurRadius), 24d);

    /// <summary>Colour saturation applied to the frost. 1 keeps the backdrop's colours unchanged.</summary>
    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<StrataBackdropBlur, double>(nameof(Saturation), 1d);

    /// <summary>Corner radius of the frosted area, matching the surface it backs.</summary>
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<StrataBackdropBlur>();

    /// <summary>Edge toward which the frost fades out, for scroll-edge bands.</summary>
    public static readonly StyledProperty<StrataBackdropFade> FadeProperty =
        AvaloniaProperty.Register<StrataBackdropBlur, StrataBackdropFade>(nameof(Fade));

    /// <summary>Length of the fade in device-independent pixels. Zero or less fades across the whole height.</summary>
    public static readonly StyledProperty<double> FadeLengthProperty =
        AvaloniaProperty.Register<StrataBackdropBlur, double>(nameof(FadeLength));

    /// <summary>Quiet period after the last partial redraw before every glass is repainted together.</summary>
    internal static readonly TimeSpan HealDelay = TimeSpan.FromMilliseconds(250);

    // Glasses attached on this UI thread. Repainting all of them in one frame leaves none partly redrawn
    // by another's repaint, so heals cannot chain into a render loop where glasses overlap.
    [ThreadStatic] private static HashSet<StrataBackdropBlur>? t_attached;
    private static int s_healQueued;
    private static long s_healGeneration;

    // GPU textures of frosts that went away, waiting for a render in which their context is current.
    private static readonly RetiredGpuResources s_retired = new();

    static StrataBackdropBlur()
    {
        AffectsRender<StrataBackdropBlur>(
            BlurRadiusProperty, SaturationProperty, CornerRadiusProperty, FadeProperty, FadeLengthProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<StrataBackdropBlur>(false);
        FocusableProperty.OverrideDefaultValue<StrataBackdropBlur>(false);
    }

    /// <inheritdoc cref="BlurRadiusProperty"/>
    public double BlurRadius
    {
        get => GetValue(BlurRadiusProperty);
        set => SetValue(BlurRadiusProperty, value);
    }

    /// <inheritdoc cref="SaturationProperty"/>
    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    /// <inheritdoc cref="CornerRadiusProperty"/>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <inheritdoc cref="FadeProperty"/>
    public StrataBackdropFade Fade
    {
        get => GetValue(FadeProperty);
        set => SetValue(FadeProperty, value);
    }

    /// <inheritdoc cref="FadeLengthProperty"/>
    public double FadeLength
    {
        get => GetValue(FadeLengthProperty);
        set => SetValue(FadeLengthProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0 || !(BlurRadius > 0))
            return;

        context.Custom(new BackdropOperation(
            new Rect(size), BlurRadius, Saturation, CornerRadius, Fade, FadeLength));
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (t_attached ??= []).Add(this);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        t_attached?.Remove(this);
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Called from the render thread after a partial redraw patched a frost. A change beneath the glass
    /// also shifts the frost just outside the redrawn area, which only a full repaint reaches; it waits
    /// for redraws to settle, so animations over the glass never pay for it.
    /// </summary>
    private static void RequestHeal()
    {
        if (Interlocked.Exchange(ref s_healQueued, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref s_healQueued, 0);
            var generation = ++s_healGeneration;
            DispatcherTimer.RunOnce(() =>
            {
                if (generation != s_healGeneration || t_attached is not { } attached)
                    return;

                foreach (var glass in attached)
                    glass.InvalidateVisual();
            }, HealDelay);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Decides whether the current redraw captures the backdrop afresh. Only pixels inside the current
    /// clip were redrawn this frame, so a clip strictly inside the remembered backdrop patches just
    /// that part of it; its other pixels have not been redrawn since they were captured.
    /// </summary>
    internal static bool ShouldRecompute(bool hasCache, SKRectI cachedVisible, SKRectI visible) =>
        !hasCache || Contains(visible, cachedVisible) || !Contains(cachedVisible, visible);

    /// <summary>Rec. 709 saturation matrix with no translation.</summary>
    internal static float[] CreateSaturationMatrix(double saturation)
    {
        var s = (float)saturation;
        const float red = 0.2126f, green = 0.7152f, blue = 0.0722f;
        var inverse = 1 - s;
        return
        [
            red * inverse + s, green * inverse, blue * inverse, 0, 0,
            red * inverse, green * inverse + s, blue * inverse, 0, 0,
            red * inverse, green * inverse, blue * inverse + s, 0, 0,
            0, 0, 0, 1, 0
        ];
    }

    private static bool Contains(SKRectI outer, SKRectI inner) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top
        && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static bool IsLost(object context) =>
        context is GRContext gpu && (gpu.Handle == IntPtr.Zero || gpu.IsAbandoned);

    /// <summary>
    /// GPU objects whose owner went away while their graphics context was not current. Releasing a texture
    /// then never reaches the driver (an OpenGL delete without a current context is dropped), so it would
    /// stay allocated for the life of the process. They wait here for a render under their own context.
    /// </summary>
    internal sealed class RetiredGpuResources
    {
        private readonly List<(object Context, IDisposable Resource)> _items = [];

        internal int Count
        {
            get
            {
                lock (_items)
                    return _items.Count;
            }
        }

        internal void Retire(object context, IDisposable? resource)
        {
            if (resource is null)
                return;

            lock (_items)
                _items.Add((context, resource));
        }

        /// <summary>
        /// Releases what was retired under <paramref name="current"/>, which the caller has made current,
        /// and what belongs to a context that has since been lost, whose objects no longer reach the driver.
        /// </summary>
        internal void ReleaseFor(object current, Func<object, bool> isLost)
        {
            List<IDisposable>? release = null;
            lock (_items)
            {
                for (var i = _items.Count - 1; i >= 0; i--)
                {
                    var (context, resource) = _items[i];
                    if (!ReferenceEquals(context, current) && !isLost(context))
                        continue;

                    (release ??= []).Add(resource);
                    _items.RemoveAt(i);
                }
            }

            if (release is null)
                return;

            foreach (var resource in release)
                resource.Dispose();
        }
    }

    private sealed class BackdropOperation : ICustomDrawOperation
    {
        private readonly double _blurRadius;
        private readonly double _saturation;
        private readonly CornerRadius _cornerRadius;
        private readonly StrataBackdropFade _fade;
        private readonly double _fadeLength;

        // Render-thread state. A new operation is created for every UI-thread render, and a changed operation
        // always repaints the control's whole bounds, so each instance starts from a full frame. The unblurred
        // backdrop then lets later partial frames patch only what they redrew. GPU-backed state is only ever
        // released while its context is current, during a render: see Release.
        private SKSurface? _backdrop;
        private GRContext? _backdropContext;
        private SKSizeI _backdropSize;
        private SKImage? _frost;
        private SKRectI _frostSource;
        private SKRectI _frostDevice;
        private SKRectI _cachedVisible;
        private SKRectI _cachedBounds;
        private bool _failed;

        // Native filter and paint objects are reused across frames: content scrolling beneath a glass
        // re-blurs it every frame, and recreating them each time showed up as a steady per-frame cost.
        private SKImageFilter? _blurFilter;
        private SKColorFilter? _saturationFilter;
        private SKImageFilter? _filter;
        private float _filterSigma = -1;
        private readonly SKPaint _copyPaint = new() { BlendMode = SKBlendMode.Src };
        private readonly SKPaint _drawPaint = new() { BlendMode = SKBlendMode.Plus };
        private readonly SKPaint _erasePaint = new() { BlendMode = SKBlendMode.DstOut };
        private readonly SKRoundRect _clip = new();
        private readonly SKPoint[] _radii = new SKPoint[4];

        public BackdropOperation(
            Rect bounds,
            double blurRadius,
            double saturation,
            CornerRadius cornerRadius,
            StrataBackdropFade fade,
            double fadeLength)
        {
            Bounds = bounds;
            _blurRadius = blurRadius;
            _saturation = saturation;
            _cornerRadius = cornerRadius;
            _fade = fade;
            _fadeLength = fadeLength;
        }

        public Rect Bounds { get; }

        public bool HitTest(Point p) => false;

        public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

        public void Render(ImmediateDrawingContext context)
        {
            if (_failed || context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                return;

            try
            {
                using var lease = feature.Lease();
                // A GPU render has its context current, so frosts that went away can now be released. Raster
                // renders (RenderTargetBitmap) can run on the UI thread, which must not touch a GPU context.
                if (lease.GrContext is { } gpu)
                    s_retired.ReleaseFor(gpu, IsLost);
                RenderCore(lease);
            }
            catch (Exception ex)
            {
                // A frost is decoration: never let a renderer quirk take the frame down with it.
                _failed = true;
                Trace.TraceWarning($"[StrataBackdropBlur] Backdrop disabled: {ex.Message}");
            }
        }

        private void RenderCore(ISkiaSharpApiLease lease)
        {
            var canvas = lease.SkCanvas;
            var surface = lease.SkSurface;
            if (surface is null || lease.CurrentOpacity <= 0)
                return;

            var matrix = canvas.TotalMatrix;
            if (Math.Abs(matrix.SkewX) > 1e-4 || Math.Abs(matrix.SkewY) > 1e-4
                || Math.Abs(matrix.Persp0) > 1e-6 || Math.Abs(matrix.Persp1) > 1e-6)
            {
                return;
            }

            var scaleX = Math.Abs(matrix.ScaleX);
            var scaleY = Math.Abs(matrix.ScaleY);
            if (scaleX <= 0 || scaleY <= 0)
                return;

            var device = matrix.MapRect(new SKRect(
                (float)Bounds.Left, (float)Bounds.Top, (float)Bounds.Right, (float)Bounds.Bottom));
            var deviceBounds = SKRectI.Ceiling(device, outwards: true);
            var visible = SKRectI.Intersect(deviceBounds, canvas.DeviceClipBounds);
            if (visible.Width <= 0 || visible.Height <= 0)
                return;

            var grContext = lease.GrContext;
            // State made for another context is released under that context, never under this one.
            if (!ReferenceEquals(grContext, _backdropContext))
                Release();

            var hasCache = _frost is not null && _backdrop is not null
                           && deviceBounds == _cachedBounds
                           && ReferenceEquals(grContext, _backdropContext);
            var full = ShouldRecompute(hasCache, _cachedVisible, visible);
            if (full)
            {
                _cachedVisible = visible;
                _cachedBounds = deviceBounds;
            }

            if (!Capture(surface, grContext, visible, full) || !Blur(grContext, (scaleX + scaleY) / 2))
            {
                // Start over from the next full frame rather than frost a half-updated backdrop.
                Release();
                return;
            }

            if (!full)
                RequestHeal();

            Draw(canvas, lease.CurrentOpacity, device, scaleX, scaleY);
        }

        /// <summary>
        /// Copies the freshly drawn pixels beneath <paramref name="region"/> into the backdrop. A full
        /// capture starts a backdrop covering exactly the region; a partial one replaces only the region.
        /// </summary>
        private bool Capture(SKSurface surface, GRContext? grContext, SKRectI region, bool full)
        {
            using var patch = surface.Snapshot(region);
            if (patch is null)
                return false;

            if (full && (_backdrop is null || !ReferenceEquals(grContext, _backdropContext)
                         || _backdropSize != region.Size))
            {
                _backdrop?.Dispose();
                _backdrop = grContext is null
                    ? SKSurface.Create(patch.Info)
                    : SKSurface.Create(grContext, true, patch.Info);
                _backdropContext = grContext;
                _backdropSize = region.Size;
            }

            if (_backdrop is null)
                return false;

            _backdrop.Canvas.DrawImage(patch, region.Left - _cachedVisible.Left, region.Top - _cachedVisible.Top, _copyPaint);
            return true;
        }

        private SKImageFilter GetFilter(float sigma)
        {
            if (_filter is not null && _filterSigma == sigma)
                return _filter;

            DisposeFilters();
            _blurFilter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
            if (Math.Abs(_saturation - 1) > 0.001)
            {
                _saturationFilter = SKColorFilter.CreateColorMatrix(CreateSaturationMatrix(_saturation));
                _filter = SKImageFilter.CreateColorFilter(_saturationFilter, _blurFilter);
            }
            else
            {
                _filter = _blurFilter;
            }

            _filterSigma = sigma;
            return _filter;
        }

        private void DisposeFilters()
        {
            if (!ReferenceEquals(_filter, _blurFilter))
                _filter?.Dispose();
            _blurFilter?.Dispose();
            _saturationFilter?.Dispose();
            _filter = null;
            _blurFilter = null;
            _saturationFilter = null;
            _filterSigma = -1;
        }

        private bool Blur(GRContext? grContext, float scale)
        {
            using var backdrop = _backdrop?.Snapshot();
            if (backdrop is null)
                return false;

            var filter = GetFilter((float)(_blurRadius * scale));

            var subset = new SKRectI(0, 0, backdrop.Width, backdrop.Height);
            SKImage? frost;
            SKRectI outSubset;
            SKPointI outOffset;
            if (grContext is null)
                frost = backdrop.ApplyImageFilter(filter, subset, subset, out outSubset, out outOffset);
            else
                frost = backdrop.ApplyImageFilter(grContext, filter, subset, subset, out outSubset, out outOffset);

            if (frost is null)
                return false;

            _frost?.Dispose();
            _frost = frost;
            _frostSource = outSubset;
            _frostDevice = new SKRectI(
                _cachedVisible.Left + outOffset.X,
                _cachedVisible.Top + outOffset.Y,
                _cachedVisible.Left + outOffset.X + outSubset.Width,
                _cachedVisible.Top + outOffset.Y + outSubset.Height);
            return true;
        }

        private void Draw(SKCanvas canvas, double opacity, SKRect device, float scaleX, float scaleY)
        {
            if (_frost is null || opacity <= 0)
                return;

            canvas.Save();
            try
            {
                canvas.ResetMatrix();
                _radii[0] = new SKPoint((float)_cornerRadius.TopLeft * scaleX, (float)_cornerRadius.TopLeft * scaleY);
                _radii[1] = new SKPoint((float)_cornerRadius.TopRight * scaleX, (float)_cornerRadius.TopRight * scaleY);
                _radii[2] = new SKPoint((float)_cornerRadius.BottomRight * scaleX, (float)_cornerRadius.BottomRight * scaleY);
                _radii[3] = new SKPoint((float)_cornerRadius.BottomLeft * scaleX, (float)_cornerRadius.BottomLeft * scaleY);
                _clip.SetRectRadii(device, _radii);
                canvas.ClipRoundRect(_clip, SKClipOperation.Intersect, antialias: true);

                // The frost replaces what lies beneath it rather than covering it. Over a translucent
                // window (Mica, acrylic) the frost is translucent as well, so blended over the sharp
                // backdrop it would let that show through. Erasing the backdrop by the glass's coverage
                // and then adding the frost blends the two by that coverage; over an opaque backdrop this
                // is exactly a normal blend.
                var alpha = (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1));
                var erase = _erasePaint;
                var paint = _drawPaint;
                erase.Color = new SKColor(0, 0, 0, alpha);
                paint.Color = new SKColor(255, 255, 255, alpha);
                var destination = SKRect.Create(_frostDevice.Left, _frostDevice.Top, _frostDevice.Width, _frostDevice.Height);
                if (_fade == StrataBackdropFade.None)
                {
                    canvas.DrawRect(destination, erase);
                    canvas.DrawImage(_frost, SKRect.Create(_frostSource.Left, _frostSource.Top,
                        _frostSource.Width, _frostSource.Height), destination, paint);
                    return;
                }

                using var image = _frost.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                    SKMatrix.CreateTranslation(_frostDevice.Left - _frostSource.Left, _frostDevice.Top - _frostSource.Top));
                var fadeLength = _fadeLength > 0 ? (float)(_fadeLength * scaleY) : device.Height;
                var (solid, clear) = _fade == StrataBackdropFade.Bottom
                    ? (new SKPoint(0, device.Bottom - fadeLength), new SKPoint(0, device.Bottom))
                    : (new SKPoint(0, device.Top + fadeLength), new SKPoint(0, device.Top));
                // An eased ramp reads as a progressive blur rather than a hard-edged band.
                using var ramp = SKShader.CreateLinearGradient(solid, clear,
                    [SKColors.White, new SKColor(255, 255, 255, 200), new SKColor(255, 255, 255, 72), SKColors.Transparent],
                    [0f, 0.35f, 0.7f, 1f], SKShaderTileMode.Clamp);
                using var shader = SKShader.CreateBlend(SKBlendMode.DstIn, image, ramp);
                erase.Shader = ramp;
                canvas.DrawRect(destination, erase);
                paint.Shader = shader;
                canvas.DrawRect(destination, paint);
            }
            finally
            {
                _erasePaint.Shader = null;
                _drawPaint.Shader = null;
                canvas.Restore();
            }
        }

        public void Dispose()
        {
            _failed = true;
            Release();
            DisposeFilters();
            _copyPaint.Dispose();
            _drawPaint.Dispose();
            _erasePaint.Dispose();
            _clip.Dispose();
        }

        /// <summary>
        /// Lets go of the frost. Raster state is freed at once. GPU state is retired until a render makes its
        /// context current: the compositor disposes replaced operations while applying its batches, before any
        /// context is current, and a GPU texture freed then is never deleted by the driver.
        /// </summary>
        private void Release()
        {
            if (_backdropContext is { } context)
            {
                s_retired.Retire(context, _frost);
                s_retired.Retire(context, _backdrop);
            }
            else
            {
                _frost?.Dispose();
                _backdrop?.Dispose();
            }

            _frost = null;
            _backdrop = null;
            _backdropContext = null;
        }
    }
}
