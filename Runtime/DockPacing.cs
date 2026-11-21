namespace DesktopDock
{
    /// <summary>Chooses the frame rate and render interval for the dock's current situation. Pure, so it's unit-tested.</summary>
    internal static class DockPacing
    {
        /// <param name="focused">The game has keyboard focus.</param>
        /// <param name="pointerInside">The cursor is over the dock window.</param>
        /// <param name="unseen">The dock can't be seen: hidden, tucked away by auto-hide, or behind a fullscreen app.</param>
        /// <returns>Target frame rate (-1 = platform default) and render interval (1 = every frame).</returns>
        public static (int frameRate, int renderInterval) Choose(DockConfig c, bool focused, bool pointerInside, bool unseen)
        {
            if (unseen)
                return (c.hiddenFrameRate > 0 ? c.hiddenFrameRate : c.backgroundFrameRate, 1);
            if (focused)
                return (c.focusedFrameRate, 1);
            // Something the player is pointing at should animate smoothly.
            if (pointerInside)
                return (c.backgroundFrameRate, 1);
            return (c.backgroundFrameRate, c.backgroundRenderInterval < 1 ? 1 : c.backgroundRenderInterval);
        }
    }
}
