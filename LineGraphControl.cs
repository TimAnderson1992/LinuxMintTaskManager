using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LinuxMintSystemMonitor;

internal sealed class LineGraphControl : Control
{
    private static readonly IBrush BackgroundBrush = Brushes.White;
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(213, 232, 247)), 1);
    private Pen? _strokePen;
    private Pen? _secondaryStrokePen;
    private IBrush? _lastStroke;
    private IBrush? _lastSecondaryStroke;

    public static readonly StyledProperty<IReadOnlyList<double>> ValuesProperty =
        AvaloniaProperty.Register<LineGraphControl, IReadOnlyList<double>>(nameof(Values), Array.Empty<double>());

    public static readonly StyledProperty<IReadOnlyList<double>?> SecondaryValuesProperty =
        AvaloniaProperty.Register<LineGraphControl, IReadOnlyList<double>?>(nameof(SecondaryValues));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LineGraphControl, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<LineGraphControl, IBrush>(nameof(Stroke), Brushes.SeaGreen);

    public static readonly StyledProperty<IBrush> SecondaryStrokeProperty =
        AvaloniaProperty.Register<LineGraphControl, IBrush>(nameof(SecondaryStroke), Brushes.LightBlue);

    public static readonly StyledProperty<bool> DrawBackgroundProperty =
        AvaloniaProperty.Register<LineGraphControl, bool>(nameof(DrawBackground), true);

    public static readonly StyledProperty<int> SampleCapacityProperty =
        AvaloniaProperty.Register<LineGraphControl, int>(nameof(SampleCapacity), MetricHistory.Capacity);

    public IReadOnlyList<double> Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IReadOnlyList<double>? SecondaryValues
    {
        get => GetValue(SecondaryValuesProperty);
        set => SetValue(SecondaryValuesProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public IBrush Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush SecondaryStroke
    {
        get => GetValue(SecondaryStrokeProperty);
        set => SetValue(SecondaryStrokeProperty, value);
    }

    public bool DrawBackground
    {
        get => GetValue(DrawBackgroundProperty);
        set => SetValue(DrawBackgroundProperty, value);
    }

    public int SampleCapacity
    {
        get => GetValue(SampleCapacityProperty);
        set => SetValue(SampleCapacityProperty, value);
    }

    static LineGraphControl()
    {
        AffectsRender<LineGraphControl>(
            ValuesProperty,
            SecondaryValuesProperty,
            MaximumProperty,
            StrokeProperty,
            SecondaryStrokeProperty,
            DrawBackgroundProperty,
            SampleCapacityProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        var strokePen = GetStrokePen();
        var secondaryStrokePen = GetSecondaryStrokePen();

        if (DrawBackground)
        {
            context.FillRectangle(BackgroundBrush, bounds);
        }

        for (var i = 1; i < 4; i++)
        {
            var y = bounds.Height * i / 4d;
            context.DrawLine(GridPen, new Point(0, y), new Point(bounds.Width, y));
        }

        for (var i = 1; i < 6; i++)
        {
            var x = bounds.Width * i / 6d;
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, bounds.Height));
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var secondaryValues = SecondaryValues;
        var highestPrimary = MaxValue(Values);
        var highestSecondary = secondaryValues is null ? 0 : MaxValue(secondaryValues);
        var max = Math.Max(Maximum, Math.Max(highestPrimary, highestSecondary));
        if (max <= 0)
        {
            max = 1;
        }

        DrawSeries(context, Values, max, bounds, strokePen);
        if (secondaryValues is not null)
        {
            DrawSeries(context, secondaryValues, max, bounds, secondaryStrokePen);
        }
    }

    private void DrawSeries(DrawingContext context, IReadOnlyList<double> values, double max, Rect bounds, Pen pen)
    {
        if (values.Count < 2)
        {
            return;
        }

        var capacity = Math.Max(2, SampleCapacity);
        Point? previous = null;
        for (var i = 0; i < values.Count; i++)
        {
            var x = bounds.Width * i / (capacity - 1);
            var normalized = Math.Clamp(values[i] / max, 0, 1);
            var y = bounds.Height - normalized * bounds.Height;
            var point = new Point(x, y);
            if (previous is { } start)
            {
                context.DrawLine(pen, start, point);
            }

            previous = point;
        }
    }

    private Pen GetStrokePen()
    {
        var stroke = Stroke;
        if (_strokePen is null || !ReferenceEquals(_lastStroke, stroke))
        {
            _lastStroke = stroke;
            _strokePen = new Pen(stroke, 1.25);
        }

        return _strokePen;
    }

    private Pen GetSecondaryStrokePen()
    {
        var stroke = SecondaryStroke;
        if (_secondaryStrokePen is null || !ReferenceEquals(_lastSecondaryStroke, stroke))
        {
            _lastSecondaryStroke = stroke;
            _secondaryStrokePen = new Pen(stroke, 1.25);
        }

        return _secondaryStrokePen;
    }

    private static double MaxValue(IReadOnlyList<double> values)
    {
        if (values is MetricHistory history)
        {
            return history.MaxValue();
        }

        var max = 0d;
        for (var i = 0; i < values.Count; i++)
        {
            max = Math.Max(max, values[i]);
        }

        return max;
    }
}
