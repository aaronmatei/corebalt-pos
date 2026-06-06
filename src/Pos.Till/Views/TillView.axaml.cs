using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Pos.Till.Views;

public partial class TillView : UserControl
{
    public TillView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
