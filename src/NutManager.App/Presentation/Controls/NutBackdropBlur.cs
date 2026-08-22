using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace NutManager.App.Presentation.Controls;

/// <summary>
/// A pane that blurs whatever the application has already drawn behind it.
///
/// Avalonia has no backdrop filter, and the three things that look like one are not: a blur effect
/// blurs the element together with its own children, the acrylic material reaches the window's
/// backdrop rather than the application's content, and a <c>VisualBrush</c> pointed at a visual that
/// is already in the tree does not paint it at all.
///
/// What does work is the one thing Skia exposes directly. By the time a control renders, the surface
/// already holds everything drawn before it, so a snapshot of that surface is the backdrop — the real
/// pixels of the page underneath, not a second rendering of it. Blurring the snapshot and painting it
/// back over the same rectangle is a backdrop filter in the only sense that matters here.
///
/// The approach is Nikita Tsukanov's, by way of the control in rocksdanister/weather. It is a custom
/// draw operation rather than anything Avalonia supports as a feature, which is worth knowing before
/// relying on it: it reads the frame buffer every time it renders, so it belongs on a small, fixed
/// band and not on a large or frequently invalidated surface.
/// </summary>
public sealed class NutBackdropBlur : Control
{
    /// <summary>How far the blur reaches, in device pixels.</summary>
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<NutBackdropBlur, double>(nameof(Radius), 12d);

    /// <summary>The glass tint laid over the blur. Alpha included; transparent means blur alone.</summary>
    public static readonly StyledProperty<Color> TintProperty =
        AvaloniaProperty.Register<NutBackdropBlur, Color>(nameof(Tint), Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public Color Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    static NutBackdropBlur() => AffectsRender<NutBackdropBlur>(RadiusProperty, TintProperty);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(default, Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        context.Custom(new BlurBehind(bounds, Radius, Tint));
    }

    /// <summary>
    /// Snapshot, blur, paint back.
    ///
    /// The canvas transform has to be inverted before the snapshot is used as a shader: the snapshot
    /// is in surface coordinates and the drawing happens in this control's, so without the inverse
    /// the backdrop would be sampled from the wrong place on screen.
    /// </summary>
    private sealed class BlurBehind : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly double _radius;
        private readonly Color _tint;

        internal BlurBehind(Rect bounds, double radius, Color tint)
        {
            _bounds = bounds;
            _radius = radius;
            _tint = tint;
        }

        /// <summary>Inflated so the blur's own spill is included when the region is invalidated.</summary>
        public Rect Bounds => _bounds.Inflate(4);

        /// <summary>Transparent to the pointer: the page underneath keeps its clicks.</summary>
        public bool HitTest(Point point) => false;

        public void Dispose()
        {
        }

        public bool Equals(ICustomDrawOperation? other) =>
            other is BlurBehind operation &&
            operation._bounds == _bounds &&
            operation._radius.Equals(_radius) &&
            operation._tint == _tint;

        public void Render(ImmediateDrawingContext context)
        {
            // Absent on any backend that is not Skia. Drawing nothing is the right answer there:
            // the band simply stops frosting rather than the window failing to render.
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } leaseFeature) return;

            using var lease = leaseFeature.Lease();
            // The surface is absent while the lease is serving a non-surface target, and there is
            // nothing to snapshot then.
            if (lease.SkSurface is not { } surface) return;
            if (!lease.SkCanvas.TotalMatrix.TryInvert(out var inverted)) return;

            var width = (int)Math.Ceiling(_bounds.Width);
            var height = (int)Math.Ceiling(_bounds.Height);
            if (width <= 0 || height <= 0) return;

            using var backdrop = surface.Snapshot();
            using var backdropShader = SKShader.CreateImage(
                backdrop, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, inverted);
            using var blurred = SKSurface.Create(
                lease.GrContext,
                false,
                new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
            if (blurred is null) return;

            var radius = (float)Math.Max(0.1d, _radius);
            using (var filter = SKImageFilter.CreateBlur(radius, radius, SKShaderTileMode.Clamp))
            using (var paint = new SKPaint { Shader = backdropShader, ImageFilter = filter })
            {
                blurred.Canvas.DrawRect(0, 0, width, height, paint);
            }

            using (var snapshot = blurred.Snapshot())
            using (var shader = SKShader.CreateImage(snapshot))
            using (var paint = new SKPaint { Shader = shader, IsAntialias = true })
            {
                lease.SkCanvas.DrawRect(0, 0, width, height, paint);
            }

            if (_tint.A == 0) return;

            using (var paint = new SKPaint
            {
                Color = new SKColor(_tint.R, _tint.G, _tint.B, _tint.A),
                IsAntialias = true
            })
            {
                lease.SkCanvas.DrawRect(0, 0, width, height, paint);
            }
        }
    }
}
