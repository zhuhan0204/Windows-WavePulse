using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BluetoothBatteryMonitor
{
    internal sealed class ToggleSwitch : ToggleButton
    {
        public ToggleSwitch()
        {
            Width = 48;
            Height = 26;
            MinWidth = 48;
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            Focusable = false;
            FocusVisualStyle = null;
            OverridesDefaultStyle = true;
            ClipToBounds = true;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            ToolTip = "点击切换";
            IsEnabledChanged += delegate { InvalidateVisual(); };
            MouseEnter += delegate { InvalidateVisual(); };
            MouseLeave += delegate { InvalidateVisual(); };
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            Color trackColor = IsChecked == true
                ? Theme.AccentColor
                : Color.FromRgb(190, 201, 214);
            if (!IsEnabled)
            {
                trackColor = Color.FromRgb(220, 226, 233);
            }
            else if (IsMouseOver && IsChecked != true)
            {
                trackColor = Color.FromRgb(166, 181, 199);
            }

            Rect track = new Rect(0.5, 1.5, System.Math.Max(0, ActualWidth - 1), System.Math.Max(0, ActualHeight - 3));
            drawingContext.DrawRoundedRectangle(
                Theme.Brush(trackColor),
                null,
                track,
                (ActualHeight - 2) / 2,
                (ActualHeight - 2) / 2);

            double radius = 10;
            double centerX = IsChecked == true ? ActualWidth - 13 : 13;
            drawingContext.DrawEllipse(
                Brushes.White,
                null,
                new Point(centerX, ActualHeight / 2),
                radius,
                radius);
        }

        protected override void OnChecked(RoutedEventArgs e)
        {
            base.OnChecked(e);
            InvalidateVisual();
        }

        protected override void OnUnchecked(RoutedEventArgs e)
        {
            base.OnUnchecked(e);
            InvalidateVisual();
        }

    }
}
