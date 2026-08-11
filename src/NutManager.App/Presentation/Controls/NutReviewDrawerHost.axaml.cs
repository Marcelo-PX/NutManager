using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace NutManager.App.Presentation.Controls;

public partial class NutReviewDrawerHost : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<NutReviewDrawerHost, bool>(nameof(IsOpen));
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<NutReviewDrawerHost, string?>(nameof(Title));
    public static readonly StyledProperty<string?> PendingTextProperty =
        AvaloniaProperty.Register<NutReviewDrawerHost, string?>(nameof(PendingText));
    public static readonly StyledProperty<object?> DrawerContentProperty =
        AvaloniaProperty.Register<NutReviewDrawerHost, object?>(nameof(DrawerContent));
    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<NutReviewDrawerHost, ICommand?>(nameof(CloseCommand));
    public static readonly StyledProperty<string?> CloseTextProperty =
        AvaloniaProperty.Register<NutReviewDrawerHost, string?>(nameof(CloseText));

    public NutReviewDrawerHost() => InitializeComponent();

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? PendingText { get => GetValue(PendingTextProperty); set => SetValue(PendingTextProperty, value); }
    public object? DrawerContent { get => GetValue(DrawerContentProperty); set => SetValue(DrawerContentProperty, value); }
    public ICommand? CloseCommand { get => GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
    public string? CloseText { get => GetValue(CloseTextProperty); set => SetValue(CloseTextProperty, value); }
}
