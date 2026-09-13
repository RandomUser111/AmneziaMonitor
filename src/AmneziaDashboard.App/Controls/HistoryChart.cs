using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AmneziaDashboard.App.Controls;

public sealed class HistoryChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<HistoryChart, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IReadOnlyList<double>?> SecondaryValuesProperty =
        AvaloniaProperty.Register<HistoryChart, IReadOnlyList<double>?>(nameof(SecondaryValues));

    public static readonly StyledProperty<double> MaxValueProperty =
        AvaloniaProperty.Register<HistoryChart, double>(nameof(MaxValue));

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<HistoryChart, IBrush?>(nameof(LineBrush));

    public static readonly StyledProperty<IBrush?> SecondaryLineBrushProperty =
        AvaloniaProperty.Register<HistoryChart, IBrush?>(nameof(SecondaryLineBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<HistoryChart, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<HistoryChart, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<IBrush?> LabelBackgroundBrushProperty =
        AvaloniaProperty.Register<HistoryChart, IBrush?>(nameof(LabelBackgroundBrush));

    public static readonly StyledProperty<string> ValueFormatProperty =
        AvaloniaProperty.Register<HistoryChart, string>(nameof(ValueFormat), "Number");

    private const double MinimumViewportFraction = 0.06;
    private const double HoverDistance = 10d;

    // Нормализованный видимый диапазон по оси X. 0..1 = весь график.
    private double _viewportStart;
    private double _viewportEnd = 1d;

    private int _hoveredSeries = -1;
    private double _hoveredValue;
    private Point _hoveredPoint;
    private bool _hasHoveredPoint;

    static HistoryChart()
    {
        AffectsRender<HistoryChart>(
            ValuesProperty,
            SecondaryValuesProperty,
            MaxValueProperty,
            LineBrushProperty,
            SecondaryLineBrushProperty,
            GridBrushProperty,
            LabelBrushProperty,
            LabelBackgroundBrushProperty,
            ValueFormatProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IReadOnlyList<double>? SecondaryValues
    {
        get => GetValue(SecondaryValuesProperty);
        set => SetValue(SecondaryValuesProperty, value);
    }

    public double MaxValue
    {
        get => GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush? SecondaryLineBrush
    {
        get => GetValue(SecondaryLineBrushProperty);
        set => SetValue(SecondaryLineBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IBrush? LabelBackgroundBrush
    {
        get => GetValue(LabelBackgroundBrushProperty);
        set => SetValue(LabelBackgroundBrushProperty, value);
    }

    public string ValueFormat
    {
        get => GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 8 || height <= 8)
            return;

        var effectiveMax = MaxValue > 0
            ? MaxValue
            : CalculateVisibleAutoMax();

        if (effectiveMax <= 0)
            effectiveMax = 1;

        const double effectiveMin = 0d;
        var plot = GetPlotRect(width, height, effectiveMin, effectiveMax);

        DrawScale(context, plot, effectiveMin, effectiveMax, height);
        DrawGrid(context, plot);
        DrawSeries(context, Values, LineBrush, effectiveMin, effectiveMax, plot);
        DrawSeries(context, SecondaryValues, SecondaryLineBrush, effectiveMin, effectiveMax, plot);

        if (_hasHoveredPoint)
            DrawHoveredPoint(context, width, height);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateHoveredPoint(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearHover();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 8 || height <= 8 || Math.Abs(e.Delta.Y) < double.Epsilon)
            return;

        var effectiveMax = MaxValue > 0 ? MaxValue : CalculateVisibleAutoMax();
        if (effectiveMax <= 0)
            effectiveMax = 1;

        var plot = GetPlotRect(width, height, 0, effectiveMax);
        var pointer = e.GetPosition(this);
        if (!plot.Contains(pointer))
            return;

        var oldSpan = _viewportEnd - _viewportStart;
        var zoomIn = e.Delta.Y > 0;
        var factor = zoomIn ? 0.80 : 1.25;
        var newSpan = Math.Clamp(oldSpan * factor, MinimumViewportFraction, 1d);

        if (Math.Abs(newSpan - oldSpan) < 0.000001)
        {
            if (newSpan >= 0.999999)
            {
                _viewportStart = 0;
                _viewportEnd = 1;
            }

            e.Handled = true;
            return;
        }

        // Сохраняем под курсором примерно ту же точку данных во время zoom.
        var pointerFraction = Math.Clamp((pointer.X - plot.Left) / Math.Max(1d, plot.Width), 0d, 1d);
        var dataAtPointer = _viewportStart + pointerFraction * oldSpan;
        var newStart = dataAtPointer - pointerFraction * newSpan;
        var newEnd = newStart + newSpan;

        if (newStart < 0)
        {
            newEnd -= newStart;
            newStart = 0;
        }

        if (newEnd > 1)
        {
            newStart -= newEnd - 1;
            newEnd = 1;
        }

        _viewportStart = Math.Clamp(newStart, 0d, 1d);
        _viewportEnd = Math.Clamp(newEnd, _viewportStart + MinimumViewportFraction, 1d);

        if (_viewportEnd - _viewportStart >= 0.999999)
        {
            _viewportStart = 0;
            _viewportEnd = 1;
        }

        ClearHover();
        InvalidateVisual();
        e.Handled = true;
    }

    private Rect GetPlotRect(double width, double height, double minValue, double maxValue)
    {
        var maxText = FormatValue(maxValue);
        var minText = FormatValue(minValue);
        var longest = Math.Max(maxText.Length, minText.Length);
        var scaleWidth = Math.Clamp(14d + longest * 6.2d, 48d, 86d);

        const double topPadding = 4d;
        const double rightPadding = 4d;
        const double bottomPadding = 4d;

        return new Rect(
            scaleWidth,
            topPadding,
            Math.Max(1d, width - scaleWidth - rightPadding),
            Math.Max(1d, height - topPadding - bottomPadding));
    }

    private void DrawScale(
        DrawingContext context,
        Rect plot,
        double minValue,
        double maxValue,
        double controlHeight)
    {
        var brush = LabelBrush ?? GridBrush;
        if (brush is null)
            return;

        var maxText = FormatValue(maxValue);
        var minText = FormatValue(minValue);
        const double fontSize = 10d;
        var maxFormatted = CreateFormattedText(maxText, fontSize, brush);
        var minFormatted = CreateFormattedText(minText, fontSize, brush);
        var textHeight = fontSize * 1.35;

        context.DrawText(maxFormatted, new Point(2, 0));
        context.DrawText(minFormatted, new Point(2, Math.Max(0, controlHeight - textHeight)));

        if (GridBrush is not null)
        {
            var pen = new Pen(GridBrush, 1);
            context.DrawLine(pen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
            context.DrawLine(pen, new Point(plot.Left - 5, plot.Top), new Point(plot.Left, plot.Top));
            context.DrawLine(pen, new Point(plot.Left - 5, plot.Bottom), new Point(plot.Left, plot.Bottom));
        }
    }

    private void DrawGrid(DrawingContext context, Rect plot)
    {
        if (GridBrush is null)
            return;

        var pen = new Pen(GridBrush, 1);
        for (var i = 1; i < 4; i++)
        {
            var y = plot.Top + plot.Height * i / 4d;
            context.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
    }

    private void DrawSeries(
        DrawingContext context,
        IReadOnlyList<double>? values,
        IBrush? brush,
        double minValue,
        double maxValue,
        Rect plot)
    {
        if (values is null || values.Count == 0 || brush is null)
            return;

        var (start, end) = GetVisibleIndexRange(values.Count);
        if (start > end)
            return;

        var pen = new Pen(brush, 2);
        Point? previous = null;

        for (var i = start; i <= end; i++)
        {
            var point = GetPoint(values, i, minValue, maxValue, plot);

            if (previous.HasValue)
                context.DrawLine(pen, previous.Value, point);

            previous = point;
        }
    }

    private void DrawHoveredPoint(DrawingContext context, double width, double height)
    {
        var brush = _hoveredSeries == 0 ? LineBrush : SecondaryLineBrush;
        if (brush is null)
            return;

        context.DrawEllipse(
            LabelBackgroundBrush ?? Brushes.White,
            new Pen(brush, 2),
            _hoveredPoint,
            5.5,
            5.5);

        context.DrawEllipse(brush, null, _hoveredPoint, 3.2, 3.2);
        DrawValueText(context, _hoveredValue, _hoveredPoint, width, height);
    }

    private void DrawValueText(
        DrawingContext context,
        double value,
        Point point,
        double width,
        double height)
    {
        if (LabelBrush is null)
            return;

        var displayText = FormatValue(value);
        const double fontSize = 11d;
        var formatted = CreateFormattedText(displayText, fontSize, LabelBrush);

        var textWidth = Math.Max(8, displayText.Length * fontSize * 0.58);
        var textHeight = fontSize * 1.35;
        var x = Math.Clamp(point.X - textWidth / 2d, 1, Math.Max(1, width - textWidth - 1));
        var y = point.Y - textHeight - 10;

        if (y < 1)
            y = Math.Min(height - textHeight - 1, point.Y + 10);

        if (LabelBackgroundBrush is not null)
        {
            var rect = new Rect(x - 4, y - 2, textWidth + 8, textHeight + 4);
            context.DrawRectangle(LabelBackgroundBrush, null, rect);
        }

        context.DrawText(formatted, new Point(x, y));
    }

    private void UpdateHoveredPoint(Point pointer)
    {
        var effectiveMax = MaxValue > 0 ? MaxValue : CalculateVisibleAutoMax();
        if (effectiveMax <= 0)
            effectiveMax = 1;

        var plot = GetPlotRect(Bounds.Width, Bounds.Height, 0, effectiveMax);
        if (!plot.Contains(pointer))
        {
            ClearHover();
            return;
        }

        var bestDistance = HoverDistance;
        var bestSeries = -1;
        var bestValue = 0d;
        var bestPoint = default(Point);

        FindNearestOnSeries(
            Values,
            0,
            pointer,
            0,
            effectiveMax,
            plot,
            ref bestDistance,
            ref bestSeries,
            ref bestValue,
            ref bestPoint);

        FindNearestOnSeries(
            SecondaryValues,
            1,
            pointer,
            0,
            effectiveMax,
            plot,
            ref bestDistance,
            ref bestSeries,
            ref bestValue,
            ref bestPoint);

        _hoveredSeries = bestSeries;
        _hoveredValue = bestValue;
        _hoveredPoint = bestPoint;
        _hasHoveredPoint = bestSeries >= 0;
    }

    private void FindNearestOnSeries(
        IReadOnlyList<double>? values,
        int series,
        Point pointer,
        double minValue,
        double maxValue,
        Rect plot,
        ref double bestDistance,
        ref int bestSeries,
        ref double bestValue,
        ref Point bestPoint)
    {
        if (values is null || values.Count == 0)
            return;

        var (start, end) = GetVisibleIndexRange(values.Count);
        if (start > end)
            return;

        if (start == end)
        {
            var only = GetPoint(values, start, minValue, maxValue, plot);
            var distance = Distance(pointer, only);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestSeries = series;
                bestValue = values[start];
                bestPoint = only;
            }

            return;
        }

        for (var i = start; i < end; i++)
        {
            var a = GetPoint(values, i, minValue, maxValue, plot);
            var b = GetPoint(values, i + 1, minValue, maxValue, plot);
            var projection = ProjectToSegment(pointer, a, b, out var t);
            var distance = Distance(pointer, projection);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestSeries = series;
            bestValue = values[i] + (values[i + 1] - values[i]) * t;
            bestPoint = projection;
        }
    }

    private (int Start, int End) GetVisibleIndexRange(int count)
    {
        if (count <= 0)
            return (0, -1);

        if (count == 1)
            return (0, 0);

        var last = count - 1;
        var start = (int)Math.Floor(_viewportStart * last);
        var end = (int)Math.Ceiling(_viewportEnd * last);

        return (
            Math.Clamp(start, 0, last),
            Math.Clamp(end, 0, last));
    }

    private Point GetPoint(
        IReadOnlyList<double> values,
        int index,
        double minValue,
        double maxValue,
        Rect plot)
    {
        var value = Math.Clamp(values[index], minValue, maxValue);
        var normalizedIndex = values.Count == 1 ? 0.5 : index / (values.Count - 1d);
        var viewportSpan = Math.Max(MinimumViewportFraction, _viewportEnd - _viewportStart);
        var normalizedX = (normalizedIndex - _viewportStart) / viewportSpan;
        var x = plot.Left + plot.Width * normalizedX;

        var valueSpan = Math.Max(0.000001, maxValue - minValue);
        var normalizedY = (value - minValue) / valueSpan;
        var y = plot.Bottom - plot.Height * normalizedY;

        return new Point(x, y);
    }

    private string FormatValue(double value)
    {
        return ValueFormat.Trim().ToLowerInvariant() switch
        {
            "percent" => $"{value:0.#}%",
            "rate" => FormatRate(value),
            "integer" => Math.Round(value).ToString("0", CultureInfo.CurrentCulture),
            _ => value.ToString("0.##", CultureInfo.CurrentCulture)
        };
    }

    private static string FormatRate(double bytesPerSecond)
    {
        var value = Math.Max(0, bytesPerSecond);
        string[] units = ["Б/с", "КБ/с", "МБ/с", "ГБ/с"];
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.#} {units[unit]}";
    }

    private double CalculateVisibleAutoMax()
    {
        var max = 0d;
        FindVisibleMax(Values, ref max);
        FindVisibleMax(SecondaryValues, ref max);
        return max <= 0 ? 1 : max;
    }

    private void FindVisibleMax(IReadOnlyList<double>? values, ref double max)
    {
        if (values is null || values.Count == 0)
            return;

        var (start, end) = GetVisibleIndexRange(values.Count);
        for (var i = start; i <= end; i++)
            max = Math.Max(max, values[i]);
    }

    private static Point ProjectToSegment(Point p, Point a, Point b, out double t)
    {
        var abX = b.X - a.X;
        var abY = b.Y - a.Y;
        var lengthSquared = abX * abX + abY * abY;

        if (lengthSquared <= 0.000001)
        {
            t = 0;
            return a;
        }

        t = ((p.X - a.X) * abX + (p.Y - a.Y) * abY) / lengthSquared;
        t = Math.Clamp(t, 0d, 1d);
        return new Point(a.X + abX * t, a.Y + abY * t);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static FormattedText CreateFormattedText(string text, double fontSize, IBrush brush)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            fontSize,
            brush);
    }

    private void ClearHover()
    {
        _hoveredSeries = -1;
        _hoveredValue = 0;
        _hoveredPoint = default;
        _hasHoveredPoint = false;
    }
}
