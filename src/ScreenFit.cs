using System;

public static partial class Theme
{
    const float MinScale = 0.6f;
    const int ScreenMargin = 16;

    // Liefert die Skalierung, bei der ein Fenster (gemessen bei 'scale') in den Arbeitsbereich passt.
    // Vergrößert nie, verkleinert höchstens bis 60 %.
    public static float FitScale(float scale, int windowWidth, int windowHeight, int workWidth, int workHeight)
    {
        float ratio = Math.Min(1f, Math.Min((workHeight - ScreenMargin) / (float)windowHeight,
                                            (workWidth - ScreenMargin) / (float)windowWidth));
        return Math.Max(MinScale, scale * ratio);
    }
}
