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
                // If loading file fails, fallback to app icon
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

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
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
        Opacity = 0;
        var translate = new TranslateTransform(0, 18);
        RenderTransform = translate;

        var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slideAnim = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(OpacityProperty, fadeAnim);
        translate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
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

        var fadeAnim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(160))
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

        BeginAnimation(OpacityProperty, fadeAnim);
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        AnimateAndClose(notifyClicked: true);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        AnimateAndClose(notifyClicked: false);
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoCloseTimer.Stop();
        CardBorder.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
    }

    private void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        CardBorder.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        if (!_isClosing)
        {
            _autoCloseTimer.Interval = TimeSpan.FromSeconds(2.5);
            _autoCloseTimer.Start();
        }
    }
}
