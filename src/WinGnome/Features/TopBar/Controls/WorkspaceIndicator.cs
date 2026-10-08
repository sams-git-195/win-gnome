using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinGnome.Features.TopBar.Controls;

/// <summary>
/// GNOME 45 style workspace dots: a small dot per workspace with the active one stretched into a pill.
/// The pill glides between dots by animating widths. Each dot has a generous transparent hit area.
/// </summary>
internal sealed class WorkspaceIndicator : StackPanel
{
    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
        nameof(Count), typeof(int), typeof(WorkspaceIndicator), new PropertyMetadata(1, (d, _) => ((WorkspaceIndicator)d).Rebuild()));

    public static readonly DependencyProperty CurrentIndexProperty = DependencyProperty.Register(
        nameof(CurrentIndex), typeof(int), typeof(WorkspaceIndicator), new PropertyMetadata(0, (d, _) => ((WorkspaceIndicator)d).UpdateDots(animate: true)));

    public static readonly DependencyProperty DotSizeProperty = DependencyProperty.Register(
        nameof(DotSize), typeof(double), typeof(WorkspaceIndicator), new PropertyMetadata(7.0, (d, _) => ((WorkspaceIndicator)d).Rebuild()));

    public static readonly DependencyProperty DotBrushProperty = DependencyProperty.Register(
        nameof(DotBrush), typeof(Brush), typeof(WorkspaceIndicator), new PropertyMetadata(Brushes.White));

    private const double ActiveWidthFactor = 3.5;
    private const double InactiveOpacity = 0.5;
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(250));
    private static readonly IEasingFunction Easing = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly List<Border> _dots = [];

    public WorkspaceIndicator()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Stretch;
        Rebuild();
    }

    /// <summary>Raised with the zero-based index of a clicked dot.</summary>
    public event EventHandler<int>? DotClicked;

    public int Count
    {
        get => (int)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public int CurrentIndex
    {
        get => (int)GetValue(CurrentIndexProperty);
        set => SetValue(CurrentIndexProperty, value);
    }

    /// <summary>Diameter of an inactive dot in DIPs; the active pill is proportionally wider.</summary>
    public double DotSize
    {
        get => (double)GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    public Brush DotBrush
    {
        get => (Brush)GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        _dots.Clear();
        var size = DotSize;
        for (var i = 0; i < Math.Max(1, Count); i++)
        {
            var dot = new Border
            {
                Height = size,
                Width = size,
                CornerRadius = new CornerRadius(size / 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            dot.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(DotBrush)) { Source = this });

            var hitArea = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(size / 2, 0, size / 2, 0),
                Child = dot,
                Tag = i,
            };
            hitArea.MouseLeftButtonUp += OnDotClicked;

            _dots.Add(dot);
            Children.Add(hitArea);
        }

        UpdateDots(animate: false);
    }

    private void UpdateDots(bool animate)
    {
        var size = DotSize;
        for (var i = 0; i < _dots.Count; i++)
        {
            var active = i == CurrentIndex;
            var width = active ? size * ActiveWidthFactor : size;
            var opacity = active ? 1.0 : InactiveOpacity;
            if (animate)
            {
                _dots[i].BeginAnimation(WidthProperty, new DoubleAnimation(width, AnimationDuration) { EasingFunction = Easing });
                _dots[i].BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, AnimationDuration) { EasingFunction = Easing });
            }
            else
            {
                // Drop any running animation so the base value is visible.
                _dots[i].BeginAnimation(WidthProperty, null);
                _dots[i].BeginAnimation(OpacityProperty, null);
                _dots[i].Width = width;
                _dots[i].Opacity = opacity;
            }
        }
    }

    private void OnDotClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int index })
        {
            e.Handled = true;
            DotClicked?.Invoke(this, index);
        }
    }
}
