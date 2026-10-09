namespace AuraMusic.Mobile.Controls;

/// <summary>
/// Matrix-style spectrum: one column of glyphs per frequency band, lit from the bottom up.
/// Feed it with <see cref="Post"/> from any thread.
/// </summary>
public sealed class SpectrumView : GraphicsView
{
    readonly SpectrumDrawable spectrum = new();
    float[]? latest;
    int redrawQueued;

    public SpectrumView()
    {
        Drawable = spectrum;
        InputTransparent = true;
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

    void DrawLatest()
    {
        Volatile.Write(ref redrawQueued, 0);
        if (Interlocked.Exchange(ref latest, null) is { } levels)
        {
            spectrum.Push(levels);
            Invalidate();
        }
    }

    public void Clear()
    {
        spectrum.Push(new float[SpectrumHub.Bands], decay: 0);
        Invalidate();
    }

    sealed class SpectrumDrawable : IDrawable
    {
        const float CellHeight = 15;
        const float Decay = 0.82f; // falls smoothly instead of flickering with every frame

        static readonly string[] Glyphs = [.. "ｦｱｳｴｵｶｷｹｺｻｼｽｾｿﾀﾂﾃﾅﾆﾇﾈﾊﾋﾎﾏﾐﾑﾒﾓﾔﾕﾗﾘﾜ0123456789".Select(glyph => glyph.ToString())];
        static readonly Color Head = Color.FromArgb("#C8FFE0");
        static readonly Color Lit = Color.FromArgb("#00C853");
        static readonly Microsoft.Maui.Graphics.Font Font = new("monospace");

        readonly float[] shown = new float[SpectrumHub.Bands];
        readonly Random random = new();
        int[] cells = [];
        int rows;

        public void Push(float[] levels, float decay = Decay)
        {
            for (int band = 0; band < shown.Length && band < levels.Length; band++)
                shown[band] = MathF.Max(levels[band], shown[band] * decay);
            for (int i = 0; i < cells.Length / 12; i++)
                cells[random.Next(cells.Length)] = random.Next(Glyphs.Length);
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            int newRows = Math.Max(1, (int)(dirtyRect.Height / CellHeight));
            if (newRows != rows)
            {
                rows = newRows;
                cells = [.. Enumerable.Range(0, rows * shown.Length).Select(_ => random.Next(Glyphs.Length))];
            }

            float columnWidth = dirtyRect.Width / shown.Length;
            canvas.Font = Font;
            canvas.FontSize = CellHeight * 0.85f;

            for (int band = 0; band < shown.Length; band++)
            {
                int lit = (int)MathF.Round(shown[band] * rows);
                for (int row = 0; row < rows; row++) // row 0 is the bottom
                {
                    bool on = row < lit;
                    canvas.FontColor = !on ? Lit.WithAlpha(0.08f)
                        : row == lit - 1 ? Head
                        : Lit.WithAlpha(0.45f + 0.55f * row / rows);
                    float y = dirtyRect.Height - (row + 1) * CellHeight;
                    canvas.DrawString(Glyphs[cells[row * shown.Length + band]], band * columnWidth, y, columnWidth, CellHeight,
                        HorizontalAlignment.Center, VerticalAlignment.Center);
                }
            }
        }
    }
}
