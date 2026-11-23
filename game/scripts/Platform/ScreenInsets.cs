using Godot;

namespace Bloom.Game.Platform;

/// <summary>How far the layout stays in from each edge of the screen, in the viewport's units.</summary>
internal readonly record struct Insets(float Left, float Top, float Right, float Bottom);

/// <summary>
/// The parts of a phone's screen that the layout must stay out of. The game draws edge to edge (Android's
/// immersive mode), so the camera cutout and the rounded corners sit over it. Android reports the cutout
/// through Godot's display safe area. Godot can't read the rounded corners, so on a phone the layout also
/// keeps <see cref="CornerDp"/> clear at the top and the bottom.
/// </summary>
internal static class ScreenInsets
{
    /// <summary>
    /// Room kept clear above and below the layout on a phone, in dp. With the layout's side margin, it keeps
    /// the corners of the top and bottom rows inside a rounded screen corner of up to about 55 dp radius.
    /// </summary>
    public const float CornerDp = 28f;

    /// <summary>The insets for the current screen; all zero on desktop, where a window has no cutout or corners.</summary>
    public static Insets For(Viewport viewport)
    {
        if (!OS.HasFeature("mobile"))
        {
            return default;
        }

        var window = DisplayServer.WindowGetSize();
        if (window.X <= 0 || window.Y <= 0)
        {
            return default;
        }

        // The safe area is in window pixels. An empty one (no insets known yet) counts as the whole window.
        var safe = DisplayServer.GetDisplaySafeArea();
        if (safe.Size.X <= 0 || safe.Size.Y <= 0)
        {
            safe = new Rect2I(Vector2I.Zero, window);
        }

        float corner = CornerDp * DisplayServer.ScreenGetDpi() / 160f;
        float toViewport = viewport.GetVisibleRect().Size.X / window.X;
        return new Insets(
            Left: Mathf.Max(0, safe.Position.X) * toViewport,
            Top: Mathf.Max(safe.Position.Y, corner) * toViewport,
            Right: Mathf.Max(0, window.X - safe.End.X) * toViewport,
            Bottom: Mathf.Max(window.Y - safe.End.Y, corner) * toViewport);
    }
}
