using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace SecsUtil.Controls
{
    public class YamlEditor : RichTextBox
    {
        private const int IndentSize = 4;
        private const string IndentString = "    ";
        private Canvas _guideLineCanvas;

        public YamlEditor()
        {
            FontFamily = new FontFamily("Consolas");
            FontSize = 11;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Padding = new Thickness(8, 8, 8, 8);
            AcceptsReturn = true;
            AcceptsTab = true;
            PreviewKeyDown += YamlEditor_PreviewKeyDown;
            TextChanged += YamlEditor_TextChanged;
            Loaded += YamlEditor_Loaded;
            LayoutUpdated += YamlEditor_LayoutUpdated;
        }

        private void YamlEditor_Loaded(object sender, RoutedEventArgs e)
        {
            CreateGuideLineCanvas();
            UpdateGuideLines();
        }

        private void CreateGuideLineCanvas()
        {
            _guideLineCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            
            ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(this);
            if (scrollViewer != null)
            {
                Decorator decorator = FindVisualChild<Decorator>(scrollViewer);
                if (decorator != null && decorator.Child is FrameworkElement fe)
                {
                    Grid grid = new Grid();
                    grid.Children.Add(fe);
                    grid.Children.Add(_guideLineCanvas);
                    Canvas.SetZIndex(_guideLineCanvas, -1);
                    decorator.Child = grid;
                }
            }
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T tChild)
                    return tChild;
                
                T result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void YamlEditor_LayoutUpdated(object sender, EventArgs e)
        {
            UpdateGuideLines();
        }

        private void YamlEditor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Tab)
            {
                e.Handled = true;
                InsertTab();
            }
            else if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                InsertNewLineWithIndent();
            }
            else if (e.Key == System.Windows.Input.Key.Back)
            {
                HandleBackspace();
            }
        }

        private void InsertTab()
        {
            if (Document.Blocks.Count == 0)
            {
                Document.Blocks.Add(new Paragraph(new Run(IndentString)));
                CaretPosition = Document.ContentEnd;
                return;
            }

            var selection = Selection;
            if (!selection.IsEmpty)
            {
                selection.Text = IndentString;
            }
            else
            {
                var caretPosition = CaretPosition;
                caretPosition.InsertTextInRun(IndentString);
                var newPosition = caretPosition.GetPositionAtOffset(IndentSize);
                if (newPosition != null)
                {
                    CaretPosition = newPosition;
                }
            }
        }

        private void InsertNewLineWithIndent()
        {
            var caretPosition = CaretPosition;
            var lineStart = caretPosition.GetLineStartPosition(0);
            if (lineStart == null)
            {
                CaretPosition.InsertTextInRun("\n");
                CaretPosition = Document.ContentEnd;
                return;
            }

            var lineText = new TextRange(lineStart, caretPosition).Text;
            var indentLevel = GetIndentLevel(lineText);

            var nextLineStart = caretPosition.GetLineStartPosition(1);
            string currentLineText;
            if (nextLineStart != null)
            {
                currentLineText = new TextRange(lineStart, nextLineStart).Text;
            }
            else
            {
                currentLineText = new TextRange(lineStart, Document.ContentEnd).Text;
            }

            bool hasColon = currentLineText.Contains(':');

            int newIndentLevel = indentLevel;
            if (hasColon && !currentLineText.TrimEnd().EndsWith(":"))
            {
                var afterColon = currentLineText.Substring(currentLineText.IndexOf(':') + 1).Trim();
                if (string.IsNullOrEmpty(afterColon) || afterColon.StartsWith("-"))
                {
                    newIndentLevel = indentLevel + 1;
                }
            }
            else if (hasColon && currentLineText.TrimEnd().EndsWith(":"))
            {
                newIndentLevel = indentLevel + 1;
            }

            string newLine = "\n" + new string(' ', newIndentLevel * IndentSize);
            CaretPosition.InsertTextInRun(newLine);
            CaretPosition = Document.ContentEnd;
        }

        private int GetIndentLevel(string lineText)
        {
            int spaces = 0;
            foreach (char c in lineText)
            {
                if (c == ' ')
                    spaces++;
                else
                    break;
            }
            return spaces / IndentSize;
        }

        private void HandleBackspace()
        {
            var caretPosition = CaretPosition;
            var lineStart = caretPosition.GetLineStartPosition(0);
            if (lineStart == null) return;

            var lineText = new TextRange(lineStart, caretPosition).Text;
            if (!lineText.EndsWith(IndentString)) return;

            var prevPosition = caretPosition.GetPositionAtOffset(-IndentSize);
            if (prevPosition != null && new TextRange(prevPosition, caretPosition).Text == IndentString)
            {
                prevPosition.DeleteTextInRun(IndentSize);
                CaretPosition = prevPosition;
            }
        }

        private void YamlEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateGuideLines();
        }

        private void UpdateGuideLines()
        {
            if (_guideLineCanvas == null) return;

            _guideLineCanvas.Children.Clear();

            var view = Document as FlowDocument;
            if (view == null) return;

            double lineHeight = view.FontSize * 1.2;
            double charWidth = view.FontSize * 0.6;
            double indentWidth = IndentSize * charWidth;

            double offset = Padding.Top;
            foreach (Block block in view.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    foreach (var inline in paragraph.Inlines)
                    {
                        if (inline is Run run)
                        {
                            int indentLevel = GetIndentLevel(run.Text);
                            for (int i = 1; i <= indentLevel; i++)
                            {
                                double x = Padding.Left + i * indentWidth;
                                Line line = new Line
                                {
                                    X1 = x,
                                    Y1 = offset,
                                    X2 = x,
                                    Y2 = offset + lineHeight,
                                    Stroke = new SolidColorBrush(Color.FromRgb(200, 200, 230)),
                                    StrokeThickness = 1,
                                    StrokeDashArray = new DoubleCollection(new[] { 3.0, 2.0 })
                                };
                                _guideLineCanvas.Children.Add(line);
                            }
                        }
                    }
                }
                offset += lineHeight;
            }

            _guideLineCanvas.Width = ActualWidth;
            _guideLineCanvas.Height = offset;
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            KeyDownEvent?.Invoke(this, e);
        }

        public event System.Windows.Input.KeyEventHandler? KeyDownEvent;

        protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            PreviewKeyDownEvent?.Invoke(this, e);
        }

        public event System.Windows.Input.KeyEventHandler? PreviewKeyDownEvent;

        public new string Text
        {
            get => new TextRange(Document.ContentStart, Document.ContentEnd).Text;
            set
            {
                Document.Blocks.Clear();
                if (!string.IsNullOrEmpty(value))
                {
                    Document.Blocks.Add(new Paragraph(new Run(value)));
                }
            }
        }
    }
}
