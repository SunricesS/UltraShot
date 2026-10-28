using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UltraShot.Core;

namespace UltraShot.UI;

public partial class OverlayWindow : Window
{
    private readonly ScreenCaptureResult _capture;
    private Point _startPoint;
    private bool _isSelecting;
    private Rect _selectedRect;

    public event Action<BitmapSource>? ScreenshotCaptured;

    public OverlayWindow(ScreenCaptureResult capture)
    {
        InitializeComponent();
        _capture = capture;

        // Position over entire virtual desktop
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        BackgroundImage.Source = _capture.FullScreenBitmap;
        BackgroundImage.Width = Width;
        BackgroundImage.Height = Height;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
        UpdateMask(Rect.Empty);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            Close();
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            _startPoint = e.GetPosition(RootGrid);
            _isSelecting = true;
            TipBadge.Visibility = Visibility.Collapsed;

            SelectionBorder.Visibility = Visibility.Visible;
            DimensionBadge.Visibility = Visibility.Visible;

            UpdateSelection(_startPoint);
            CaptureMouse();
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting) return;

        var currentPoint = e.GetPosition(RootGrid);
        UpdateSelection(currentPoint);
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting || e.ChangedButton != MouseButton.Left) return;

        _isSelecting = false;
        ReleaseMouseCapture();

        if (_selectedRect.Width > 5 && _selectedRect.Height > 5)
        {
            CompleteCapture();
        }
        else
        {
            // Click without drag, cancel
            Close();
        }
    }

    private void UpdateSelection(Point currentPoint)
    {
        double x = Math.Min(_startPoint.X, currentPoint.X);
        double y = Math.Min(_startPoint.Y, currentPoint.Y);
        double w = Math.Abs(_startPoint.X - currentPoint.X);
        double h = Math.Abs(_startPoint.Y - currentPoint.Y);

        _selectedRect = new Rect(x, y, w, h);

        SelectionBorder.Margin = new Thickness(x, y, 0, 0);
        SelectionBorder.Width = w;
        SelectionBorder.Height = h;

        // Scale factors to compute real pixel dimensions for badge
        double scaleX = _capture.FullScreenBitmap.PixelWidth / ActualWidth;
        double scaleY = _capture.FullScreenBitmap.PixelHeight / ActualHeight;

        int pixelW = (int)Math.Round(w * scaleX);
        int pixelH = (int)Math.Round(h * scaleY);
        DimensionText.Text = $"{pixelW} × {pixelH}";

        // Position dimension badge near selection
        double badgeTop = y - 32;
        if (badgeTop < 10) badgeTop = y + h + 10;
        DimensionBadge.Margin = new Thickness(x, badgeTop, 0, 0);

        UpdateMask(_selectedRect);
    }

    private void UpdateMask(Rect cutRect)
    {
        var fullRect = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        if (cutRect.IsEmpty || cutRect.Width <= 0 || cutRect.Height <= 0)
        {
            OverlayMaskPath.Data = fullRect;
        }
        else
        {
            var cutout = new RectangleGeometry(cutRect);
            OverlayMaskPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, cutout);
        }
    }

    private void CompleteCapture()
    {
        try
        {
            double scaleX = _capture.FullScreenBitmap.PixelWidth / ActualWidth;
            double scaleY = _capture.FullScreenBitmap.PixelHeight / ActualHeight;

            int pixelX = (int)Math.Round(_selectedRect.X * scaleX);
            int pixelY = (int)Math.Round(_selectedRect.Y * scaleY);
            int pixelW = (int)Math.Round(_selectedRect.Width * scaleX);
            int pixelH = (int)Math.Round(_selectedRect.Height * scaleY);

            var cropped = ScreenCaptureService.CropBitmap(_capture.FullScreenBitmap, pixelX, pixelY, pixelW, pixelH);

            // Instant clipboard copy as required!
            if (SettingsManager.Current.CopyImageToClipboard)
            {
                Clipboard.SetImage(cropped);
            }

            ScreenshotCaptured?.Invoke(cropped);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Kırpma hatası: {ex.Message}");
        }
        finally
        {
            Close();
        }
    }
}

