using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UltraShot.UI;

public partial class NotificationToastWindow : Window
{
    public event Action? NotificationClicked;
    public event Action? NotificationClosed;

    private readonly DispatcherTimer _autoCloseTimer;
    private bool _isClosing;
    private TranslateTransform _translateTransform = new(0, 0);

    private bool _isMouseDown;
    private bool _isDragging;
    private Point _mouseDownScreenPoint;

    public NotificationToastWindow(string title, string text, string? imagePath = null, BitmapSource? bitmap = null)
    {
        InitializeComponent();

        TitleTextBlock.Text = title;
        MessageTextBlock.Text = text;

        if (bitmap != null)
        {
            ThumbnailImage.Source = bitmap;
        }
        else if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(imagePath);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                ThumbnailImage.Source = bi;
            }
            catch
            {
                try
                {
                    ThumbnailImage.Source = new BitmapImage(new Uri("pack://application:,,,/ico/UltraShot.ico"));
                }
                catch { }
            }
        }
        else
        {
            try
            {
                ThumbnailImage.Source = new BitmapImage(new Uri("pack://application:,,,/ico/UltraShot.ico"));
            }
            catch { }
        }

        // Auto close after 5 seconds if untouched
        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            AnimateAndClose(notifyClicked: false);
        };

        Loaded += NotificationToastWindow_Loaded;
    }

    private void NotificationToastWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PositionInBottomRight();
        StartEntranceAnimation();
        _autoCloseTimer.Start();
    }

    private void PositionInBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 14;
        Top = workArea.Bottom - Height - 14;
    }

    private void StartEntranceAnimation()
    {
        CardBorder.Opacity = 0;
        _translateTransform = new TranslateTransform(0, 18);
        CardBorder.RenderTransform = _translateTransform;

        var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slideAnim = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        CardBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        _translateTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
    }

    public void CloseImmediately()
    {
        if (_isClosing) return;
        _isClosing = true;
        _autoCloseTimer.Stop();
        Close();
    }

    private void AnimateAndClose(bool notifyClicked = false)
    {
        if (_isClosing) return;
        _isClosing = true;
        _autoCloseTimer.Stop();

        var fadeAnim = new DoubleAnimation(CardBorder.Opacity, 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeAnim.Completed += (s, e) =>
        {
            if (notifyClicked)
            {
                NotificationClicked?.Invoke();
            }
            NotificationClosed?.Invoke();
            Close();
        };

        CardBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
    }

    private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClosing) return;

        _mouseDownScreenPoint = PointToScreen(e.GetPosition(this));
        _isMouseDown = true;
        _isDragging = false;
        CardBorder.CaptureMouse();
    }

    private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMouseDown || !CardBorder.IsMouseCaptured || _isClosing) return;

        Point currentScreenPoint = PointToScreen(e.GetPosition(this));
        double deltaX = currentScreenPoint.X - _mouseDownScreenPoint.X;

        // Start dragging after 6px horizontal movement to distinguish from click
        if (!_isDragging && deltaX > 6)
        {
            _isDragging = true;
            _autoCloseTimer.Stop();
            _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            CardBorder.BeginAnimation(UIElement.OpacityProperty, null);
        }

        if (_isDragging)
        {
            // Only allow dragging to the right
            double currentX = Math.Max(0, deltaX);
            _translateTransform.X = currentX;

            // Fade out proportionally while swiping right
            double opacityFactor = 1.0 - (currentX / (ActualWidth * 0.75));
            CardBorder.Opacity = Math.Clamp(opacityFactor, 0.05, 1.0);
        }
    }

    private void Card_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMouseDown) return;
        _isMouseDown = false;

        if (CardBorder.IsMouseCaptured)
        {
            CardBorder.ReleaseMouseCapture();
        }

        if (_isClosing) return;

        if (_isDragging)
        {
            double draggedDistance = _translateTransform.X;
            // Dismiss threshold: 60px
            if (draggedDistance > 60)
            {
                SlideOutToRightAndDismiss();
            }
            else
            {
                SnapBack();
            }
        }
        else
        {
            // Standard click without dragging: open editor and close
            AnimateAndClose(notifyClicked: true);
        }
    }

    private void SlideOutToRightAndDismiss()
    {
        if (_isClosing) return;
        _isClosing = true;
        _autoCloseTimer.Stop();

        double startX = _translateTransform.X;
        double targetX = ActualWidth + 40;

        var slideAnim = new DoubleAnimation(startX, targetX, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var fadeAnim = new DoubleAnimation(CardBorder.Opacity, 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        slideAnim.Completed += (s, e) =>
        {
            NotificationClosed?.Invoke();
            Close();
        };

        _translateTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
        CardBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
    }

    private void SnapBack()
    {
        var snapSlide = new DoubleAnimation(_translateTransform.X, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var snapFade = new DoubleAnimation(CardBorder.Opacity, 1.0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        _translateTransform.BeginAnimation(TranslateTransform.XProperty, snapSlide);
        CardBorder.BeginAnimation(UIElement.OpacityProperty, snapFade);

        // Resume 5-second timer
        _autoCloseTimer.Interval = TimeSpan.FromSeconds(5);
        _autoCloseTimer.Start();
    }

    private void CloseButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AnimateAndClose(notifyClicked: false);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        AnimateAndClose(notifyClicked: false);
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_isDragging)
        {
            _autoCloseTimer.Stop();
        }
    }

    private void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isClosing && !_isMouseDown && !_isDragging)
        {
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(5);
            _autoCloseTimer.Start();
        }
    }
}
