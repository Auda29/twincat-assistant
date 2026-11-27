using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TwinCatAssistant.Native
{
    internal sealed class SourceDiffView : DockPanel
    {
        private const double LineHeight = 19;
        private const double FontSize = 13;
        private const int LinesPerLargeBlock = 64;
        private static readonly FontFamily CodeFont = new FontFamily("Consolas");
        private static readonly Brush Removed = ColorBrush(0xFD, 0xEC, 0xEF);
        private static readonly Brush RemovedInline = ColorBrush(0xF9, 0xC6, 0xCE);
        private static readonly Brush Added = ColorBrush(0xEB, 0xF9, 0xEE);
        private static readonly Brush AddedInline = ColorBrush(0xB8, 0xEB, 0xC4);
        private static readonly Brush Filler = ColorBrush(0xF4, 0xF6, 0xF8);
        private static readonly Brush LineNumber = ColorBrush(0x53, 0x61, 0x6E);

        internal static SourceDiffView Build(string before, string after, bool rollback)
        {
            return new SourceDiffView(SourceDiff.Compute(rollback ? after : before, rollback ? before : after));
        }

        private SourceDiffView(SourceDiff diff)
        {
            var before = HostText.Get("EngineeringDiffBefore");
            var after = HostText.Get("EngineeringDiffAfter");
            var toolbar = Columns();
            var controls = new DockPanel { Margin = new Thickness(4, 0, 0, 4) };
            var previous = NavigationButton("▲", "EngineeringDiffPrevious");
            var next = NavigationButton("▼", "EngineeringDiffNext");
            if (diff.HunkRowIndices.Count > 0)
            {
                SetDock(next, Dock.Right);
                SetDock(previous, Dock.Right);
                controls.Children.Add(next);
                controls.Children.Add(previous);
            }
            controls.Children.Add(new TextBlock
            {
                Text = HostText.Format("EngineeringDiffSummary", diff.AddedLines, diff.RemovedLines,
                    diff.ModifiedLines, diff.HunkRowIndices.Count),
                VerticalAlignment = VerticalAlignment.Center
            });
            AddColumn(toolbar, new TextBlock { Text = before, VerticalAlignment = VerticalAlignment.Center }, 0);
            var rightToolbar = new DockPanel();
            var afterLabel = new TextBlock { Text = after, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            SetDock(afterLabel, Dock.Left);
            rightToolbar.Children.Add(afterLabel);
            rightToolbar.Children.Add(controls);
            AddColumn(toolbar, rightToolbar, 1);
            SetDock(toolbar, Dock.Top);
            Children.Add(toolbar);

            var panes = Columns();
            int maxColumns = 0;
            var left = Pane(diff, true, before, ref maxColumns);
            var right = Pane(diff, false, after, ref maxColumns);
            AddColumn(panes, left.Panel, 0);
            AddColumn(panes, right.Panel, 1);
            Children.Add(panes);

            // Both FlowDocuments share a page width and physical row height.
            // Reserve room for tab stops and wider fallback glyphs in ST comments.
            double textWidth = Math.Min(maxColumns * 8.0 + 8, 1_000_000);
            panes.SizeChanged += (sender, args) =>
            {
                double viewport = panes.ActualWidth / 2 - 4 - left.Gutter.Width - SystemParameters.VerticalScrollBarWidth - 2;
                double width = Math.Max(textWidth, viewport);
                left.Text.Document.PageWidth = right.Text.Document.PageWidth = width;
            };

            var synchronizing = false;
            void Sync(ScrollChangedEventArgs args, RichTextBox source, RichTextBox target, Gutter gutter)
            {
                gutter.InvalidateVisual();
                if (synchronizing || (args.VerticalChange == 0 && args.HorizontalChange == 0)) return;
                synchronizing = true;
                try
                {
                    if (args.VerticalChange != 0 && target.VerticalOffset != source.VerticalOffset)
                        target.ScrollToVerticalOffset(source.VerticalOffset);
                    if (args.HorizontalChange != 0 && target.HorizontalOffset != source.HorizontalOffset)
                        target.ScrollToHorizontalOffset(source.HorizontalOffset);
                }
                finally { synchronizing = false; }
            }
            left.Text.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((sender, args) => Sync(args, left.Text, right.Text, left.Gutter)));
            right.Text.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler((sender, args) => Sync(args, right.Text, left.Text, right.Gutter)));

            int hunk = 0;
            void Jump(int index)
            {
                hunk = index;
                previous.IsEnabled = index > 0;
                next.IsEnabled = index + 1 < diff.HunkRowIndices.Count;
                double offset = diff.HunkRowIndices[index] * LineHeight;
                left.Text.ScrollToVerticalOffset(offset);
                right.Text.ScrollToVerticalOffset(offset);
            }
            previous.Click += (sender, args) => Jump(hunk - 1);
            next.Click += (sender, args) => Jump(hunk + 1);
            if (diff.FirstChangedRowIndex.HasValue) Loaded += (sender, args) => Jump(0);
        }

        private static (DockPanel Panel, RichTextBox Text, Gutter Gutter) Pane(SourceDiff diff, bool left, string label, ref int maxColumns)
        {
            var document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontFamily = CodeFont, FontSize = FontSize,
                Foreground = Brushes.Black,
                LineHeight = LineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
            // For large diffs, group adjacent lines with the same background
            // into short paragraphs to limit WPF's block layout overhead.
            int blockLimit = diff.Rows.Count >= 1024 ? LinesPerLargeBlock : 1;
            int blockLines = blockLimit;
            Brush blockBackground = null;
            Paragraph paragraph = null;
            foreach (var row in diff.Rows)
            {
                string line = left ? row.LeftText : row.RightText;
                Brush background = line == null ? Filler : row.Kind == SourceDiffKind.Unchanged ? null : left ? Removed : Added;
                if (blockLines == blockLimit || background != blockBackground)
                {
                    paragraph = new Paragraph { Margin = new Thickness(0), Background = background };
                    document.Blocks.Add(paragraph);
                    blockBackground = background;
                    blockLines = 0;
                }
                else paragraph.Inlines.Add(new LineBreak());
                blockLines++;
                if (line != null)
                {
                    int columns = 0;
                    foreach (char c in line) columns += c == '\t' ? 8 : c > 127 ? 2 : 1;
                    maxColumns = Math.Max(maxColumns, columns);
                    int length = left ? row.LeftHighlightLength : row.RightHighlightLength;
                    int start = row.HighlightStart;
                    if (length == 0)
                    {
                        if (line.Length > 0) paragraph.Inlines.Add(new Run(line));
                    }
                    else
                    {
                        if (start > 0) paragraph.Inlines.Add(new Run(line.Substring(0, start)));
                        paragraph.Inlines.Add(new Run(line.Substring(start, length)) { Background = left ? RemovedInline : AddedInline });
                        if (start + length < line.Length) paragraph.Inlines.Add(new Run(line.Substring(start + length)));
                    }
                }
            }
            var text = new RichTextBox(document)
            {
                IsReadOnly = true, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
                Background = Brushes.White, IsDocumentEnabled = false
            };
            AutomationProperties.SetName(text, label);
            var gutter = new Gutter(diff, left, text) { Width = 52, ClipToBounds = true };
            var panel = new DockPanel { Background = Filler };
            SetDock(gutter, Dock.Left);
            panel.Children.Add(gutter);
            panel.Children.Add(text);
            return (panel, text, gutter);
        }

        private sealed class Gutter : FrameworkElement
        {
            private readonly SourceDiff _diff;
            private readonly bool _left;
            private readonly RichTextBox _text;

            internal Gutter(SourceDiff diff, bool left, RichTextBox text)
            {
                _diff = diff;
                _left = left;
                _text = text;
            }

            protected override void OnRender(DrawingContext drawing)
            {
                var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                double offset = _text.VerticalOffset;
                var type = new Typeface(CodeFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                for (int i = (int)(offset / LineHeight); i < _diff.Rows.Count; i++)
                {
                    double y = i * LineHeight - offset;
                    if (y >= ActualHeight - SystemParameters.HorizontalScrollBarHeight) break;
                    var row = _diff.Rows[i];
                    var number = _left ? row.LeftLineNumber : row.RightLineNumber;
                    if (!number.HasValue) continue;
                    var text = new FormattedText(number.Value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, type, FontSize, LineNumber, dpi);
                    drawing.DrawText(text, new Point(ActualWidth - text.Width - 8, y + (LineHeight - text.Height) / 2));
                }
            }
        }

        private static Grid Columns()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            return grid;
        }

        private static void AddColumn(Grid grid, FrameworkElement child, int column)
        {
            child.Margin = column == 0 ? new Thickness(0, 0, 4, 0) : new Thickness(4, 0, 0, 0);
            Grid.SetColumn(child, column);
            grid.Children.Add(child);
        }

        private static Button NavigationButton(string glyph, string key)
        {
            var label = HostText.Get(key);
            var button = new Button { Content = glyph, ToolTip = label, MinWidth = 28, Margin = new Thickness(4, 0, 0, 0) };
            AutomationProperties.SetName(button, label);
            return button;
        }

        private static Brush ColorBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
    }
}
