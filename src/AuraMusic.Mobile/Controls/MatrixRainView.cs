namespace AuraMusic.Mobile.Controls;

/// <summary>
/// Falling-glyph background. It only animates while on screen and is meant to be shown dimmed,
/// so whatever sits on top stays readable.
/// </summary>
public sealed class MatrixRainView : GraphicsView
{
    readonly MatrixRain rain = new();
    readonly IDispatcherTimer timer;

    public MatrixRainView()
    {
        Drawable = rain;
        InputTransparent = true;

        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(80); // a background: 12 fps is plenty and spares slower phones
        timer.Tick += (_, _) =>
        {
            rain.Step();
            Invalidate();
        };
        Loaded += (_, _) => timer.Start();
        Unloaded += (_, _) => timer.Stop();
    }

    sealed class MatrixRain : IDrawable
    {
        const float CellSize = 24;
        const int TrailLength = 12;

        static readonly string[] Glyphs = [.. "ｦｱｳｴｵｶｷｹｺｻｼｽｾｿﾀﾂﾃﾅﾆﾇﾈﾊﾋﾎﾏﾐﾑﾒﾓﾔﾕﾗﾘﾜ0123456789:=*+<>".Select(glyph => glyph.ToString())];
        static readonly Color Head = Color.FromArgb("#C8FFE0");
        // Same emerald as the code rain of the splash artwork, so the two blend.
        static readonly Color Trail = Color.FromArgb("#00C853");
        static readonly Microsoft.Maui.Graphics.Font Font = new("monospace");

        readonly Random random = new();
        int columns, rows;
        float[] heads = [];
        float[] speeds = [];
        int[] cells = [];

        public void Step()
        {
            for (int column = 0; column < columns; column++)
            {
                heads[column] += speeds[column];
                if (heads[column] - TrailLength > rows)
                    ResetColumn(column);
            }
            // A few glyphs flicker to something else each frame.
            for (int i = 0; i < cells.Length / 60; i++)
                cells[random.Next(cells.Length)] = random.Next(Glyphs.Length);
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            EnsureGrid(dirtyRect);
            canvas.Font = Font;
            canvas.FontSize = CellSize * 0.8f;

            for (int column = 0; column < columns; column++)
            {
                int head = (int)heads[column];
                for (int t = 0; t < TrailLength; t++)
                {
                    int row = head - t;
                    if (row < 0 || row >= rows)
                        continue;
                    canvas.FontColor = t == 0 ? Head : Trail.WithAlpha(1f - (float)t / TrailLength);
                    canvas.DrawString(Glyphs[cells[row * columns + column]], column * CellSize, row * CellSize, CellSize, CellSize,
                        HorizontalAlignment.Center, VerticalAlignment.Center);
                }
            }
        }

        void EnsureGrid(RectF bounds)
        {
            int newColumns = (int)Math.Ceiling(bounds.Width / CellSize);
            int newRows = (int)Math.Ceiling(bounds.Height / CellSize);
            if (newColumns == columns && newRows == rows)
                return;

            (columns, rows) = (newColumns, newRows);
            heads = new float[columns];
            speeds = new float[columns];
            cells = [.. Enumerable.Range(0, columns * rows).Select(_ => random.Next(Glyphs.Length))];
            for (int column = 0; column < columns; column++)
            {
                ResetColumn(column);
                heads[column] = random.Next(-rows, rows); // already mid-fall on the first frame
            }
        }

        void ResetColumn(int column)
        {
            heads[column] = -random.Next(TrailLength);
            speeds[column] = 0.25f + random.NextSingle() * 0.6f;
        }
    }
}
