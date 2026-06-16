using Secs4Net;
using Secs4Net.Sml;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SecsUtil;

public partial class MainWindow : Window
{
    private static readonly ILogger _logger = Log.ForContext<MainWindow>();
    private readonly ConnectionManager _connectionManager = new();
    private List<LogEntry> _allLogEntries = new();
    private List<MessageTemplate> _templates = new();
    private MessageTemplate? _selectedTemplate;
    private System.Windows.Threading.DispatcherTimer? _timerUpdateTimer;
    private bool _templateSourceDirty;
    private bool _loadingTemplateSource;

    public MainWindow()
    {
        InitializeComponent();
        LoadTemplates();
        LoadTemplateSource();
        UpdateConnectionUiState(ConnectionState.NotConnected);
        _connectionManager.ConnectionChanged += OnConnectionChanged;
        _connectionManager.MessageReceived += OnMessageReceived;
        _connectionManager.RawDataReceived += OnRawDataReceived;
        _connectionManager.AutoReplyFactory = CreateTemplateReply;
        _logger.Information("SecsUtil started");
        
        txtTemplateSearch.TextChanged += (s, e) => FilterTemplates();
        txtFilter.TextChanged += (s, e) => ApplyLogFilter();
        txtTemplateFunction.TextChanged += (s, e) => UpdateAutoReplyEditorState();
        
        treeTemplates.SelectedItemChanged += treeTemplates_SelectedItemChanged;
        
        _timerUpdateTimer = new System.Windows.Threading.DispatcherTimer();
        _timerUpdateTimer.Interval = TimeSpan.FromMilliseconds(100);
        _timerUpdateTimer.Tick += UpdateTimerStatus;
        
    }

    private void UpdateTimerStatus(object? sender, EventArgs e)
    {
        if (_connectionManager.IsConnected)
        {
            _connectionManager.UpdateTimerStatus();
            
            lblT3.Content = $"{_connectionManager.T3Remaining}ms";
            lblT3Status.Content = _connectionManager.T3Active ? "运行中" : "空闲";
            lblT3Status.Foreground = _connectionManager.T3Active ? Brushes.Red : Brushes.Green;
            pbT3.Value = _connectionManager.T3Progress;
            
            lblT5.Content = $"{_connectionManager.T5Remaining}ms";
            lblT5Status.Content = _connectionManager.T5Active ? "运行中" : "空闲";
            lblT5Status.Foreground = _connectionManager.T5Active ? Brushes.Red : Brushes.Green;
            pbT5.Value = _connectionManager.T5Progress;
            
            lblT7.Content = $"{_connectionManager.T7Remaining}ms";
            lblT7Status.Content = _connectionManager.T7Active ? "运行中" : "空闲";
            lblT7Status.Foreground = _connectionManager.T7Active ? Brushes.Red : Brushes.Green;
            pbT7.Value = _connectionManager.T7Progress;
            
            lblT8.Content = $"{_connectionManager.T8Remaining}ms";
            lblT8Status.Content = _connectionManager.T8Active ? "运行中" : "空闲";
            lblT8Status.Foreground = _connectionManager.T8Active ? Brushes.Red : Brushes.Green;
            pbT8.Value = _connectionManager.T8Progress;
            
            lblSession.Content = _connectionManager.SessionId.HasValue ? $"0x{_connectionManager.SessionId.Value:X4}" : "-";
        }
        else
        {
            lblT3.Content = "-";
            lblT3Status.Content = "空闲";
            lblT3Status.Foreground = Brushes.Green;
            pbT3.Value = 0;
            
            lblT5.Content = "-";
            lblT5Status.Content = "空闲";
            lblT5Status.Foreground = Brushes.Green;
            pbT5.Value = 0;
            
            lblT7.Content = "-";
            lblT7Status.Content = "空闲";
            lblT7Status.Foreground = Brushes.Green;
            pbT7.Value = 0;
            
            lblT8.Content = "-";
            lblT8Status.Content = "空闲";
            lblT8Status.Foreground = Brushes.Green;
            pbT8.Value = 0;
            
            lblSession.Content = "-";
        }
    }

    private void LoadTemplates()
    {
        _templates = TemplateManager.LoadAllTemplates();
        BuildTemplateTree();
    }

    private void BuildTemplateTree()
    {
        BuildTemplateNodes(_templates);
    }

    private void FilterTemplates()
    {
        var search = txtTemplateSearch.Text.ToLower();
        if (string.IsNullOrWhiteSpace(search))
        {
            BuildTemplateTree();
            return;
        }

        var filtered = _templates.Where(t => 
            t.Name.ToLower().Contains(search) || 
            t.Description.ToLower().Contains(search) ||
            $"S{t.Stream}F{t.Function}".ToLower().Contains(search) ||
            GetPairHeader(t.Stream, GetPairFunction(t.Function)).ToLower().Contains(search)).ToList();

        BuildTemplateNodes(filtered, expandAll: true);
    }

    private void BuildTemplateNodes(IEnumerable<MessageTemplate> templates, bool expandAll = false)
    {
        treeTemplates.Items.Clear();

        var templateList = templates.ToList();
        var grouped = templateList.GroupBy(t => t.Stream).OrderBy(g => g.Key);

        foreach (var streamGroup in grouped)
        {
            var streamNode = new TreeViewItem
            {
                Header = $"Stream {streamGroup.Key}",
                IsExpanded = true
            };

            var pairGroups = streamGroup
                .GroupBy(t => GetPairFunction(t.Function))
                .OrderBy(g => g.Key);

            foreach (var pairGroup in pairGroups)
            {
                var pairNode = new TreeViewItem
                {
                    Header = GetPairHeader(streamGroup.Key, pairGroup.Key),
                    IsExpanded = expandAll || pairGroup.Count() <= 4
                };

                foreach (var template in pairGroup.OrderBy(t => t.Function).ThenBy(t => t.Name))
                {
                    var autoReply = template.AutoReply ? " [Auto]" : string.Empty;
                    var textBlock = new TextBlock
                    {
                        Text = $"{template.Name}{autoReply}",
                        ToolTip = template.Description
                    };
                    var templateNode = new TreeViewItem
                    {
                        Header = textBlock,
                        Tag = template
                    };
                    pairNode.Items.Add(templateNode);
                }

                streamNode.Items.Add(pairNode);
            }

            treeTemplates.Items.Add(streamNode);
        }
    }

    private static byte GetPairFunction(byte function)
    {
        if (function == 0)
            return 0;

        return function % 2 == 0 ? (byte)(function - 1) : function;
    }

    private static string GetPairHeader(byte stream, byte pairFunction)
    {
        if (pairFunction == 0)
            return $"S{stream}F0";

        return $"S{stream}F{pairFunction}/F{pairFunction + 1}";
    }

    private static bool InferReplyExpected(byte function)
    {
        return function > 0 && function % 2 == 1;
    }

    private void treeTemplates_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var selectedItem = treeTemplates.SelectedItem as TreeViewItem;
        if (selectedItem?.Tag is MessageTemplate template)
        {
            _selectedTemplate = template;
            txtTemplateName.Text = template.Name;
            txtTemplateDesc.Text = template.Description;
            txtTemplateStream.Text = template.Stream.ToString();
            txtTemplateFunction.Text = template.Function.ToString();
            chkTemplateAutoReply.IsChecked = template.AutoReply;
            txtTemplateSml.Document.Blocks.Clear();
            txtTemplateSml.Document.Blocks.Add(new Paragraph(new Run(template.SecsItem?.ToYamlString() ?? string.Empty)));
            UpdateAutoReplyEditorState();
        }
    }

    private void AddTemplate_Click(object sender, RoutedEventArgs e)
    {
        var template = new MessageTemplate
        {
            Name = "新模板",
            Description = "自定义模板",
            Stream = 1,
            Function = 1,
            ReplyExpected = true
        };
        
        _templates.Add(template);
        BuildTemplateTree();
        SelectTemplateInTree(template);
        UpdateTemplateFields(template);
    }

    private void SelectTemplateInTree(MessageTemplate template)
    {
        foreach (TreeViewItem streamNode in treeTemplates.Items)
        {
            if (streamNode.Header.ToString() == $"Stream {template.Stream}")
            {
                streamNode.IsExpanded = true;
                foreach (TreeViewItem pairNode in streamNode.Items)
                {
                    if (pairNode.Header.ToString() == GetPairHeader(template.Stream, GetPairFunction(template.Function)))
                    {
                        pairNode.IsExpanded = true;
                        foreach (TreeViewItem templateNode in pairNode.Items)
                        {
                            if (templateNode.Tag == template)
                            {
                                templateNode.IsSelected = true;
                                templateNode.BringIntoView();
                                return;
                            }
                        }
                    }
                }
            }
        }
    }

    private void UpdateTemplateFields(MessageTemplate template)
    {
        txtTemplateName.Text = template.Name;
        txtTemplateDesc.Text = template.Description;
        txtTemplateStream.Text = template.Stream.ToString();
        txtTemplateFunction.Text = template.Function.ToString();
        chkTemplateAutoReply.IsChecked = template.AutoReply;
        txtTemplateSml.Document.Blocks.Clear();
        txtTemplateSml.Document.Blocks.Add(new Paragraph(new Run(template.SecsItem?.ToYamlString() ?? string.Empty)));
        UpdateAutoReplyEditorState();
    }

    private void treeTemplates_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var treeView = sender as TreeView;
        if (treeView == null) return;
        
        var hitTest = VisualTreeHelper.HitTest(treeView, e.GetPosition(treeView));
        if (hitTest != null)
        {
            var container = FindVisualParent<TreeViewItem>(hitTest.VisualHit);
            if (container != null && container.Tag is MessageTemplate)
            {
                container.IsSelected = true;
                var contextMenu = treeView.ContextMenu;
                if (contextMenu != null)
                {
                    contextMenu.PlacementTarget = treeView;
                    contextMenu.IsOpen = true;
                }
            }
        }
    }

    private T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        if (parent == null) return null;
        if (parent is T) return parent as T;
        return FindVisualParent<T>(parent);
    }

    private async void SendTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTemplate == null) return;
        
        if (!_connectionManager.IsConnected)
        {
            MessageBox.Show("请先建立连接");
            return;
        }

        try
        {
            var message = _selectedTemplate.CreateMessage();
            statusText.Text = "发送消息中...";

            var response = await _connectionManager.SendMessageAsync(message);

            if (response != null)
            {
                txtMessageDetail.Text = $"响应消息:\r\n{response.SecsItem?.GetSml() ?? "无"}";
            }

            statusText.Text = "已发送";
        }
        catch (Exception ex)
        {
            AddLog($"发送失败: {ex.Message}", isError: true);
            _logger.Error(ex, "Send template message failed");
            statusText.Text = "发送失败";
        }
    }

    private async void SendTemplateDirectly_Click(object sender, RoutedEventArgs e)
    {
        SendTemplate_Click(sender, e);
    }

    private void EditTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTemplate != null)
        {
            UpdateTemplateFields(_selectedTemplate);
        }
    }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTemplate != null)
        {
            if (MessageBox.Show("确定要删除此模板吗？", "确认删除", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                TemplateManager.DeleteTemplate(_selectedTemplate);
                _templates.Remove(_selectedTemplate);
                BuildTemplateTree();
                ClearTemplateFields();
                LoadTemplateSource();
            }
        }
    }

    private MessageTemplate _copiedTemplate = null;

    private void CopyTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTemplate != null)
        {
            _copiedTemplate = new MessageTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = _selectedTemplate.Name,
                Description = _selectedTemplate.Description,
                Stream = _selectedTemplate.Stream,
                Function = _selectedTemplate.Function,
                ReplyExpected = _selectedTemplate.ReplyExpected,
                AutoReply = _selectedTemplate.AutoReply,
                SecsItem = CloneSecsItemData(_selectedTemplate.SecsItem)
            };
        }
    }

    private void PasteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_copiedTemplate != null)
        {
            var newTemplate = new MessageTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = _copiedTemplate.Name,
                Description = _copiedTemplate.Description,
                Stream = _copiedTemplate.Stream,
                Function = _copiedTemplate.Function,
                ReplyExpected = _copiedTemplate.ReplyExpected,
                AutoReply = _copiedTemplate.AutoReply,
                SecsItem = CloneSecsItemData(_copiedTemplate.SecsItem)
            };

            TemplateManager.AddTemplate(newTemplate);
            _templates.Add(newTemplate);
            AddTemplateToTree(newTemplate);
            LoadTemplateSource();
        }
    }

    private void AddTemplateToTree(MessageTemplate template)
    {
        BuildTemplateTree();
        SelectTemplateInTree(template);
        UpdateTemplateFields(template);
    }

    private SecsItemData? CloneSecsItemData(SecsItemData? data)
    {
        if (data == null) return null;
        
        return new SecsItemData
        {
            Type = data.Type,
            Values = new List<object>(data.Values),
            Children = data.Children?.Select(CloneSecsItemData).ToList(),
            Data = data.Data != null ? new Dictionary<string, object>(data.Data) : null
        };
    }

    private MessageTemplate? _draggedTemplate = null;
    private Point _dragStartPoint;
    private bool _isDragInProgress = false;
    private const double DRAG_THRESHOLD = 5;

    private void treeTemplates_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (treeViewItem?.Tag is MessageTemplate template)
        {
            _draggedTemplate = template;
            _dragStartPoint = e.GetPosition(treeTemplates);
            _isDragInProgress = true;
        }
    }

    private void treeTemplates_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragInProgress || _draggedTemplate == null) return;

        Point currentPoint = e.GetPosition(treeTemplates);
        double deltaX = Math.Abs(currentPoint.X - _dragStartPoint.X);
        double deltaY = Math.Abs(currentPoint.Y - _dragStartPoint.Y);

        if (deltaX >= DRAG_THRESHOLD || deltaY >= DRAG_THRESHOLD)
        {
            var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (treeViewItem != null)
            {
                DragDrop.DoDragDrop(treeViewItem, _draggedTemplate, DragDropEffects.Move);
            }
            _isDragInProgress = false;
        }
    }

    private void treeTemplates_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragInProgress = false;
    }

    private void treeTemplates_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(MessageTemplate)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void treeTemplates_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(MessageTemplate)))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void treeTemplates_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(MessageTemplate))) return;
        
        var draggedTemplate = e.Data.GetData(typeof(MessageTemplate)) as MessageTemplate;
        if (draggedTemplate == null) return;

        var targetItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (targetItem?.Tag is MessageTemplate targetTemplate)
        {
            if (draggedTemplate.Stream == targetTemplate.Stream && 
                GetPairFunction(draggedTemplate.Function) == GetPairFunction(targetTemplate.Function) &&
                draggedTemplate.Id != targetTemplate.Id)
            {
                int oldIndex = _templates.FindIndex(t => t.Id == draggedTemplate.Id);
                int newIndex = _templates.FindIndex(t => t.Id == targetTemplate.Id);

                if (oldIndex >= 0 && newIndex >= 0)
                {
                    _templates.RemoveAt(oldIndex);
                    _templates.Insert(newIndex, draggedTemplate);
                    TemplateManager.SaveAllTemplates(_templates);
                    BuildTemplateTree();
                    SelectTemplateInTree(draggedTemplate);
                    LoadTemplateSource();
                }
            }
        }
        _draggedTemplate = null;
    }

    private T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T t)
                return t;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private void treeTemplates_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            var selectedItem = treeTemplates.SelectedItem as TreeViewItem;
            if (selectedItem?.Tag is MessageTemplate template && selectedItem.Header is TextBlock textBlock)
            {
                StartRename(selectedItem, textBlock, template);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Delete)
        {
            var selectedItem = treeTemplates.SelectedItem as TreeViewItem;
            if (selectedItem?.Tag is MessageTemplate template)
            {
                DeleteTemplate_Internal(template);
                e.Handled = true;
            }
        }
    }

    private void DeleteTemplate_Internal(MessageTemplate template)
    {
        TemplateManager.DeleteTemplate(template);
        _templates.Remove(template);
        RemoveTemplateFromTree(template);
        ClearTemplateFields();
        LoadTemplateSource();
    }

    private void RemoveTemplateFromTree(MessageTemplate template)
    {
        BuildTemplateTree();
    }

    private void StartRename(TreeViewItem item, TextBlock textBlock, MessageTemplate template)
    {
        var textBox = new TextBox
        {
            Text = textBlock.Text,
            Width = textBlock.ActualWidth + 20,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.LightBlue,
            Background = Brushes.White
        };

        textBox.SelectAll();
        textBox.Focus();

        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                string newName = textBox.Text.Trim();
                if (!string.IsNullOrEmpty(newName) && newName != template.Name)
                {
                    template.Name = newName;
                    textBlock.Text = newName;
                    TemplateManager.UpdateTemplate(template);
                    _templates = TemplateManager.LoadAllTemplates();
                    UpdateTemplateFields(template);
                    BuildTemplateTree();
                    SelectTemplateInTree(template);
                    LoadTemplateSource();
                }
                item.Header = textBlock;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                item.Header = textBlock;
                e.Handled = true;
            }
        };

        textBox.LostFocus += (s, e) =>
        {
            item.Header = textBlock;
        };

        item.Header = textBox;
    }

    private void treeTemplates_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.C)
            {
                CopyTemplate_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.V)
            {
                PasteTemplate_Click(sender, e);
                e.Handled = true;
            }
        }
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTemplate != null)
        {
            _selectedTemplate.Name = txtTemplateName.Text;
            _selectedTemplate.Description = txtTemplateDesc.Text;
            _selectedTemplate.Stream = byte.TryParse(txtTemplateStream.Text, out var s) ? s : (byte)1;
            _selectedTemplate.Function = byte.TryParse(txtTemplateFunction.Text, out var f) ? f : (byte)1;
            _selectedTemplate.ReplyExpected = InferReplyExpected(_selectedTemplate.Function);
            _selectedTemplate.AutoReply = chkTemplateAutoReply.IsChecked == true && _selectedTemplate.Function % 2 == 0;
            
            var yamlText = new TextRange(txtTemplateSml.Document.ContentStart, txtTemplateSml.Document.ContentEnd).Text.Trim();
            if (!string.IsNullOrEmpty(yamlText))
            {
                try
                {
                    _selectedTemplate.SecsItem = DeserializeSecsItem(yamlText);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"YAML格式不正确，无法解析SecsItem数据：{ex.Message}");
                    return;
                }
            }
            else
            {
                _selectedTemplate.SecsItem = null;
            }

            TemplateManager.UpdateTemplate(_selectedTemplate);
            BuildTemplateTree();
            SelectTemplateInTree(_selectedTemplate);
            LoadTemplateSource();
            MessageBox.Show("模板已保存");
        }
    }

    private void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTemplate != null)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "YAML 文件 (*.yaml)|*.yaml|YML 文件 (*.yml)|*.yml",
                FileName = $"{_selectedTemplate.Name.Replace(" ", "_")}.yaml"
            };
            
            if (dlg.ShowDialog() == true)
            {
                TemplateManager.ExportTemplates(new List<MessageTemplate> { _selectedTemplate }, dlg.FileName);
                MessageBox.Show("模板已导出");
            }
        }
    }

    private void ExportTemplates_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "YAML 文件 (*.yaml)|*.yaml|YML 文件 (*.yml)|*.yml",
            FileName = "secsutil_templates.yaml"
        };
        
        if (dlg.ShowDialog() == true)
        {
            TemplateManager.ExportTemplates(_templates, dlg.FileName);
            MessageBox.Show($"已导出 {_templates.Count} 个模板");
        }
    }

    private void ImportTemplates_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "YAML 文件 (*.yaml)|*.yaml|YML 文件 (*.yml)|*.yml"
        };
        
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var imported = TemplateManager.ImportTemplates(dlg.FileName, overwrite: false);
                if (imported.Count == 0)
                {
                    MessageBox.Show("未能从文件中解析出任何模板，请检查 YAML 格式是否正确。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _templates = TemplateManager.LoadAllTemplates();
                txtTemplateFileName.Text = System.IO.Path.GetFileName(dlg.FileName);
                BuildTemplateTree();
                ClearTemplateFields();
                LoadTemplateSource();
                MessageBox.Show($"已覆盖导入 {imported.Count} 个模板");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入模板失败：{ex.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }



    private void LoadTemplateSource()
    {
        try
        {
            if (!File.Exists(TemplateManager.DefaultTemplateFile))
                TemplateManager.SaveAllTemplates(_templates);

            var yaml = File.ReadAllText(TemplateManager.DefaultTemplateFile, Encoding.UTF8);
            _loadingTemplateSource = true;
            txtTemplateSource.SetHighlightedText(yaml);
            _templateSourceDirty = false;
            statusText.Text = $"源码已加载: {Path.GetFileName(TemplateManager.DefaultTemplateFile)}";
        }
        catch (Exception ex)
        {
            statusText.Text = $"源码加载失败: {ex.Message}";
        }
        finally
        {
            _loadingTemplateSource = false;
        }
    }

    private void txtTemplateSource_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingTemplateSource)
            return;

        _templateSourceDirty = true;
        statusText.Text = "源码已修改";
    }

    private void ReloadTemplateSource_Click(object sender, RoutedEventArgs e)
    {
        if (_templateSourceDirty &&
            MessageBox.Show("源码尚未保存，确定要重新加载文件吗？", "重新加载源码", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        LoadTemplateSource();
    }

    private void SaveTemplateSource_Click(object sender, RoutedEventArgs e)
    {
        var yaml = GetTemplateSourceText();
        var validation = ValidateTemplateSourceYaml(yaml);
        if (!validation.IsValid)
        {
            HighlightTemplateSource(yaml, validation.Line);
            MessageBox.Show(validation.Message, "源码校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(TemplateManager.DefaultTemplateFile) ?? string.Empty);
        File.WriteAllText(TemplateManager.DefaultTemplateFile, yaml, Encoding.UTF8);
        _templateSourceDirty = false;
        HighlightTemplateSource(yaml);
        statusText.Text = "源码已保存";
    }

    private void ValidateTemplateSource_Click(object sender, RoutedEventArgs e)
    {
        var yaml = GetTemplateSourceText();
        var validation = ValidateTemplateSourceYaml(yaml);
        HighlightTemplateSource(yaml, validation.Line);

        if (validation.IsValid)
        {
            statusText.Text = "源码校验通过";
            MessageBox.Show(validation.Message, "源码校验", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            statusText.Text = "源码校验失败";
            MessageBox.Show(validation.Message, "源码校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyTemplateSource_Click(object sender, RoutedEventArgs e)
    {
        SaveTemplateSource_Click(sender, e);
        if (_templateSourceDirty)
            return;

        _templates = TemplateManager.LoadAllTemplates();
        BuildTemplateTree();
        ClearTemplateFields();
        statusText.Text = $"指令库已刷新，共 {_templates.Count} 个模板";
    }

    private void LocateTemplateSource_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTemplate == null)
        {
            MessageBox.Show("请先在指令库中选择一个模板。", "定位模板", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var found = SelectInTemplateSource($"Name: {_selectedTemplate.Name}");
        if (!found)
            found = SelectInTemplateSource(_selectedTemplate.Name);

        statusText.Text = found ? $"已定位模板: {_selectedTemplate.Name}" : "源码中未找到当前模板";
    }

    private void txtSourceSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SelectInTemplateSource(txtSourceSearch.Text);
            e.Handled = true;
        }
    }

    private string GetTemplateSourceText()
    {
        return new TextRange(txtTemplateSource.Document.ContentStart, txtTemplateSource.Document.ContentEnd).Text.TrimEnd('\r', '\n');
    }

    private void HighlightTemplateSource(string yaml, int? errorLine = null)
    {
        _loadingTemplateSource = true;
        txtTemplateSource.SetHighlightedText(yaml, errorLine);
        _loadingTemplateSource = false;
    }

    private (bool IsValid, string Message, int? Line) ValidateTemplateSourceYaml(string yaml)
    {
        try
        {
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.NullNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .WithTypeConverter(new SecsItemDataYamlConverter())
                .Build();

            var templates = deserializer.Deserialize<List<MessageTemplate>>(yaml) ?? new List<MessageTemplate>();
            if (templates.Count == 0)
                return (false, "源码中没有解析到任何模板。", null);

            foreach (var template in templates)
            {
                if (string.IsNullOrWhiteSpace(template.Name))
                    return (false, "存在未填写 Name 的模板。", null);

                if (template.Stream == 0)
                    return (false, $"模板 {template.Name} 的 Stream 无效。", null);

                if (template.SecsItem != null)
                    _ = template.SecsItem.ToSecsItem();
            }

            return (true, $"源码校验通过，共 {templates.Count} 个模板。", null);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            int? line = ex.Start.Line > 0 ? ex.Start.Line : null;
            return (false, $"YAML 语法错误: {ex.Message}", line);
        }
        catch (Exception ex)
        {
            return (false, $"模板内容错误: {ex.Message}", null);
        }
    }

    private bool SelectInTemplateSource(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return false;

        var text = GetTemplateSourceText();
        var index = text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return false;

        var start = GetTextPointerAtOffset(txtTemplateSource.Document.ContentStart, index);
        var end = GetTextPointerAtOffset(txtTemplateSource.Document.ContentStart, index + search.Length);
        if (start == null || end == null)
            return false;

        txtTemplateSource.Focus();
        txtTemplateSource.Selection.Select(start, end);
        start.Paragraph?.BringIntoView();
        return true;
    }

    private static TextPointer? GetTextPointerAtOffset(TextPointer start, int targetOffset)
    {
        var navigator = start;
        var offset = 0;

        while (navigator != null)
        {
            if (navigator.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var text = navigator.GetTextInRun(LogicalDirection.Forward);
                if (offset + text.Length >= targetOffset)
                    return navigator.GetPositionAtOffset(targetOffset - offset);

                offset += text.Length;
            }

            navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
        }

        return null;
    }

    private void ClearTemplateFields()
    {
        txtTemplateName.Clear();
        txtTemplateDesc.Clear();
        txtTemplateStream.Text = "1";
        txtTemplateFunction.Text = "1";
        chkTemplateAutoReply.IsChecked = false;
        UpdateAutoReplyEditorState();
        txtTemplateSml.Document.Blocks.Clear();
        _selectedTemplate = null;
    }

    private void UpdateAutoReplyEditorState()
    {
        if (chkTemplateAutoReply == null || txtTemplateFunction == null)
            return;

        var isEvenFunction = byte.TryParse(txtTemplateFunction.Text, out var function) && function > 0 && function % 2 == 0;
        chkTemplateAutoReply.IsEnabled = isEvenFunction;

        if (!isEvenFunction)
            chkTemplateAutoReply.IsChecked = false;
    }

    private void AddLog(string message, bool isReceived = false, bool isError = false, byte? stream = null, byte? function = null, byte[]? rawData = null, SecsMessage? secsMessage = null, int messageId = 0)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            IsReceived = isReceived,
            IsError = isError,
            Stream = stream,
            Function = function,
            Message = message,
            RawData = rawData,
            SecsMessage = secsMessage
        };
        
        _allLogEntries.Add(entry);
        
        txtLog.Dispatcher.Invoke(() =>
        {
            var direction = isReceived ? "<--" : "-->";
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var msgId = messageId > 0 ? $"[0x{messageId:X8}]" : "";
            
            var paragraph = new Paragraph();
            
            var timestampRun = new Run($"[{timestamp}] ");
            timestampRun.Foreground = new SolidColorBrush(Colors.Gray);
            paragraph.Inlines.Add(timestampRun);
            
            var directionRun = new Run($"{direction} ");
            if (isError)
            {
                directionRun.Foreground = new SolidColorBrush(Colors.Red);
            }
            else if (isReceived)
            {
                directionRun.Foreground = new SolidColorBrush(Colors.Blue);
            }
            else
            {
                directionRun.Foreground = new SolidColorBrush(Colors.DarkGreen);
            }
            paragraph.Inlines.Add(directionRun);
            
            var messageRun = new Run($"{msgId} {message}\n");
            messageRun.Foreground = new SolidColorBrush(Colors.Black);
            paragraph.Inlines.Add(messageRun);
            
            txtLog.Document.Blocks.Add(paragraph);
            txtLog.ScrollToEnd();
        });
        
        logCount.Text = $"{_allLogEntries.Count} 条日志";
    }

    private void ApplyLogFilter()
    {
        var filter = txtFilter.Text.Trim().ToLower();
        
        txtLog.Dispatcher.Invoke(() =>
        {
            txtLog.Document.Blocks.Clear();
            
            IEnumerable<LogEntry> entries = string.IsNullOrWhiteSpace(filter) 
                ? _allLogEntries 
                : _allLogEntries.Where(e => 
                    e.Message.ToLower().Contains(filter) ||
                    (e.Stream.HasValue && $"S{e.Stream}F{e.Function}".ToLower().Contains(filter))
                );
            
            foreach (var entry in entries)
            {
                var paragraph = new Paragraph();
                
                var timestampRun = new Run($"[{entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")}] ");
                timestampRun.Foreground = new SolidColorBrush(Colors.Gray);
                paragraph.Inlines.Add(timestampRun);
                
                var direction = entry.IsReceived ? "<--" : "-->";
                var directionRun = new Run($"{direction} ");
                if (entry.IsError)
                {
                    directionRun.Foreground = new SolidColorBrush(Colors.Red);
                }
                else if (entry.IsReceived)
                {
                    directionRun.Foreground = new SolidColorBrush(Colors.Blue);
                }
                else
                {
                    directionRun.Foreground = new SolidColorBrush(Colors.DarkGreen);
                }
                paragraph.Inlines.Add(directionRun);
                
                var messageRun = new Run($"{entry.Message}\n");
                messageRun.Foreground = new SolidColorBrush(Colors.Black);
                paragraph.Inlines.Add(messageRun);
                
                txtLog.Document.Blocks.Add(paragraph);
            }
            
            txtLog.ScrollToVerticalOffset(double.MaxValue);
        });
    }

    private void OnConnectionChanged(object? sender, ConnectionState state)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateConnectionUiState(state);
            
            if (state == ConnectionState.Selected)
            {
                lblMode.Content = _connectionManager.Mode == ConnectionMode.Active ? "Active" : "Passive";
                lblPeer.Content = _connectionManager.PeerAddress;
                _timerUpdateTimer?.Start();
                
                AddLog($"[{DateTime.Now:HH:mm:ss}] 连接成功 - {_connectionManager.Mode}模式 - 目标: {_connectionManager.PeerAddress}", 
                    false, false, null, null, null, null);
            }
            else if (state == ConnectionState.NotConnected)
            {
                lblMode.Content = "-";
                lblPeer.Content = "-";
                _timerUpdateTimer?.Stop();
                
                AddLog($"[{DateTime.Now:HH:mm:ss}] 连接已断开", false, false, null, null, null, null);
            }
            else
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 连接状态变化: {state}", false, false, null, null, null, null);
            }
        });
    }

    private void OnMessageReceived(object? sender, SecsMessageEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var logMessage = BuildSecsLogMessage(e.Message);
            AddLog(logMessage, e.IsReceived, false, e.Message.S, e.Message.F, null, e.Message, e.MessageId);
            
            txtMessageDetail.Text = $"消息ID: {e.MessageId:X8}\r\n" +
                                   $"方向: {(e.IsReceived ? "接收" : "发送")}\r\n" +
                                   $"SF: S{e.Message.S}F{e.Message.F}\r\n" +
                                   $"等待响应: {e.Message.ReplyExpected}\r\n" +
                                   $"名称: {e.Message.Name ?? "未命名"}\r\n" +
                                   $"数据:\r\n{e.Message.SecsItem?.GetSml() ?? "无"}";
        });
    }

    private void UpdateConnectionUiState(ConnectionState state)
    {
        lblStatus.Content = state.ToString();
        btnConnect.IsEnabled = state == ConnectionState.NotConnected;
        btnDisconnect.IsEnabled = state != ConnectionState.NotConnected;

        var (brush, text) = state switch
        {
            ConnectionState.Selected => (Brushes.DodgerBlue, "已连接"),
            ConnectionState.NotConnected => (new SolidColorBrush(Color.FromRgb(209, 52, 56)), "未连接"),
            _ => (new SolidColorBrush(Color.FromRgb(255, 185, 0)), "连接中")
        };

        statusLed.Fill = brush;
        bottomConnectionLed.Fill = brush;
        bottomConnectionText.Text = text;
    }

    private static string BuildSecsLogMessage(SecsMessage message)
    {
        var header = string.IsNullOrWhiteSpace(message.Name)
            ? $"S{message.S}F{message.F}"
            : $"S{message.S}F{message.F} {message.Name}";

        var sml = message.SecsItem?.GetSml();
        return string.IsNullOrWhiteSpace(sml)
            ? header
            : $"{header}\r\n{sml}";
    }

    private void OnRawDataReceived(object? sender, RawDataEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var hexBuilder = new StringBuilder();
            var asciiBuilder = new StringBuilder();
            
            for (int i = 0; i < e.Data.Length; i++)
            {
                hexBuilder.Append($"{e.Data[i]:X2} ");
                asciiBuilder.Append(e.Data[i] >= 32 && e.Data[i] <= 126 ? (char)e.Data[i] : '.');
                if ((i + 1) % 16 == 0 && i != e.Data.Length - 1)
                {
                    hexBuilder.Append("\n");
                    asciiBuilder.Append("\n");
                }
            }
            
            txtHexData.Text = $"偏移量\t十六进制\t\tASCII\n";
            var lines = hexBuilder.ToString().Split('\n');
            var asciiLines = asciiBuilder.ToString().Split('\n');
            
            int offset = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                txtHexData.Text += $"{offset:X8}\t{lines[i].PadRight(48)}\t{asciiLines[i]}\n";
                offset += 16;
            }
        });
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var mode = rbActive.IsChecked == true ? ConnectionMode.Active : ConnectionMode.Passive;
            var ip = txtIpAddress.Text;
            var port = int.Parse(txtPort.Text);
            var deviceId = ushort.Parse(txtDeviceId.Text);
            var t3 = int.Parse(txtT3.Text);
            var t5 = int.Parse(txtT5.Text);
            var t6 = int.Parse(txtT6.Text);
            var t7 = int.Parse(txtT7.Text);

            UpdateConnectionUiState(ConnectionState.Connecting);

            if (mode == ConnectionMode.Active)
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 正在连接到 {ip}:{port} (DeviceId: {deviceId})...", isError: false);
            }
            else
            {
                AddLog($"[{DateTime.Now:HH:mm:ss}] 正在监听 {ip}:{port} (DeviceId: {deviceId})...", isError: false);
            }

            await _connectionManager.ConnectAsync(mode, ip, port, deviceId, t3, t5, t6, t7);
        }
        catch (Exception ex)
        {
            UpdateConnectionUiState(ConnectionState.NotConnected);
            AddLog($"[{DateTime.Now:HH:mm:ss}] 连接失败: {ex.Message}", isError: true);
            MessageBox.Show($"连接失败: {ex.Message}");
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        btnDisconnect.IsEnabled = false;

        try
        {
            await _connectionManager.DisconnectAsync();
        }
        catch (Exception ex)
        {
            AddLog($"[{DateTime.Now:HH:mm:ss}] 断开失败: {ex.Message}", isError: true);
            MessageBox.Show($"断开失败: {ex.Message}");
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        txtLog.Document.Blocks.Clear();
        _allLogEntries.Clear();
        logCount.Text = "0 条日志";
    }

    private void SaveLog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = $"secsutil_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllLines(dlg.FileName, _allLogEntries.Select(e => e.DisplayText));
            MessageBox.Show("日志已保存");
        }
    }

    private void FilterLog_Click(object sender, RoutedEventArgs e)
    {
        ApplyLogFilter();
    }
    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("SecsUtil - SECS-II Communication Tool\n\n基于Secs4Net库开发\n版本: 1.0.0");
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }



    private readonly string[] _yamlKeywords = { "List: ", "ASCII: ", "Binary: ", "U1: ", "U2: ", "U4: ", "U8: ", "I1: ", "I2: ", "I4: ", "I8: ", "F4: ", "F8: ", "Boolean: ", "Values: ", "Children: " };

    private void txtTemplateSml_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!lstAutoComplete.IsVisible)
            return;

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            lstAutoComplete.Visibility = Visibility.Collapsed;
        }
        else if (e.Key == Key.Tab && lstAutoComplete.Items.Count > 0)
        {
            e.Handled = true;
            InsertAutoCompleteSelection();
        }
        else if (e.Key == Key.Down)
        {
            e.Handled = true;
            if (lstAutoComplete.SelectedIndex < lstAutoComplete.Items.Count - 1)
            {
                lstAutoComplete.SelectedIndex++;
            }
        }
        else if (e.Key == Key.Up)
        {
            e.Handled = true;
            if (lstAutoComplete.SelectedIndex > 0)
            {
                lstAutoComplete.SelectedIndex--;
            }
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            InsertAutoCompleteSelection();
        }
        else if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            e.Handled = false;
            lstAutoComplete.Visibility = Visibility.Collapsed;
        }
    }

    private void txtTemplateSml_TextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            var yamlText = new TextRange(txtTemplateSml.Document.ContentStart, txtTemplateSml.Document.ContentEnd).Text.Trim();
            if (string.IsNullOrWhiteSpace(yamlText))
            {
                txtSmlPreview.Text = string.Empty;
                return;
            }
            
            ShowAutoComplete();
        }
        catch { }
        
        try
        {
            var yamlText = new TextRange(txtTemplateSml.Document.ContentStart, txtTemplateSml.Document.ContentEnd).Text.Trim();
            if (string.IsNullOrWhiteSpace(yamlText))
            {
                txtSmlPreview.Text = string.Empty;
                return;
            }

            var secsItemData = DeserializeSecsItem(yamlText);
            if (secsItemData != null && !string.IsNullOrWhiteSpace(secsItemData.Type))
            {
                try
                {
                    var secsItem = secsItemData.ToSecsItem();
                    txtSmlPreview.Text = secsItem.GetSml();
                }
                catch
                {
                    txtSmlPreview.Text = "数据格式无效";
                }
            }
            else
            {
                txtSmlPreview.Text = "无法解析YAML格式";
            }
        }
        catch (Exception ex)
        {
            txtSmlPreview.Text = $"解析错误: {ex.Message}";
        }
    }

    private void ShowAutoComplete()
    {
        var caretPosition = txtTemplateSml.CaretPosition;
        var lineStart = caretPosition.GetLineStartPosition(0);
        if (lineStart == null) return;

        var lineText = new TextRange(lineStart, caretPosition).Text;
        var lastColonIndex = lineText.LastIndexOf(':');
        var lastSpaceIndex = lineText.LastIndexOf(' ');
        var startIndex = Math.Max(lastColonIndex, lastSpaceIndex) + 1;
        var prefix = lineText.Substring(startIndex);

        if (string.IsNullOrWhiteSpace(prefix))
        {
            lstAutoComplete.Visibility = Visibility.Collapsed;
            return;
        }

        var matches = _yamlKeywords.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            lstAutoComplete.Visibility = Visibility.Collapsed;
            return;
        }

        lstAutoComplete.Items.Clear();
        matches.ForEach(m => lstAutoComplete.Items.Add(m));
        lstAutoComplete.SelectedIndex = 0;

        var caretRect = txtTemplateSml.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
        lstAutoComplete.Margin = new Thickness(caretRect.Left + txtTemplateSml.Margin.Left, caretRect.Bottom + txtTemplateSml.Margin.Top, 0, 0);
        lstAutoComplete.Width = 150;
        lstAutoComplete.Visibility = Visibility.Visible;
    }

    private void InsertAutoCompleteSelection()
    {
        if (lstAutoComplete.SelectedItem == null) return;

        var selectedText = lstAutoComplete.SelectedItem.ToString();
        var caretPosition = txtTemplateSml.CaretPosition;
        var lineStart = caretPosition.GetLineStartPosition(0);
        if (lineStart == null) return;

        var lineText = new TextRange(lineStart, caretPosition).Text;
        var lastColonIndex = lineText.LastIndexOf(':');
        var lastSpaceIndex = lineText.LastIndexOf(' ');
        var startIndex = Math.Max(lastColonIndex, lastSpaceIndex) + 1;

        var backspaceCount = lineText.Length - startIndex;
        for (int i = 0; i < backspaceCount; i++)
        {
            caretPosition = caretPosition.GetPositionAtOffset(-1);
            caretPosition.DeleteTextInRun(1);
        }

        caretPosition.InsertTextInRun(selectedText);
        txtTemplateSml.CaretPosition = caretPosition.GetPositionAtOffset(selectedText.Length);
        lstAutoComplete.Visibility = Visibility.Collapsed;
        txtTemplateSml.Focus();
    }

    private void lstAutoComplete_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void lstAutoComplete_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        InsertAutoCompleteSelection();
    }

    private SecsMessage? CreateTemplateReply(SecsMessage requestMessage)
    {
        var replyFunction = requestMessage.F + 1;
        var template = _templates
            .Where(t => t.Stream == requestMessage.S && t.Function == replyFunction && t.AutoReply)
            .FirstOrDefault();

        if (template == null)
            return null;

        var reply = template.CreateMessage();
        reply.Name = $"Auto Reply - {template.Name}";
        return reply;
    }

    private void InsertItem_Click(object sender, RoutedEventArgs e)
    {
        var type = GetSelectedItemType();
        var values = txtItemValues.Text.Trim();
        InsertEditorText(BuildYamlSnippet(type, values, GetCurrentIndent()));
        txtTemplateSml.Focus();
    }

    private void InsertList_Click(object sender, RoutedEventArgs e)
    {
        InsertEditorText($"{GetCurrentIndent()}List:\n{GetCurrentIndent()}  ");
        txtTemplateSml.Focus();
    }

    private void InsertAck_Click(object sender, RoutedEventArgs e)
    {
        InsertEditorText($"{GetCurrentIndent()}Binary: [0]");
        txtTemplateSml.Focus();
    }

    private void InsertRemoteCommand_Click(object sender, RoutedEventArgs e)
    {
        var indent = GetCurrentIndent();
        var snippet =
            $"{indent}List:\n" +
            $"{indent}  - ASCII: [\"COMMAND\"]\n" +
            $"{indent}  - List:\n" +
            $"{indent}      - List:\n" +
            $"{indent}          - ASCII: [\"PARAM\"]\n" +
            $"{indent}          - ASCII: [\"VALUE\"]";
        InsertEditorText(snippet);
        txtTemplateSml.Focus();
    }

    private void InsertEventReport_Click(object sender, RoutedEventArgs e)
    {
        var indent = GetCurrentIndent();
        var snippet =
            $"{indent}List:\n" +
            $"{indent}  - U4: [0]\n" +
            $"{indent}  - U4: [0]\n" +
            $"{indent}  - List:\n" +
            $"{indent}      - List:\n" +
            $"{indent}          - U4: [0]\n" +
            $"{indent}          - List: []";
        InsertEditorText(snippet);
        txtTemplateSml.Focus();
    }

    private void WrapSelectionList_Click(object sender, RoutedEventArgs e)
    {
        var selected = txtTemplateSml.Selection.Text.TrimEnd();
        var indent = GetCurrentIndent();

        if (string.IsNullOrWhiteSpace(selected))
        {
            InsertEditorText($"{indent}List:\n{indent}  ");
            return;
        }

        var childIndent = indent + "  ";
        var wrapped = $"{indent}List:\n" + string.Join("\n", selected.Split('\n').Select(line => childIndent + line.TrimEnd('\r')));
        txtTemplateSml.Selection.Text = wrapped;
        txtTemplateSml.Focus();
    }

    private void ValidateTemplate_Click(object sender, RoutedEventArgs e)
    {
        var yamlText = GetEditorText().Trim();
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            MessageBox.Show("请先输入 SecsItem YAML。", "校验", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var data = DeserializeSecsItem(yamlText);
            var item = data.ToSecsItem();
            txtSmlPreview.Text = item.GetSml();
            statusText.Text = "模板校验通过";
            MessageBox.Show("模板校验通过。", "校验", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            txtSmlPreview.Text = $"解析错误: {ex.Message}";
            statusText.Text = "模板校验失败";
            MessageBox.Show($"模板校验失败: {ex.Message}", "校验", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadSml_Click(object sender, RoutedEventArgs e)
    {
        var text = GetEditorText().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show("请先在编辑器中粘贴完整 SML 消息。", "从 SML 读取", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var message = text.ToSecsMessage();
            if (message == null)
                throw new InvalidOperationException("无法解析 SML 消息。");

            txtTemplateStream.Text = message.S.ToString();
            txtTemplateFunction.Text = message.F.ToString();
            chkTemplateAutoReply.IsChecked = false;
            UpdateAutoReplyEditorState();

            var data = SecsItemDataExtensions.FromSecsItem(message.SecsItem);
            SetEditorText(data?.ToYamlString() ?? string.Empty);
            txtSmlPreview.Text = message.SecsItem?.GetSml() ?? "无";
            statusText.Text = "已从 SML 读取";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"SML 解析失败: {ex.Message}", "从 SML 读取", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string GetSelectedItemType()
    {
        if (cmbItemType.SelectedItem is ComboBoxItem item && item.Content is string type)
            return type;

        return "List";
    }

    private string BuildYamlSnippet(string type, string values, string indent)
    {
        if (type == "List")
            return $"{indent}List:\n{indent}  ";

        var formattedValues = string.IsNullOrWhiteSpace(values)
            ? string.Empty
            : string.Join(", ", values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(v => FormatYamlValue(type, v)));

        return $"{indent}{type}: [{formattedValues}]";
    }

    private static string FormatYamlValue(string type, string value)
    {
        if (type == "ASCII")
            return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

        if (type == "Boolean")
            return value.Equals("1", StringComparison.OrdinalIgnoreCase) ? "true" :
                value.Equals("0", StringComparison.OrdinalIgnoreCase) ? "false" :
                value.ToLowerInvariant();

        return value;
    }

    private string GetCurrentIndent()
    {
        var caret = txtTemplateSml.CaretPosition;
        var lineStart = caret.GetLineStartPosition(0);
        if (lineStart == null)
            return string.Empty;

        var lineText = new TextRange(lineStart, caret).Text;
        return new string(lineText.TakeWhile(c => c == ' ').ToArray());
    }

    private string GetEditorText()
    {
        return new TextRange(txtTemplateSml.Document.ContentStart, txtTemplateSml.Document.ContentEnd).Text;
    }

    private void SetEditorText(string text)
    {
        txtTemplateSml.Document.Blocks.Clear();
        txtTemplateSml.Document.Blocks.Add(new Paragraph(new Run(text)));
    }

    private void InsertEditorText(string text)
    {
        if (!txtTemplateSml.Selection.IsEmpty)
        {
            txtTemplateSml.Selection.Text = text;
            return;
        }

        txtTemplateSml.CaretPosition.InsertTextInRun(text);
    }

    private static SecsItemData DeserializeSecsItem(string yamlText)
    {
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.NullNamingConvention.Instance)
            .WithTypeConverter(new SecsItemDataYamlConverter())
            .Build();

        var data = deserializer.Deserialize<SecsItemData>(yamlText);
        if (data == null || string.IsNullOrWhiteSpace(data.Type))
            throw new InvalidOperationException("YAML 未包含有效的 SECS Item 类型。");

        return data;
    }

    protected override void OnClosed(EventArgs e)
    {
        _connectionManager.Dispose();
        base.OnClosed(e);
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public bool IsReceived { get; set; }
    public bool IsError { get; set; }
    public byte? Stream { get; set; }
    public byte? Function { get; set; }
    public string Message { get; set; } = string.Empty;
    public byte[]? RawData { get; set; }
    public SecsMessage? SecsMessage { get; set; }
    
    public string DisplayText => $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] {(IsReceived ? "<--" : "-->")} {Message}";
}
