using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using UltraShot.Core;

namespace UltraShot.UI;

public partial class EditorWindow : Window
{
    private string? _filePath;
    private readonly DateTime? _capturedAt;
    private BitmapSource? _originalBitmap;
    private Color _selectedColor = Color.FromRgb(255, 59, 48); // Red
    private double _strokeThickness = 4;
    private string _currentTool = "Pen";
    private string _previousDrawTool = "Pen";
    private bool _isFitMode = true;

    // Shape drawing state
    private Point _shapeStartPoint;
    private bool _isDrawingShape;
    private Shape? _previewShape;
    private readonly Stack<UIElement> _undoShapes = new();
    private Rect _cropRect;
    private Rectangle? _cropGuideRect;

    // Text and eyedropper state
    private System.Windows.Controls.TextBox? _activeTextBox;
    private BitmapSource? _eyedropperSnapshot;
    private readonly List<string> _recentColors = [];

    public EditorWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
    }

    public EditorWindow(BitmapSource bitmap, DateTime? capturedAt = null)
    {
        InitializeComponent();
        _originalBitmap = bitmap;
        _capturedAt = capturedAt;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowDimensions();
        LoadImage();
        LoadRecentColors();
        if (PenToolBtn != null) PenToolBtn.IsChecked = true;
        if (StrokeThicknessCombo != null) StrokeThicknessCombo.SelectedIndex = 1;
        UpdateDrawingAttributes();
    }

    private void ApplyWindowDimensions()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1240, Math.Floor(workArea.Width * 0.92));
        Height = Math.Min(860, Math.Floor(workArea.Height * 0.92));
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
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
                    FilePathStatusText.Text = !string.IsNullOrEmpty(_filePath) ? System.IO.Path.GetFileName(_filePath) : "New Screenshot";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        if (ShapesCanvas != null)
        {
            ShapesCanvas.Cursor = _currentTool switch
            {
                "Eyedropper" => System.Windows.Input.Cursors.Cross,
                "Text" => System.Windows.Input.Cursors.IBeam,
                _ => null
            };
        }
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            CommitActiveTextBox();

            _currentTool = rb.Name switch
            {
                "HighlighterToolBtn" => "Highlighter",
                "ArrowToolBtn" => "Arrow",
                "RectToolBtn" => "Rect",
                "TextToolBtn" => "Text",
                "CropToolBtn" => "Crop",
                "EyedropperToolBtn" => "Eyedropper",
                _ => "Pen"
            };

            if (_currentTool is "Pen" or "Highlighter" or "Arrow" or "Rect" or "Text")
            {
                _previousDrawTool = _currentTool;
            }

            // Capture a single composite snapshot for O(1) pixel sampling while Eyedropper is active
            _eyedropperSnapshot = _currentTool == "Eyedropper" ? RenderToBitmap() : null;

            if (ToolStatusText != null)
            {
                ToolStatusText.Text = _currentTool switch
                {
                    "Text" => "Text tool active  •  Click to type  •  Enter: Confirm  •  Shift+Enter: New line  •  Esc: Cancel",
                    "Eyedropper" => "Pick tool active  •  Click anywhere on the image to sample a color",
                    _ => $"{_currentTool} tool active"
                };
            }
            UpdateDrawingAttributes();
        }
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            ApplySelectedColor(color, addToRecent: false);
        }
    }

    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.Color.FromArgb(_selectedColor.R, _selectedColor.G, _selectedColor.B)
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var picked = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            ApplySelectedColor(picked, addToRecent: true);
        }
    }

    private void ApplySelectedColor(Color color, bool addToRecent)
    {
        _selectedColor = color;
        if (ActiveColorPreview != null)
        {
            ActiveColorPreview.Background = new SolidColorBrush(color);
        }

        if (_activeTextBox != null)
        {
            _activeTextBox.Foreground = new SolidColorBrush(color);
            _activeTextBox.CaretBrush = new SolidColorBrush(color);
        }

        if (addToRecent)
        {
            PushRecentColor(color);
        }

        UpdateDrawingAttributes();
    }

    private void LoadRecentColors()
    {
        _recentColors.Clear();
        if (SettingsManager.Current.RecentColors != null)
        {
            foreach (var hex in SettingsManager.Current.RecentColors)
            {
                if (!string.IsNullOrWhiteSpace(hex) && !_recentColors.Contains(hex, StringComparer.OrdinalIgnoreCase))
                {
                    _recentColors.Add(hex);
                    if (_recentColors.Count >= 3) break;
                }
            }
        }
        RefreshRecentColorButtons();
    }

    private void PushRecentColor(Color color)
    {
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _recentColors.RemoveAll(c => c.Equals(hex, StringComparison.OrdinalIgnoreCase));
        _recentColors.Insert(0, hex);
        if (_recentColors.Count > 3)
        {
            _recentColors.RemoveRange(3, _recentColors.Count - 3);
        }

        SettingsManager.Current.RecentColors = [.. _recentColors];
        SettingsManager.Save();
        RefreshRecentColorButtons();
    }

    private void RefreshRecentColorButtons()
    {
        Button?[] buttons = [RecentColorBtn0, RecentColorBtn1, RecentColorBtn2];
        for (int i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            if (btn == null) continue;

            if (i < _recentColors.Count)
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(_recentColors[i]);
                    btn.Background = new SolidColorBrush(color);
                    btn.Tag = _recentColors[i];
                    btn.ToolTip = $"Recent Color: {_recentColors[i]}";
                    btn.IsEnabled = true;
                    btn.Opacity = 1.0;
                    continue;
                }
                catch { }
            }

            btn.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            btn.Tag = null;
            btn.ToolTip = $"Recent Color {i + 1} (Empty)";
            btn.IsEnabled = false;
            btn.Opacity = 0.35;
        }
    }

    private void Thickness_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (StrokeThicknessCombo?.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(), out var thickness))
        {
            _strokeThickness = thickness;
            if (_activeTextBox != null)
            {
                _activeTextBox.FontSize = GetTextFontSize();
            }
            UpdateDrawingAttributes();
        }
    }

    private double GetTextFontSize() => _strokeThickness switch
    {
        <= 2 => 14,
        <= 4 => 18,
        <= 8 => 26,
        _ => 38
    };

    private void ShapesCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        var clickPoint = e.GetPosition(ShapesCanvas);

        if (_currentTool == "Eyedropper")
        {
            PickColorAtPoint(clickPoint);
            return;
        }

        if (_currentTool == "Text")
        {
            CommitActiveTextBox();
            StartTextInputAt(clickPoint);
            return;
        }

        _shapeStartPoint = clickPoint;
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

    private void PickColorAtPoint(Point point)
    {
        var source = _eyedropperSnapshot ?? _originalBitmap;
        if (source == null) return;

        int px = Math.Clamp((int)Math.Floor(point.X), 0, source.PixelWidth - 1);
        int py = Math.Clamp((int)Math.Floor(point.Y), 0, source.PixelHeight - 1);

        BitmapSource pbgraSource = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

        var pixel = new byte[4];
        new CroppedBitmap(pbgraSource, new Int32Rect(px, py, 1, 1)).CopyPixels(pixel, 4, 0);
        var picked = Color.FromRgb(pixel[2], pixel[1], pixel[0]);

        _eyedropperSnapshot = null;
        ApplySelectedColor(picked, addToRecent: true);
        RestorePreviousDrawTool();

        if (ToolStatusText != null)
        {
            ToolStatusText.Text = $"Color picked: #{picked.R:X2}{picked.G:X2}{picked.B:X2}";
        }
    }

    private void RestorePreviousDrawTool()
    {
        var target = _previousDrawTool switch
        {
            "Highlighter" => HighlighterToolBtn,
            "Arrow" => ArrowToolBtn,
            "Rect" => RectToolBtn,
            "Text" => TextToolBtn,
            _ => PenToolBtn
        };
        if (target != null)
        {
            target.IsChecked = true;
        }
    }

    private void StartTextInputAt(Point point)
    {
        double maxX = Math.Max(0, ShapesCanvas.Width - 40);
        double maxY = Math.Max(0, ShapesCanvas.Height - 24);
        double x = Math.Clamp(point.X, 0, maxX);
        double y = Math.Clamp(point.Y, 0, maxY);

        var tb = new System.Windows.Controls.TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
            Foreground = new SolidColorBrush(_selectedColor),
            CaretBrush = new SolidColorBrush(_selectedColor),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 0, 210, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = GetTextFontSize(),
            FontWeight = FontWeights.SemiBold,
            MinWidth = 80
        };

        tb.PreviewKeyDown += (s, args) =>
        {
            if (args.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                args.Handled = true;
                CommitActiveTextBox();
            }
            else if (args.Key == Key.Escape)
            {
                args.Handled = true;
                CancelActiveTextBox();
            }
        };

        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        ShapesCanvas.Children.Add(tb);
        _activeTextBox = tb;

        Dispatcher.BeginInvoke(() => tb.Focus());
    }

    private bool CommitActiveTextBox()
    {
        if (_activeTextBox == null) return false;

        var tb = _activeTextBox;
        _activeTextBox = null;

        double left = Canvas.GetLeft(tb);
        double top = Canvas.GetTop(tb);
        ShapesCanvas.Children.Remove(tb);

        if (string.IsNullOrWhiteSpace(tb.Text)) return false;

        var textBlock = new TextBlock
        {
            Text = tb.Text,
            Foreground = tb.Foreground,
            FontSize = tb.FontSize,
            FontWeight = tb.FontWeight,
            Padding = new Thickness(4, 2, 4, 2)
        };

        Canvas.SetLeft(textBlock, left);
        Canvas.SetTop(textBlock, top);
        ShapesCanvas.Children.Add(textBlock);
        _undoShapes.Push(textBlock);
        return true;
    }

    private void CancelActiveTextBox()
    {
        if (_activeTextBox == null) return;
        ShapesCanvas.Children.Remove(_activeTextBox);
        _activeTextBox = null;
    }

    private void ZoomToggle_Click(object sender, RoutedEventArgs e)
    {
        _isFitMode = !_isFitMode;
        if (_isFitMode)
        {
            CanvasScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            CanvasScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            CanvasViewbox.Stretch = Stretch.Uniform;
            CanvasViewbox.StretchDirection = StretchDirection.DownOnly;
            ZoomToggleText.Text = "View: Fit";
        }
        else
        {
            CanvasScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            CanvasScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            CanvasViewbox.Stretch = Stretch.None;
            ZoomToggleText.Text = "View: 100%";
        }
    }

    private void ApplyCrop_Click(object sender, RoutedEventArgs e)
    {
        CommitActiveTextBox();

        if (_cropRect.Width < 10 || _cropRect.Height < 10)
        {
            MessageBox.Show("Please select a valid region on the image to crop.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
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
        if (_activeTextBox != null)
        {
            CancelActiveTextBox();
            return;
        }

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
        if (CommitActiveTextBox())
        {
            CanvasContainer.UpdateLayout();
        }

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
        ToolStatusText.Text = "Image copied to clipboard! (Press Ctrl+V to paste)";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var bitmap = RenderToBitmap();

        if (string.IsNullOrEmpty(_filePath))
        {
            _filePath = StorageService.GenerateUniqueFilePath(_capturedAt);
        }

        StorageService.SaveBitmapDirect(bitmap, _filePath);
        FilePathStatusText.Text = System.IO.Path.GetFileName(_filePath);
        ToolStatusText.Text = $"Saved: {System.IO.Path.GetFileName(_filePath)}";
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var ext = !string.IsNullOrEmpty(_filePath)
            ? System.IO.Path.GetExtension(_filePath)
            : SettingsManager.Current.GetFileExtension();
        if (string.IsNullOrEmpty(ext))
        {
            ext = SettingsManager.Current.GetFileExtension();
        }

        var sfd = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
            FilterIndex = ext.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => 2,
                ".bmp" => 3,
                _ => 1
            },
            FileName = !string.IsNullOrEmpty(_filePath)
                ? System.IO.Path.GetFileName(_filePath)
                : StorageService.GenerateFileName(ext, _capturedAt),
            DefaultExt = ext
        };

        if (sfd.ShowDialog() == true)
        {
            var bitmap = RenderToBitmap();
            StorageService.SaveBitmapDirect(bitmap, sfd.FileName);
            _filePath = sfd.FileName;
            FilePathStatusText.Text = System.IO.Path.GetFileName(_filePath);
            ToolStatusText.Text = $"Saved as: {System.IO.Path.GetFileName(_filePath)}";
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_activeTextBox != null)
        {
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                Save_Click(this, new RoutedEventArgs());
            }
            return;
        }

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

