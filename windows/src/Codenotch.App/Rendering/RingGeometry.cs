using System;
using System.Windows;
using System.Windows.Media;

namespace Codenotch.App.Rendering;

/// <summary>
/// The concentric-dial maths, ported straight from Rings._paint() in extension.js
/// and draw.rs, so the tray icon, the notch dial and the flyout header dial are
/// the same drawing at three sizes.
///
/// Sizes are whatever unit the caller draws in: the tray renderer feeds it device
/// pixels (16/20/24/32), the WPF controls feed it DIPs and let the render
/// transform scale.
/// </summary>
internal static class RingGeometry
{
    /// <summary>Both arcs start at the top and sweep clockwise.</summary>
    public const double TopAngleRadians = -Math.PI / 2;

    public readonly record struct RingSpec(double Cx, double Cy,
        double OuterRadius, double OuterWidth, double InnerRadius, double InnerWidth, bool DrawInner);

    /// <summary>Alpha on the inner (weekly) arc, so the session ring stays the loud one.</summary>
    public const double InnerAlpha = 0.55;

    public static RingSpec For(double size, bool haveWeekly)
    {
        double outerW = Math.Max(2.5, size * 0.095);
        double innerW = Math.Max(1.5, size * 0.06);
        double rOuter = size / 2 - outerW / 2 - 0.5;
        double rInner = rOuter - outerW / 2 - innerW / 2 - 2.5;
        // Below 3 the inner ring is a smudge, so at 16 px it only appears when there
        // really is weekly data to show — exactly the JS condition.
        return new RingSpec(size / 2, size / 2, rOuter, outerW, rInner, innerW, haveWeekly || rInner > 3);
    }

    /// <summary>
    /// Draws one ring: a full-circle track, then the used arc on top. A fraction of
    /// zero draws no arc at all, so an unused window reads as an empty ring rather
    /// than a hairline.
    /// </summary>
    public static void DrawRing(DrawingContext dc, double cx, double cy, double radius, double width,
                                double? fraction, Color toneColor, double alpha, Color trackColor)
    {
        if (radius <= 0 || width <= 0)
            return;

        var center = new Point(cx, cy);

        // Butt cap on the track: it is a closed circle, so the cap never shows.
        var trackPen = new Pen(new SolidColorBrush(trackColor), width);
        trackPen.Freeze();
        dc.DrawEllipse(null, trackPen, center, radius, radius);

        if (fraction is not double frac || frac <= 0)
            return;

        double sweep = 2 * Math.PI * Math.Min(1.0, frac);
        var toneBrush = new SolidColorBrush(toneColor) { Opacity = alpha };
        toneBrush.Freeze();
        var arcPen = new Pen(toneBrush, width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        arcPen.Freeze();

        // A full sweep as a single arc segment is degenerate in WPF (start == end),
        // so draw it as the circle it is.
        if (sweep >= 2 * Math.PI - 1e-6)
        {
            dc.DrawEllipse(null, arcPen, center, radius, radius);
            return;
        }

        Point start = OnCircle(center, radius, TopAngleRadians);
        Point end = OnCircle(center, radius, TopAngleRadians + sweep);
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0,
            isLargeArc: sweep > Math.PI, SweepDirection.Clockwise, isStroked: true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        dc.DrawGeometry(null, arcPen, geometry);
    }

    private static Point OnCircle(Point center, double radius, double angle)
        => new(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));

    /// <summary>
    /// Draws the whole dial: outer ring = session, inner = weekly, tone from the
    /// session window's severity (Stale when there is no session reading).
    /// </summary>
    public static void DrawDial(DrawingContext dc, double size, int? sessionUsed, int? weeklyUsed,
                                Color toneColor, Color trackColor)
    {
        RingSpec spec = For(size, weeklyUsed.HasValue);
        double? session = sessionUsed is int s ? s / 100.0 : null;
        double? weekly = weeklyUsed is int w ? w / 100.0 : null;

        DrawRing(dc, spec.Cx, spec.Cy, spec.OuterRadius, spec.OuterWidth, session, toneColor, 1.0, trackColor);
        if (spec.DrawInner)
            DrawRing(dc, spec.Cx, spec.Cy, spec.InnerRadius, spec.InnerWidth, weekly, toneColor, InnerAlpha, trackColor);
    }
}
