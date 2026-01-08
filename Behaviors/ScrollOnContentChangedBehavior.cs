using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

public class ScrollOnContentChangedBehavior : Behavior<ScrollViewer>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        
        if (AssociatedObject?.Content is Control content)
        {
            content.PropertyChanged += Content_PropertyChanged;
        }
    }
    
    protected override void OnDetaching()
    {
        if (AssociatedObject?.Content is Control content)
        {
            content.PropertyChanged -= Content_PropertyChanged;
        }
        base.OnDetaching();
    }
    
    private void Content_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBlock.TextProperty && AssociatedObject != null)
        {
            AssociatedObject.ScrollToEnd();
        }
    }
}