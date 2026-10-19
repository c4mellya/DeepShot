using System.Windows;
using System.Windows.Input;

namespace ZeepCut.History;

/// <summary>Distinguish an image drag from a click using Windows' drag thresholds.</summary>
internal static class HistoryImageDrag
{
    internal static void Attach(FrameworkElement image, Action<DependencyObject> beginDrag)
    {
        Point? start = null;

        void Reset()
        {
            start = null;
            if (image.IsMouseCaptured) image.ReleaseMouseCapture();
        }

        image.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // Leave double-clicks available for the gallery's preview action.
            if (e.ClickCount != 1) return;
            start = e.GetPosition(image);
            if (!image.CaptureMouse())
            {
                start = null;
                return;
            }
            // Thumbnail dragging exports the file; dragging card text may still scroll.
            e.Handled = true;
        };
        image.PreviewMouseMove += (_, e) =>
        {
            if (start is not { } origin) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                Reset();
                return;
            }
            var position = e.GetPosition(image);
            if (Math.Abs(position.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(position.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            // DoDragDrop manages its own mouse capture and Escape cancellation.
            Reset();
            e.Handled = true;
            beginDrag(image);
        };
        image.PreviewMouseLeftButtonUp += (_, _) => Reset();
        image.LostMouseCapture += (_, _) => start = null;
    }
}
