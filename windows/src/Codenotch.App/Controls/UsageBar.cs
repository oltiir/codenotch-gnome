using System;
using System.Windows;
using System.Windows.Media;
using Codenotch.App.Theme;
using Codenotch.Core.Model;
using Codenotch.Core.Presentation;

namespace Codenotch.App.Controls;

/// <summary>
/// The rounded bar from the callout and the popup: 6 px tall, radius 3, filling
/// left to right with percent used. Port of UsageBar.setUsed() in extension.js.
/// </summary>
internal sealed class UsageBar : FrameworkElement
{
    private const double BarHeight = 6;
    private const double BarRadius = 3;

    public static readonly DependencyProperty BarWidthProperty = DependencyProperty.Register(
        nameof(BarWidth), typeof(double), typeof(UsageBar),
        new FrameworkPropertyMetadata(120.0, FrameworkPropertyMetadataOptions.AffectsMeasure
                                           | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UsedProperty = DependencyProperty.Register(
        nameof(Used), typeof(int), typeof(UsageBar),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    public int Used
    {
        get => (int)GetValue(UsedProperty);
        set => SetValue(UsedProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(BarWidth, BarHeight);

    protected override void OnRender(DrawingContext dc)
    {
        double width = BarWidth;
        if (width <= 0)
            return;

        var track = new Rect(0, 0, width, BarHeight);
        dc.DrawRoundedRectangle(ToneBrushes.Track, null, track, BarRadius, BarRadius);

        int used = Used;
        if (used <= 0)
            return;

        // The 3 px floor keeps 1% visible instead of rounding it away.
        double fill = Math.Min(width, Math.Max(3, Num.JsRound(width * used / 100.0)));
        dc.DrawRoundedRectangle(ToneBrushes.For(Tone.Of(used)), null,
            new Rect(0, 0, fill, BarHeight), BarRadius, BarRadius);
    }
}
