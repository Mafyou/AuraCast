namespace AuraMusic.Mobile.Controls;

/// <summary>Matrix-styled replacement for <c>DisplayAlertAsync</c>: a title, a message and one or two buttons.</summary>
public sealed class MatrixDialog : Popup<bool>
{
    MatrixDialog(string title, string message, string accept, string? cancel)
    {
        BackgroundColor = Theme.Color("MatrixClear"); // the Border below is the dialog
        Padding = 0;
        Margin = new Thickness(24, 0);

        var buttons = new Grid { ColumnSpacing = 12 };
        if (cancel is not null)
        {
            buttons.ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)];
            buttons.Add(CreateButton(cancel, "MatrixDialogButton", result: false), 0);
        }
        buttons.Add(CreateButton(accept, "MatrixDialogPrimaryButton", result: true), cancel is null ? 0 : 1);

        Content = new Border
        {
            Style = Theme.Style("MatrixDialogBorder"),
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    new Label { Text = $"> {title}", Style = Theme.Style("MatrixStatus") },
                    new Label { Text = message, Style = Theme.Style("MatrixBody") },
                    buttons,
                },
            },
        };
    }

    /// <returns><see langword="true"/> when the user picked <paramref name="accept"/>.</returns>
    public static async Task<bool> ShowAsync(Page page, string title, string message, string accept, string? cancel = null)
    {
        var options = new PopupOptions
        {
            CanBeDismissedByTappingOutsideOfPopup = cancel is not null,
            PageOverlayColor = Theme.Color("MatrixOverlay"),
            Shape = null,  // the Border draws the frame
            Shadow = null,
        };
        var result = await page.ShowPopupAsync<bool>(new MatrixDialog(title, message, accept, cancel), options);
        return result.Result;
    }

    Button CreateButton(string text, string style, bool result)
    {
        var button = new Button { Text = text.ToUpperInvariant(), Style = Theme.Style(style) };
        button.Clicked += async (_, _) => await CloseAsync(result);
        return button;
    }
}
