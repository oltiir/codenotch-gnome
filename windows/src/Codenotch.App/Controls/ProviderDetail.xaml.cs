using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Codenotch.App.Theme;
using Codenotch.Core.Model;
using Codenotch.Core.Presentation;

namespace Codenotch.App.Controls;

/// <summary>
/// Port of buildDetail() in extension.js, reused by the flyout and the notch's
/// hover callout. The sizes below are the stylesheet's CSS values resolved against
/// GNOME's 11 pt (~14.67 px) base and rounded to Windows-idiomatic DIPs.
/// </summary>
public sealed partial class ProviderDetail : UserControl
{
    private const double BlockSpacing = 8;
    private const double HeaderSpacing = 6;
    private const double RowSpacing = 12;
    private const double LeftColumnMinWidth = 128;
    private const double PercentMinWidth = 58;

    public static readonly DependencyProperty BarWidthProperty = DependencyProperty.Register(
        nameof(BarWidth), typeof(double), typeof(ProviderDetail),
        new PropertyMetadata(120.0, OnBarWidthChanged));

    private ProviderReading? _reading;
    private DateTimeOffset _now;
    private TimeZoneInfo _zone = TimeZoneInfo.Local;

    public ProviderDetail()
    {
        InitializeComponent();
    }

    public double BarWidth
    {
        get => (double)GetValue(BarWidthProperty);
        set => SetValue(BarWidthProperty, value);
    }

    private static void OnBarWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProviderDetail detail && detail._reading is not null)
            detail.Update(detail._reading, detail._now, detail._zone);
    }

    /// <summary>Rebuilds the block. Cheap enough to do on every poll (three or four rows).</summary>
    public void Update(ProviderReading reading, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(reading);
        _reading = reading;
        _now = now;
        _zone = zone ?? TimeZoneInfo.Local;

        Root.Children.Clear();
        Root.Children.Add(Header(reading.Provider));

        if (!reading.HasData)
        {
            Root.Children.Add(Muted($"{reading.Provider.Name}: no data", 11, new Thickness(0, BlockSpacing, 0, 0)));
            if (reading.Error is string error && error.Length > 0 && error != "no data")
                Root.Children.Add(Muted(error, 11, new Thickness(0, 2, 0, 0)));
            return;
        }

        bool dividerDone = false;
        foreach (UsageWindow window in reading.Windows)
        {
            if (window.Group == WindowGroup.Weekly && !dividerDone)
            {
                Root.Children.Add(Divider());
                dividerDone = true;
            }
            Root.Children.Add(Row(window, _now, _zone));
        }

        if (reading.PaceSummary is string pace && pace.Length > 0)
            Root.Children.Add(Muted(pace, 11, new Thickness(0, 2, 0, 0)));
    }

    private static UIElement Header(ProviderIdentity provider)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock
        {
            Text = provider.Glyph,
            FontFamily = GlyphFont,
            FontSize = 12,
            Foreground = ToneBrushes.Glyph,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, HeaderSpacing, 0),
        });
        head.Children.Add(new TextBlock
        {
            Text = provider.Name,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = ToneBrushes.Text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return head;
    }

    private static UIElement Divider() => new TextBlock
    {
        Text = "Weekly limits",
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        Foreground = ToneBrushes.Muted,
        Margin = new Thickness(0, BlockSpacing + 4, 0, 0),
    };

    private UIElement Row(UsageWindow window, DateTimeOffset now, TimeZoneInfo zone)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, BlockSpacing, 0, 0),
        };

        var left = new StackPanel { MinWidth = LeftColumnMinWidth, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = window.Label,
            FontSize = 12,
            Foreground = ToneBrushes.Text,
        });
        string reset = UsageText.ResetLine(window.ResetsAt, now, zone);
        if (reset.Length > 0)
        {
            var resetText = new TextBlock
            {
                Text = reset,
                FontSize = 11,
                Foreground = ToneBrushes.Muted,
                Margin = new Thickness(0, 1, 0, 0),
            };
            Typography.SetNumeralAlignment(resetText, FontNumeralAlignment.Tabular);
            left.Children.Add(resetText);
        }
        row.Children.Add(left);

        var bar = new UsageBar
        {
            BarWidth = BarWidth,
            Used = window.Used,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(RowSpacing, 0, RowSpacing, 0),
        };
        row.Children.Add(bar);

        var percent = new TextBlock
        {
            Text = $"{window.Used}% used",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = ToneBrushes.For(Tone.Of(window.Used)),
            MinWidth = PercentMinWidth,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Typography.SetNumeralAlignment(percent, FontNumeralAlignment.Tabular);
        row.Children.Add(percent);

        return row;
    }

    /// <summary>The width one row occupies, so wrapping text wraps at the block's edge.</summary>
    private double RowWidth => LeftColumnMinWidth + RowSpacing + BarWidth + RowSpacing + PercentMinWidth;

    /// <summary>
    /// Capped on purpose: a wrapping TextBlock handed infinite available width (which
    /// is what a SizeToContent window does) lays itself out on one line and drags the
    /// whole surface wider instead of wrapping.
    /// </summary>
    private TextBlock Muted(string text, double fontSize, Thickness margin) => new()
    {
        Text = text,
        FontSize = fontSize,
        Foreground = ToneBrushes.Muted,
        Margin = margin,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = RowWidth,
    };

    /// <summary>
    /// The PROVIDERS glyphs (✱ ◎ △ ⌘ ✦) are all in Segoe UI Symbol; the emoji font
    /// is only a backstop for a provider id whose first letter is exotic.
    /// </summary>
    internal static readonly FontFamily GlyphFont =
        new("Segoe UI Symbol, Segoe UI Emoji, Segoe UI");
}
