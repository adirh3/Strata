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
/// its bounds (and, with <see cref="SamplesBeyondEdges"/>, just around them) earlier in the same frame,
/// clipped to <see cref="CornerRadius"/>.
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

    /// <summary>Length of the fade at both side edges, in device-independent pixels. Zero leaves them sharp.</summary>
    public static readonly StyledProperty<double> HorizontalFadeLengthProperty =
        AvaloniaProperty.Register<StrataBackdropBlur, double>(nameof(HorizontalFadeLength));

    /// <summary>
    /// Whether the frost also takes in what lies just beyond its edges, as glass does, so content nearing
    /// the rim blurs into it gradually instead of the rim stretching whatever crosses it. The capture
    /// grows by three blur radii on every side, which suits small floating glass such as a navigation pill.
    /// </summary>
    public static readonly StyledProperty<bool> SamplesBeyondEdgesProperty =
        AvaloniaProperty.Register<StrataBackdropBlur, bool>(nameof(SamplesBeyondEdges));

    /// <summary>Quiet period after the last partial redraw before every glass is repainted together.</summary>
    internal static readonly TimeSpan HealDelay = TimeSpan.FromMilliseconds(250);

    // Glasses attached on this UI thread. Repainting all of them in one frame leaves none partly redrawn
    // by another's repaint, so heals cannot chain into a render loop where glasses overlap.
    [ThreadStatic] private static HashSet<StrataBackdropBlur>? t_attached;
    private static int s_healQueued;
    private static long s_healGeneration;

    // Fallback for renderers that do not expose the platform context needed to release a texture.
    private static readonly RetiredGpuResources s_retired = new();

    static StrataBackdropBlur()
    {
        AffectsRender<StrataBackdropBlur>(
            BlurRadiusProperty, SaturationProperty, CornerRadiusProperty, FadeProperty, FadeLengthProperty,
            HorizontalFadeLengthProperty,
            SamplesBeyondEdgesProperty);
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

    /// <inheritdoc cref="HorizontalFadeLengthProperty"/>
    public double HorizontalFadeLength
    {
        get => GetValue(HorizontalFadeLengthProperty);
        set => SetValue(HorizontalFadeLengthProperty, value);
    }

    /// <inheritdoc cref="SamplesBeyondEdgesProperty"/>
    public bool SamplesBeyondEdges
    {
        get => GetValue(SamplesBeyondEdgesProperty);
        set => SetValue(SamplesBeyondEdgesProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0 || !(BlurRadius > 0))
            return;

        context.Custom(new BackdropOperation(
            new Rect(size), BlurRadius, Saturation, CornerRadius, Fade, FadeLength, HorizontalFadeLength,
            SamplesBeyondEdges ? 3 * BlurRadius : 0));
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
                {
                    if (glass.IsEffectivelyVisible && glass.BlurRadius > 0
                        && glass.Bounds.Width > 0 && glass.Bounds.Height > 0)
                    {
                        glass.InvalidateVisual();
                    }
                }
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

    /// <summary>
    /// The device pixels a glass captures in this redraw: its own bounds, widened by <paramref name="margin"/>
    /// when it samples beyond its edges, limited to the pixels redrawn this frame. Nothing when the glass
    /// itself was not redrawn.
    /// </summary>
    internal static SKRectI CaptureRegion(SKRectI glass, int margin, SKRectI redrawn)
    {
        if (!glass.IntersectsWith(redrawn))
            return SKRectI.Empty;

        return SKRectI.Intersect(margin > 0 ? SKRectI.Inflate(glass, margin, margin) : glass, redrawn);
    }

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

    internal static SKShader? CreateFadeMask(
        SKRect bounds, StrataBackdropFade fade, float fadeLength, float horizontalFadeLength)
    {
        // An eased ramp, from the clear edge inward, reads as a progressive blur rather than a hard-edged band.
        SKColor faint = new(255, 255, 255, 72), most = new(255, 255, 255, 200);
        SKShader? mask = null;
        if (fade != StrataBackdropFade.None)
        {
            var length = fadeLength > 0 ? fadeLength : bounds.Height;
            var (clear, solid) = fade == StrataBackdropFade.Bottom
                ? (new SKPoint(0, bounds.Bottom), new SKPoint(0, bounds.Bottom - length))
                : (new SKPoint(0, bounds.Top), new SKPoint(0, bounds.Top + length));
            mask = SKShader.CreateLinearGradient(clear, solid,
                [SKColors.Transparent, faint, most, SKColors.White], [0f, 0.3f, 0.65f, 1f], SKShaderTileMode.Clamp);
        }

        if (horizontalFadeLength > 0 && bounds.Width > 0)
        {
            var inset = Math.Min(horizontalFadeLength / bounds.Width, 0.5f);
            var horizontal = SKShader.CreateLinearGradient(
                new SKPoint(bounds.Left, 0), new SKPoint(bounds.Right, 0),
                [SKColors.Transparent, faint, most, SKColors.White, SKColors.White, most, faint, SKColors.Transparent],
                [0f, 0.3f * inset, 0.65f * inset, inset, 1 - inset, 1 - 0.65f * inset, 1 - 0.3f * inset, 1f],
                SKShaderTileMode.Clamp);
            if (mask is null)
                return horizontal;

            using (mask)
            using (horizontal)
                return SKShader.CreateBlend(SKBlendMode.DstIn, mask, horizontal);
        }

        return mask;
    }

    private static bool IsLost(object context) =>
        context is GRContext gpu && (gpu.Handle == IntPtr.Zero || gpu.IsAbandoned);

    /// <summary>
    /// Releases GPU objects with their owning platform context current. Without access to that context,
    /// they wait for a render under their own context rather than issuing an off-context texture delete.
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

        internal void Retire(object context, IDisposable? resource, IPlatformGraphicsContext? platformContext = null)
        {
            if (resource is null)
                return;

            if (platformContext is not null)
            {
                if (!platformContext.IsLost)
                {
                    using (platformContext.EnsureCurrent())
                        resource.Dispose();
                    return;
                }

                // A lost native context is safe to release against only once Skia has abandoned it.
                if (IsLost(context))
                {
                    resource.Dispose();
                    return;
                }
            }

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
        private readonly double _horizontalFadeLength;
        private readonly double _sampleMargin;

        // Render-thread state. A new operation is created for every UI-thread render, and a changed operation
        // always repaints the control's whole bounds. Keep its snapshot as the blur input until the first
        // partial redraw needs a writable copy. Both are released only in their owning context: see Release.
        private SKImage? _captureImage;
        private SKSurface? _backdrop;
        private GRContext? _backdropContext;
        private IPlatformGraphicsContext? _backdropPlatformContext;
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
            double fadeLength,
            double horizontalFadeLength,
            double sampleMargin)
        {
            Bounds = bounds;
            _blurRadius = blurRadius;
            _saturation = saturation;
            _cornerRadius = cornerRadius;
            _fade = fade;
            _fadeLength = fadeLength;
            _horizontalFadeLength = horizontalFadeLength;
            _sampleMargin = sampleMargin;
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
            var margin = (int)Math.Ceiling(_sampleMargin * (scaleX + scaleY) / 2);
            var visible = CaptureRegion(deviceBounds, margin, canvas.DeviceClipBounds);
            if (visible.Width <= 0 || visible.Height <= 0)
                return;

            var grContext = lease.GrContext;
            // State made for another context is released under that context, never under this one.
            if (!ReferenceEquals(grContext, _backdropContext))
                Release();

            if (grContext is not null && _backdropPlatformContext is null)
            {
                // The nested lease temporarily forbids Skia calls; end it before capturing or drawing.
                using var platformLease = lease.TryLeasePlatformGraphicsApi();
                _backdropPlatformContext = platformLease?.Context;
            }

            var hasCache = (_backdrop is not null || _captureImage is not null) && deviceBounds == _cachedBounds;
            var full = ShouldRecompute(hasCache, _cachedVisible, visible);
            if (full)
            {
                _cachedVisible = visible;
                _cachedBounds = deviceBounds;
            }

            if (!Capture(surface, grContext, visible, full))
            {
                Release();
                return;
            }

            // Keep the full input (including overscan) for the kernel, but only produce pixels this
            // frame can replace. In particular, a caret-sized redraw need not filter the whole glass.
            var output = SKRectI.Intersect(visible, deviceBounds);
            using var frost = Blur(grContext, (scaleX + scaleY) / 2, output, out var source, out var offset);
            if (frost is null)
            {
                Release();
                return;
            }

            if (!full)
                RequestHeal();

            var destination = SKRect.Create(_cachedVisible.Left + offset.X, _cachedVisible.Top + offset.Y,
                source.Width, source.Height);
            Draw(canvas, frost, source, destination, lease.CurrentOpacity, device, scaleX, scaleY);
        }

        private bool Capture(SKSurface surface, GRContext? grContext, SKRectI region, bool full)
        {
            if (full)
            {
                _backdrop?.Dispose();
                _backdrop = null;
                _captureImage?.Dispose();
                _captureImage = surface.Snapshot(region);
                _backdropContext = grContext;
                return _captureImage is not null;
            }

            if (_captureImage is { } image)
            {
                _backdrop = grContext is null ? SKSurface.Create(image.Info) : SKSurface.Create(grContext, true, image.Info);
                if (_backdrop is null)
                    return false;
                _backdrop.Canvas.DrawImage(image, 0, 0, _copyPaint);
                _captureImage = null;
                image.Dispose();
            }

            using var patch = surface.Snapshot(region);
            if (patch is null || _backdrop is null)
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

        private SKImage? Blur(GRContext? grContext, float scale, SKRectI output, out SKRectI source, out SKPointI offset)
        {
            using var buffered = _backdrop?.Snapshot();
            var backdrop = _captureImage ?? buffered;
            if (backdrop is null)
            {
                source = default;
                offset = default;
                return null;
            }

            var filter = GetFilter((float)(_blurRadius * scale));
            var input = new SKRectI(0, 0, backdrop.Width, backdrop.Height);
            output.Offset(-_cachedVisible.Left, -_cachedVisible.Top);
            return grContext is null
                ? backdrop.ApplyImageFilter(filter, input, output, out source, out offset)
                : backdrop.ApplyImageFilter(grContext, filter, input, output, out source, out offset);
        }

        private void Draw(SKCanvas canvas, SKImage frost, SKRectI source, SKRect destination,
            double opacity, SKRect device, float scaleX, float scaleY)
        {
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
                using var ramp = CreateFadeMask(device, _fade,
                    (float)(_fadeLength * scaleY), (float)(_horizontalFadeLength * scaleX));
                if (ramp is null)
                {
                    canvas.DrawRect(destination, erase);
                    canvas.DrawImage(frost, source, destination, paint);
                    return;
                }

                using var image = frost.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                    SKMatrix.CreateTranslation(destination.Left - source.Left, destination.Top - source.Top));
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
        /// Lets go of the frost under its owning context, even if no glass will render again. The compositor
        /// disposes replaced operations before making a context current, so the release must establish one.
        /// </summary>
        private void Release()
        {
            if (_backdropContext is { } context)
            {
                s_retired.Retire(context, _backdrop, _backdropPlatformContext);
                s_retired.Retire(context, _captureImage, _backdropPlatformContext);
            }
            else
            {
                _backdrop?.Dispose();
                _captureImage?.Dispose();
            }

            _backdrop = null;
            _captureImage = null;
            _backdropContext = null;
            _backdropPlatformContext = null;
        }
    }
}
