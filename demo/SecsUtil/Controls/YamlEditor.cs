using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Text.RegularExpressions;

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
                var insertPosition = CaretPosition;
                insertPosition.InsertTextInRun("\n");
                CaretPosition = insertPosition.GetPositionAtOffset(1, LogicalDirection.Forward) ?? Document.ContentEnd;
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
            var caretPositionBeforeInsert = CaretPosition;
            caretPositionBeforeInsert.InsertTextInRun(newLine);
            CaretPosition = caretPositionBeforeInsert.GetPositionAtOffset(newLine.Length, LogicalDirection.Forward) ?? Document.ContentEnd;
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

        public void SetHighlightedText(string text, int? errorLine = null)
        {
            Document.Blocks.Clear();
            Document.PagePadding = new Thickness(0);

            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0),
                    LineHeight = FontSize * 1.45,
                    Background = errorLine == i + 1 ? new SolidColorBrush(Color.FromRgb(255, 235, 235)) : Brushes.Transparent
                };

                foreach (var run in CreateHighlightedRuns(lines[i]))
                    paragraph.Inlines.Add(run);

                Document.Blocks.Add(paragraph);
            }
        }

        private static IEnumerable<Run> CreateHighlightedRuns(string line)
        {
            var commentIndex = FindCommentIndex(line);
            var code = commentIndex >= 0 ? line[..commentIndex] : line;
            var comment = commentIndex >= 0 ? line[commentIndex..] : string.Empty;

            var keyMatch = Regex.Match(code, @"^(\s*-\s*)?([A-Za-z_][\w.-]*)(\s*:)");
            if (keyMatch.Success)
            {
                if (!string.IsNullOrEmpty(keyMatch.Groups[1].Value))
                    yield return NewRun(keyMatch.Groups[1].Value, Color.FromRgb(80, 80, 80));

                yield return NewRun(keyMatch.Groups[2].Value, Color.FromRgb(0, 92, 170), FontWeights.SemiBold);
                yield return NewRun(keyMatch.Groups[3].Value, Color.FromRgb(80, 80, 80));

                foreach (var run in HighlightValues(code[keyMatch.Length..]))
                    yield return run;
            }
            else
            {
                foreach (var run in HighlightValues(code))
                    yield return run;
            }

            if (!string.IsNullOrEmpty(comment))
                yield return NewRun(comment, Color.FromRgb(0, 128, 0));
        }

        private static IEnumerable<Run> HighlightValues(string text)
        {
            var pattern = new Regex("(\"(?:\\\\.|[^\"])*\"|'(?:\\\\.|[^'])*'|\\btrue\\b|\\bfalse\\b|\\b\\d+(?:\\.\\d+)?\\b|\\[|\\]|,)", RegexOptions.IgnoreCase);
            var last = 0;

            foreach (Match match in pattern.Matches(text))
            {
                if (match.Index > last)
                    yield return NewRun(text[last..match.Index], Color.FromRgb(40, 40, 40));

                var token = match.Value;
                var color = token.StartsWith("\"") || token.StartsWith("'")
                    ? Color.FromRgb(163, 21, 21)
                    : token.Equals("true", StringComparison.OrdinalIgnoreCase) || token.Equals("false", StringComparison.OrdinalIgnoreCase)
                        ? Color.FromRgb(128, 0, 128)
                        : char.IsDigit(token[0])
                            ? Color.FromRgb(9, 134, 88)
                            : Color.FromRgb(90, 90, 90);

                yield return NewRun(token, color);
                last = match.Index + match.Length;
            }

            if (last < text.Length)
                yield return NewRun(text[last..], Color.FromRgb(40, 40, 40));
        }

        private static int FindCommentIndex(string line)
        {
            var inSingle = false;
            var inDouble = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"' && !inSingle)
                    inDouble = !inDouble;
                else if (c == '\'' && !inDouble)
                    inSingle = !inSingle;
                else if (c == '#' && !inSingle && !inDouble)
                    return i;
            }

            return -1;
        }

        private static Run NewRun(string text, Color color, FontWeight? weight = null)
        {
            var run = new Run(text)
            {
                Foreground = new SolidColorBrush(color)
            };

            if (weight.HasValue)
                run.FontWeight = weight.Value;

            return run;
        }
    }
}
