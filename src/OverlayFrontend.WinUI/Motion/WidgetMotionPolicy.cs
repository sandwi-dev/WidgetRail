using System.Numerics;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

internal readonly record struct WidgetMotionOptions(WidgetSectionAnimation Section, WidgetModalAnimation Modal,
    WidgetFocusAnimation Focus, bool AnimateDialogs, bool Reduced, double Speed)
{
    internal static WidgetMotionOptions From(AppearanceSettings appearance, bool systemAnimationsEnabled)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        var reduced = appearance.Motion == MotionPreference.Reduced ||
            appearance.Motion == MotionPreference.System && !systemAnimationsEnabled;
        var speed = double.IsFinite(appearance.WidgetAnimationSpeed)
            ? Math.Clamp(appearance.WidgetAnimationSpeed, AppearanceSettings.MinimumWidgetAnimationSpeed,
                AppearanceSettings.MaximumWidgetAnimationSpeed) : 1;
        return new(appearance.SectionAnimation, appearance.ModalAnimation, appearance.FocusAnimation,
            appearance.AnimateWidgetModals, reduced, speed);
    }

    internal TimeSpan Duration(double milliseconds) => Reduced ? TimeSpan.Zero : TimeSpan.FromMilliseconds(milliseconds /
        (double.IsFinite(Speed) ? Math.Clamp(Speed, .5, 2) : 1));
}

/// <summary>Viewport-local presentation only. Insets are outside the content transform.</summary>
internal readonly record struct WidgetMotionPose(Vector3 Translation, Vector3 Scale, float Opacity, Vector4 Insets)
{
    internal static WidgetMotionPose Identity => new(Vector3.Zero, Vector3.One, 1, Vector4.Zero);
}

internal readonly record struct WidgetMotionRecipe(WidgetMotionPose From, WidgetMotionPose To, TimeSpan Duration);
internal readonly record struct WidgetSectionMotion(WidgetMotionRecipe Incoming, WidgetMotionRecipe Outgoing);

/// <summary>Existing global preset meaning, independent of controls and compositor resources.</summary>
internal static class WidgetMotionPolicy
{
    internal static WidgetSectionMotion Section(WidgetMotionOptions options, Vector2 viewport, int direction)
    {
        ValidateSize(viewport);
        var incoming = WidgetMotionPose.Identity;
        var outgoing = WidgetMotionPose.Identity;
        var sign = direction < 0 ? -1 : 1;
        var duration = options.Duration(options.Section == WidgetSectionAnimation.None ? 0 : 208);
        if (duration == TimeSpan.Zero) return new(new(incoming, incoming, duration), new(outgoing, outgoing, duration));
        switch (options.Section)
        {
            case WidgetSectionAnimation.Slide:
                incoming = incoming with { Translation = new(sign * viewport.X, 0, 0) };
                outgoing = outgoing with { Translation = new(-sign * viewport.X, 0, 0) }; break;
            case WidgetSectionAnimation.VerticalSlide:
                incoming = incoming with { Translation = new(0, sign * viewport.Y, 0) };
                outgoing = outgoing with { Translation = new(0, -sign * viewport.Y, 0) }; break;
            case WidgetSectionAnimation.Reveal:
            case WidgetSectionAnimation.CoverSlide:
                incoming = incoming with { Insets = sign > 0 ? new(viewport.X, 0, 0, 0) : new(0, 0, viewport.X, 0) };
                outgoing = outgoing with { Insets = sign > 0 ? new(0, 0, viewport.X, 0) : new(viewport.X, 0, 0, 0) };
                if (options.Section == WidgetSectionAnimation.CoverSlide)
                    incoming = incoming with { Translation = new(sign * viewport.X, 0, 0) };
                break;
            case WidgetSectionAnimation.Paging:
                incoming = incoming with { Translation = new(0, viewport.Y, 0) };
                outgoing = outgoing with { Translation = new(0, -viewport.Y * .08f, 0), Scale = new(.96f, .96f, 1),
                    Insets = new(0, 0, 0, viewport.Y) }; break;
            default: throw new ArgumentOutOfRangeException(nameof(options));
        }
        return new(new(incoming, WidgetMotionPose.Identity, duration), new(WidgetMotionPose.Identity, outgoing, duration));
    }

    internal static WidgetMotionRecipe Dialog(WidgetMotionOptions options, bool opening, bool scrim = false)
    {
        var hidden = WidgetMotionPose.Identity with { Opacity = 0 };
        if (!scrim) hidden = hidden with { Translation = new(0, 14, 0),
            Scale = options.Modal == WidgetModalAnimation.Zoom ? new(.9f, .9f, 1) : Vector3.One };
        var duration = options.Duration(options.AnimateDialogs ? 260 : 0);
        return opening ? new(hidden, WidgetMotionPose.Identity, duration) : new(WidgetMotionPose.Identity, hidden, duration);
    }

    /// <summary>Apply only to the focus decoration. Text/artwork and WRSS control scale are separate.</summary>
    internal static WidgetMotionRecipe Focus(WidgetMotionOptions options, Vector2 size, bool entering)
    {
        ValidateSize(size);
        var hidden = WidgetMotionPose.Identity with { Opacity = 0 };
        var duration = options.Duration(options.Focus switch
        {
            WidgetFocusAnimation.Fade => 240, WidgetFocusAnimation.Settle => 220,
            WidgetFocusAnimation.None => 0, _ => throw new ArgumentOutOfRangeException(nameof(options)),
        });
        if (entering && options.Focus == WidgetFocusAnimation.Settle)
        {
            var inset = Math.Min(Math.Min(size.X, size.Y) * .15f, 6);
            hidden = hidden with { Scale = new(1 - 2 * inset / size.X, 1 - 2 * inset / size.Y, 1), Opacity = .65f };
        }
        return entering ? new(hidden, WidgetMotionPose.Identity, duration) :
            new(WidgetMotionPose.Identity, WidgetMotionPose.Identity with { Opacity = 0 }, duration);
    }

    /// <summary>Headers translate without resizing glyphs; selection surfaces may also resize.</summary>
    internal static WidgetMotionRecipe Layout(WidgetMotionOptions options, Vector2 displacement, Vector2 priorSize,
        Vector2 currentSize, bool selectionSurface)
    {
        ValidateSize(priorSize); ValidateSize(currentSize);
        if (!float.IsFinite(displacement.X) || !float.IsFinite(displacement.Y)) throw new ArgumentOutOfRangeException(nameof(displacement));
        // The compositor target scales around the current surface center.
        // Compensate that pivot so the old selection's top-left stays exact.
        var translation = selectionSurface ? displacement + (priorSize - currentSize) / 2 : displacement;
        var from = WidgetMotionPose.Identity with { Translation = new(translation, 0),
            Scale = selectionSurface ? new(priorSize.X / currentSize.X, priorSize.Y / currentSize.Y, 1) : Vector3.One };
        return new(from, WidgetMotionPose.Identity, options.Duration(options.Section == WidgetSectionAnimation.None ? 0 : 208));
    }

    private static void ValidateSize(Vector2 size)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
    }
}
