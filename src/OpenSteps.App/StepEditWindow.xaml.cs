using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfLine = System.Windows.Shapes.Line;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace OpenSteps.App;

/// <summary>What the step editor starts from. Edit coordinates are in the original screenshot's pixels.</summary>
public sealed record StepEditRequest(
    int StepNumber,
    string OriginalScreenshotPath,
    IReadOnlyList<RedactionRegion> Redactions,
    IReadOnlyList<ScreenshotAnnotation> Annotations,
    ScreenBounds? Crop,
    string Title,
    string? Description,
    string SuggestedTitle,
    bool HasLegacyEdits,
    StepEditTool InitialTool = StepEditTool.Box);

public enum StepEditTool
{
    Crop,
    Box,
    Arrow,
    Callout,
    Highlight,
    Blur,
    Circle
}

/// <summary>
/// Edits one step: its title and description, and its screenshot (crop, boxes, arrows, numbered callouts,
/// highlights, blur). The original capture is always shown and never changed; edits are kept as a list and
/// drawn onto a copy when the step is saved, so they can be changed or removed later.
/// </summary>
public partial class StepEditWindow : Window
{
    private const string AnnotationRed = "#D92D20";
    private const string HighlightYellow = "#FFD84D";

    private readonly StepEditRequest _request;
    private readonly BitmapSource _imageSource;
    private readonly int _imageWidth;
    private readonly int _imageHeight;
    private readonly double _stroke;
    private readonly double _calloutRadius;
    private readonly Stack<EditState> _undo = new();
    private readonly Stack<EditState> _redo = new();

    private List<RedactionRegion> _redactions;
    private List<ScreenshotAnnotation> _annotations;
    private ScreenBounds? _crop;
    private StepEditTool _tool;
    private WpfPoint? _dragStart;
    private Shape? _previewShape;
    private bool _saved;
    private bool _textChanged;

    public StepEditWindow(StepEditRequest request)
    {
        InitializeComponent();
        _request = request;
        Title = $"Edit step {request.StepNumber}";

        _imageSource = LoadImage(request.OriginalScreenshotPath);
        ScreenshotImage.Source = _imageSource;
        _imageWidth = _imageSource.PixelWidth;
        _imageHeight = _imageSource.PixelHeight;
        _stroke = ScreenshotEditing.StrokeFor(_imageWidth, _imageHeight);
        _calloutRadius = ScreenshotEditing.CalloutRadiusFor(_imageWidth, _imageHeight);

        _redactions = [.. request.Redactions];
        _annotations = request.Annotations.Select(ScreenshotEditing.Clone).ToList();
        _crop = ScreenshotEditing.NormalizeCrop(request.Crop, _imageWidth, _imageHeight);

        TitleBox.Text = request.Title;
        DescriptionBox.Text = request.Description ?? string.Empty;
        SuggestedTitleText.Text = $"Suggested: {request.SuggestedTitle}";
        SuggestedTitleButton.Visibility = string.IsNullOrWhiteSpace(request.SuggestedTitle) || request.SuggestedTitle == request.Title
            ? Visibility.Collapsed
            : Visibility.Visible;
        LegacyNotice.Visibility = request.HasLegacyEdits ? Visibility.Visible : Visibility.Collapsed;
        _textChanged = false;

        SelectTool(request.InitialTool);
        Loaded += (_, _) => RenderEdits();
        SizeChanged += (_, _) => RenderEdits();
    }

    public IReadOnlyList<RedactionRegion> Redactions => _redactions;

    public IReadOnlyList<ScreenshotAnnotation> Annotations => _annotations;

    public ScreenBounds? Crop => _crop;

    public string StepTitle => TitleBox.Text.Trim();

    public string StepDescription => DescriptionBox.Text.TrimEnd();

    /// <summary>True when the screenshot edits (not just the text) were changed.</summary>
    public bool ScreenshotChanged { get; private set; }

    private bool HasUnsavedChanges => ScreenshotChanged || _textChanged;

    // ---- Tools -------------------------------------------------------------------------------------------------

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<StepEditTool>(tag, out var tool))
        {
            SelectTool(tool);
        }
    }

    private void SelectTool(StepEditTool tool)
    {
        _tool = tool;
        foreach (var (button, buttonTool) in ToolButtons())
        {
            var selected = buttonTool == tool;
            button.Background = selected ? new SolidColorBrush(WpfColor.FromRgb(0xE6, 0xEC, 0xFF)) : (WpfBrush)FindResource("PanelBackgroundBrush");
            button.BorderBrush = selected ? (WpfBrush)FindResource("FocusBorderBrush") : (WpfBrush)FindResource("BorderBrush");
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        ToolHintText.Text = tool switch
        {
            StepEditTool.Crop => "Drag over the part of the screenshot to keep. Drag again to change it.",
            StepEditTool.Box => "Drag to draw a box.",
            StepEditTool.Arrow => "Drag from where the arrow starts to what it points at.",
            StepEditTool.Callout => $"Click to place callout {ScreenshotEditing.NextCalloutNumber(_annotations)}.",
            StepEditTool.Highlight => "Drag to highlight an area.",
            StepEditTool.Blur => "Drag over anything private to blur it.",
            StepEditTool.Circle => "Drag to draw a circle.",
            _ => string.Empty
        };
    }

    private IEnumerable<(WpfButton Button, StepEditTool Tool)> ToolButtons()
    {
        yield return (CropToolButton, StepEditTool.Crop);
        yield return (BoxToolButton, StepEditTool.Box);
        yield return (ArrowToolButton, StepEditTool.Arrow);
        yield return (CalloutToolButton, StepEditTool.Callout);
        yield return (HighlightToolButton, StepEditTool.Highlight);
        yield return (BlurToolButton, StepEditTool.Blur);
        yield return (CircleToolButton, StepEditTool.Circle);
    }

    // ---- Mouse -------------------------------------------------------------------------------------------------

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var start = e.GetPosition(OverlayCanvas);
        if (_tool == StepEditTool.Callout)
        {
            AddCallout(start);
            return;
        }

        _dragStart = start;
        _previewShape = CreatePreviewShape();
        OverlayCanvas.Children.Add(_previewShape);
        OverlayCanvas.CaptureMouse();
    }

    private void OverlayCanvas_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_dragStart is { } start && _previewShape is not null)
        {
            PositionPreviewShape(_previewShape, start, e.GetPosition(OverlayCanvas));
        }
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start)
        {
            return;
        }

        var end = e.GetPosition(OverlayCanvas);
        CancelDrag();
        AddDragEdit(start, end);
        RenderEdits();
    }

    /// <summary>Right-click removes the topmost edit under the pointer.</summary>
    private void OverlayCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ToImagePoint(e.GetPosition(OverlayCanvas)) is not { } point)
        {
            return;
        }

        var tolerance = 8 / Math.Max(0.01, GetImageScale());
        var annotation = ScreenshotEditing.HitTestAnnotation(_annotations, point.X, point.Y, tolerance);
        if (annotation >= 0)
        {
            Change(() =>
            {
                var removed = _annotations[annotation];
                _annotations.RemoveAt(annotation);
                if (ScreenshotEditing.IsNumberedCallout(removed))
                {
                    ScreenshotEditing.RenumberCallouts(_annotations);
                }
            });
            return;
        }

        var redaction = ScreenshotEditing.HitTestRedaction(_redactions, point.X, point.Y);
        if (redaction >= 0)
        {
            Change(() => _redactions.RemoveAt(redaction));
        }
    }

    private void CancelDrag()
    {
        _dragStart = null;
        if (_previewShape is not null)
        {
            OverlayCanvas.Children.Remove(_previewShape);
            _previewShape = null;
        }

        OverlayCanvas.ReleaseMouseCapture();
    }

    private void AddDragEdit(WpfPoint start, WpfPoint end)
    {
        if (_tool == StepEditTool.Arrow)
        {
            if (ToImagePoint(start) is not { } from || ToImagePoint(end) is not { } to || Distance(from, to) < 6)
            {
                return;
            }

            Change(() => _annotations.Add(new ScreenshotAnnotation
            {
                Type = ScreenshotAnnotationType.Arrow,
                X1 = from.X,
                Y1 = from.Y,
                X2 = to.X,
                Y2 = to.Y,
                Color = AnnotationRed,
                StrokeThickness = _stroke
            }));
            return;
        }

        var region = RedactionCoordinateMapper.ToImageRegion(
            start.X, start.Y, end.X, end.Y,
            OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight,
            _imageWidth, _imageHeight,
            _tool == StepEditTool.Circle ? RedactionMode.RedCircle : RedactionMode.Pixelate);
        if (region is null)
        {
            return;
        }

        switch (_tool)
        {
            case StepEditTool.Crop:
                if (ScreenshotEditing.NormalizeCrop(new ScreenBounds(region.X, region.Y, region.Width, region.Height), _imageWidth, _imageHeight) is { } crop)
                {
                    Change(() => _crop = crop);
                }

                break;
            case StepEditTool.Blur:
            case StepEditTool.Circle:
                Change(() => _redactions.Add(region));
                break;
            default:
                var highlight = _tool == StepEditTool.Highlight;
                Change(() => _annotations.Add(new ScreenshotAnnotation
                {
                    Type = highlight ? ScreenshotAnnotationType.Highlight : ScreenshotAnnotationType.Rectangle,
                    X1 = region.X,
                    Y1 = region.Y,
                    X2 = region.X + region.Width,
                    Y2 = region.Y + region.Height,
                    Color = highlight ? HighlightYellow : AnnotationRed,
                    Opacity = highlight ? 0.35 : 1,
                    StrokeThickness = _stroke
                }));
                break;
        }
    }

    private void AddCallout(WpfPoint displayPoint)
    {
        if (ToImagePoint(displayPoint) is not { } point)
        {
            return;
        }

        Change(() => _annotations.Add(new ScreenshotAnnotation
        {
            Type = ScreenshotAnnotationType.Marker,
            X1 = point.X,
            Y1 = point.Y,
            X2 = _calloutRadius,
            Y2 = _calloutRadius,
            Text = ScreenshotEditing.NextCalloutNumber(_annotations).ToString(),
            Color = AnnotationRed,
            StrokeThickness = _stroke
        }));
    }

    // ---- Undo, reset, crop -------------------------------------------------------------------------------------

    /// <summary>Applies a screenshot edit so it can be undone.</summary>
    private void Change(Action change)
    {
        _undo.Push(Snapshot());
        _redo.Clear();
        change();
        ScreenshotChanged = true;
        RenderEdits();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Push(Snapshot());
        Restore(_undo.Pop());
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Push(Snapshot());
        Restore(_redo.Pop());
    }

    private void RemoveCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_crop is not null)
        {
            Change(() => _crop = null);
        }
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_crop is null && _redactions.Count == 0 && _annotations.Count == 0 && !_request.HasLegacyEdits)
        {
            return;
        }

        Change(() =>
        {
            _crop = null;
            _redactions.Clear();
            _annotations.Clear();
        });
    }

    private EditState Snapshot()
    {
        return new EditState([.. _redactions], _annotations.Select(ScreenshotEditing.Clone).ToList(), _crop);
    }

    private void Restore(EditState state)
    {
        _redactions = [.. state.Redactions];
        _annotations = state.Annotations.Select(ScreenshotEditing.Clone).ToList();
        _crop = state.Crop;
        ScreenshotChanged = true;
        RenderEdits();
    }

    // ---- Text, save, close -------------------------------------------------------------------------------------

    private void Text_Changed(object sender, TextChangedEventArgs e)
    {
        _textChanged = true;
    }

    private void UseSuggestedTitle_Click(object sender, RoutedEventArgs e)
    {
        TitleBox.Text = _request.SuggestedTitle;
        TitleBox.Focus();
        TitleBox.CaretIndex = TitleBox.Text.Length;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveAndClose();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            TitleBox.Text = _request.SuggestedTitle;
        }

        _saved = true;
        DialogResult = true;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_saved || !HasUnsavedChanges)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            this,
            "Save your changes to this step?",
            "OpenSteps",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
        }
        else if (answer == MessageBoxResult.Yes)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(SaveAndClose);
        }
    }

    private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.S)
        {
            SaveAndClose();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _dragStart is not null)
        {
            CancelDrag();
            e.Handled = true;
            return;
        }

        // Text boxes keep their own undo.
        if (Keyboard.FocusedElement is WpfTextBox)
        {
            return;
        }

        if (ctrl && e.Key == Key.Z)
        {
            Undo();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.Y)
        {
            Redo();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    // ---- Drawing -----------------------------------------------------------------------------------------------

    private void RenderEdits()
    {
        if (_dragStart is not null)
        {
            return;
        }

        OverlayCanvas.Children.Clear();
        foreach (var region in _redactions)
        {
            RenderRedaction(region);
        }

        foreach (var annotation in _annotations)
        {
            RenderAnnotation(annotation);
        }

        RenderCrop();

        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
        RemoveCropButton.IsEnabled = _crop is not null;
        ResetButton.IsEnabled = _crop is not null || _redactions.Count > 0 || _annotations.Count > 0 || (_request.HasLegacyEdits && !ScreenshotChanged);
        if (_tool == StepEditTool.Callout)
        {
            ToolHintText.Text = $"Click to place callout {ScreenshotEditing.NextCalloutNumber(_annotations)}.";
        }
    }

    /// <summary>Dims everything outside the crop and outlines it, so the hidden part can still be seen and recovered.</summary>
    private void RenderCrop()
    {
        if (_crop is not { } crop)
        {
            return;
        }

        var image = ToDisplayRect(0, 0, _imageWidth, _imageHeight);
        var keep = ToDisplayRect(crop.X, crop.Y, crop.Width, crop.Height);
        var shade = new SolidColorBrush(WpfColor.FromArgb(150, 15, 23, 42));
        AddShade(shade, image.Left, image.Top, image.Width, keep.Top - image.Top);
        AddShade(shade, image.Left, keep.Bottom, image.Width, image.Bottom - keep.Bottom);
        AddShade(shade, image.Left, keep.Top, keep.Left - image.Left, keep.Height);
        AddShade(shade, keep.Right, keep.Top, image.Right - keep.Right, keep.Height);

        var border = new WpfRectangle
        {
            Width = keep.Width,
            Height = keep.Height,
            Stroke = WpfBrushes.White,
            StrokeThickness = 2,
            StrokeDashArray = [4, 3],
            IsHitTestVisible = false
        };
        Canvas.SetLeft(border, keep.X);
        Canvas.SetTop(border, keep.Y);
        OverlayCanvas.Children.Add(border);
    }

    private void AddShade(WpfBrush brush, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var rectangle = new WpfRectangle { Width = width, Height = height, Fill = brush, IsHitTestVisible = false };
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        OverlayCanvas.Children.Add(rectangle);
    }

    private void RenderRedaction(RedactionRegion region)
    {
        var display = ToDisplayRect(region.X, region.Y, region.Width, region.Height);
        if (region.Mode == RedactionMode.Pixelate)
        {
            var pixelated = new System.Windows.Controls.Image
            {
                Source = CreatePixelatedRegionSource(region),
                Stretch = Stretch.Fill,
                Width = display.Width,
                Height = display.Height,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(pixelated, BitmapScalingMode.NearestNeighbor);
            Place(pixelated, display);
        }
        else if (region.Mode == RedactionMode.BlackBox)
        {
            Place(new WpfRectangle { Fill = WpfBrushes.Black, Width = display.Width, Height = display.Height, IsHitTestVisible = false }, display);
        }
        else if (region.Mode == RedactionMode.RedCircle)
        {
            Place(new WpfEllipse
            {
                Stroke = WpfBrushes.Red,
                StrokeThickness = Math.Max(3, Math.Min(display.Width, display.Height) / 18),
                Width = display.Width,
                Height = display.Height,
                IsHitTestVisible = false
            }, display);
        }
    }

    private void RenderAnnotation(ScreenshotAnnotation annotation)
    {
        var scale = GetImageScale();
        var color = ParseColor(annotation.Color);
        var thickness = Math.Max(2, annotation.StrokeThickness * scale);
        switch (annotation.Type)
        {
            case ScreenshotAnnotationType.Rectangle:
            {
                var display = AnnotationRect(annotation);
                Place(new WpfRectangle { Stroke = new SolidColorBrush(color), StrokeThickness = thickness, Width = display.Width, Height = display.Height, IsHitTestVisible = false }, display);
                break;
            }

            case ScreenshotAnnotationType.Highlight:
            {
                var display = AnnotationRect(annotation);
                Place(new WpfRectangle
                {
                    Fill = new SolidColorBrush(WpfColor.FromArgb((byte)(255 * Math.Clamp(annotation.Opacity, 0, 1)), color.R, color.G, color.B)),
                    Stroke = new SolidColorBrush(WpfColor.FromArgb(190, color.R, color.G, color.B)),
                    StrokeThickness = Math.Max(1, thickness / 2),
                    Width = display.Width,
                    Height = display.Height,
                    IsHitTestVisible = false
                }, display);
                break;
            }

            case ScreenshotAnnotationType.Arrow:
            {
                var start = ToDisplayPoint(annotation.X1, annotation.Y1);
                var end = ToDisplayPoint(annotation.X2, annotation.Y2);
                var brush = new SolidColorBrush(color);
                OverlayCanvas.Children.Add(new WpfLine
                {
                    X1 = start.X,
                    Y1 = start.Y,
                    X2 = end.X,
                    Y2 = end.Y,
                    Stroke = brush,
                    StrokeThickness = thickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false
                });
                OverlayCanvas.Children.Add(CreateArrowHead(start, end, thickness, brush));
                break;
            }

            case ScreenshotAnnotationType.Marker:
            {
                var center = ToDisplayPoint(annotation.X1, annotation.Y1);
                var radius = Math.Max(9, Math.Abs(annotation.X2) * scale);
                var marker = new Grid { Width = radius * 2, Height = radius * 2, IsHitTestVisible = false };
                marker.Children.Add(new WpfEllipse { Fill = new SolidColorBrush(color), Stroke = WpfBrushes.White, StrokeThickness = Math.Max(1.5, thickness / 2) });
                marker.Children.Add(new TextBlock
                {
                    Text = annotation.Text,
                    Foreground = WpfBrushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = Math.Max(9, radius * 0.95),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                });
                Canvas.SetLeft(marker, center.X - radius);
                Canvas.SetTop(marker, center.Y - radius);
                OverlayCanvas.Children.Add(marker);
                break;
            }
        }
    }

    private Shape CreatePreviewShape()
    {
        var red = new SolidColorBrush(ParseColor(AnnotationRed));
        var thickness = Math.Max(2, _stroke * GetImageScale());
        return _tool switch
        {
            StepEditTool.Arrow => new WpfLine { Stroke = red, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round },
            StepEditTool.Circle => new WpfEllipse { Stroke = WpfBrushes.Red, StrokeThickness = 3 },
            StepEditTool.Highlight => new WpfRectangle { Fill = new SolidColorBrush(WpfColor.FromArgb(95, 255, 216, 77)), Stroke = new SolidColorBrush(WpfColor.FromRgb(214, 154, 0)), StrokeThickness = 1 },
            StepEditTool.Box => new WpfRectangle { Stroke = red, StrokeThickness = thickness },
            StepEditTool.Crop => new WpfRectangle { Stroke = WpfBrushes.White, StrokeThickness = 2, StrokeDashArray = [4, 3], Fill = new SolidColorBrush(WpfColor.FromArgb(30, 255, 255, 255)) },
            _ => new WpfRectangle { Stroke = WpfBrushes.White, StrokeThickness = 2, Fill = new SolidColorBrush(WpfColor.FromArgb(60, 15, 23, 42)) }
        };
    }

    private static void PositionPreviewShape(Shape shape, WpfPoint start, WpfPoint end)
    {
        if (shape is WpfLine line)
        {
            line.X1 = start.X;
            line.Y1 = start.Y;
            line.X2 = end.X;
            line.Y2 = end.Y;
            return;
        }

        Canvas.SetLeft(shape, Math.Min(start.X, end.X));
        Canvas.SetTop(shape, Math.Min(start.Y, end.Y));
        shape.Width = Math.Abs(end.X - start.X);
        shape.Height = Math.Abs(end.Y - start.Y);
    }

    private static Polygon CreateArrowHead(WpfPoint start, WpfPoint end, double thickness, WpfBrush brush)
    {
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var length = Math.Max(12, thickness * 4);
        const double spread = Math.PI / 7;
        return new Polygon
        {
            Fill = brush,
            IsHitTestVisible = false,
            Points =
            [
                end,
                new WpfPoint(end.X - length * Math.Cos(angle - spread), end.Y - length * Math.Sin(angle - spread)),
                new WpfPoint(end.X - length * Math.Cos(angle + spread), end.Y - length * Math.Sin(angle + spread))
            ]
        };
    }

    private void Place(UIElement element, Rect display)
    {
        Canvas.SetLeft(element, display.X);
        Canvas.SetTop(element, display.Y);
        OverlayCanvas.Children.Add(element);
    }

    private BitmapSource CreatePixelatedRegionSource(RedactionRegion region)
    {
        var x = Math.Clamp(region.X, 0, _imageWidth);
        var y = Math.Clamp(region.Y, 0, _imageHeight);
        var width = Math.Clamp(region.Width, 0, _imageWidth - x);
        var height = Math.Clamp(region.Height, 0, _imageHeight - y);
        if (width <= 0 || height <= 0)
        {
            return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        }

        const int blockSize = 18;
        var stride = width * 4;
        var source = new byte[stride * height];
        _imageSource.CopyPixels(new Int32Rect(x, y, width, height), source, stride, 0);
        var output = new byte[source.Length];
        for (var blockY = 0; blockY < height; blockY += blockSize)
        {
            for (var blockX = 0; blockX < width; blockX += blockSize)
            {
                var right = Math.Min(width, blockX + blockSize);
                var bottom = Math.Min(height, blockY + blockSize);
                long b = 0, g = 0, r = 0, a = 0;
                var count = 0;
                for (var py = blockY; py < bottom; py++)
                {
                    for (var px = blockX; px < right; px++)
                    {
                        var i = py * stride + px * 4;
                        b += source[i];
                        g += source[i + 1];
                        r += source[i + 2];
                        a += source[i + 3];
                        count++;
                    }
                }

                for (var py = blockY; py < bottom; py++)
                {
                    for (var px = blockX; px < right; px++)
                    {
                        var i = py * stride + px * 4;
                        output[i] = (byte)(b / count);
                        output[i + 1] = (byte)(g / count);
                        output[i + 2] = (byte)(r / count);
                        output[i + 3] = (byte)(a / count);
                    }
                }
            }
        }

        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, output, stride);
    }

    // ---- Coordinates -------------------------------------------------------------------------------------------

    private Rect AnnotationRect(ScreenshotAnnotation annotation)
    {
        var left = Math.Min(annotation.X1, annotation.X2);
        var top = Math.Min(annotation.Y1, annotation.Y2);
        return ToDisplayRect(left, top, Math.Abs(annotation.X2 - annotation.X1), Math.Abs(annotation.Y2 - annotation.Y1));
    }

    private Rect ToDisplayRect(double x, double y, double width, double height)
    {
        var topLeft = ToDisplayPoint(x, y);
        var scale = GetImageScale();
        return new Rect(topLeft.X, topLeft.Y, Math.Max(0, width * scale), Math.Max(0, height * scale));
    }

    private WpfPoint ToDisplayPoint(double imageX, double imageY)
    {
        var scale = GetImageScale();
        var offsetX = (OverlayCanvas.ActualWidth - _imageWidth * scale) / 2;
        var offsetY = (OverlayCanvas.ActualHeight - _imageHeight * scale) / 2;
        return new WpfPoint(offsetX + imageX * scale, offsetY + imageY * scale);
    }

    private (double X, double Y)? ToImagePoint(WpfPoint display)
    {
        return RedactionCoordinateMapper.DisplayToImagePoint(display.X, display.Y, OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight, _imageWidth, _imageHeight);
    }

    private double GetImageScale()
    {
        if (OverlayCanvas.ActualWidth <= 0 || OverlayCanvas.ActualHeight <= 0 || _imageWidth <= 0 || _imageHeight <= 0)
        {
            return 1;
        }

        return Math.Min(OverlayCanvas.ActualWidth / _imageWidth, OverlayCanvas.ActualHeight / _imageHeight);
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    private static WpfColor ParseColor(string? value)
    {
        try
        {
            return (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(value) ? AnnotationRed : value);
        }
        catch
        {
            return Colors.Red;
        }
    }

    private static BitmapSource LoadImage(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Screenshot was not found.", path);
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();

        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private sealed record EditState(List<RedactionRegion> Redactions, List<ScreenshotAnnotation> Annotations, ScreenBounds? Crop);
}
