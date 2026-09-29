using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LumaProfiles.Controls;

/// <summary>
/// Draws two versions of the same picture separated by a vertical divider that follows the mouse:
/// the original on the left, the profile applied on the right.
/// </summary>
public sealed class BeforeAfterView : FrameworkElement
{
    public static readonly DependencyProperty BeforeProperty = DependencyProperty.Register(
        nameof(Before), typeof(ImageSource), typeof(BeforeAfterView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AfterProperty = DependencyProperty.Register(
        nameof(After), typeof(ImageSource), typeof(BeforeAfterView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(BeforeAfterView),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender, null, CoercePosition));

    public static readonly DependencyProperty BeforeLabelProperty = DependencyProperty.Register(
        nameof(BeforeLabel), typeof(string), typeof(BeforeAfterView),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AfterLabelProperty = DependencyProperty.Register(
        nameof(AfterLabel), typeof(string), typeof(BeforeAfterView),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush LabelBackground = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0x07, 0x10, 0x18)));
    private static readonly Pen DividerPen = Freeze(new Pen(Brushes.White, 2));
    private static readonly Pen ChevronPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x14, 0x1E, 0x28)), 2)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round
    });

    public BeforeAfterView()
    {
        IsHitTestVisible = true;
        ClipToBounds = true;
        Cursor = Cursors.SizeWE;
    }

    public ImageSource? Before { get => (ImageSource?)GetValue(BeforeProperty); set => SetValue(BeforeProperty, value); }

    public ImageSource? After { get => (ImageSource?)GetValue(AfterProperty); set => SetValue(AfterProperty, value); }

    /// <summary>Divider position from 0 (all "after") to 1 (all "before").</summary>
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    public string BeforeLabel { get => (string)GetValue(BeforeLabelProperty); set => SetValue(BeforeLabelProperty, value); }

    public string AfterLabel { get => (string)GetValue(AfterLabelProperty); set => SetValue(AfterLabelProperty, value); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (ActualWidth > 0) Position = e.GetPosition(this).X / ActualWidth;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearValue(PositionProperty);
    }

    protected override void OnRender(DrawingContext context)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // Transparent fill keeps the whole area hit-testable even when no image is available.
        context.DrawRectangle(Brushes.Transparent, null, bounds);

        var split = bounds.Width * Position;
        if (After is { } after) DrawCovering(context, after, bounds);

        if (Before is { } before)
        {
            context.PushClip(new RectangleGeometry(new Rect(0, 0, split, bounds.Height)));
            DrawCovering(context, before, bounds);
            context.Pop();
        }

        context.DrawLine(DividerPen, new Point(split, 0), new Point(split, bounds.Height));
        DrawHandle(context, new Point(split, bounds.Height / 2));
        DrawLabel(context, BeforeLabel, new Point(8, bounds.Height - 8), alignRight: false, bounds.Width, split);
        DrawLabel(context, AfterLabel, new Point(bounds.Width - 8, bounds.Height - 8), alignRight: true, bounds.Width, split);
    }

    /// <summary>UniformToFill: the picture covers the whole area, cropping the overflow evenly.</summary>
    private static void DrawCovering(DrawingContext context, ImageSource image, Rect bounds)
    {
        var scale = Math.Max(bounds.Width / image.Width, bounds.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        context.DrawImage(image, new Rect((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height));
    }

    private static void DrawHandle(DrawingContext context, Point center)
    {
        context.DrawEllipse(Brushes.White, null, center, 15, 15);
        context.DrawLine(ChevronPen, new Point(center.X - 3, center.Y - 5), new Point(center.X - 7, center.Y));
        context.DrawLine(ChevronPen, new Point(center.X - 7, center.Y), new Point(center.X - 3, center.Y + 5));
        context.DrawLine(ChevronPen, new Point(center.X + 3, center.Y - 5), new Point(center.X + 7, center.Y));
        context.DrawLine(ChevronPen, new Point(center.X + 7, center.Y), new Point(center.X + 3, center.Y + 5));
    }

    /// <summary>Small caption pinned to a bottom corner; hidden when the divider would cover it.</summary>
    private void DrawLabel(DrawingContext context, string text, Point anchor, bool alignRight, double width, double split)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var formatted = new FormattedText(
            text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            10.5, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var box = new Rect(
            alignRight ? anchor.X - formatted.Width - 12 : anchor.X,
            anchor.Y - formatted.Height - 6,
            formatted.Width + 12, formatted.Height + 6);

        if (alignRight ? split > box.Left : split < box.Right) return;
        context.DrawRoundedRectangle(LabelBackground, null, box, 8, 8);
        context.DrawText(formatted, new Point(box.Left + 6, box.Top + 3));
    }

    private static object CoercePosition(DependencyObject d, object value) => Math.Clamp((double)value, 0.02, 0.98);

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
