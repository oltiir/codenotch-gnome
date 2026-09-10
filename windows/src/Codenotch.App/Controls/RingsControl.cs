using System.Windows;
using System.Windows.Media;
using Codenotch.App.Rendering;
using Codenotch.App.Theme;
using Codenotch.Core.Presentation;

namespace Codenotch.App.Controls;

/// <summary>
/// The concentric dial as a WPF element. Outer ring is the session window, inner
/// the weekly one; both fill clockwise as you use them, and the colour tracks the
/// session window's severity.
/// </summary>
internal sealed class RingsControl : FrameworkElement
{
    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(RingsControl),
        new FrameworkPropertyMetadata(46.0, FrameworkPropertyMetadataOptions.AffectsMeasure
                                          | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SessionUsedProperty = DependencyProperty.Register(
        nameof(SessionUsed), typeof(int?), typeof(RingsControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WeeklyUsedProperty = DependencyProperty.Register(
        nameof(WeeklyUsed), typeof(int?), typeof(RingsControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    /// <summary>Percent used, or null for no data (which paints the Stale tone).</summary>
    public int? SessionUsed
    {
        get => (int?)GetValue(SessionUsedProperty);
        set => SetValue(SessionUsedProperty, value);
    }

    public int? WeeklyUsed
    {
        get => (int?)GetValue(WeeklyUsedProperty);
        set => SetValue(WeeklyUsedProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Diameter, Diameter);

    protected override void OnRender(DrawingContext dc)
    {
        double size = Diameter;
        if (size <= 0)
            return;
        ToneKind tone = Tone.Of(SessionUsed);
        RingGeometry.DrawDial(dc, size, SessionUsed, WeeklyUsed,
            ToneBrushes.For(tone).Color, ToneBrushes.Track.Color);
    }
}
