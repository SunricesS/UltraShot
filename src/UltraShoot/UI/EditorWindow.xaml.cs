using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using UltraShoot.Core;

namespace UltraShoot.UI;

public partial class EditorWindow : Window
{
    private string? _filePath;
    private BitmapSource? _originalBitmap;
    private Color _selectedColor = Color.FromRgb(255, 59, 48); // Red
    private double _strokeThickness = 4;
    private string _currentTool = "Pen";

    // Shape drawing state
    private Point _shapeStartPoint;
    private bool _isDrawingShape;
    private Shape? _previewShape;
    private readonly Stack<UIElement> _undoShapes = new();
    private Rect _cropRect;
    private Rectangle? _cropGuideRect;

    public EditorWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
    }

    public EditorWindow(BitmapSource bitmap)
    {
        InitializeComponent();
        _originalBitmap = bitmap;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadImage();
        if (PenToolBtn != null) PenToolBtn.IsChecked = true;
        if (StrokeThicknessCombo != null) StrokeThicknessCombo.SelectedIndex = 1;
        UpdateDrawingAttributes();
    }

    private void LoadImage()
    {
        try
        {
            if (_originalBitmap == null && !string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                byte[] bytes = File.ReadAllBytes(_filePath);
                using var ms = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = ms;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                _originalBitmap = bitmap;
            }

            if (_originalBitmap != null)
            {
                SourceImage.Source = _originalBitmap;
                CanvasContainer.Width = _originalBitmap.PixelWidth;
                CanvasContainer.Height = _originalBitmap.PixelHeight;

                DrawingInkCanvas.Width = _originalBitmap.PixelWidth;
                DrawingInkCanvas.Height = _originalBitmap.PixelHeight;

                ShapesCanvas.Width = _originalBitmap.PixelWidth;
                ShapesCanvas.Height = _originalBitmap.PixelHeight;

                if (DimensionsStatusText != null)
                    DimensionsStatusText.Text = $"{_originalBitmap.PixelWidth} × {_originalBitmap.PixelHeight}";
                if (FilePathStatusText != null)
                    FilePathStatusText.Text = !string.IsNullOrEmpty(_filePath) ? System.IO.Path.GetFileName(_filePath) : "Yeni Ekran Görüntüsü";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Resim yüklenemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateDrawingAttributes()
    {
        if (DrawingInkCanvas == null) return;

        var attr = new DrawingAttributes
        {
            Color = _selectedColor,
            Width = _strokeThickness,
            Height = _strokeThickness,
            FitToCurve = true
        };

        if (_currentTool == "Highlighter")
        {
            attr.IsHighlighter = true;
            attr.Color = Color.FromArgb(120, _selectedColor.R, _selectedColor.G, _selectedColor.B);
            attr.Width = _strokeThickness * 3;
            attr.Height = _strokeThickness * 3;
        }

        DrawingInkCanvas.DefaultDrawingAttributes = attr;

        if (_currentTool is "Pen" or "Highlighter")
        {
            DrawingInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            if (ShapesCanvas != null) ShapesCanvas.IsHitTestVisible = false;
        }
        else
        {
            DrawingInkCanvas.EditingMode = InkCanvasEditingMode.None;
            if (ShapesCanvas != null) ShapesCanvas.IsHitTestVisible = true;
        }

        if (ApplyCropBtn != null)
        {
            ApplyCropBtn.Visibility = _currentTool == "Crop" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            _currentTool = rb.Name switch
            {
                "HighlighterToolBtn" => "Highlighter",
                "ArrowToolBtn" => "Arrow",
                "RectToolBtn" => "Rect",
                "CropToolBtn" => "Crop",
                _ => "Pen"
            };

            if (ToolStatusText != null)
            {
                ToolStatusText.Text = $"{_currentTool} aracı aktif";
            }
            UpdateDrawingAttributes();
        }
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            _selectedColor = (Color)ColorConverter.ConvertFromString(hex);
            UpdateDrawingAttributes();
        }
    }

    private void Thickness_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (StrokeThicknessCombo?.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(), out var thickness))
        {
            _strokeThickness = thickness;
            UpdateDrawingAttributes();
        }
    }

    private void ShapesCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        _shapeStartPoint = e.GetPosition(ShapesCanvas);
        _isDrawingShape = true;

        if (_currentTool == "Rect")
        {
            _previewShape = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(_selectedColor),
                StrokeThickness = _strokeThickness,
                Fill = Brushes.Transparent
            };
            ShapesCanvas.Children.Add(_previewShape);
        }
        else if (_currentTool == "Arrow")
        {
            _previewShape = new System.Windows.Shapes.Path
            {
                Stroke = new SolidColorBrush(_selectedColor),
                StrokeThickness = _strokeThickness,
                Fill = new SolidColorBrush(_selectedColor)
            };
            ShapesCanvas.Children.Add(_previewShape);
        }
        else if (_currentTool == "Crop")
        {
            if (_cropGuideRect == null)
            {
                _cropGuideRect = new Rectangle
                {
                    Stroke = Brushes.Aqua,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Fill = new SolidColorBrush(Color.FromArgb(40, 0, 200, 255))
                };
                ShapesCanvas.Children.Add(_cropGuideRect);
            }
        }

        ShapesCanvas.CaptureMouse();
    }

    private void ShapesCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawingShape) return;

        var current = e.GetPosition(ShapesCanvas);

        if (_currentTool == "Rect" && _previewShape is Rectangle rect)
        {
            double x = Math.Min(_shapeStartPoint.X, current.X);
            double y = Math.Min(_shapeStartPoint.Y, current.Y);
            double w = Math.Abs(_shapeStartPoint.X - current.X);
            double h = Math.Abs(_shapeStartPoint.Y - current.Y);

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            rect.Width = w;
            rect.Height = h;
        }
        else if (_currentTool == "Arrow" && _previewShape is System.Windows.Shapes.Path path)
        {
            path.Data = CreateArrowGeometry(_shapeStartPoint, current, _strokeThickness);
        }
        else if (_currentTool == "Crop" && _cropGuideRect != null)
        {
            double x = Math.Min(_shapeStartPoint.X, current.X);
            double y = Math.Min(_shapeStartPoint.Y, current.Y);
            double w = Math.Abs(_shapeStartPoint.X - current.X);
            double h = Math.Abs(_shapeStartPoint.Y - current.Y);

            _cropRect = new Rect(x, y, w, h);
            Canvas.SetLeft(_cropGuideRect, x);
            Canvas.SetTop(_cropGuideRect, y);
            _cropGuideRect.Width = w;
            _cropGuideRect.Height = h;
        }
    }

    private void ShapesCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawingShape || e.ChangedButton != MouseButton.Left) return;

        _isDrawingShape = false;
        ShapesCanvas.ReleaseMouseCapture();

        if (_previewShape != null)
        {
            _undoShapes.Push(_previewShape);
            _previewShape = null;
        }
    }

    private Geometry CreateArrowGeometry(Point start, Point end, double thickness)
    {
        var geom = new GeometryGroup();
        geom.Children.Add(new LineGeometry(start, end));

        // Arrowhead
        var dir = end - start;
        if (dir.Length > 0)
        {
            dir.Normalize();
            var normal = new Vector(-dir.Y, dir.X);
            double headLength = Math.Max(14, thickness * 3.5);
            double headWidth = Math.Max(10, thickness * 2.5);

            Point arrowTip = end;
            Point arrowLeft = end - dir * headLength + normal * headWidth;
            Point arrowRight = end - dir * headLength - normal * headWidth;

            var headGeom = new PathGeometry();
            var figure = new PathFigure { StartPoint = arrowTip, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(arrowLeft, true));
            figure.Segments.Add(new LineSegment(arrowRight, true));
            headGeom.Figures.Add(figure);
            geom.Children.Add(headGeom);
        }

        return geom;
    }

    private void ApplyCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_cropRect.Width < 10 || _cropRect.Height < 10)
        {
            MessageBox.Show("Lütfen kırpmak için görsel üzerinde geçerli bir alan seçin.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Render current visual to bitmap first, then crop
        var currentRendered = RenderToBitmap();
        int cx = (int)Math.Max(0, _cropRect.X);
        int cy = (int)Math.Max(0, _cropRect.Y);
        int cw = (int)Math.Min(_cropRect.Width, currentRendered.PixelWidth - cx);
        int ch = (int)Math.Min(_cropRect.Height, currentRendered.PixelHeight - cy);

        var cropped = ScreenCaptureService.CropBitmap(currentRendered, cx, cy, cw, ch);
        _originalBitmap = cropped;

        // Reset canvases
        DrawingInkCanvas.Strokes.Clear();
        ShapesCanvas.Children.Clear();
        _undoShapes.Clear();
        _cropGuideRect = null;
        _cropRect = Rect.Empty;

        // Reload cropped image
        LoadImage();

        // Switch back to pen
        PenToolBtn.IsChecked = true;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoShapes.Count > 0)
        {
            var shape = _undoShapes.Pop();
            ShapesCanvas.Children.Remove(shape);
        }
        else if (DrawingInkCanvas.Strokes.Count > 0)
        {
            DrawingInkCanvas.Strokes.RemoveAt(DrawingInkCanvas.Strokes.Count - 1);
        }
    }

    private RenderTargetBitmap RenderToBitmap()
    {
        int w = (int)CanvasContainer.ActualWidth;
        int h = (int)CanvasContainer.ActualHeight;

        if (w <= 0 || h <= 0)
        {
            w = _originalBitmap?.PixelWidth ?? 800;
            h = _originalBitmap?.PixelHeight ?? 600;
        }

        // Temporarily hide crop guides during render
        if (_cropGuideRect != null) _cropGuideRect.Visibility = Visibility.Collapsed;

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(CanvasContainer);

        if (_cropGuideRect != null) _cropGuideRect.Visibility = Visibility.Visible;

        return rtb;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var bitmap = RenderToBitmap();
        Clipboard.SetImage(bitmap);
        ToolStatusText.Text = "Görsel panoya kopyalandı! (Ctrl+V ile yapıştırabilirsiniz)";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var bitmap = RenderToBitmap();

        if (string.IsNullOrEmpty(_filePath))
        {
            _filePath = StorageService.GenerateUniqueFilePath();
        }

        StorageService.SaveBitmapDirect(bitmap, _filePath);
        ToolStatusText.Text = $"Kaydedildi: {System.IO.Path.GetFileName(_filePath)}";
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var ext = SettingsManager.Current.GetFileExtension();
        var sfd = new SaveFileDialog
        {
            Filter = "PNG Resmi (*.png)|*.png|JPEG Resmi (*.jpg)|*.jpg|Bitmap Resmi (*.bmp)|*.bmp",
            FileName = !string.IsNullOrEmpty(_filePath) ? System.IO.Path.GetFileName(_filePath) : $"UltraShoot_Screenshot{ext}",
            DefaultExt = ext
        };

        if (sfd.ShowDialog() == true)
        {
            var bitmap = RenderToBitmap();
            StorageService.SaveBitmapDirect(bitmap, sfd.FileName);
            _filePath = sfd.FileName;
            FilePathStatusText.Text = System.IO.Path.GetFileName(_filePath);
            ToolStatusText.Text = $"Farklı kaydedildi: {System.IO.Path.GetFileName(_filePath)}";
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Undo_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Copy_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Save_Click(this, new RoutedEventArgs());
        }
    }
}
