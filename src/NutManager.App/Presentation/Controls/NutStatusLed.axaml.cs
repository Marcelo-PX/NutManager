using System.Numerics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace NutManager.App.Presentation.Controls;

/// <summary>How the indicator should read. Mirrors the connection states the shell already exposes.</summary>
public enum NutLedState
{
    Unavailable,
    Healthy,
    Pending,
    Critical
}

/// <summary>
/// The shell's status light. The breathing halo is the only continuous animation in the
/// application and it is confined to this control: it runs on the render thread through the
/// Composition API, so there is no timer and no UI-thread work per frame. Healthy, pending, and
/// critical states keep a static semantic glow; the healthy state alone breathes. The core remains
/// stable throughout, so disabling motion never removes the state cue.
/// </summary>
public partial class NutStatusLed : UserControl
{
    private const string ScaleTarget = "Scale";
    private const string OpacityTarget = "Opacity";

    public static readonly StyledProperty<NutLedState> StateProperty =
        AvaloniaProperty.Register<NutStatusLed, NutLedState>(nameof(State));

    private bool _pulseRunning;

    public NutStatusLed()
    {
        InitializeComponent();
        PointerEntered += (_, _) => ApplyHover(true);
        PointerExited += (_, _) => ApplyHover(false);
    }

    public NutLedState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>
    /// Colour for every layer. Redundant with the state text beside the control, never the only cue.
    /// </summary>
    public IBrush? StateBrush => this.FindResource(State switch
    {
        // Healthy uses the LED's own green rather than the shared healthy token: a small lit ball
        // needs more saturation to read as lit than badge text does.
        NutLedState.Healthy => "NutLedHealthyBrush",
        NutLedState.Pending => "NutWarningBrush",
        NutLedState.Critical => "NutCriticalBrush",
        _ => "NutUnavailableBrush"
    }) as IBrush;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopPulse();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != StateProperty) return;
        RaisePropertyChanged(StateBrushProperty, default, default);
        ApplyState();
    }

    private void ApplyState()
    {
        var period = PulsePeriodFor(State);

        // The shadow colour lives in a style, so the state is handed to both halo layers as a class.
        ApplyStateClasses(AmbientHalo);
        ApplyStateClasses(Halo);
        AmbientHalo.Opacity = State switch
        {
            NutLedState.Healthy => 0.58,
            NutLedState.Pending => 0.42,
            NutLedState.Critical => 0.48,
            _ => 0
        };
        Highlight.Opacity = State == NutLedState.Unavailable ? 0.18 : 0.34;

        if (period == TimeSpan.Zero)
        {
            StopPulse();
            return;
        }

        StartPulse(period);
    }

    public static TimeSpan PulsePeriodFor(NutLedState state) => state switch
    {
        NutLedState.Healthy => TimeSpan.FromSeconds(2.0),
        _ => TimeSpan.Zero
    };

    private void ApplyStateClasses(Border halo)
    {
        halo.Classes.Set("healthy", State == NutLedState.Healthy);
        halo.Classes.Set("pending", State == NutLedState.Pending);
        halo.Classes.Set("critical", State == NutLedState.Critical);
    }

    private void StartPulse(TimeSpan period)
    {
        if (ElementComposition.GetElementVisual(Halo) is not { } halo) return;

        // Restarting an already-running animation would visibly jump the halo, so a state that
        // keeps the same period leaves the running one alone.
        if (_pulseRunning && Halo.Tag is TimeSpan current && current == period) return;
        Halo.Tag = period;

        // Scaling is centred on the layer, otherwise the halo would grow towards the bottom right.
        halo.CenterPoint = new Vector3D(Halo.Width / 2, Halo.Height / 2, 0);

        var easing = new SineEaseInOut();
        var scale = halo.Compositor.CreateVector3DKeyFrameAnimation();
        scale.Target = ScaleTarget;
        scale.Duration = period;
        scale.IterationBehavior = AnimationIterationBehavior.Forever;
        scale.InsertKeyFrame(0f, new Vector3D(1, 1, 1), easing);
        scale.InsertKeyFrame(0.5f, new Vector3D(1.65, 1.65, 1), easing);
        scale.InsertKeyFrame(1f, new Vector3D(1, 1, 1), easing);

        var opacity = halo.Compositor.CreateScalarKeyFrameAnimation();
        opacity.Target = OpacityTarget;
        opacity.Duration = period;
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;
        opacity.InsertKeyFrame(0f, 0.12f, easing);
        opacity.InsertKeyFrame(0.5f, 0.82f, easing);
        opacity.InsertKeyFrame(1f, 0.12f, easing);

        halo.StartAnimation(ScaleTarget, scale);
        halo.StartAnimation(OpacityTarget, opacity);

        _pulseRunning = true;
    }

    private void StopPulse()
    {
        Halo.Tag = null;
        _pulseRunning = false;
        if (ElementComposition.GetElementVisual(Halo) is { } halo)
        {
            halo.StopAnimation(ScaleTarget);
            halo.StopAnimation(OpacityTarget);
            halo.Scale = new Vector3D(1, 1, 1);
            halo.Opacity = State == NutLedState.Unavailable ? 0f : 0.75f;
        }

        // The core is intentionally never animated. It remains the stable, non-motion state cue.
    }

    private void ApplyHover(bool over)
    {
        // Hover only deepens what is already there; it never changes the pulse rate, which would
        // make the indicator look like it had changed state.
        Highlight.Opacity = over ? 0.5 : State == NutLedState.Unavailable ? 0.18 : 0.34;
    }

    private static readonly DirectProperty<NutStatusLed, IBrush?> StateBrushProperty =
        AvaloniaProperty.RegisterDirect<NutStatusLed, IBrush?>(nameof(StateBrush), owner => owner.StateBrush);
}
