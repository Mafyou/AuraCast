namespace AuraMusic.Mobile.Controls;

/// <summary>
/// Matrix rain background made of pre-rendered, vertically seamless glyph tiles that scroll down forever.
/// Drawing the glyphs live cost ~0.2 ms each and saturated the UI thread; moving an image is nearly free.
/// </summary>
public sealed class MatrixRainView : ContentView
{
    const double TileAspect = 1600.0 / 720; // height / width of rain_*.png

    // Two layers at different speeds give depth: the dim, small glyphs fall slower.
    readonly Layer far = new("rain_far.png", TimeSpan.FromSeconds(28));
    readonly Layer near = new("rain_near.png", TimeSpan.FromSeconds(16));

    public MatrixRainView()
    {
        InputTransparent = true;
        IsClippedToBounds = true;
        Content = new Grid { Children = { far.Tiles, near.Tiles } };
        SizeChanged += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    void Start()
    {
        if (Width <= 0)
            return;
        double tile = Width * TileAspect;
        far.Start(Width, tile);
        near.Start(Width, tile);
    }

    void Stop()
    {
        far.Stop();
        near.Stop();
    }

    sealed class Layer(string source, TimeSpan perTile)
    {
        const string AnimationName = "rain";

        // Two copies stacked: while one scrolls out at the bottom, the other fills the top.
        public VerticalStackLayout Tiles { get; } = new()
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Start,
            Children = { new Image { Source = source, Aspect = Aspect.Fill }, new Image { Source = source, Aspect = Aspect.Fill } },
        };

        public void Start(double width, double tileHeight)
        {
            foreach (var image in Tiles.Children.Cast<Image>())
                (image.WidthRequest, image.HeightRequest) = (width, tileHeight);

            Stop();
            Tiles.Animate(AnimationName, new Animation(offset => Tiles.TranslationY = offset, -tileHeight, 0),
                rate: 32, length: (uint)perTile.TotalMilliseconds, easing: Easing.Linear, repeat: () => true);
        }

        public void Stop() => Tiles.AbortAnimation(AnimationName);
    }
}
