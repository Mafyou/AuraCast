namespace AuraMusic.Mobile.Controls;

/// <summary>Matrix-styled replacement for <c>DisplayAlertAsync</c>: a title, a message and one or two buttons.</summary>
public sealed class MatrixDialog : Popup<bool>
{
    MatrixDialog(string title, string message, string accept, string? cancel)
    {
        BackgroundColor = Colors.Transparent;
        Padding = 0;
        Margin = new Thickness(24, 0);

        var buttons = new Grid { ColumnSpacing = 12 };
        if (cancel is not null)
        {
            buttons.ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)];
            buttons.Add(CreateButton(cancel, "MatrixButton", result: false), 0);
        }
        buttons.Add(CreateButton(accept, "MatrixPrimaryButton", result: true), cancel is null ? 0 : 1);

        var panel = new Border
        {
            Style = Resource<Style>("MatrixPanelBorder"),
            BackgroundColor = Resource<Color>("MatrixBackground"),
            Stroke = Resource<Color>("MatrixGreen"),
            Padding = new Thickness(20, 18),
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new Label { Text = $"> {title}", Style = Resource<Style>("MatrixStatus") },
                    new Label { Text = message, Style = Resource<Style>("MatrixBody") },
                    buttons,
                },
            },
        };
        panel.Shadow = new Shadow { Brush = Resource<Color>("MatrixGreen"), Radius = 24, Opacity = 0.45f, Offset = new Point(0, 0) };
        Content = panel;
    }

    /// <returns><see langword="true"/> when the user picked <paramref name="accept"/>.</returns>
    public static async Task<bool> ShowAsync(Page page, string title, string message, string accept, string? cancel = null)
    {
        var options = new PopupOptions
        {
            CanBeDismissedByTappingOutsideOfPopup = cancel is not null,
            PageOverlayColor = Color.FromArgb("#B3000000"),
            Shape = null,  // the Border draws the frame
            Shadow = null,
        };
        var result = await page.ShowPopupAsync<bool>(new MatrixDialog(title, message, accept, cancel), options);
        return result.Result;
    }

    Button CreateButton(string text, string style, bool result)
    {
        var button = new Button { Text = text.ToUpperInvariant(), Style = Resource<Style>(style), HeightRequest = 50, FontSize = 15 };
        button.Clicked += async (_, _) => await CloseAsync(result);
        return button;
    }

    static T Resource<T>(string key) => (T)Application.Current!.Resources[key];
}
