namespace AuraMusic.Mobile.Controls;

/// <summary>
/// Matrix-style spectrum: green bars drawn as plain rectangles under a fixed stencil with glyph-shaped holes,
/// so each band reads as a column of letters lighting up, for ~30 draw calls a frame instead of 128 glyphs.
/// Feed it with <see cref="Post"/> from any thread.
/// </summary>
public sealed class SpectrumView : ContentView
{
    readonly BarsDrawable bars = new();
    readonly GraphicsView canvas;
    float[]? latest;
    int redrawQueued;

    public SpectrumView()
    {
        InputTransparent = true;
        canvas = new GraphicsView { Drawable = bars };
        Content = new Grid
        {
            Children =
            {
                canvas,
                new Image { Source = "spectrum_stencil.png", Aspect = Aspect.Fill }, // 16 × 8 cells, like the bars
            },
        };
    }

    /// <summary>
    /// Safe from any thread. Only the newest levels are kept and at most one redraw is queued, so a slow
    /// phone shows the music one frame late at worst instead of falling further and further behind.
    /// </summary>
    public void Post(float[] levels)
    {
        Volatile.Write(ref latest, levels);
        if (Interlocked.Exchange(ref redrawQueued, 1) == 0)
            Dispatcher.Dispatch(DrawLatest);
    }

    public void Clear()
    {
        bars.Push(new float[SpectrumHub.Bands], decay: 0);
        canvas.Invalidate();
    }

    void DrawLatest()
    {
        Volatile.Write(ref redrawQueued, 0);
        if (Interlocked.Exchange(ref latest, null) is { } levels)
        {
            bars.Push(levels);
            canvas.Invalidate();
        }
    }

    sealed class BarsDrawable : IDrawable
    {
        public const int Rows = 8; // must match spectrum_stencil.png
        const float Decay = 0.82f; // falls smoothly instead of flickering with every frame

        static readonly Color Unlit = Theme.Color("MatrixSpectrumUnlit");
        static readonly Color Lit = Theme.Color("MatrixSpectrumLit");
        static readonly Color Head = Theme.Color("MatrixHead");

        readonly float[] shown = new float[SpectrumHub.Bands];

        public void Push(float[] levels, float decay = Decay)
        {
            for (int band = 0; band < shown.Length && band < levels.Length; band++)
                shown[band] = MathF.Max(levels[band], shown[band] * decay);
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float columnWidth = dirtyRect.Width / shown.Length;
            float rowHeight = dirtyRect.Height / Rows;

            canvas.FillColor = Unlit; // unlit letters stay faintly visible
            canvas.FillRectangle(dirtyRect);

            for (int band = 0; band < shown.Length; band++)
            {
                int lit = (int)MathF.Round(shown[band] * Rows); // whole cells, so letters light up fully
                if (lit == 0)
                    continue;
                float x = band * columnWidth;
                float top = dirtyRect.Height - lit * rowHeight;
                canvas.FillColor = Lit;
                canvas.FillRectangle(x, top + rowHeight, columnWidth, (lit - 1) * rowHeight);
                canvas.FillColor = Head;
                canvas.FillRectangle(x, top, columnWidth, rowHeight);
            }
        }
    }
}
