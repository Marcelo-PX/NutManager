using System.Numerics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace NutManager.App.Presentation.Controls;

/// <summary>
/// The small set of looping effects the navigation glyphs use, expressed once.
///
/// Styles cannot do this: a keyframe <c>Animation</c> aimed at <c>RenderTransform</c> throws
/// "no animator registered" at startup, even with <c>TransformOperations</c> values, because the
/// transition and the animator are different mechanisms and only the former supports it. These run
/// on the compositor instead, which also keeps them off the UI thread. Every loop is started when a
/// row becomes active and stopped when it stops being active, so an idle sidebar animates nothing.
/// </summary>
public static class NutIconMotion
{
    private const string OffsetProperty = "Offset";
    private const string OpacityProperty = "Opacity";
    private const string RotationProperty = "RotationAngle";
    private const string ScaleProperty = "Scale";

    private static readonly Easing Smooth = new SineEaseInOut();

    /// <summary>Blinks a layer between full and near-off, like an activity light.</summary>
    public static void Blink(Visual target, TimeSpan period, TimeSpan delay, double low = 0.12)
    {
        if (Visual(target) is not { } visual) return;
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Target = OpacityProperty;
        animation.Duration = period;
        animation.DelayTime = delay;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0f, 1f);
        animation.InsertKeyFrame(0.42f, (float)low);
        animation.InsertKeyFrame(0.52f, 1f);
        animation.InsertKeyFrame(1f, 1f);
        visual.StartAnimation(OpacityProperty, animation);
    }

    /// <summary>Slides a layer along X and back, for knobs travelling their track.</summary>
    public static void Slide(Visual target, double distance, TimeSpan period)
    {
        if (Visual(target) is not { } visual) return;
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.Target = OffsetProperty;
        animation.Duration = period;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0f, new Vector3D(0, 0, 0), Smooth);
        animation.InsertKeyFrame(0.5f, new Vector3D(distance, 0, 0), Smooth);
        animation.InsertKeyFrame(1f, new Vector3D(0, 0, 0), Smooth);
        visual.StartAnimation(OffsetProperty, animation);
    }

    /// <summary>Sweeps a layer across X in one direction and restarts, like a trace being read.</summary>
    public static void Sweep(Visual target, double from, double to, TimeSpan period)
    {
        if (Visual(target) is not { } visual) return;
        var offset = visual.Compositor.CreateVector3DKeyFrameAnimation();
        offset.Target = OffsetProperty;
        offset.Duration = period;
        offset.IterationBehavior = AnimationIterationBehavior.Forever;
        offset.InsertKeyFrame(0f, new Vector3D(from, 0, 0));
        offset.InsertKeyFrame(1f, new Vector3D(to, 0, 0));
        visual.StartAnimation(OffsetProperty, offset);

        // Fading in at the start and out at the end hides the jump back to the beginning.
        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.Target = OpacityProperty;
        opacity.Duration = period;
        opacity.IterationBehavior = AnimationIterationBehavior.Forever;
        opacity.InsertKeyFrame(0f, 0f);
        opacity.InsertKeyFrame(0.18f, 1f);
        opacity.InsertKeyFrame(0.82f, 1f);
        opacity.InsertKeyFrame(1f, 0f);
        visual.StartAnimation(OpacityProperty, opacity);
    }

    /// <summary>Turns a layer continuously. Radians: the compositor has no degree-based angle.</summary>
    public static void Spin(Visual target, Size size, TimeSpan period)
    {
        if (Visual(target) is not { } visual) return;
        visual.CenterPoint = new Vector3D(size.Width / 2, size.Height / 2, 0);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Target = RotationProperty;
        animation.Duration = period;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0f, 0f, new LinearEasing());
        animation.InsertKeyFrame(1f, (float)(Math.PI * 2), new LinearEasing());
        visual.StartAnimation(RotationProperty, animation);
    }

    /// <summary>Breathes a layer's size a little, for a bar that is filling.</summary>
    public static void Breathe(Visual target, Size size, double peak, TimeSpan period)
    {
        if (Visual(target) is not { } visual) return;
        visual.CenterPoint = new Vector3D(size.Width / 2, size.Height / 2, 0);
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.Target = ScaleProperty;
        animation.Duration = period;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0f, new Vector3D(1, 1, 1), Smooth);
        animation.InsertKeyFrame(0.5f, new Vector3D(peak, peak, 1), Smooth);
        animation.InsertKeyFrame(1f, new Vector3D(1, 1, 1), Smooth);
        visual.StartAnimation(ScaleProperty, animation);
    }

    /// <summary>Moves a layer up and settles it once, for non-looping administrative feedback.</summary>
    public static void NudgeOnce(Visual target, double distance, TimeSpan duration)
    {
        if (Visual(target) is not { } visual) return;
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.Target = OffsetProperty;
        animation.Duration = duration;
        animation.IterationCount = 1;
        animation.InsertKeyFrame(0f, new Vector3D(0, 0, 0), Smooth);
        animation.InsertKeyFrame(0.45f, new Vector3D(0, distance, 0), Smooth);
        animation.InsertKeyFrame(1f, new Vector3D(0, 0, 0), Smooth);
        visual.StartAnimation(OffsetProperty, animation);
    }

    /// <summary>Scales and fades a detail layer once, without becoming a notification loop.</summary>
    public static void PopOnce(Visual target, Size size, double peak, TimeSpan duration)
    {
        if (Visual(target) is not { } visual) return;
        visual.CenterPoint = new Vector3D(size.Width / 2, size.Height / 2, 0);

        var scale = visual.Compositor.CreateVector3DKeyFrameAnimation();
        scale.Target = ScaleProperty;
        scale.Duration = duration;
        scale.IterationCount = 1;
        scale.InsertKeyFrame(0f, new Vector3D(0.86, 0.86, 1), Smooth);
        scale.InsertKeyFrame(0.55f, new Vector3D(peak, peak, 1), Smooth);
        scale.InsertKeyFrame(1f, new Vector3D(1, 1, 1), Smooth);
        visual.StartAnimation(ScaleProperty, scale);

        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.Target = OpacityProperty;
        opacity.Duration = duration;
        opacity.IterationCount = 1;
        opacity.InsertKeyFrame(0f, 0.25f, Smooth);
        opacity.InsertKeyFrame(0.4f, 1f, Smooth);
        opacity.InsertKeyFrame(1f, 1f, Smooth);
        visual.StartAnimation(OpacityProperty, opacity);
    }

    /// <summary>Breathes a layer's opacity between two levels, for a halo behind a live reading.</summary>
    public static void Glow(Visual target, double low, double high, TimeSpan period)
    {
        if (Visual(target) is not { } visual) return;
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Target = OpacityProperty;
        animation.Duration = period;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0f, (float)low, Smooth);
        animation.InsertKeyFrame(0.5f, (float)high, Smooth);
        animation.InsertKeyFrame(1f, (float)low, Smooth);
        visual.StartAnimation(OpacityProperty, animation);
    }

    /// <summary>Stops every loop on a layer and returns it to its authored position.</summary>
    public static void Reset(Visual target, double restingOpacity)
    {
        if (Visual(target) is not { } visual) return;
        visual.StopAnimation(OffsetProperty);
        visual.StopAnimation(OpacityProperty);
        visual.StopAnimation(RotationProperty);
        visual.StopAnimation(ScaleProperty);
        visual.Offset = new Vector3D(0, 0, 0);
        visual.RotationAngle = 0f;
        visual.Scale = new Vector3D(1, 1, 1);
        visual.Opacity = (float)restingOpacity;
    }

    private static CompositionVisual? Visual(Visual target) => ElementComposition.GetElementVisual(target);
}
