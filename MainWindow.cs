using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace LinuxMintSystemMonitor;

public sealed class MainWindow : Window
{
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(20, 20, 20));
    private static readonly IBrush MutedTextBrush = new SolidColorBrush(Color.FromRgb(88, 88, 88));
    private static readonly IBrush ThinBorderBrush = new SolidColorBrush(Color.FromRgb(205, 205, 205));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromRgb(213, 232, 247));
    private static readonly IBrush CpuLineBrush = new SolidColorBrush(Color.FromRgb(38, 139, 210));
    private static readonly IBrush NetworkReceiveBrush = new SolidColorBrush(Color.FromRgb(23, 83, 137));
    private static readonly IBrush NetworkSendBrush = new SolidColorBrush(Color.FromRgb(91, 172, 224));
    private static readonly IBrush MenuBackgroundBrush = new SolidColorBrush(Color.FromRgb(248, 248, 248));
    private static readonly IBrush PerformanceListBackgroundBrush = new SolidColorBrush(Color.FromRgb(250, 250, 250));
    private static readonly IBrush HeaderRowBrush = new SolidColorBrush(Color.FromRgb(240, 246, 251));
    private static readonly IBrush AlternateRowBrush = new SolidColorBrush(Color.FromRgb(247, 250, 252));
    private static readonly IBrush CellBorderBrush = new SolidColorBrush(Color.FromRgb(232, 232, 232));
    private static readonly IBrush SelectionBorderBrush = new SolidColorBrush(Color.FromRgb(151, 203, 236));
    private static readonly IBrush MemoryUsedBrush = new SolidColorBrush(Color.FromRgb(121, 184, 224));
    private static readonly IBrush MemoryCachedBrush = new SolidColorBrush(Color.FromRgb(188, 219, 239));

    private readonly MetricsRefreshService _refreshService = new();
    private readonly MetricHistory _cpuHistory = new();
    private readonly MetricHistory _ramHistory = new();
    private readonly MetricHistory _diskActiveHistory = new();
    private readonly MetricHistory _diskTransferHistory = new();
    private readonly MetricHistory _diskReadHistory = new();
    private readonly MetricHistory _diskWriteHistory = new();
    private readonly MetricHistory _networkReceiveHistory = new();
    private readonly MetricHistory _networkTransmitHistory = new();
    private readonly Dictionary<int, GpuHistorySet> _gpuHistories = new();
    private readonly List<GpuListBinding> _gpuListBindings = new();
    private readonly List<MetricHistory> _cpuCoreHistories = new();
    private readonly List<CpuGraphBinding> _cpuGraphBindings = new();
    private readonly Dictionary<string, TextBlock> _summaryValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _memorySummaryValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _diskSummaryValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _networkSummaryValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _gpuSummaryValues = new(StringComparer.Ordinal);
    private readonly Dictionary<AppTab, Border> _tabBorders = new();
    private readonly Dictionary<string, Bitmap?> _iconCache = new(StringComparer.Ordinal);
    private readonly ObservableCollection<ProcessDisplayRow> _processDisplayRows = new();
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _shutdown = new();
    private SingleInstanceFocusService? _focusService;
    private MenuItem? _alwaysOnTopMenuItem;
    private int _metricsRefreshRunning;
    private int _processRefreshRunning;
    private int _startupRefreshRunning;
    private long _lastAllocatedBytes;
    private DateTimeOffset _lastDiagnosticsAt = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastProcessRefreshAt = DateTimeOffset.MinValue;

    private SystemMetrics? _lastMetrics;
    private AppTab _selectedTab = AppTab.Performance;
    private PerformancePage _selectedPage = PerformancePage.Cpu;
    private ContentControl? _mainHost;
    private Control? _performanceView;
    private Control? _processView;
    private Control? _startupView;
    private Control? _detailsView;
    private Grid? _processTable;
    private ContentControl? _processHeaderHost;
    private ListBox? _processListBox;
    private Grid? _startupTable;
    private Button? _endTaskButton;
    private Button? _enableStartupButton;
    private Button? _disableStartupButton;
    private TextBox? _processSearchBox;
    private CheckBox? _showSystemProcessesCheckBox;
    private ComboBox? _processModeComboBox;
    private IReadOnlyList<ProcessRow> _processRows = Array.Empty<ProcessRow>();
    private IReadOnlyList<StartupApplication> _startupRows = Array.Empty<StartupApplication>();
    private ProcessColumn _processSortColumn = ProcessColumn.Cpu;
    private bool _processSortAscending;
    private ProcessViewMode _processViewMode = ProcessViewMode.Grouped;
    private bool _showSystemProcesses;
    private bool _systemProcessesExpanded;
    private readonly HashSet<string> _expandedAppGroups = new(StringComparer.Ordinal);
    private int? _selectedProcessPid;
    private string? _selectedProcessGroupKey;
    private string? _selectedStartupId;
    private ContentControl? _detailHost;
    private Border? _cpuRow;
    private Border? _memoryRow;
    private Border? _diskRow;
    private Border? _networkRow;
    private TextBlock? _cpuListValue;
    private TextBlock? _memoryListValue;
    private TextBlock? _diskListValue;
    private TextBlock? _networkListValue;
    private TextBlock? _cpuTitleModel;
    private TextBlock? _totalCpuValue;
    private LineGraphControl? _totalCpuGraph;
    private TextBlock? _memoryTotalText;
    private TextBlock? _memoryGraphValue;
    private LineGraphControl? _memoryDetailGraph;
    private TextBlock? _diskTitleModel;
    private TextBlock? _diskActiveValue;
    private TextBlock? _diskTransferValue;
    private LineGraphControl? _diskActiveGraph;
    private LineGraphControl? _diskTransferGraph;
    private TextBlock? _networkHeaderTitle;
    private TextBlock? _networkHeaderDetails;
    private TextBlock? _networkGraphValue;
    private LineGraphControl? _networkDetailGraph;
    private TextBlock? _gpuHeaderName;
    private TextBlock? _gpuGraphValue;
    private TextBlock? _gpuMemoryGraphValue;
    private TextBlock? _gpuTemperatureGraphValue;
    private TextBlock? _gpuEncoderDecoderGraphValue;
    private LineGraphControl? _gpuDetailGraph;
    private LineGraphControl? _gpuMemoryGraph;
    private LineGraphControl? _gpuTemperatureGraph;
    private LineGraphControl? _gpuEncoderDecoderGraph;
    private ColumnDefinition? _memoryUsedColumn;
    private ColumnDefinition? _memoryCachedColumn;
    private ColumnDefinition? _memoryAvailableColumn;
    private Grid? _processorGrid;
    private int _processorGridColumns;
    private LineGraphControl? _cpuMiniGraph;
    private LineGraphControl? _memoryMiniGraph;
    private LineGraphControl? _diskMiniGraph;
    private LineGraphControl? _networkMiniGraph;
    private StackPanel? _performanceList;
    private TextBlock? _gpuHeaderTitle;
    private TextBlock? _gpuNoteText;
    private int _selectedGpuIndex;

    public MainWindow()
    {
        Title = "Linux Mint System Monitor";
        Width = 1080;
        Height = 720;
        MinWidth = 860;
        MinHeight = 560;
        FontFamily = FontFamily.Default;
        FontSize = 12;
        Background = Brushes.White;
        Content = BuildTaskManagerShell();

        _lastAllocatedBytes = GC.GetTotalAllocatedBytes();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _timer.Tick += OnTimerTick;

        SizeChanged += (_, _) =>
        {
            var columns = DetermineProcessorColumns(_cpuCoreHistories.Count);
            if (_processorGrid is not null && columns != _processorGridColumns)
            {
                RebuildProcessorGrid();
                UpdateProcessorGraphs();
            }
        };

        Opened += async (_, _) =>
        {
            _focusService = new SingleInstanceFocusService(this);
            _focusService.Start();
            await RefreshAllAsync();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _shutdown.Cancel();
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _focusService?.Dispose();
            DisposeIconCache();
            _shutdown.Dispose();
        };
    }

    private async void OnTimerTick(object? sender, EventArgs args)
    {
        await RefreshAllAsync();
    }

    private Control BuildTaskManagerShell()
    {
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star)
            },
            Background = Brushes.White
        };

        root.Children.Add(BuildMenuRow());

        var tabs = BuildTabRow();
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        _performanceView = BuildPerformanceView();
        _mainHost = new ContentControl
        {
            Content = _performanceView
        };
        Grid.SetRow(_mainHost, 2);
        root.Children.Add(_mainHost);

        return root;
    }

    private Control BuildPerformanceView()
    {
        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(245, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star)
            }
        };
        body.Children.Add(BuildPerformanceList());

        _detailHost = new ContentControl
        {
            Content = BuildCpuDetailPanel()
        };
        Grid.SetColumn(_detailHost, 1);
        body.Children.Add(_detailHost);

        return body;
    }

    private Control BuildMenuRow()
    {
        var runNewTask = new MenuItem { Header = "Run new task" };
        runNewTask.Click += (_, _) => ShowRunNewTaskDialog();

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Close();

        _alwaysOnTopMenuItem = new MenuItem
        {
            Header = "Always on top",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = Topmost
        };
        _alwaysOnTopMenuItem.Click += (_, _) =>
        {
            Topmost = _alwaysOnTopMenuItem.IsChecked == true;
        };

        return new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = MenuBackgroundBrush,
            Child = new Menu
            {
                Background = MenuBackgroundBrush,
                ItemsSource = new[]
                {
                    new MenuItem
                    {
                        Header = "File",
                        ItemsSource = new[] { runNewTask, exit }
                    },
                    new MenuItem
                    {
                        Header = "Options",
                        ItemsSource = new[] { _alwaysOnTopMenuItem }
                    },
                    new MenuItem { Header = "View" }
                }
            }
        };
    }

    private async void ShowRunNewTaskDialog()
    {
        var commandBox = new TextBox
        {
            Watermark = "Command",
            MinWidth = 360,
            MinHeight = 28
        };
        var runButton = BuildDialogButton("Run", () => { });
        var cancelButton = BuildDialogButton("Cancel", () => { });
        var dialog = new Window
        {
            Title = "Run new task",
            Width = 440,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(1, GridUnitType.Star),
                    new RowDefinition(GridLength.Auto)
                },
                Margin = new Thickness(16),
                Children =
                {
                    commandBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, runButton }
                    }
                }
            }
        };

        if (dialog.Content is Grid grid && grid.Children[1] is StackPanel buttons)
        {
            Grid.SetRow(buttons, 1);
        }

        cancelButton.Click += (_, _) => dialog.Close();
        runButton.Click += (_, _) =>
        {
            var command = commandBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            _ = Task.Run(() => LaunchCommand(command));
            dialog.Close();
        };

        commandBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter)
            {
                return;
            }

            var command = commandBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            _ = Task.Run(() => LaunchCommand(command));
            dialog.Close();
        };

        await dialog.ShowDialog(this);
    }

    private static void LaunchCommand(string command)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { "-c", command },
                UseShellExecute = false
            });
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
        }
    }

    private Control BuildTabRow()
    {
        var tabs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0
        };

        foreach (var tab in new[]
                 {
                     (Name: "Processes", Tab: AppTab.Processes),
                     (Name: "Performance", Tab: AppTab.Performance),
                     (Name: "Startup", Tab: AppTab.Startup),
                     (Name: "Details", Tab: AppTab.Details)
                 })
        {
            var selected = tab.Tab == _selectedTab;
            var border = new Border
            {
                BorderBrush = selected ? CpuLineBrush : Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, selected ? 2 : 0),
                Padding = new Thickness(12, 8, 12, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text = tab.Name,
                    FontSize = 12,
                    Foreground = TextBrush
                }
            };
            _tabBorders[tab.Tab] = border;
            border.PointerPressed += (_, _) => SelectTab(tab.Tab);

            tabs.Children.Add(border);
        }

        return new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.White,
            Child = tabs
        };
    }

    private Control BuildPerformanceList()
    {
        var list = new StackPanel
        {
            Spacing = 0,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _performanceList = list;

        _cpuRow = BuildPerformanceListItem("CPU", true, out _cpuListValue, out _cpuMiniGraph);
        _memoryRow = BuildPerformanceListItem("Memory", false, out _memoryListValue, out _memoryMiniGraph);
        _diskRow = BuildPerformanceListItem("Disk 0", false, out _diskListValue, out _diskMiniGraph);
        _networkRow = BuildPerformanceListItem("Network", false, out _networkListValue, out _networkMiniGraph);

        _cpuRow.Cursor = new Cursor(StandardCursorType.Hand);
        _memoryRow.Cursor = new Cursor(StandardCursorType.Hand);
        _diskRow.Cursor = new Cursor(StandardCursorType.Hand);
        _networkRow.Cursor = new Cursor(StandardCursorType.Hand);
        _cpuRow.PointerPressed += (_, _) => SelectPage(PerformancePage.Cpu);
        _memoryRow.PointerPressed += (_, _) => SelectPage(PerformancePage.Memory);
        _diskRow.PointerPressed += (_, _) => SelectPage(PerformancePage.Disk);
        _networkRow.PointerPressed += (_, _) => SelectPage(PerformancePage.Network);

        list.Children.Add(_cpuRow);
        list.Children.Add(_memoryRow);
        list.Children.Add(_diskRow);
        list.Children.Add(_networkRow);

        return new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Background = PerformanceListBackgroundBrush,
            Child = list
        };
    }

    private void SelectTab(AppTab tab)
    {
        if (_selectedTab == tab)
        {
            return;
        }

        _selectedTab = tab;
        UpdateTabSelection();

        if (_mainHost is null)
        {
            return;
        }

        if (tab == AppTab.Processes)
        {
            _processView ??= BuildProcessesPanel();
            _mainHost.Content = _processView;
            _ = RefreshProcessesAsync();
            return;
        }

        if (tab == AppTab.Startup)
        {
            _startupView ??= BuildStartupPanel();
            _mainHost.Content = _startupView;
            _ = RefreshStartupAsync();
            return;
        }

        if (tab == AppTab.Details)
        {
            _detailsView ??= BuildDetailsPanel();
            _mainHost.Content = _detailsView;
            return;
        }

        _performanceView ??= BuildPerformanceView();
        _mainHost.Content = _performanceView;
        if (_lastMetrics is not null)
        {
            UpdatePerformanceList(_lastMetrics);
            UpdateActiveDetail(_lastMetrics);
        }
    }

    private void UpdateTabSelection()
    {
        foreach (var (tab, border) in _tabBorders)
        {
            var selected = tab == _selectedTab;
            border.BorderBrush = selected ? CpuLineBrush : Brushes.Transparent;
            border.BorderThickness = new Thickness(0, 0, 0, selected ? 2 : 0);
        }
    }

    private static Border BuildPerformanceListItem(string title, bool selected, out TextBlock valueText, out LineGraphControl graph)
    {
        graph = new LineGraphControl
        {
            Width = 54,
            Height = 38,
            MinHeight = 38,
            Maximum = 100,
            Stroke = CpuLineBrush,
            ClipToBounds = true
        };

        valueText = new TextBlock
        {
            Text = "-",
            FontSize = 12,
            Foreground = MutedTextBrush,
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        var textColumn = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        textColumn.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            Foreground = TextBrush
        });
        Grid.SetRow(valueText, 1);
        textColumn.Children.Add(valueText);

        var itemGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(60, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star)
            }
        };
        Grid.SetColumn(textColumn, 1);
        itemGrid.Children.Add(graph);
        itemGrid.Children.Add(textColumn);

        return new Border
        {
            Background = selected ? SelectionBrush : Brushes.Transparent,
            BorderBrush = selected ? SelectionBorderBrush : Brushes.Transparent,
            BorderThickness = new Thickness(0, selected ? 1 : 0),
            MinHeight = 64,
            Padding = new Thickness(8, 8),
            Child = itemGrid
        };
    }

    private Control BuildProcessesPanel()
    {
        _processTable = null;
        _processListBox = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemsSource = _processDisplayRows,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<ProcessDisplayRow>((row, _) => BuildProcessDisplayRow(row), supportsRecycling: true),
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _endTaskButton = new Button
        {
            Content = "End Task",
            Padding = new Thickness(12, 5),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsEnabled = SelectedProcessCanEndTask()
        };
        _endTaskButton.Click += (_, _) => EndSelectedProcess();
        _processSearchBox = new TextBox
        {
            Watermark = "Search",
            Width = 220,
            MinHeight = 28,
            VerticalAlignment = VerticalAlignment.Center
        };
        _processSearchBox.TextChanged += (_, _) => RenderProcessRows();

        _processModeComboBox = new ComboBox
        {
            Width = 180,
            MinHeight = 28,
            SelectedIndex = 0,
            ItemsSource = new[] { "Grouped", "Apps", "Background processes", "System processes", "All processes" },
            VerticalAlignment = VerticalAlignment.Center
        };
        _processModeComboBox.SelectionChanged += (_, _) =>
        {
            _processViewMode = _processModeComboBox.SelectedIndex switch
            {
                1 => ProcessViewMode.Apps,
                2 => ProcessViewMode.Background,
                3 => ProcessViewMode.System,
                4 => ProcessViewMode.All,
                _ => ProcessViewMode.Grouped
            };
            RenderProcessRows();
        };

        _showSystemProcessesCheckBox = new CheckBox
        {
            Content = "Show system processes",
            IsChecked = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        _showSystemProcessesCheckBox.IsCheckedChanged += (_, _) =>
        {
            _showSystemProcesses = _showSystemProcessesCheckBox.IsChecked == true;
            RenderProcessRows();
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(14, 10, 14, 8),
            Children =
            {
                new TextBlock
                {
                    Text = "Processes",
                    FontSize = 22,
                    Foreground = TextBrush,
                    VerticalAlignment = VerticalAlignment.Center
                },
                _endTaskButton
            }
        };
        Grid.SetColumn(_endTaskButton, 1);

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(14, 0, 14, 8),
            Children =
            {
                _processSearchBox,
                _processModeComboBox,
                _showSystemProcessesCheckBox
            }
        };

        _processHeaderHost = new ContentControl { Content = BuildProcessHeaderGrid() };
        var tableHost = new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(1, GridUnitType.Star)
                },
                Children =
                {
                    _processHeaderHost,
                    _processListBox
                }
            }
        };
        Grid.SetRow(_processListBox, 1);
        Grid.SetRow(controls, 1);
        Grid.SetRow(tableHost, 2);

        return new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star)
            },
            Background = Brushes.White,
            Children =
            {
                header,
                controls,
                tableHost
            }
        };
    }

    private void BuildProcessHeaderRow()
    {
        if (_processTable is null)
        {
            return;
        }

        _processTable.ColumnDefinitions.Clear();
        foreach (var width in new[] { 2.4, 0.7, 0.8, 0.9, 1.1, 1.1, 1.0, 0.9 })
        {
            _processTable.ColumnDefinitions.Add(new ColumnDefinition(width, GridUnitType.Star));
        }

        var headers = new[]
        {
            (Text: "Name", Column: ProcessColumn.Name),
            (Text: "PID", Column: ProcessColumn.Pid),
            (Text: "CPU %", Column: ProcessColumn.Cpu),
            (Text: "Memory", Column: ProcessColumn.Memory),
            (Text: "Disk read/write", Column: ProcessColumn.Disk),
            (Text: "Network I/O", Column: ProcessColumn.Network),
            (Text: "User", Column: ProcessColumn.User),
            (Text: "Status", Column: ProcessColumn.Status)
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var header = headers[i];
            var marker = _processSortColumn == header.Column ? (_processSortAscending ? " ↑" : " ↓") : string.Empty;
            var button = new Button
            {
                Content = header.Text + marker,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 6),
                Background = MenuBackgroundBrush,
                BorderBrush = ThinBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Foreground = TextBrush,
                FontSize = 12
            };
            button.Click += (_, _) => SortProcessesBy(header.Column);
            Grid.SetColumn(button, i);
            Grid.SetRow(button, 0);
            _processTable.Children.Add(button);
        }
    }

    private Control BuildProcessHeaderGrid()
    {
        var grid = new Grid();
        foreach (var width in new[] { 2.4, 0.7, 0.8, 0.9, 1.1, 1.1, 1.0, 0.9 })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(width, GridUnitType.Star));
        }

        var headers = new[]
        {
            (Text: "Name", Column: ProcessColumn.Name),
            (Text: "PID", Column: ProcessColumn.Pid),
            (Text: "CPU %", Column: ProcessColumn.Cpu),
            (Text: "Memory", Column: ProcessColumn.Memory),
            (Text: "Disk read/write", Column: ProcessColumn.Disk),
            (Text: "Network I/O", Column: ProcessColumn.Network),
            (Text: "User", Column: ProcessColumn.User),
            (Text: "Status", Column: ProcessColumn.Status)
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var header = headers[i];
            var marker = _processSortColumn == header.Column ? (_processSortAscending ? " ↑" : " ↓") : string.Empty;
            var button = new Button
            {
                Content = header.Text + marker,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 6),
                Background = MenuBackgroundBrush,
                BorderBrush = ThinBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Foreground = TextBrush,
                FontSize = 12
            };
            button.Click += (_, _) => SortProcessesBy(header.Column);
            Grid.SetColumn(button, i);
            grid.Children.Add(button);
        }

        return grid;
    }

    private void SortProcessesBy(ProcessColumn column)
    {
        if (_processSortColumn == column)
        {
            _processSortAscending = !_processSortAscending;
        }
        else
        {
            _processSortColumn = column;
            _processSortAscending = column is ProcessColumn.Name or ProcessColumn.User or ProcessColumn.Status;
        }

        if (_processHeaderHost is not null)
        {
            _processHeaderHost.Content = BuildProcessHeaderGrid();
        }

        RenderProcessRows();
    }

    private async Task RefreshProcessesAsync()
    {
        if (Interlocked.Exchange(ref _processRefreshRunning, 1) == 1)
        {
            return;
        }

        try
        {
            var rows = await _refreshService.ReadProcessesAsync(_shutdown.Token);
            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            _processRows = rows;
            if (_selectedProcessPid is { } pid && !_processRows.Any(process => process.Pid == pid))
            {
                _selectedProcessPid = null;
            }

            if (_endTaskButton is not null)
            {
                _endTaskButton.IsEnabled = SelectedProcessCanEndTask();
            }

            RenderProcessRows();
            LogDiagnostics();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Title = $"Linux Mint System Monitor - process read error: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _processRefreshRunning, 0);
        }
    }

    private void RenderProcessRows()
    {
        if (_processListBox is null)
        {
            return;
        }

        var visibleRows = FilterProcessRows(_processRows).ToArray();
        var renderIndex = 0;
        var rows = new List<ProcessDisplayRow>();

        if (_processViewMode == ProcessViewMode.Grouped)
        {
            AddVirtualAppsGroup(rows, visibleRows.Where(static row => row.Category == ProcessCategory.App), ref renderIndex);
            AddVirtualProcessGroup(rows, "Background processes", visibleRows.Where(static row => row.Category == ProcessCategory.Background), ref renderIndex, expanded: true);

            var systemRows = visibleRows.Where(static row => row.Category == ProcessCategory.System).ToArray();
            if (_showSystemProcesses || systemRows.Length > 0)
            {
                AddVirtualProcessGroup(rows, "System processes", systemRows, ref renderIndex, _systemProcessesExpanded);
            }

            UpdateProcessDisplayRows(rows);
            return;
        }

        var sorted = SortProcessRows(visibleRows).ToArray();
        for (var i = 0; i < sorted.Length; i++)
        {
            rows.Add(ProcessDisplayRow.ForProcess(sorted[i], i % 2 == 1, Indent: false));
        }

        UpdateProcessDisplayRows(rows);
    }

    private void UpdateProcessDisplayRows(IReadOnlyList<ProcessDisplayRow> rows)
    {
        while (_processDisplayRows.Count > rows.Count)
        {
            _processDisplayRows.RemoveAt(_processDisplayRows.Count - 1);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (i < _processDisplayRows.Count)
            {
                _processDisplayRows[i] = rows[i];
            }
            else
            {
                _processDisplayRows.Add(rows[i]);
            }
        }
    }

    private void AddVirtualAppsGroup(List<ProcessDisplayRow> target, IEnumerable<ProcessRow> rows, ref int renderIndex)
    {
        var appRows = rows.ToArray();
        if (appRows.Length == 0)
        {
            return;
        }

        target.Add(ProcessDisplayRow.ForHeader($"▼ Apps ({appRows.Length})", IsSystemHeader: false));
        var grouped = appRows
            .GroupBy(static row => row.AppGroupKey ?? $"pid:{row.Pid}", StringComparer.Ordinal)
            .Select(group => ProcessAppGroup.From(group.Key, group))
            .ToArray();

        foreach (var appGroup in SortAppGroups(grouped))
        {
            target.Add(ProcessDisplayRow.ForAppGroup(appGroup, renderIndex % 2 == 1));
            renderIndex++;

            if (!_expandedAppGroups.Contains(appGroup.Key))
            {
                continue;
            }

            foreach (var child in SortProcessRows(appGroup.Children))
            {
                target.Add(ProcessDisplayRow.ForProcess(child, renderIndex % 2 == 1, Indent: true));
                renderIndex++;
            }
        }
    }

    private void AddVirtualProcessGroup(List<ProcessDisplayRow> target, string title, IEnumerable<ProcessRow> rows, ref int renderIndex, bool expanded)
    {
        var sortedRows = SortProcessRows(rows).ToArray();
        if (sortedRows.Length == 0 && title != "System processes")
        {
            return;
        }

        var isSystem = title == "System processes";
        var headerText = isSystem
            ? $"{(expanded ? "▼" : "▶")} {title} ({sortedRows.Length})"
            : $"▼ {title} ({sortedRows.Length})";
        target.Add(ProcessDisplayRow.ForHeader(headerText, isSystem));

        if (!expanded)
        {
            return;
        }

        foreach (var process in sortedRows)
        {
            target.Add(ProcessDisplayRow.ForProcess(process, renderIndex % 2 == 1, Indent: false));
            renderIndex++;
        }
    }

    private IEnumerable<ProcessRow> FilterProcessRows(IEnumerable<ProcessRow> rows)
    {
        var query = _processSearchBox?.Text?.Trim();
        var filtered = rows;

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(row =>
                row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.User.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        filtered = _processViewMode switch
        {
            ProcessViewMode.Apps => filtered.Where(static row => row.Category == ProcessCategory.App),
            ProcessViewMode.Background => filtered.Where(static row => row.Category == ProcessCategory.Background),
            ProcessViewMode.System => filtered.Where(static row => row.Category == ProcessCategory.System),
            ProcessViewMode.All => filtered,
            _ => _showSystemProcesses
                ? filtered
                : filtered.Where(static row => row.Category != ProcessCategory.System)
        };

        return filtered;
    }

    private int AddAppsGroup(IEnumerable<ProcessRow> rows, int tableRow, ref int renderIndex)
    {
        if (_processTable is null)
        {
            return tableRow;
        }

        var appRows = rows.ToArray();
        if (appRows.Length == 0)
        {
            return tableRow;
        }

        _processTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var groupHeader = new Border
        {
            Background = HeaderRowBrush,
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6),
            Child = new TextBlock
            {
                Text = $"▼ Apps ({appRows.Length})",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = TextBrush
            }
        };
        Grid.SetColumnSpan(groupHeader, _processTable.ColumnDefinitions.Count);
        Grid.SetRow(groupHeader, tableRow++);
        _processTable.Children.Add(groupHeader);

        var grouped = appRows
            .GroupBy(static row => row.AppGroupKey ?? $"pid:{row.Pid}", StringComparer.Ordinal)
            .Select(group => ProcessAppGroup.From(group.Key, group))
            .ToArray();

        foreach (var appGroup in SortAppGroups(grouped))
        {
            _processTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddAppParentRow(appGroup, tableRow++, renderIndex % 2 == 1);
            renderIndex++;

            if (!_expandedAppGroups.Contains(appGroup.Key))
            {
                continue;
            }

            foreach (var child in SortProcessRows(appGroup.Children))
            {
                _processTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                AddProcessRow(child, tableRow++, renderIndex % 2 == 1, indent: true);
                renderIndex++;
            }
        }

        return tableRow;
    }

    private IEnumerable<ProcessAppGroup> SortAppGroups(IEnumerable<ProcessAppGroup> groups)
    {
        Func<ProcessAppGroup, object> key = _processSortColumn switch
        {
            ProcessColumn.Name => static group => group.Name,
            ProcessColumn.Pid => static group => group.PrimaryPid,
            ProcessColumn.Cpu => static group => group.CpuPercent,
            ProcessColumn.Memory => static group => group.ResidentBytes,
            ProcessColumn.Disk => static group => group.DiskReadBytesPerSecond + group.DiskWriteBytesPerSecond,
            ProcessColumn.Network => static group => group.NetworkIo,
            ProcessColumn.User => static group => group.User,
            ProcessColumn.Status => static group => group.Status,
            _ => static group => group.CpuPercent
        };

        return _processSortAscending
            ? groups.OrderBy(key).ThenBy(static group => group.Name)
            : groups.OrderByDescending(key).ThenBy(static group => group.Name);
    }

    private int AddProcessGroup(string title, IEnumerable<ProcessRow> rows, int tableRow, ref int renderIndex, bool expanded)
    {
        if (_processTable is null)
        {
            return tableRow;
        }

        var sortedRows = SortProcessRows(rows).ToArray();
        if (sortedRows.Length == 0 && title != "System processes")
        {
            return tableRow;
        }

        _processTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var isSystem = title == "System processes";
        var headerText = isSystem
            ? $"{(expanded ? "▼" : "▶")} {title} ({sortedRows.Length})"
            : $"▼ {title} ({sortedRows.Length})";
        var header = new Border
        {
            Background = HeaderRowBrush,
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6),
            Child = new TextBlock
            {
                Text = headerText,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = TextBrush
            }
        };
        if (isSystem)
        {
            header.Cursor = new Cursor(StandardCursorType.Hand);
            header.PointerPressed += (_, _) =>
            {
                _systemProcessesExpanded = !_systemProcessesExpanded;
                RenderProcessRows();
            };
        }

        Grid.SetColumnSpan(header, _processTable.ColumnDefinitions.Count);
        Grid.SetRow(header, tableRow++);
        _processTable.Children.Add(header);

        if (!expanded)
        {
            return tableRow;
        }

        foreach (var process in sortedRows)
        {
            _processTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddProcessRow(process, tableRow++, renderIndex % 2 == 1);
            renderIndex++;
        }

        return tableRow;
    }

    private IEnumerable<ProcessRow> SortProcessRows(IEnumerable<ProcessRow> rows)
    {
        Func<ProcessRow, object> key = _processSortColumn switch
        {
            ProcessColumn.Name => static row => row.Name,
            ProcessColumn.Pid => static row => row.Pid,
            ProcessColumn.Cpu => static row => row.CpuPercent,
            ProcessColumn.Memory => static row => row.ResidentBytes,
            ProcessColumn.Disk => static row => row.DiskReadBytesPerSecond + row.DiskWriteBytesPerSecond,
            ProcessColumn.Network => static row => row.NetworkIo,
            ProcessColumn.User => static row => row.User,
            ProcessColumn.Status => static row => row.Status,
            _ => static row => row.CpuPercent
        };

        return _processSortAscending
            ? rows.OrderBy(key).ThenBy(static row => row.Pid)
            : rows.OrderByDescending(key).ThenBy(static row => row.Pid);
    }

    private void AddAppParentRow(ProcessAppGroup appGroup, int rowIndex, bool alternate)
    {
        if (_processTable is null)
        {
            return;
        }

        var selected = _selectedProcessGroupKey == appGroup.Key;
        var background = selected
            ? SelectionBrush
            : alternate ? AlternateRowBrush : Brushes.White;
        var expanded = _expandedAppGroups.Contains(appGroup.Key);
        var values = new[]
        {
            $"{(expanded ? "▼" : "▶")} {appGroup.Name} ({appGroup.ProcessCount})",
            appGroup.PrimaryPid.ToString(),
            $"{appGroup.CpuPercent:0.0}",
            FormatBytes(appGroup.ResidentBytes),
            $"{FormatRate(appGroup.DiskReadBytesPerSecond)} / {FormatRate(appGroup.DiskWriteBytesPerSecond)}",
            appGroup.NetworkIo,
            appGroup.User,
            appGroup.Status
        };

        for (var column = 0; column < values.Length; column++)
        {
            var localColumn = column;
            var cell = new Border
            {
                Background = background,
                BorderBrush = CellBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                MinHeight = 28,
                Padding = new Thickness(8, 4),
                Child = column == 0
                    ? BuildProcessNameContent(values[column], appGroup.IconPath, indent: false, FontWeight.SemiBold)
                    : new TextBlock
                    {
                        Text = values[column],
                        FontSize = 12,
                        Foreground = TextBrush,
                        FontWeight = FontWeight.Normal,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                ContextMenu = BuildProcessGroupContextMenu(appGroup)
            };
            cell.PointerPressed += (_, args) =>
            {
                SelectProcessGroup(appGroup.Key);
                if (args.ClickCount == 2 || localColumn == 0)
                {
                    ToggleAppGroup(appGroup.Key);
                }
            };

            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, rowIndex);
            _processTable.Children.Add(cell);
        }
    }

    private void AddProcessRow(ProcessRow process, int rowIndex, bool alternate, bool indent = false)
    {
        if (_processTable is null)
        {
            return;
        }

        var selected = _selectedProcessPid == process.Pid;
        var background = selected
            ? SelectionBrush
            : alternate ? AlternateRowBrush : Brushes.White;
        var values = new[]
        {
            indent ? $"    {process.Name}" : process.Name,
            process.Pid.ToString(),
            $"{process.CpuPercent:0.0}",
            FormatBytes(process.ResidentBytes),
            $"{FormatRate(process.DiskReadBytesPerSecond)} / {FormatRate(process.DiskWriteBytesPerSecond)}",
            process.NetworkIo,
            process.User,
            process.Status
        };

        for (var column = 0; column < values.Length; column++)
        {
            var cell = new Border
            {
                Background = background,
                BorderBrush = CellBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                MinHeight = 28,
                Padding = new Thickness(8, 4),
                Child = column == 0
                    ? BuildProcessNameContent(values[column], process.IconPath, indent, FontWeight.Normal)
                    : new TextBlock
                    {
                        Text = values[column],
                        FontSize = 12,
                        Foreground = TextBrush,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                ContextMenu = BuildProcessContextMenu(process)
            };
            cell.PointerPressed += (_, _) => SelectProcess(process.Pid);

            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, rowIndex);
            _processTable.Children.Add(cell);
        }
    }

    private Control BuildProcessDisplayRow(ProcessDisplayRow row)
    {
        if (row.Kind == ProcessDisplayKind.Header)
        {
            var header = new Border
            {
                Background = HeaderRowBrush,
                BorderBrush = ThinBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 6),
                MinHeight = 30,
                Child = new TextBlock
                {
                    Text = row.Title,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = TextBrush
                }
            };

            if (row.IsSystemHeader)
            {
                header.Cursor = new Cursor(StandardCursorType.Hand);
                header.PointerPressed += (_, _) =>
                {
                    _systemProcessesExpanded = !_systemProcessesExpanded;
                    RenderProcessRows();
                };
            }

            return header;
        }

        if (row.AppGroup is { } appGroup)
        {
            return BuildVirtualProcessGrid(
                row,
                new[]
                {
                    $"{(_expandedAppGroups.Contains(appGroup.Key) ? "▼" : "▶")} {appGroup.Name} ({appGroup.ProcessCount})",
                    appGroup.PrimaryPid.ToString(),
                    $"{appGroup.CpuPercent:0.0}",
                    FormatBytes(appGroup.ResidentBytes),
                    $"{FormatRate(appGroup.DiskReadBytesPerSecond)} / {FormatRate(appGroup.DiskWriteBytesPerSecond)}",
                    appGroup.NetworkIo,
                    appGroup.User,
                    appGroup.Status
                },
                appGroup.IconPath,
                FontWeight.SemiBold,
                BuildProcessGroupContextMenu(appGroup),
                (_, args) =>
                {
                    SelectProcessGroup(appGroup.Key);
                    if (args.ClickCount == 2)
                    {
                        ToggleAppGroup(appGroup.Key);
                    }
                });
        }

        var process = row.Process;
        if (process is null)
        {
            return new Border();
        }

        return BuildVirtualProcessGrid(
            row,
            new[]
            {
                process.Name,
                process.Pid.ToString(),
                $"{process.CpuPercent:0.0}",
                FormatBytes(process.ResidentBytes),
                $"{FormatRate(process.DiskReadBytesPerSecond)} / {FormatRate(process.DiskWriteBytesPerSecond)}",
                process.NetworkIo,
                process.User,
                process.Status
            },
            process.IconPath,
            FontWeight.Normal,
            BuildProcessContextMenu(process),
            (_, _) => SelectProcess(process.Pid));
    }

    private Control BuildVirtualProcessGrid(
        ProcessDisplayRow row,
        IReadOnlyList<string> values,
        string? iconPath,
        FontWeight nameWeight,
        ContextMenu contextMenu,
        EventHandler<PointerPressedEventArgs> pointerPressed)
    {
        var selected = row.AppGroup is not null
            ? _selectedProcessGroupKey == row.AppGroup.Key
            : row.Process is not null && _selectedProcessPid == row.Process.Pid;
        var background = selected
            ? SelectionBrush
            : row.Alternate ? AlternateRowBrush : Brushes.White;
        var grid = new Grid
        {
            Background = background,
            ContextMenu = contextMenu
        };
        foreach (var width in new[] { 2.4, 0.7, 0.8, 0.9, 1.1, 1.1, 1.0, 0.9 })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(width, GridUnitType.Star));
        }

        grid.PointerPressed += pointerPressed;
        for (var column = 0; column < values.Count; column++)
        {
            var cell = new Border
            {
                BorderBrush = CellBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                MinHeight = 28,
                Padding = new Thickness(8, 4),
                Child = column == 0
                    ? BuildProcessNameContent(values[column], iconPath, row.Indent, nameWeight)
                    : new TextBlock
                    {
                        Text = values[column],
                        FontSize = 12,
                        Foreground = TextBrush,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    }
            };
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private ContextMenu BuildProcessContextMenu(ProcessRow process)
    {
        var endTask = new MenuItem
        {
            Header = string.IsNullOrWhiteSpace(process.EndTaskReason) ? "End Task" : $"End Task ({process.EndTaskReason})",
            IsEnabled = process.CanEndTask
        };
        endTask.Click += (_, _) =>
        {
            _selectedProcessPid = process.Pid;
            EndSelectedProcess();
        };

        var openLocation = new MenuItem
        {
            Header = "Open File Location",
            IsEnabled = !string.IsNullOrWhiteSpace(process.ExecutablePath)
        };
        openLocation.Click += (_, _) => OpenProcessLocation(process);

        var copyPid = new MenuItem { Header = "Copy PID" };
        copyPid.Click += (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                _ = clipboard.SetTextAsync(process.Pid.ToString());
            }
        };

        return new ContextMenu
        {
            ItemsSource = new[] { endTask, openLocation, copyPid }
        };
    }

    private Control BuildProcessNameContent(string name, string? iconPath, bool indent, FontWeight fontWeight)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(indent ? 18 : 0, 0, 0, 0)
        };

        var bitmap = LoadIcon(iconPath);
        if (bitmap is not null)
        {
            panel.Children.Add(new Image
            {
                Source = bitmap,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        else
        {
            panel.Children.Add(new Border
            {
                Width = 16,
                Height = 16,
                Background = Brushes.Transparent
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = indent ? name.TrimStart() : name,
            FontSize = 12,
            Foreground = TextBrush,
            FontWeight = fontWeight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        return panel;
    }

    private Bitmap? LoadIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        if (_iconCache.TryGetValue(iconPath, out var cached))
        {
            return cached;
        }

        try
        {
            var bitmap = new Bitmap(iconPath);
            _iconCache[iconPath] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _iconCache[iconPath] = null;
            return null;
        }
    }

    private void DisposeIconCache()
    {
        foreach (var bitmap in _iconCache.Values)
        {
            bitmap?.Dispose();
        }

        _iconCache.Clear();
    }

    private ContextMenu BuildProcessGroupContextMenu(ProcessAppGroup appGroup)
    {
        var endTask = new MenuItem
        {
            Header = "End Task",
            IsEnabled = appGroup.CanEndTask
        };
        endTask.Click += async (_, _) =>
        {
            _selectedProcessGroupKey = appGroup.Key;
            await EndSelectedProcessGroupAsync();
        };

        var copyPid = new MenuItem { Header = "Copy PID" };
        copyPid.Click += (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                _ = clipboard.SetTextAsync(string.Join(", ", appGroup.Children.Select(static row => row.Pid)));
            }
        };

        return new ContextMenu
        {
            ItemsSource = new[] { endTask, copyPid }
        };
    }

    private void SelectProcess(int pid)
    {
        _selectedProcessPid = pid;
        _selectedProcessGroupKey = null;
        if (_endTaskButton is not null)
        {
            _endTaskButton.IsEnabled = SelectedProcessCanEndTask();
        }

        RenderProcessRows();
    }

    private void SelectProcessGroup(string groupKey)
    {
        _selectedProcessGroupKey = groupKey;
        _selectedProcessPid = null;
        if (_endTaskButton is not null)
        {
            _endTaskButton.IsEnabled = SelectedProcessGroupCanEndTask();
        }

        RenderProcessRows();
    }

    private void ToggleAppGroup(string groupKey)
    {
        if (!_expandedAppGroups.Add(groupKey))
        {
            _expandedAppGroups.Remove(groupKey);
        }

        RenderProcessRows();
    }

    private void EndSelectedProcess()
    {
        if (_selectedProcessGroupKey is not null)
        {
            _ = EndSelectedProcessGroupAsync();
            return;
        }

        if (_selectedProcessPid is not { } pid)
        {
            return;
        }

        var row = _processRows.FirstOrDefault(process => process.Pid == pid);
        if (row is null || !row.CanEndTask)
        {
            return;
        }

        ProcessMetricsReader.EndTask(pid);
        _ = RefreshProcessesAsync();
    }

    private async Task EndSelectedProcessGroupAsync()
    {
        if (_selectedProcessGroupKey is not { } groupKey)
        {
            return;
        }

        var group = ProcessAppGroup.From(groupKey, _processRows.Where(row => (row.AppGroupKey ?? $"pid:{row.Pid}") == groupKey));
        if (!group.CanEndTask)
        {
            return;
        }

        var confirmed = await ConfirmEndTaskAsync($"End {group.Name} and its {group.ProcessCount} processes?");
        if (!confirmed)
        {
            return;
        }

        foreach (var process in group.Children.Where(static row => row.CanEndTask))
        {
            ProcessMetricsReader.EndTask(process.Pid);
        }

        _ = RefreshProcessesAsync();
    }

    private async Task<bool> ConfirmEndTaskAsync(string message)
    {
        var result = false;
        var dialog = new Window
        {
            Title = "End Task",
            Width = 360,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(1, GridUnitType.Star),
                    new RowDefinition(GridLength.Auto)
                },
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = TextBrush,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            BuildDialogButton("Cancel", () => result = false),
                            BuildDialogButton("End Task", () => result = true)
                        }
                    }
                }
            }
        };

        if (dialog.Content is Grid grid && grid.Children[1] is StackPanel buttons)
        {
            Grid.SetRow(buttons, 1);
            foreach (var button in buttons.Children.OfType<Button>())
            {
                button.Click += (_, _) => dialog.Close();
            }
        }

        await dialog.ShowDialog(this);
        return result;
    }

    private static Button BuildDialogButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 5),
            MinWidth = 78
        };
        button.Click += (_, _) => action();
        return button;
    }

    private bool SelectedProcessCanEndTask()
    {
        return _selectedProcessGroupKey is not null
            ? SelectedProcessGroupCanEndTask()
            : _selectedProcessPid is { } pid
                && _processRows.FirstOrDefault(process => process.Pid == pid) is { CanEndTask: true };
    }

    private bool SelectedProcessGroupCanEndTask()
    {
        if (_selectedProcessGroupKey is not { } groupKey)
        {
            return false;
        }

        return _processRows
            .Where(row => (row.AppGroupKey ?? $"pid:{row.Pid}") == groupKey)
            .Any(static row => row.CanEndTask);
    }

    private static void OpenProcessLocation(ProcessRow process)
    {
        if (string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(process.ExecutablePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                ArgumentList = { directory },
                UseShellExecute = false
            });
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    private Control BuildStartupPanel()
    {
        _startupTable = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto)
            }
        };
        BuildStartupHeaderRow();

        _disableStartupButton = new Button
        {
            Content = "Disable",
            Padding = new Thickness(12, 5),
            IsEnabled = false
        };
        _disableStartupButton.Click += (_, _) => DisableSelectedStartup();
        _enableStartupButton = new Button
        {
            Content = "Enable",
            Padding = new Thickness(12, 5),
            IsEnabled = false
        };
        _enableStartupButton.Click += (_, _) => EnableSelectedStartup();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { _enableStartupButton, _disableStartupButton }
        };
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(14, 10, 14, 8),
            Children =
            {
                new TextBlock
                {
                    Text = "Startup",
                    FontSize = 22,
                    Foreground = TextBrush,
                    VerticalAlignment = VerticalAlignment.Center
                },
                buttons
            }
        };
        Grid.SetColumn(buttons, 1);

        var tableHost = new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = _startupTable
            }
        };
        Grid.SetRow(tableHost, 1);

        return new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star)
            },
            Background = Brushes.White,
            Children = { header, tableHost }
        };
    }

    private void BuildStartupHeaderRow()
    {
        if (_startupTable is null)
        {
            return;
        }

        _startupTable.ColumnDefinitions.Clear();
        foreach (var width in new[] { 1.5, 1.0, 0.7, 2.3, 2.0 })
        {
            _startupTable.ColumnDefinitions.Add(new ColumnDefinition(width, GridUnitType.Star));
        }

        var headers = new[] { "Name", "Publisher/source", "Status", "Command", "Location" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = new Border
            {
                Background = MenuBackgroundBrush,
                BorderBrush = ThinBorderBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(8, 6),
                Child = new TextBlock
                {
                    Text = headers[i],
                    FontSize = 12,
                    Foreground = TextBrush
                }
            };
            Grid.SetColumn(cell, i);
            Grid.SetRow(cell, 0);
            _startupTable.Children.Add(cell);
        }
    }

    private async Task RefreshStartupAsync()
    {
        if (Interlocked.Exchange(ref _startupRefreshRunning, 1) == 1)
        {
            return;
        }

        try
        {
            _startupRows = await _refreshService.ReadStartupApplicationsAsync(_shutdown.Token);
            RenderStartupRows();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Title = $"Linux Mint System Monitor - startup read error: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _startupRefreshRunning, 0);
        }
    }

    private void RenderStartupRows()
    {
        if (_startupTable is null)
        {
            return;
        }

        _startupTable.Children.Clear();
        _startupTable.RowDefinitions.Clear();
        _startupTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        BuildStartupHeaderRow();

        for (var i = 0; i < _startupRows.Count; i++)
        {
            _startupTable.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddStartupRow(_startupRows[i], i + 1, i % 2 == 1);
        }

        UpdateStartupButtons();
    }

    private void AddStartupRow(StartupApplication app, int rowIndex, bool alternate)
    {
        if (_startupTable is null)
        {
            return;
        }

        var selected = _selectedStartupId == app.Id;
        var background = selected
            ? SelectionBrush
            : alternate ? AlternateRowBrush : Brushes.White;
        var values = new[]
        {
            app.Name,
            app.Source,
            app.Enabled ? "Enabled" : "Disabled",
            app.Command,
            app.Location
        };

        for (var column = 0; column < values.Length; column++)
        {
            var cell = new Border
            {
                Background = background,
                BorderBrush = CellBorderBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                MinHeight = 30,
                Padding = new Thickness(8, 4),
                Child = new TextBlock
                {
                    Text = values[column],
                    FontSize = 12,
                    Foreground = TextBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            cell.PointerPressed += (_, _) =>
            {
                _selectedStartupId = app.Id;
                RenderStartupRows();
            };
            Grid.SetColumn(cell, column);
            Grid.SetRow(cell, rowIndex);
            _startupTable.Children.Add(cell);
        }
    }

    private void UpdateStartupButtons()
    {
        var selected = _startupRows.FirstOrDefault(app => app.Id == _selectedStartupId);
        if (_enableStartupButton is not null)
        {
            _enableStartupButton.IsEnabled = selected is { Enabled: false };
        }

        if (_disableStartupButton is not null)
        {
            _disableStartupButton.IsEnabled = selected is { Enabled: true };
        }
    }

    private async void DisableSelectedStartup()
    {
        var selected = _startupRows.FirstOrDefault(app => app.Id == _selectedStartupId);
        if (selected is null)
        {
            return;
        }

        await _refreshService.DisableStartupAsync(selected, _shutdown.Token);
        await RefreshStartupAsync();
    }

    private async void EnableSelectedStartup()
    {
        var selected = _startupRows.FirstOrDefault(app => app.Id == _selectedStartupId);
        if (selected is null)
        {
            return;
        }

        await _refreshService.EnableStartupAsync(selected, _shutdown.Token);
        await RefreshStartupAsync();
    }

    private Control BuildDetailsPanel()
    {
        var details = SystemDetailsReader.Read();
        var grid = new Grid
        {
            Margin = new Thickness(18, 14, 18, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition(220, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star)
            }
        };

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var title = new TextBlock
        {
            Text = "Details",
            FontSize = 28,
            Foreground = TextBrush,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetColumnSpan(title, 2);
        grid.Children.Add(title);

        for (var i = 0; i < details.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock
            {
                Text = details[i].Name,
                Foreground = MutedTextBrush,
                FontSize = 12,
                Margin = new Thickness(0, 0, 14, 8)
            };
            var value = new TextBlock
            {
                Text = details[i].Value,
                Foreground = TextBrush,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(name, i + 1);
            Grid.SetRow(value, i + 1);
            Grid.SetColumn(value, 1);
            grid.Children.Add(name);
            grid.Children.Add(value);
        }

        return new ScrollViewer
        {
            Background = Brushes.White,
            Content = grid
        };
    }

    private void SelectPage(PerformancePage page)
    {
        if (_selectedPage == page)
        {
            return;
        }

        _selectedPage = page;
        UpdateSelection();

        if (_detailHost is not null)
        {
            _detailHost.Content = page switch
            {
                PerformancePage.Memory => BuildMemoryDetailPanel(),
                PerformancePage.Disk => BuildDiskDetailPanel(),
                PerformancePage.Network => BuildNetworkDetailPanel(),
                PerformancePage.Gpu => BuildGpuDetailPanel(),
                _ => BuildCpuDetailPanel()
            };
        }

        if (_lastMetrics is not null)
        {
            UpdateActiveDetail(_lastMetrics);
        }
    }

    private void UpdateSelection()
    {
        SetSelected(_cpuRow, _selectedPage == PerformancePage.Cpu);
        SetSelected(_memoryRow, _selectedPage == PerformancePage.Memory);
        SetSelected(_diskRow, _selectedPage == PerformancePage.Disk);
        SetSelected(_networkRow, _selectedPage == PerformancePage.Network);
        foreach (var binding in _gpuListBindings)
        {
            SetSelected(binding.Row, _selectedPage == PerformancePage.Gpu && binding.Index == _selectedGpuIndex);
        }
    }

    private static void SetSelected(Border? row, bool selected)
    {
        if (row is null)
        {
            return;
        }

        row.Background = selected ? SelectionBrush : Brushes.Transparent;
        row.BorderBrush = selected ? SelectionBorderBrush : Brushes.Transparent;
        row.BorderThickness = new Thickness(0, selected ? 1 : 0);
    }

    private Control BuildCpuDetailPanel()
    {
        var panel = new Grid
        {
            Margin = new Thickness(18, 14, 18, 12),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        _cpuTitleModel = new TextBlock
        {
            Text = "-",
            FontSize = 13,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                new TextBlock
                {
                    Text = "CPU",
                    FontSize = 28,
                    Foreground = TextBrush
                },
                _cpuTitleModel
            }
        };
        Grid.SetColumn(_cpuTitleModel, 1);
        panel.Children.Add(header);

        var labelRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(0, 2, 0, 4)
        };
        labelRow.Children.Add(new TextBlock
        {
            Text = "% Utilization over 10 minutes",
            FontSize = 12,
            Foreground = MutedTextBrush
        });
        var maximumLabel = new TextBlock
        {
            Text = "100%",
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(maximumLabel, 1);
        labelRow.Children.Add(maximumLabel);
        Grid.SetRow(labelRow, 1);
        panel.Children.Add(labelRow);

        var totalCard = BuildTotalCpuGraph();
        Grid.SetRow(totalCard, 2);
        panel.Children.Add(totalCard);

        _processorGrid = new Grid
        {
            Margin = new Thickness(0, 0, 8, 4)
        };
        Grid.SetRow(_processorGrid, 3);
        panel.Children.Add(_processorGrid);

        var summary = BuildSummaryGrid();
        Grid.SetRow(summary, 4);
        panel.Children.Add(summary);

        RebuildProcessorGrid();
        return panel;
    }

    private Control BuildTotalCpuGraph()
    {
        _totalCpuValue = new TextBlock
        {
            Text = "-",
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _totalCpuGraph = new LineGraphControl
        {
            Height = 96,
            MinHeight = 96,
            Maximum = 100,
            Stroke = CpuLineBrush,
            ClipToBounds = true
        };
        Grid.SetRow(_totalCpuGraph, 1);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                new TextBlock
                {
                    Text = "Total",
                    FontSize = 11,
                    Foreground = TextBrush
                },
                _totalCpuValue
            }
        };
        Grid.SetColumn(_totalCpuValue, 1);

        return new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Padding = new Thickness(5),
            Margin = new Thickness(0, 0, 8, 6),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                Children =
                {
                    header,
                    _totalCpuGraph
                }
            }
        };
    }

    private void RebuildProcessorGrid()
    {
        if (_processorGrid is null)
        {
            return;
        }

        _processorGrid.Children.Clear();
        _processorGrid.RowDefinitions.Clear();
        _processorGrid.ColumnDefinitions.Clear();
        _cpuGraphBindings.Clear();

        var graphCount = _cpuCoreHistories.Count;
        _processorGridColumns = DetermineProcessorColumns(graphCount);
        for (var i = 0; i < _processorGridColumns; i++)
        {
            _processorGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        }

        var rows = Math.Max(1, (int)Math.Ceiling(graphCount / (double)_processorGridColumns));
        for (var i = 0; i < rows; i++)
        {
            _processorGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var i = 0; i < _cpuCoreHistories.Count; i++)
        {
            AddProcessorGraph($"CPU {i}", _cpuCoreHistories[i], i, CpuLineBrush);
        }
    }

    private void AddProcessorGraph(string title, MetricHistory history, int index, IBrush stroke)
    {
        if (_processorGrid is null)
        {
            return;
        }

        var value = new TextBlock
        {
            Text = "-",
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var graph = new LineGraphControl
        {
            Height = 52,
            MinHeight = 50,
            Maximum = 100,
            Stroke = stroke,
            ClipToBounds = true
        };

        var card = new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Margin = new Thickness(0, 0, 5, 4),
            Padding = new Thickness(3),
            MinWidth = 118,
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(1, GridUnitType.Star),
                            new ColumnDefinition(GridLength.Auto)
                        },
                        Children =
                        {
                            new TextBlock
                            {
                                Text = title,
                                FontSize = 11,
                                Foreground = TextBrush
                            },
                            value
                        }
                    },
                    graph
                }
            }
        };

        Grid.SetColumn(value, 1);
        Grid.SetRow(graph, 1);
        Grid.SetColumn(card, index % _processorGridColumns);
        Grid.SetRow(card, index / _processorGridColumns);
        _processorGrid.Children.Add(card);
        _cpuGraphBindings.Add(new CpuGraphBinding(history, value, graph));
    }

    private Control BuildSummaryGrid()
    {
        _summaryValues.Clear();

        var grid = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        var labels = new[]
        {
            "Utilization",
            "Speed",
            "Processes",
            "Threads",
            "Handles",
            "Up time",
            "Base speed",
            "Sockets",
            "Cores",
            "Logical processors",
            "Virtualization",
            "Cache"
        };

        for (var i = 0; i < labels.Length; i++)
        {
            var value = new TextBlock
            {
                Text = "-",
                FontSize = 16,
                Foreground = TextBrush
            };
            _summaryValues[labels[i]] = value;

            var stat = new StackPanel
            {
                Spacing = 1,
                Margin = new Thickness(0, 0, 14, 4),
                Children =
                {
                    new TextBlock
                    {
                        Text = labels[i],
                        FontSize = 11,
                        Foreground = MutedTextBrush
                    },
                    value
                }
            };

            Grid.SetColumn(stat, i % 6);
            Grid.SetRow(stat, i / 6);
            grid.Children.Add(stat);
        }

        return grid;
    }

    private Control BuildMemoryDetailPanel()
    {
        _memorySummaryValues.Clear();

        var panel = new Grid
        {
            Margin = new Thickness(18, 14, 18, 12),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star)
            }
        };

        _memoryTotalText = new TextBlock
        {
            Text = "-",
            FontSize = 18,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                new TextBlock
                {
                    Text = "Memory",
                    FontSize = 28,
                    Foreground = TextBrush
                },
                _memoryTotalText
            }
        };
        Grid.SetColumn(_memoryTotalText, 1);
        panel.Children.Add(header);

        var labelRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(0, 2, 0, 4)
        };
        labelRow.Children.Add(new TextBlock
        {
            Text = "Memory usage over 10 minutes",
            FontSize = 12,
            Foreground = MutedTextBrush
        });
        var topRight = new TextBlock
        {
            Text = "100%",
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(topRight, 1);
        labelRow.Children.Add(topRight);
        Grid.SetRow(labelRow, 1);
        panel.Children.Add(labelRow);

        _memoryGraphValue = new TextBlock
        {
            Text = "-",
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _memoryDetailGraph = new LineGraphControl
        {
            Height = 260,
            MinHeight = 180,
            Maximum = 100,
            Stroke = CpuLineBrush,
            ClipToBounds = true
        };

        var graphCard = new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Padding = new Thickness(5),
            Margin = new Thickness(0, 0, 8, 8),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(1, GridUnitType.Star)
                },
                Children =
                {
                    _memoryGraphValue,
                    _memoryDetailGraph
                }
            }
        };
        Grid.SetRow(_memoryDetailGraph, 1);
        Grid.SetRow(graphCard, 2);
        panel.Children.Add(graphCard);

        var compositionLabel = new TextBlock
        {
            Text = "Memory composition",
            FontSize = 12,
            Foreground = MutedTextBrush,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(compositionLabel, 3);
        panel.Children.Add(compositionLabel);

        var composition = BuildMemoryCompositionBar();
        Grid.SetRow(composition, 4);
        panel.Children.Add(composition);

        var stats = BuildMemorySummaryGrid();
        Grid.SetRow(stats, 5);
        panel.Children.Add(stats);

        return panel;
    }

    private Control BuildMemoryCompositionBar()
    {
        _memoryUsedColumn = new ColumnDefinition(1, GridUnitType.Star);
        _memoryCachedColumn = new ColumnDefinition(1, GridUnitType.Star);
        _memoryAvailableColumn = new ColumnDefinition(1, GridUnitType.Star);

        var bar = new Grid
        {
            Height = 34,
            ColumnDefinitions =
            {
                _memoryUsedColumn,
                _memoryCachedColumn,
                _memoryAvailableColumn
            },
            Children =
            {
                new Border { Background = MemoryUsedBrush },
                new Border { Background = MemoryCachedBrush },
                new Border { Background = Brushes.White }
            }
        };
        Grid.SetColumn(bar.Children[1], 1);
        Grid.SetColumn(bar.Children[2], 2);

        return new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 8),
            Child = bar
        };
    }

    private Control BuildMemorySummaryGrid()
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 2, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        var labels = new[]
        {
            "In use",
            "Available",
            "Committed",
            "Cached",
            "Paged pool",
            "Non-paged pool",
            "Speed",
            "Slots used",
            "Form factor",
            "Hardware reserved"
        };

        for (var i = 0; i < labels.Length; i++)
        {
            var value = new TextBlock
            {
                Text = "-",
                FontSize = 16,
                Foreground = TextBrush
            };
            _memorySummaryValues[labels[i]] = value;

            var stat = new StackPanel
            {
                Spacing = 1,
                Margin = new Thickness(0, 0, 14, 6),
                Children =
                {
                    new TextBlock
                    {
                        Text = labels[i],
                        FontSize = 11,
                        Foreground = MutedTextBrush
                    },
                    value
                }
            };

            Grid.SetColumn(stat, i % 5);
            Grid.SetRow(stat, i / 5);
            grid.Children.Add(stat);
        }

        return grid;
    }

    private Control BuildDiskDetailPanel()
    {
        _diskSummaryValues.Clear();

        var panel = new Grid
        {
            Margin = new Thickness(18, 14, 18, 12),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star)
            }
        };

        _diskTitleModel = new TextBlock
        {
            Text = "-",
            FontSize = 13,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                new TextBlock
                {
                    Text = "Disk 0",
                    FontSize = 28,
                    Foreground = TextBrush
                },
                _diskTitleModel
            }
        };
        Grid.SetColumn(_diskTitleModel, 1);
        panel.Children.Add(header);

        var activeLabel = BuildGraphLabelRow("Active time over 10 minutes", "100%");
        Grid.SetRow(activeLabel, 1);
        panel.Children.Add(activeLabel);

        _diskActiveValue = new TextBlock
        {
            Text = "-",
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _diskActiveGraph = new LineGraphControl
        {
            Height = 210,
            MinHeight = 160,
            Maximum = 100,
            Stroke = CpuLineBrush,
            ClipToBounds = true
        };
        var activeGraphCard = BuildDetailGraphCard(_diskActiveValue, _diskActiveGraph, new Thickness(0, 0, 8, 8));
        Grid.SetRow(activeGraphCard, 2);
        panel.Children.Add(activeGraphCard);

        var transferLabel = BuildGraphLabelRow("Disk transfer rate over 10 minutes", string.Empty);
        Grid.SetRow(transferLabel, 3);
        panel.Children.Add(transferLabel);

        _diskTransferValue = new TextBlock
        {
            Text = "-",
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _diskTransferGraph = new LineGraphControl
        {
            Height = 130,
            MinHeight = 96,
            Maximum = 1024,
            Stroke = CpuLineBrush,
            ClipToBounds = true
        };
        var transferGraphCard = BuildDetailGraphCard(_diskTransferValue, _diskTransferGraph, new Thickness(0, 0, 8, 10));
        Grid.SetRow(transferGraphCard, 4);
        panel.Children.Add(transferGraphCard);

        var stats = BuildDiskSummaryGrid();
        Grid.SetRow(stats, 5);
        panel.Children.Add(stats);

        return panel;
    }

    private static Control BuildGraphLabelRow(string leftText, string rightText)
    {
        var labelRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Margin = new Thickness(0, 2, 0, 4)
        };

        labelRow.Children.Add(new TextBlock
        {
            Text = leftText,
            FontSize = 12,
            Foreground = MutedTextBrush
        });

        var right = new TextBlock
        {
            Text = rightText,
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(right, 1);
        labelRow.Children.Add(right);
        return labelRow;
    }

    private static Control BuildDetailGraphCard(TextBlock value, LineGraphControl graph, Thickness margin)
    {
        Grid.SetRow(graph, 1);
        return new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Padding = new Thickness(5),
            Margin = margin,
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(1, GridUnitType.Star)
                },
                Children =
                {
                    value,
                    graph
                }
            }
        };
    }

    private Control BuildDiskSummaryGrid()
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 2, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        var labels = new[]
        {
            "Active time",
            "Average response time",
            "Read speed",
            "Write speed",
            "Capacity",
            "Formatted",
            "System disk",
            "Page file",
            "Type"
        };

        for (var i = 0; i < labels.Length; i++)
        {
            var value = new TextBlock
            {
                Text = "-",
                FontSize = 16,
                Foreground = TextBrush
            };
            _diskSummaryValues[labels[i]] = value;

            var stat = new StackPanel
            {
                Spacing = 1,
                Margin = new Thickness(0, 0, 14, 6),
                Children =
                {
                    new TextBlock
                    {
                        Text = labels[i],
                        FontSize = 11,
                        Foreground = MutedTextBrush
                    },
                    value
                }
            };

            Grid.SetColumn(stat, i % 5);
            Grid.SetRow(stat, i / 5);
            grid.Children.Add(stat);
        }

        return grid;
    }

    private Control BuildNetworkDetailPanel()
    {
        _networkSummaryValues.Clear();

        var panel = new Grid
        {
            Margin = new Thickness(18, 14, 18, 12),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star)
            }
        };

        _networkHeaderTitle = new TextBlock
        {
            Text = "Network",
            FontSize = 28,
            Foreground = TextBrush
        };
        _networkHeaderDetails = new TextBlock
        {
            Text = "-",
            FontSize = 13,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                _networkHeaderTitle,
                _networkHeaderDetails
            }
        };
        Grid.SetColumn(_networkHeaderDetails, 1);
        panel.Children.Add(header);

        var label = BuildGraphLabelRow("Send + Receive throughput over 10 minutes", string.Empty);
        Grid.SetRow(label, 1);
        panel.Children.Add(label);

        _networkGraphValue = new TextBlock
        {
            Text = "-",
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _networkDetailGraph = new LineGraphControl
        {
            Height = 310,
            MinHeight = 220,
            Maximum = 1024,
            Stroke = NetworkReceiveBrush,
            SecondaryStroke = NetworkSendBrush,
            ClipToBounds = true
        };
        var graphCard = BuildDetailGraphCard(_networkGraphValue, _networkDetailGraph, new Thickness(0, 0, 8, 10));
        Grid.SetRow(graphCard, 2);
        panel.Children.Add(graphCard);

        var stats = BuildNetworkSummaryGrid();
        Grid.SetRow(stats, 3);
        panel.Children.Add(stats);

        return panel;
    }

    private Control BuildNetworkSummaryGrid()
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 2, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        var labels = new[]
        {
            "Send speed",
            "Receive speed",
            "Total sent",
            "Total received",
            "Link speed",
            "IPv4 address",
            "IPv6 address",
            "MAC address",
            "Adapter name",
            "DNS suffix",
            "Connection type"
        };

        for (var i = 0; i < labels.Length; i++)
        {
            var value = new TextBlock
            {
                Text = "-",
                FontSize = 16,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap
            };
            _networkSummaryValues[labels[i]] = value;

            var stat = new StackPanel
            {
                Spacing = 1,
                Margin = new Thickness(0, 0, 14, 6),
                Children =
                {
                    new TextBlock
                    {
                        Text = labels[i],
                        FontSize = 11,
                        Foreground = MutedTextBrush
                    },
                    value
                }
            };

            Grid.SetColumn(stat, i % 5);
            Grid.SetRow(stat, i / 5);
            grid.Children.Add(stat);
        }

        return grid;
    }

    private Control BuildGpuDetailPanel()
    {
        _gpuSummaryValues.Clear();

        var panel = new Grid
        {
            Margin = new Thickness(18, 14, 18, 12),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(1, GridUnitType.Star)
            }
        };

        _gpuHeaderName = new TextBlock
        {
            Text = "-",
            FontSize = 13,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _gpuHeaderTitle = new TextBlock
        {
            Text = "GPU 0",
            FontSize = 28,
            Foreground = TextBrush
        };

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                _gpuHeaderTitle,
                _gpuHeaderName
            }
        };
        Grid.SetColumn(_gpuHeaderName, 1);
        panel.Children.Add(header);

        var usageLabel = BuildGraphLabelRow("3D / utilization over 10 minutes", "100%");
        Grid.SetRow(usageLabel, 1);
        panel.Children.Add(usageLabel);

        _gpuGraphValue = new TextBlock
        {
            Text = "-",
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _gpuDetailGraph = new LineGraphControl
        {
            Height = 210,
            MinHeight = 160,
            Maximum = 100,
            Stroke = CpuLineBrush,
            ClipToBounds = true
        };
        var usageGraphCard = BuildDetailGraphCard(_gpuGraphValue, _gpuDetailGraph, new Thickness(0, 0, 8, 8));
        Grid.SetRow(usageGraphCard, 2);
        panel.Children.Add(usageGraphCard);

        var optionalGraphs = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star)
            }
        };

        _gpuMemoryGraphValue = BuildSmallGraphValue();
        _gpuMemoryGraph = BuildSmallGpuGraph();
        var memoryGraph = BuildMiniDetailGraphCard("VRAM usage", _gpuMemoryGraphValue, _gpuMemoryGraph);
        optionalGraphs.Children.Add(memoryGraph);

        _gpuTemperatureGraphValue = BuildSmallGraphValue();
        _gpuTemperatureGraph = BuildSmallGpuGraph(maximum: 100);
        var temperatureGraph = BuildMiniDetailGraphCard("Temperature", _gpuTemperatureGraphValue, _gpuTemperatureGraph);
        Grid.SetColumn(temperatureGraph, 1);
        optionalGraphs.Children.Add(temperatureGraph);

        _gpuEncoderDecoderGraphValue = BuildSmallGraphValue();
        _gpuEncoderDecoderGraph = BuildSmallGpuGraph();
        _gpuEncoderDecoderGraph.SecondaryStroke = NetworkSendBrush;
        var encoderGraph = BuildMiniDetailGraphCard("Encoder / Decoder", _gpuEncoderDecoderGraphValue, _gpuEncoderDecoderGraph);
        Grid.SetColumn(encoderGraph, 2);
        optionalGraphs.Children.Add(encoderGraph);

        Grid.SetRow(optionalGraphs, 3);
        panel.Children.Add(optionalGraphs);

        _gpuNoteText = new TextBlock
        {
            Text = string.Empty,
            FontSize = 12,
            Foreground = MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 8, 8)
        };
        Grid.SetRow(_gpuNoteText, 4);
        panel.Children.Add(_gpuNoteText);

        var stats = BuildGpuSummaryGrid();
        Grid.SetRow(stats, 5);
        panel.Children.Add(stats);

        return panel;
    }

    private static TextBlock BuildSmallGraphValue()
    {
        return new TextBlock
        {
            Text = "-",
            FontSize = 11,
            Foreground = MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        };
    }

    private static LineGraphControl BuildSmallGpuGraph(double maximum = 100)
    {
        return new LineGraphControl
        {
            Height = 86,
            MinHeight = 72,
            Maximum = maximum,
            Stroke = CpuLineBrush,
            ClipToBounds = true
        };
    }

    private static Control BuildMiniDetailGraphCard(string title, TextBlock value, LineGraphControl graph)
    {
        Grid.SetRow(value, 1);
        Grid.SetRow(graph, 2);
        return new Border
        {
            BorderBrush = ThinBorderBrush,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Padding = new Thickness(5),
            Margin = new Thickness(0, 0, 8, 10),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(1, GridUnitType.Star)
                },
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 11,
                        Foreground = TextBrush
                    },
                    value,
                    graph
                }
            }
        };
    }

    private Control BuildGpuSummaryGrid()
    {
        var grid = new Grid
        {
            Margin = new Thickness(0, 2, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star),
                new ColumnDefinition(1, GridUnitType.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };

        var labels = new[]
        {
            "Utilization",
            "Dedicated GPU memory",
            "Shared GPU memory",
            "Temperature",
            "Driver version",
            "PCI device name",
            "Vendor",
            "Source",
            "Status"
        };

        for (var i = 0; i < labels.Length; i++)
        {
            var value = new TextBlock
            {
                Text = "-",
                FontSize = 16,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap
            };
            _gpuSummaryValues[labels[i]] = value;

            var stat = new StackPanel
            {
                Spacing = 1,
                Margin = new Thickness(0, 0, 14, 6),
                Children =
                {
                    new TextBlock
                    {
                        Text = labels[i],
                        FontSize = 11,
                        Foreground = MutedTextBrush
                    },
                    value
                }
            };

            Grid.SetColumn(stat, i % 4);
            Grid.SetRow(stat, i / 4);
            grid.Children.Add(stat);
        }

        return grid;
    }

    private async Task RefreshMetricsAsync()
    {
        if (Interlocked.Exchange(ref _metricsRefreshRunning, 1) == 1)
        {
            return;
        }

        try
        {
            var metrics = await _refreshService.ReadSystemMetricsAsync(_shutdown.Token);
            ApplyMetrics(metrics);
            Title = "Linux Mint System Monitor";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Title = $"Linux Mint System Monitor - read error: {ex.Message}";
        }
        finally
        {
            Interlocked.Exchange(ref _metricsRefreshRunning, 0);
        }
    }

    private void ApplyMetrics(SystemMetrics metrics)
    {
        _lastMetrics = metrics;
        _cpuHistory.Add(metrics.CpuPercent);
        _ramHistory.Add(metrics.RamPercent);
        _diskActiveHistory.Add(metrics.DiskActivePercent);
        _diskTransferHistory.Add(metrics.DiskReadBytesPerSecond + metrics.DiskWriteBytesPerSecond);
        _diskReadHistory.Add(metrics.DiskReadBytesPerSecond);
        _diskWriteHistory.Add(metrics.DiskWriteBytesPerSecond);
        _networkReceiveHistory.Add(metrics.NetworkReceiveBytesPerSecond);
        _networkTransmitHistory.Add(metrics.NetworkTransmitBytesPerSecond);
        EnsureGpuRows(metrics.Gpus);
        foreach (var gpu in metrics.Gpus)
        {
            var histories = GetGpuHistories(gpu.Index);
            if (gpu.UtilizationPercent is { } gpuUtilization)
            {
                histories.Utilization.Add(gpuUtilization);
            }

            if (gpu.DedicatedMemoryUsedBytes is { } gpuMemoryUsed)
            {
                histories.Memory.Add(gpuMemoryUsed);
            }

            if (gpu.TemperatureCelsius is { } gpuTemperature)
            {
                histories.Temperature.Add(gpuTemperature);
            }

            if (gpu.EncoderUtilizationPercent is { } encoderUtilization)
            {
                histories.Encoder.Add(encoderUtilization);
            }

            if (gpu.DecoderUtilizationPercent is { } decoderUtilization)
            {
                histories.Decoder.Add(decoderUtilization);
            }
        }

        EnsureCpuCoreHistories(metrics.CpuCorePercents.Count);
        for (var i = 0; i < metrics.CpuCorePercents.Count; i++)
        {
            _cpuCoreHistories[i].Add(metrics.CpuCorePercents[i]);
        }

        if (_cpuGraphBindings.Count != _cpuCoreHistories.Count)
        {
            RebuildProcessorGrid();
        }

        UpdatePerformanceList(metrics);
        UpdateActiveDetail(metrics);
        LogDiagnostics();
    }

    private async Task RefreshAllAsync()
    {
        if (WindowState == WindowState.Minimized || _shutdown.IsCancellationRequested)
        {
            return;
        }

        if (_selectedTab == AppTab.Performance)
        {
            await RefreshMetricsAsync();
        }

        if (_selectedTab == AppTab.Processes)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastProcessRefreshAt < TimeSpan.FromSeconds(3))
            {
                return;
            }

            _lastProcessRefreshAt = now;
            await RefreshProcessesAsync();
        }
    }

    private void UpdateActiveDetail(SystemMetrics metrics)
    {
        if (_selectedPage == PerformancePage.Memory)
        {
            UpdateMemoryDetail(metrics);
            return;
        }

        if (_selectedPage == PerformancePage.Disk)
        {
            UpdateDiskDetail(metrics);
            return;
        }

        if (_selectedPage == PerformancePage.Network)
        {
            UpdateNetworkDetail(metrics);
            return;
        }

        if (_selectedPage == PerformancePage.Gpu)
        {
            UpdateGpuDetail(metrics);
            return;
        }

        UpdateProcessorGraphs();
        UpdateCpuSummary(metrics);
    }

    private void EnsureCpuCoreHistories(int count)
    {
        while (_cpuCoreHistories.Count < count)
        {
            _cpuCoreHistories.Add(new MetricHistory());
        }
    }

    private void EnsureGpuRows(IReadOnlyList<GpuDetails> gpus)
    {
        if (_performanceList is null)
        {
            return;
        }

        foreach (var gpu in gpus)
        {
            if (_gpuListBindings.Any(binding => binding.Index == gpu.Index))
            {
                continue;
            }

            var row = BuildPerformanceListItem($"GPU {gpu.Index}", false, out var value, out var graph);
            row.Cursor = new Cursor(StandardCursorType.Hand);
            var index = gpu.Index;
            row.PointerPressed += (_, _) => SelectGpu(index);
            _performanceList.Children.Add(row);
            _gpuListBindings.Add(new GpuListBinding(index, row, value, graph));
        }

        foreach (var stale in _gpuListBindings.Where(binding => gpus.All(gpu => gpu.Index != binding.Index)).ToArray())
        {
            _performanceList.Children.Remove(stale.Row);
            _gpuListBindings.Remove(stale);
        }

        if (gpus.All(gpu => gpu.Index != _selectedGpuIndex))
        {
            _selectedGpuIndex = gpus.FirstOrDefault()?.Index ?? 0;
        }

        UpdateSelection();
    }

    private void SelectGpu(int index)
    {
        _selectedGpuIndex = index;
        _selectedPage = PerformancePage.Gpu;
        UpdateSelection();

        if (_detailHost is not null)
        {
            _detailHost.Content = BuildGpuDetailPanel();
        }

        if (_lastMetrics is not null)
        {
            UpdateGpuDetail(_lastMetrics);
        }
    }

    private GpuHistorySet GetGpuHistories(int index)
    {
        if (!_gpuHistories.TryGetValue(index, out var histories))
        {
            histories = new GpuHistorySet(new MetricHistory(), new MetricHistory(), new MetricHistory(), new MetricHistory(), new MetricHistory());
            _gpuHistories[index] = histories;
        }

        return histories;
    }

    private void UpdatePerformanceList(SystemMetrics metrics)
    {
        SetText(_cpuListValue, $"{metrics.CpuPercent:0}%");
        SetText(_memoryListValue, $"{metrics.RamPercent:0}%  {FormatBytes(metrics.RamUsedBytes)} / {FormatBytes(metrics.RamTotalBytes)}");
        SetText(_diskListValue, $"R {FormatRate(metrics.DiskReadBytesPerSecond)}  W {FormatRate(metrics.DiskWriteBytesPerSecond)}");
        SetText(_networkListValue, $"S {FormatRate(metrics.NetworkTransmitBytesPerSecond)}  R {FormatRate(metrics.NetworkReceiveBytesPerSecond)}");
        SetText(_cpuTitleModel, string.IsNullOrWhiteSpace(metrics.CpuModelName) ? "CPU model unavailable" : metrics.CpuModelName);

        UpdateGraph(_cpuMiniGraph, _cpuHistory, 100);
        UpdateGraph(_memoryMiniGraph, _ramHistory, 100);
        UpdateGraph(_diskMiniGraph, _diskTransferHistory, MaxGraphValue(metrics.DiskReadBytesPerSecond, metrics.DiskWriteBytesPerSecond));
        UpdateNetworkGraph(_networkMiniGraph, MaxGraphValue(metrics.NetworkReceiveBytesPerSecond, metrics.NetworkTransmitBytesPerSecond));
        foreach (var gpu in metrics.Gpus)
        {
            var binding = _gpuListBindings.FirstOrDefault(item => item.Index == gpu.Index);
            if (binding is null)
            {
                continue;
            }

            SetText(binding.Value, $"{gpu.Name} {FormatPercentOrDash(gpu.UtilizationPercent)}");
            UpdateGraph(binding.Graph, GetGpuHistories(gpu.Index).Utilization, 100);
        }
    }

    private void UpdateProcessorGraphs()
    {
        SetText(_totalCpuValue, $"{_cpuHistory.Last:0}%");
        UpdateGraph(_totalCpuGraph, _cpuHistory, 100);

        foreach (var binding in _cpuGraphBindings)
        {
            var last = binding.History.Last;
            binding.Value.Text = $"{last:0}%";
            binding.Graph.Values = binding.History;
            binding.Graph.Maximum = 100;
            binding.Graph.InvalidateVisual();
        }
    }

    private void UpdateCpuSummary(SystemMetrics metrics)
    {
        var details = metrics.CpuDetails;
        SetSummary("Utilization", $"{metrics.CpuPercent:0}%");
        SetSummary("Speed", FormatFrequency(details.CurrentMhz));
        SetSummary("Processes", details.Processes.ToString("N0"));
        SetSummary("Threads", details.Threads.ToString("N0"));
        SetSummary("Handles", details.Handles.ToString("N0"));
        SetSummary("Up time", FormatUptime(details.UpTime));
        SetSummary("Base speed", FormatFrequency(details.MaxMhz));
        SetSummary("Sockets", details.Sockets.ToString());
        SetSummary("Cores", details.Cores.ToString());
        SetSummary("Logical processors", details.LogicalProcessors.ToString());
        SetSummary("Virtualization", details.Virtualization);
        SetSummary("Cache", FormatCaches(details));
    }

    private void UpdateMemoryDetail(SystemMetrics metrics)
    {
        var details = metrics.MemoryDetails;
        SetText(_memoryTotalText, FormatBytes(details.TotalBytes));
        SetText(_memoryGraphValue, $"{metrics.RamPercent:0}%  {FormatBytes(details.UsedBytes)} / {FormatBytes(details.TotalBytes)}");
        UpdateGraph(_memoryDetailGraph, _ramHistory, 100);
        UpdateMemoryComposition(details);

        SetMemorySummary("In use", FormatBytes(details.UsedBytes));
        SetMemorySummary("Available", FormatBytes(details.AvailableBytes));
        SetMemorySummary("Committed", $"{FormatBytes(details.CommittedBytes)} / {FormatBytes(details.CommitLimitBytes)}");
        SetMemorySummary("Cached", FormatBytes(details.CachedBytes));
        SetMemorySummary("Paged pool", details.PagedPoolBytes is null ? "Not available" : FormatBytes(details.PagedPoolBytes.Value));
        SetMemorySummary("Non-paged pool", details.NonPagedPoolBytes is null ? "Not available" : FormatBytes(details.NonPagedPoolBytes.Value));
        SetMemorySummary("Speed", details.Speed);
        SetMemorySummary("Slots used", details.SlotsUsed);
        SetMemorySummary("Form factor", details.FormFactor);
        SetMemorySummary("Hardware reserved", details.HardwareReserved);
    }

    private void UpdateDiskDetail(SystemMetrics metrics)
    {
        var details = metrics.DiskDetails;
        var transferRate = metrics.DiskReadBytesPerSecond + metrics.DiskWriteBytesPerSecond;

        SetText(_diskTitleModel, string.IsNullOrWhiteSpace(details.ModelName) ? "Disk model unavailable" : details.ModelName);
        SetText(_diskActiveValue, $"{metrics.DiskActivePercent:0}%");
        SetText(_diskTransferValue, $"{FormatRate(transferRate)}");
        UpdateGraph(_diskActiveGraph, _diskActiveHistory, 100);
        UpdateGraph(_diskTransferGraph, _diskTransferHistory, MaxGraphValue(transferRate));

        SetDiskSummary("Active time", $"{metrics.DiskActivePercent:0}%");
        SetDiskSummary("Average response time", $"{metrics.DiskAverageResponseMilliseconds:0.0} ms");
        SetDiskSummary("Read speed", FormatRate(metrics.DiskReadBytesPerSecond));
        SetDiskSummary("Write speed", FormatRate(metrics.DiskWriteBytesPerSecond));
        SetDiskSummary("Capacity", FormatBytes(details.CapacityBytes));
        SetDiskSummary("Formatted", FormatBytes(details.FormattedBytes));
        SetDiskSummary("System disk", FormatOptionalBoolean(details.IsSystemDisk));
        SetDiskSummary("Page file", FormatOptionalBoolean(details.HasPageFile));
        SetDiskSummary("Type", string.IsNullOrWhiteSpace(details.Type) ? "Unknown" : details.Type);
    }

    private void UpdateNetworkDetail(SystemMetrics metrics)
    {
        var details = metrics.NetworkDetails;
        var maximum = MaxGraphValue(metrics.NetworkReceiveBytesPerSecond, metrics.NetworkTransmitBytesPerSecond);

        SetText(_networkHeaderTitle, string.IsNullOrWhiteSpace(details.HeaderName) ? details.InterfaceName : details.HeaderName);
        SetText(_networkHeaderDetails, BuildNetworkHeaderDetails(details));
        SetText(_networkGraphValue, $"Receive {FormatRate(metrics.NetworkReceiveBytesPerSecond)}  Send {FormatRate(metrics.NetworkTransmitBytesPerSecond)}");
        UpdateNetworkGraph(_networkDetailGraph, maximum);

        SetNetworkSummary("Send speed", FormatRate(metrics.NetworkTransmitBytesPerSecond));
        SetNetworkSummary("Receive speed", FormatRate(metrics.NetworkReceiveBytesPerSecond));
        SetNetworkSummary("Total sent", FormatBytes(metrics.NetworkTotalTransmitBytes));
        SetNetworkSummary("Total received", FormatBytes(metrics.NetworkTotalReceiveBytes));
        SetNetworkSummary("Link speed", FormatLinkSpeed(details.LinkSpeedBitsPerSecond));
        SetNetworkSummary("IPv4 address", details.IPv4Address);
        SetNetworkSummary("IPv6 address", details.IPv6Address);
        SetNetworkSummary("MAC address", details.MacAddress);
        SetNetworkSummary("Adapter name", details.AdapterName);
        SetNetworkSummary("DNS suffix", details.DnsSuffix);
        SetNetworkSummary("Connection type", details.ConnectionType);
    }

    private void UpdateGpuDetail(SystemMetrics metrics)
    {
        var details = metrics.Gpus.FirstOrDefault(gpu => gpu.Index == _selectedGpuIndex)
            ?? metrics.Gpus.FirstOrDefault()
            ?? GpuMetricsReader.BuildUnknown(0);
        _selectedGpuIndex = details.Index;
        var histories = GetGpuHistories(details.Index);
        SetText(_gpuHeaderTitle, $"GPU {details.Index}");
        SetText(_gpuHeaderName, details.Name);
        SetText(_gpuGraphValue, details.UtilizationPercent is null
            ? "Utilization not available"
            : $"{details.UtilizationPercent.Value:0}%");
        UpdateGraph(_gpuDetailGraph, histories.Utilization, 100);

        var memoryMaximum = details.DedicatedMemoryTotalBytes is { } totalBytes && totalBytes > 0
            ? totalBytes
            : histories.Memory.MaxValue();
        SetText(_gpuMemoryGraphValue, FormatDedicatedGpuMemory(details));
        UpdateGraph(_gpuMemoryGraph, histories.Memory, Math.Max(1, memoryMaximum));

        SetText(_gpuTemperatureGraphValue, details.TemperatureCelsius is null ? "-" : $"{details.TemperatureCelsius.Value:0} C");
        UpdateGraph(_gpuTemperatureGraph, histories.Temperature, 100);

        SetText(_gpuEncoderDecoderGraphValue, $"Enc {FormatPercentOrDash(details.EncoderUtilizationPercent)}  Dec {FormatPercentOrDash(details.DecoderUtilizationPercent)}");
        UpdateDualGraph(_gpuEncoderDecoderGraph, histories.Encoder, histories.Decoder, 100);
        SetText(_gpuNoteText, details.Note);

        SetGpuSummary("Utilization", details.UtilizationPercent is null ? "Utilization not available" : $"{details.UtilizationPercent.Value:0}%");
        SetGpuSummary("Dedicated GPU memory", FormatDedicatedGpuMemory(details));
        SetGpuSummary("Shared GPU memory", details.SharedMemoryBytes is null ? "Not available" : FormatBytes(details.SharedMemoryBytes.Value));
        SetGpuSummary("Temperature", details.TemperatureCelsius is null ? "Not available" : $"{details.TemperatureCelsius.Value:0} C");
        SetGpuSummary("Driver version", details.DriverVersion);
        SetGpuSummary("PCI device name", details.PciDeviceName);
        SetGpuSummary("Vendor", details.Vendor);
        SetGpuSummary("Source", details.Source);
        SetGpuSummary("Status", details.Status);
    }

    private void UpdateMemoryComposition(MemoryDetails details)
    {
        if (_memoryUsedColumn is null || _memoryCachedColumn is null || _memoryAvailableColumn is null)
        {
            return;
        }

        var used = Math.Max(1d, details.UsedBytes);
        var cached = Math.Max(1d, Math.Min(details.CachedBytes, details.AvailableBytes));
        var available = Math.Max(1d, details.AvailableBytes > details.CachedBytes ? details.AvailableBytes - details.CachedBytes : details.AvailableBytes);

        _memoryUsedColumn.Width = new GridLength(used, GridUnitType.Star);
        _memoryCachedColumn.Width = new GridLength(cached, GridUnitType.Star);
        _memoryAvailableColumn.Width = new GridLength(available, GridUnitType.Star);
    }

    private int DetermineProcessorColumns(int graphCount)
    {
        var detailWidth = Math.Max(0, Bounds.Width - 245 - 36);
        if (graphCount <= 4)
        {
            return 2;
        }

        if (graphCount <= 8)
        {
            return detailWidth >= 560 ? 4 : 2;
        }

        if (graphCount <= 16)
        {
            if (detailWidth >= 620)
            {
                return 4;
            }

            return detailWidth >= 460 ? 3 : 2;
        }

        return detailWidth >= 980 ? 6 : 4;
    }

    private static void UpdateGraph(LineGraphControl? graph, MetricHistory history, double maximum)
    {
        if (graph is null)
        {
            return;
        }

        graph.Values = history;
        graph.Maximum = maximum;
        graph.InvalidateVisual();
    }

    private void UpdateNetworkGraph(LineGraphControl? graph, double maximum)
    {
        if (graph is null)
        {
            return;
        }

        graph.Values = _networkReceiveHistory;
        graph.SecondaryValues = _networkTransmitHistory;
        graph.Maximum = maximum;
        graph.InvalidateVisual();
    }

    private static void UpdateDualGraph(LineGraphControl? graph, MetricHistory primary, MetricHistory secondary, double maximum)
    {
        if (graph is null)
        {
            return;
        }

        graph.Values = primary;
        graph.SecondaryValues = secondary;
        graph.Maximum = maximum;
        graph.InvalidateVisual();
    }

    private void LogDiagnostics()
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastDiagnosticsAt;
        if (elapsed < TimeSpan.FromSeconds(30))
        {
            return;
        }

        var heapBytes = GC.GetTotalMemory(forceFullCollection: false);
        var allocatedBytes = GC.GetTotalAllocatedBytes();
        var allocationRate = elapsed.TotalSeconds <= 0
            ? 0d
            : (allocatedBytes - _lastAllocatedBytes) / elapsed.TotalSeconds;

        _lastAllocatedBytes = allocatedBytes;
        _lastDiagnosticsAt = now;

        Debug.WriteLine(
            $"[MemoryDiagnostics] heap={FormatBytes(heapBytes)} allocRate={FormatRate(allocationRate)} histories={GetHistorySampleCount()}/{GetHistoryCapacity()} processRows={_processRows.Count} renderedRows={_processDisplayRows.Count}");
    }

    private int GetHistorySampleCount()
    {
        var count = _cpuHistory.Count
            + _ramHistory.Count
            + _diskActiveHistory.Count
            + _diskTransferHistory.Count
            + _diskReadHistory.Count
            + _diskWriteHistory.Count
            + _networkReceiveHistory.Count
            + _networkTransmitHistory.Count;

        foreach (var history in _cpuCoreHistories)
        {
            count += history.Count;
        }

        foreach (var histories in _gpuHistories.Values)
        {
            count += histories.Utilization.Count
                + histories.Memory.Count
                + histories.Temperature.Count
                + histories.Encoder.Count
                + histories.Decoder.Count;
        }

        return count;
    }

    private int GetHistoryCapacity()
    {
        var seriesCount = 8 + _cpuCoreHistories.Count + _gpuHistories.Count * 5;
        return seriesCount * MetricHistory.Capacity;
    }

    private void SetSummary(string name, string value)
    {
        if (_summaryValues.TryGetValue(name, out var text))
        {
            text.Text = value;
        }
    }

    private void SetMemorySummary(string name, string value)
    {
        if (_memorySummaryValues.TryGetValue(name, out var text))
        {
            text.Text = value;
        }
    }

    private void SetDiskSummary(string name, string value)
    {
        if (_diskSummaryValues.TryGetValue(name, out var text))
        {
            text.Text = value;
        }
    }

    private void SetNetworkSummary(string name, string value)
    {
        if (_networkSummaryValues.TryGetValue(name, out var text))
        {
            text.Text = value;
        }
    }

    private void SetGpuSummary(string name, string value)
    {
        if (_gpuSummaryValues.TryGetValue(name, out var text))
        {
            text.Text = value;
        }
    }

    private static void SetText(TextBlock? textBlock, string text)
    {
        if (textBlock is not null)
        {
            textBlock.Text = text;
        }
    }

    private static TextBlock MenuText(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = TextBrush
        };
    }

    private static double MaxGraphValue(params double[] values)
    {
        var max = values.Max();
        if (max < 1024)
        {
            return 1024;
        }

        var power = Math.Pow(1024, Math.Floor(Math.Log(max, 1024)));
        return Math.Ceiling(max / power) * power;
    }

    private static string FormatRate(double bytesPerSecond)
    {
        return $"{FormatBytes(bytesPerSecond)}/s";
    }

    private static string FormatPercentOrDash(double? value)
    {
        return value is null ? "-" : $"{value.Value:0}%";
    }

    private static string FormatDedicatedGpuMemory(GpuDetails details)
    {
        if (details.DedicatedMemoryUsedBytes is null && details.DedicatedMemoryTotalBytes is null)
        {
            return "Not available";
        }

        var used = details.DedicatedMemoryUsedBytes is null ? "-" : FormatBytes(details.DedicatedMemoryUsedBytes.Value);
        var total = details.DedicatedMemoryTotalBytes is null ? "-" : FormatBytes(details.DedicatedMemoryTotalBytes.Value);
        return $"{used} / {total}";
    }

    private static string FormatLinkSpeed(ulong? bitsPerSecond)
    {
        if (bitsPerSecond is null || bitsPerSecond.Value == 0)
        {
            return "Not available";
        }

        string[] units = ["bps", "Kbps", "Mbps", "Gbps", "Tbps"];
        var value = bitsPerSecond.Value;
        var unit = 0;
        var scaled = (double)value;

        while (scaled >= 1000 && unit < units.Length - 1)
        {
            scaled /= 1000;
            unit++;
        }

        return unit == 0 ? $"{scaled:0} {units[unit]}" : $"{scaled:0.0} {units[unit]}";
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    private static string FormatFrequency(double? mhz)
    {
        if (mhz is null)
        {
            return "-";
        }

        return mhz >= 1000 ? $"{mhz.Value / 1000d:0.00} GHz" : $"{mhz.Value:0} MHz";
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        return $"{(int)uptime.TotalDays}:{uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
    }

    private static string FormatCaches(CpuDetails details)
    {
        var caches = new[]
        {
            string.IsNullOrWhiteSpace(details.L1Cache) ? null : $"L1 {details.L1Cache}",
            string.IsNullOrWhiteSpace(details.L2Cache) ? null : $"L2 {details.L2Cache}",
            string.IsNullOrWhiteSpace(details.L3Cache) ? null : $"L3 {details.L3Cache}"
        }.Where(static value => value is not null);

        return string.Join("  ", caches) is { Length: > 0 } value ? value : "-";
    }

    private static string FormatOptionalBoolean(bool? value)
    {
        return value switch
        {
            true => "Yes",
            false => "No",
            _ => "Unknown"
        };
    }

    private static string BuildNetworkHeaderDetails(NetworkDetails details)
    {
        var parts = new[]
        {
            FormatLinkSpeed(details.LinkSpeedBitsPerSecond),
            string.IsNullOrWhiteSpace(details.Description) ? null : details.Description
        }.Where(static value => !string.IsNullOrWhiteSpace(value) && value != "Not available");

        return string.Join("  ", parts) is { Length: > 0 } value ? value : "-";
    }

    private sealed record CpuGraphBinding(MetricHistory History, TextBlock Value, LineGraphControl Graph);

    private sealed record GpuListBinding(int Index, Border Row, TextBlock Value, LineGraphControl Graph);

    private sealed record GpuHistorySet(
        MetricHistory Utilization,
        MetricHistory Memory,
        MetricHistory Temperature,
        MetricHistory Encoder,
        MetricHistory Decoder);

    private sealed record ProcessDisplayRow(
        ProcessDisplayKind Kind,
        string Title,
        bool IsSystemHeader,
        ProcessRow? Process,
        ProcessAppGroup? AppGroup,
        bool Alternate,
        bool Indent)
    {
        public static ProcessDisplayRow ForHeader(string title, bool IsSystemHeader)
        {
            return new ProcessDisplayRow(ProcessDisplayKind.Header, title, IsSystemHeader, null, null, false, false);
        }

        public static ProcessDisplayRow ForProcess(ProcessRow process, bool Alternate, bool Indent)
        {
            return new ProcessDisplayRow(ProcessDisplayKind.Process, string.Empty, false, process, null, Alternate, Indent);
        }

        public static ProcessDisplayRow ForAppGroup(ProcessAppGroup appGroup, bool Alternate)
        {
            return new ProcessDisplayRow(ProcessDisplayKind.AppGroup, string.Empty, false, null, appGroup, Alternate, false);
        }
    }

    private enum ProcessDisplayKind
    {
        Header,
        Process,
        AppGroup
    }

    private sealed record ProcessAppGroup(
        string Key,
        string Name,
        IReadOnlyList<ProcessRow> Children,
        int PrimaryPid,
        double CpuPercent,
        ulong ResidentBytes,
        double DiskReadBytesPerSecond,
        double DiskWriteBytesPerSecond,
        string NetworkIo,
        string User,
        string Status,
        bool CanEndTask)
    {
        public int ProcessCount => Children.Count;

        public static ProcessAppGroup From(string key, IEnumerable<ProcessRow> rows)
        {
            var children = rows.ToArray();
            var first = children.FirstOrDefault();
            var name = first?.AppGroupName ?? first?.Name ?? "App";
            var users = children.Select(static row => row.User).Distinct(StringComparer.Ordinal).ToArray();
            var statuses = children.Select(static row => row.Status).Distinct(StringComparer.Ordinal).ToArray();
            return new ProcessAppGroup(
                key,
                name,
                children,
                children.Select(static row => row.Pid).DefaultIfEmpty(0).Min(),
                children.Sum(static row => row.CpuPercent),
                (ulong)children.Sum(static row => (decimal)row.ResidentBytes),
                children.Sum(static row => row.DiskReadBytesPerSecond),
                children.Sum(static row => row.DiskWriteBytesPerSecond),
                "-",
                users.Length == 1 ? users[0] : "Multiple",
                statuses.Length == 1 ? statuses[0] : "Multiple",
                children.Any(static row => row.CanEndTask));
        }

        public string? IconPath => Children.FirstOrDefault(static row => !string.IsNullOrWhiteSpace(row.IconPath))?.IconPath;
    }

    private enum AppTab
    {
        Processes,
        Performance,
        Startup,
        Details
    }

    private enum PerformancePage
    {
        Cpu,
        Memory,
        Disk,
        Network,
        Gpu
    }

    private enum ProcessColumn
    {
        Name,
        Pid,
        Cpu,
        Memory,
        Disk,
        Network,
        User,
        Status
    }

    private enum ProcessViewMode
    {
        Grouped,
        Apps,
        Background,
        System,
        All
    }
}
