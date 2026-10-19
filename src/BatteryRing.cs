using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BluetoothBatteryMonitor
{
    internal sealed class BatteryRing : Grid
    {
        private readonly Ellipse _track;
        private readonly Ellipse _fullRing;
        private readonly Path _progress;
        private readonly TextBlock _text;
        private readonly TextBlock _subtitle;
        private double _ringSize;
        private int? _percent;

        public BatteryRing(double size)
        {
            _track = new Ellipse { Fill = Brushes.Transparent };
            _fullRing = new Ellipse { Fill = Brushes.Transparent, Visibility = Visibility.Collapsed };
            _progress = new Path
            {
                Fill = Brushes.Transparent,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            _text = Theme.Text("--", 11, FontWeights.Bold, Theme.TextColor);
            _text.HorizontalAlignment = HorizontalAlignment.Center;
            _text.VerticalAlignment = VerticalAlignment.Center;
            _text.TextAlignment = TextAlignment.Center;
            _subtitle = Theme.Text(string.Empty, 7.5, FontWeights.Normal, Theme.MutedColor);
            _subtitle.HorizontalAlignment = HorizontalAlignment.Center;
            _subtitle.TextAlignment = TextAlignment.Center;
            _subtitle.Margin = new Thickness(0, -1, 0, 0);
            StackPanel label = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.Children.Add(_text);
            label.Children.Add(_subtitle);
            Children.Add(_track);
            Children.Add(_fullRing);
            Children.Add(_progress);
            Children.Add(label);
            SetSize(size);
        }

        public void SetSize(double size)
        {
            _ringSize = Math.Max(34, Math.Min(86, size));
            Width = _ringSize;
            Height = _ringSize;
            double thickness = Math.Max(3.2, Math.Min(6.5, _ringSize * 0.085));
            _track.StrokeThickness = thickness;
            _fullRing.StrokeThickness = thickness;
            _progress.StrokeThickness = thickness;
            _text.FontSize = Math.Max(9.5, Math.Min(16.5, _ringSize * 0.235));
            _subtitle.FontSize = Math.Max(7, Math.Min(9, _ringSize * 0.105));
            UpdateGeometry();
        }

        public void Update(
            int? percent,
            Color foreground,
            Color trackColor,
            string subtitle = null,
            Color? progressColor = null,
            string displayText = null)
        {
            _percent = percent.HasValue ? Math.Max(0, Math.Min(100, percent.Value)) : (int?)null;
            Color ringColor = progressColor ?? foreground;
            _track.Stroke = Theme.Brush(trackColor);
            _fullRing.Stroke = Theme.Brush(ringColor);
            _progress.Stroke = Theme.Brush(ringColor);
            _text.Foreground = Theme.Brush(foreground);
            _subtitle.Foreground = Theme.Brush(Theme.Blend(foreground, trackColor, 0.28));
            _text.Text = !string.IsNullOrWhiteSpace(displayText)
                ? displayText
                : _percent.HasValue ? _percent.Value + "%" : "--";
            double textRatio = !string.IsNullOrWhiteSpace(displayText)
                ? 0.17
                : _percent.HasValue && _percent.Value >= 100 ? 0.205 : 0.235;
            _text.FontSize = Math.Max(9.2, Math.Min(16.5, _ringSize * textRatio));
            _subtitle.Text = subtitle ?? string.Empty;
            _subtitle.Visibility = string.IsNullOrWhiteSpace(_subtitle.Text) ? Visibility.Collapsed : Visibility.Visible;
            UpdateGeometry();
        }

        private void UpdateGeometry()
        {
            if (!_percent.HasValue || _percent.Value <= 0)
            {
                _progress.Visibility = Visibility.Collapsed;
                _fullRing.Visibility = Visibility.Collapsed;
                return;
            }
            if (_percent.Value >= 100)
            {
                _progress.Visibility = Visibility.Collapsed;
                _fullRing.Visibility = Visibility.Visible;
                return;
            }

            _fullRing.Visibility = Visibility.Collapsed;
            _progress.Visibility = Visibility.Visible;
            double stroke = _progress.StrokeThickness;
            double center = _ringSize / 2.0;
            double radius = Math.Max(1, center - stroke / 2.0 - 1);
            double angle = (_percent.Value / 100.0 * Math.PI * 2.0) - (Math.PI / 2.0);
            Point start = new Point(center, center - radius);
            Point end = new Point(center + radius * Math.Cos(angle), center + radius * Math.Sin(angle));
            ArcSegment arc = new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                IsLargeArc = _percent.Value > 50,
                SweepDirection = SweepDirection.Clockwise
            };
            PathFigure figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(arc);
            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            _progress.Data = geometry;
        }
    }
}
