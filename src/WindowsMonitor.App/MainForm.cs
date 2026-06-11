using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;
using WindowsMonitor.Infrastructure;
using WindowsMonitor.Infrastructure.Capture;
using WindowsMonitor.Infrastructure.Licensing;
using WindowsMonitor.Infrastructure.Ocr;
using WindowsMonitor.Infrastructure.Persistence;
using WindowsMonitor.Infrastructure.Taskbar;
using WindowsMonitor.Infrastructure.Updates;
using WindowsMonitor.Infrastructure.Win32;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AntButton = AntdUI.Button;
using AntInput = AntdUI.Input;
using AntPanel = AntdUI.Panel;
using AntLabel = AntdUI.Label;
using AntMenu = AntdUI.Menu;
using AntMenuItem = AntdUI.MenuItem;
using AntTable = AntdUI.Table;
using AntColumn = AntdUI.Column;
using AntCellButton = AntdUI.CellButton;

namespace WindowsMonitor.App;

public sealed class MainForm : Form
{
    private readonly IMonitorRepository _repository = new SqliteMonitorRepository(AppPaths.DatabasePath);
    private readonly IWindowInventoryService _windowInventory = new WindowInventoryService();
    private readonly IMachineCodeService _machineCodeService = new MachineCodeService();
    private readonly ICaptureService _captureService = new DesktopCaptureService();
    private readonly IOcrEngine _ocrEngine = new WindowsOcrEngine();
    private readonly HttpClient _httpClient = new();
    private readonly RuleMatcher _ruleMatcher = new();
    private readonly EventCooldown _cooldown = new();
    private readonly System.Windows.Forms.Timer _monitorTimer;
    private readonly System.Windows.Forms.Timer _pulseTimer;
    private readonly NotifyIcon _notifyIcon;
    private readonly ILicenseService _licenseService;
    private readonly GitHubReleaseUpdateService _updateService;
    private readonly ITaskbarFlashDetector _taskbarFlashDetector = new WinEventTaskbarFlashDetector();
    private readonly System.Windows.Forms.Timer _licenseTimer;

    private readonly AntPanel _content = new() { Dock = DockStyle.Fill, Back = Color.FromArgb(247, 249, 252), Padding = new Padding(16), BorderWidth = 0 };
    private readonly AntLabel _title = new() { Dock = DockStyle.Top, Height = 58, Font = new Font("Segoe UI", 18, FontStyle.Bold), Padding = new Padding(20, 10, 0, 0), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(17, 24, 39), BackColor = Color.FromArgb(247, 249, 252) };
    private readonly ToolStripStatusLabel _statusText = new("就绪");

    private AntTable? _rulesGrid;
    private AntTable? _eventsGrid;
    private DataGridView? _windowsGrid;
    private DataGridView? _flashGrid;
    private AntInput? _ocrOutput;
    private AntInput? _updateOutput;
    private AntInput? _licenseOutput;
    private AntInput? _licenseCodeInput;
    private AntButton? _monitorToggleButton;

    private bool _initialized;
    private bool _monitoringEnabled = true;
    private bool _pulseOn;
    private DateTimeOffset _lastOcrScan = DateTimeOffset.MinValue;
    private IReadOnlyList<MonitorRule> _rules = [];
    private IReadOnlyList<RuleRow> _ruleRows = [];
    private Guid? _selectedRuleId;
    private readonly Dictionary<Guid, HitState> _hitStates = [];
    private LicenseValidationResult? _licenseValidation;
    private string _machineCode = string.Empty;

    public MainForm()
    {
        _licenseService = new OfflineLicenseService(_repository, _httpClient);
        _updateService = new GitHubReleaseUpdateService(_httpClient);

        Text = BuildMetadata.DisplayName;
        MinimumSize = new Size(1200, 780);
        Size = new Size(1360, 860);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        LoadBrandIcon();

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = BuildMetadata.DisplayName,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        _monitorTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _monitorTimer.Tick += async (_, _) => await MonitorTickAsync();
        _pulseTimer = new System.Windows.Forms.Timer { Interval = 700 };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulseOn = !_pulseOn;
            RefreshMonitorToggle();
        };
        _pulseTimer.Start();
        _licenseTimer = new System.Windows.Forms.Timer { Interval = 60 * 60 * 1000 };
        _licenseTimer.Tick += async (_, _) => await ValidateLicenseAsync(forceRemoteCheck: true);
        _taskbarFlashDetector.FlashDetected += OnTaskbarFlashDetected;

        BuildLayout();
        FormClosing += OnFormClosing;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await _repository.InitializeAsync();
        _machineCode = _machineCodeService.GetMachineCode();
        await ValidateLicenseAsync(forceRemoteCheck: true);
        await LoadRulesAsync();
        if (IsLicenseUsable)
        {
            StartConfiguredTaskbarRules();
            await ShowDashboardAsync();
        }
        else
        {
            _monitoringEnabled = false;
            _taskbarFlashDetector.Stop();
            await ShowLicenseAsync();
        }

        _monitorTimer.Start();
        _licenseTimer.Start();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var nav = new AntPanel
        {
            Dock = DockStyle.Fill,
            Back = Color.FromArgb(17, 24, 39),
            BackColor = Color.FromArgb(17, 24, 39),
            BorderWidth = 0,
            Padding = new Padding(12),
            Radius = 0
        };
        var brand = new AntButton
        {
            Text = BuildMetadata.DisplayName,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(17, 24, 39),
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(6, 8, 0, 0),
            Radius = 8,
            Type = AntdUI.TTypeMini.Default
        };
        brand.Click += async (_, _) =>
        {
            if (!IsLicenseUsable)
            {
                await ShowLicenseAsync();
                SetStatus("需要有效授权，监听功能已锁定。");
                return;
            }

            await ShowDashboardAsync();
        };
        nav.Controls.Add(brand);

        var menu = new AntMenu
        {
            Dock = DockStyle.Fill,
            ColorScheme = AntdUI.TAMode.Dark,
            BackColor = Color.FromArgb(17, 24, 39),
            ForeColor = Color.FromArgb(229, 231, 235),
            BackHover = Color.FromArgb(31, 41, 55),
            BackActive = Color.FromArgb(22, 119, 255),
            ForeActive = Color.White,
            Radius = 8,
            Unique = true
        };
        menu.Items.Add(new AntMenuItem("控制台") { ID = "Dashboard", Select = true });
        menu.Items.Add(new AntMenuItem("规则") { ID = "Rules" });
        menu.Items.Add(new AntMenuItem("事件") { ID = "Events" });
        menu.Items.Add(new AntMenuItem("授权") { ID = "License" });
        menu.Items.Add(new AntMenuItem("更新") { ID = "Updates" });
        menu.Items.Add(new AntMenuItem("关于") { ID = "About" });
        menu.ItemClick += async (_, e) =>
        {
            if (!IsLicenseUsable && e.Item.ID is not ("License" or "About"))
            {
                await ShowLicenseAsync();
                SetStatus("需要有效授权，监听功能已锁定。");
                return;
            }

            switch (e.Item.ID)
            {
                case "Dashboard":
                    await ShowDashboardAsync();
                    break;
                case "Rules":
                    await ShowRulesAsync();
                    break;
                case "Events":
                    await ShowEventsAsync();
                    break;
                case "License":
                    await ShowLicenseAsync();
                    break;
                case "Updates":
                    await ShowUpdatesAsync();
                    break;
                case "About":
                    ShowAbout();
                    break;
            }
        };
        nav.Controls.Add(menu);

        var main = new Panel { Dock = DockStyle.Fill };
        var status = new StatusStrip();
        status.Items.Add(_statusText);
        main.Controls.Add(_content);
        main.Controls.Add(_title);
        main.Controls.Add(status);

        root.Controls.Add(nav, 0, 0);
        root.Controls.Add(main, 1, 0);
        Controls.Add(root);
    }

    private static void AddNav(Control parent, string text, Action handler)
    {
        var button = new AntButton
        {
            Text = text,
            Width = 190,
            Height = 38,
            Margin = new Padding(0, 3, 0, 3),
            BackColor = Color.FromArgb(55, 65, 81),
            BackHover = Color.FromArgb(75, 85, 99),
            BackActive = Color.FromArgb(22, 119, 255),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Cursor = Cursors.Hand,
            Radius = 6,
            BorderWidth = 0
        };
        button.Click += (_, _) => handler();
        parent.Controls.Add(button);
    }

    private void SetPage(string pageTitle)
    {
        _title.Text = pageTitle;
        _monitorToggleButton = null;
        _content.Controls.Clear();
    }

    private bool IsLicenseUsable => _licenseValidation?.IsUsable == true;

    private async Task ValidateLicenseAsync(bool forceRemoteCheck)
    {
        if (string.IsNullOrWhiteSpace(_machineCode))
        {
            _machineCode = _machineCodeService.GetMachineCode();
        }

        _licenseValidation = await _licenseService.ValidateAsync(_machineCode, forceRemoteCheck);
        if (!IsLicenseUsable)
        {
            _monitoringEnabled = false;
            _taskbarFlashDetector.Stop();
            if (!string.Equals(_title.Text, "授权管理", StringComparison.OrdinalIgnoreCase))
            {
                await ShowLicenseAsync();
            }
        }

        SetStatus(_licenseValidation.Message);
        if (_licenseOutput is not null)
        {
            _licenseOutput.Text = LicenseText(_machineCode, _licenseValidation);
        }
    }

    private void LoadBrandIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (!File.Exists(iconPath))
        {
            return;
        }

        try
        {
            Icon = new Icon(iconPath);
        }
        catch
        {
            Icon = SystemIcons.Application;
        }
    }

    private async Task ShowDashboardAsync()
    {
        SetPage("控制台");
        var events = await _repository.GetRecentEventsAsync(20);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 116,
            BackColor = Color.White,
            Padding = new Padding(8, 4, 0, 8)
        };
        _monitorToggleButton = new AntButton
        {
            Width = 96,
            Height = 96,
            Radius = 48,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Margin = new Padding(0, 0, 16, 0)
        };
        _monitorToggleButton.Click += (_, _) =>
        {
            _monitoringEnabled = !_monitoringEnabled;
            RefreshMonitorToggle();
        };
        RefreshMonitorToggle();
        top.Controls.Add(_monitorToggleButton);

        var enabledRuleCount = _rules.Count(rule => rule.Enabled);
        var metrics = new TableLayoutPanel { Dock = DockStyle.Top, Height = 110, ColumnCount = 3, Padding = new Padding(0, 0, 0, 12) };
        for (var i = 0; i < 3; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        metrics.Controls.Add(Metric("开启规则", enabledRuleCount.ToString()), 0, 0);
        metrics.Controls.Add(Metric("规则数量", _rules.Count.ToString()), 1, 0);
        metrics.Controls.Add(Metric("最近事件", events.Count.ToString()), 2, 0);

        _eventsGrid = EventTable();
        _eventsGrid.Dock = DockStyle.Fill;
        FillEventsGrid(events);

        _content.Controls.Add(Card("最近事件", _eventsGrid));
        _content.Controls.Add(metrics);
        _content.Controls.Add(top);
    }

    private async Task ShowRulesAsync()
    {
        SetPage("规则");
        await LoadRulesAsync();

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, BackColor = Color.White, Padding = new Padding(4, 2, 0, 0) };
        var add = Button("新增规则", 95);
        var edit = Button("编辑", 80);
        var delete = Button("删除", 80);
        add.Click += async (_, _) => await EditRuleAsync(null);
        edit.Click += async (_, _) => await EditSelectedRuleAsync();
        delete.Click += async (_, _) => await DeleteSelectedRuleAsync();
        actions.Controls.Add(add);
        actions.Controls.Add(edit);
        actions.Controls.Add(delete);

        _rulesGrid = RulesTable();
        _rulesGrid.Dock = DockStyle.Fill;
        _rulesGrid.ContextMenuStrip = BuildRuleContextMenu();
        _rulesGrid.CellClick += (_, e) =>
        {
            if (e.Record is RuleRow row)
            {
                SelectRule(row.Rule);
            }
        };
        _rulesGrid.CellDoubleClick += async (_, e) =>
        {
            if (e.Record is RuleRow row)
            {
                SelectRule(row.Rule);
                await EditRuleAsync(row.Rule);
            }
        };
        _rulesGrid.CellButtonClick += async (_, e) =>
        {
            if (e.Record is RuleRow row)
            {
                SelectRule(row.Rule);
                await ToggleRuleAsync(row.Rule);
            }
        };
        FillRulesGrid();

        var card = Card("监听规则", _rulesGrid);
        card.Controls.Add(actions);
        _content.Controls.Add(card);
    }

    private void ShowWindows()
    {
        SetPage("窗口列表");
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, BackColor = Color.White };
        var refresh = Button("刷新", 90);
        refresh.Click += (_, _) => FillWindowsGrid();
        actions.Controls.Add(refresh);

        _windowsGrid = Grid(["进程", "标题", "句柄", "位置"]);
        _windowsGrid.Dock = DockStyle.Fill;
        FillWindowsGrid();

        var card = Card("可见顶层窗口", _windowsGrid);
        card.Controls.Add(actions);
        _content.Controls.Add(card);
    }

    private void ShowOcr()
    {
        SetPage("文字识别");
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, BackColor = Color.White };
        var scan = Button("识别桌面", 110);
        var copy = Button("复制文本", 90);
        _ocrOutput = new AntInput
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Font = new Font("Consolas", 10F),
            WordWrap = false,
            Radius = 6
        };
        scan.Click += async (_, _) => await RunOcrScanAsync(force: true, showResult: true);
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_ocrOutput.Text))
            {
                Clipboard.SetText(_ocrOutput.Text);
            }
        };
        actions.Controls.Add(scan);
        actions.Controls.Add(copy);
        var card = Card("桌面识别结果", _ocrOutput);
        card.Controls.Add(actions);
        _content.Controls.Add(card);
    }

    private void ShowTaskbarFlash()
    {
        SetPage("任务栏闪烁");
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, BackColor = Color.White };
        var refresh = Button("刷新", 90);
        var start = Button("监听所选", 110);
        var stop = Button("停止监听", 110);

        _flashGrid = Grid(["选择", "进程", "标题", "句柄"]);
        _flashGrid.ReadOnly = false;
        _flashGrid.Columns[0].ReadOnly = false;
        for (var i = 1; i < _flashGrid.Columns.Count; i++)
        {
            _flashGrid.Columns[i].ReadOnly = true;
        }
        _flashGrid.Dock = DockStyle.Fill;
        FillFlashGrid();

        refresh.Click += (_, _) => FillFlashGrid();
        start.Click += async (_, _) => await SaveSelectedFlashRulesAsync();
        stop.Click += (_, _) =>
        {
            _taskbarFlashDetector.Stop();
            SetStatus("任务栏闪烁监听已停止。");
        };
        actions.Controls.Add(refresh);
        actions.Controls.Add(start);
        actions.Controls.Add(stop);

        var card = Card("选择运行中的软件", _flashGrid);
        card.Controls.Add(actions);
        _content.Controls.Add(card);
    }

    private async Task ShowEventsAsync()
    {
        SetPage("事件");
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, BackColor = Color.White };
        var refresh = Button("刷新", 90);
        var clear = Button("清空事件", 110);
        refresh.Click += async (_, _) => FillEventsGrid(await _repository.GetRecentEventsAsync(200), includeStatus: true);
        clear.Click += async (_, _) =>
        {
            var confirm = MessageBox.Show(this, "确定清空所有事件日志吗？", "清空事件", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            await _repository.ClearEventsAsync();
            FillEventsGrid([]);
            SetStatus("事件已清空。");
        };
        actions.Controls.Add(refresh);
        actions.Controls.Add(clear);

        _eventsGrid = EventTable(includeStatus: true);
        _eventsGrid.Dock = DockStyle.Fill;
        FillEventsGrid(await _repository.GetRecentEventsAsync(200), includeStatus: true);
        var card = Card("事件日志", _eventsGrid);
        card.Controls.Add(actions);
        _content.Controls.Add(card);
    }

    private async Task ShowLicenseAsync()
    {
        SetPage("授权管理");
        if (string.IsNullOrWhiteSpace(_machineCode))
        {
            _machineCode = _machineCodeService.GetMachineCode();
        }

        _licenseValidation ??= await _licenseService.ValidateAsync(_machineCode);
        _licenseOutput = new AntInput
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            Font = new Font("Consolas", 10F),
            Radius = 6,
            Text = LicenseText(_machineCode, _licenseValidation)
        };
        _licenseCodeInput = new AntInput
        {
            Dock = DockStyle.Fill,
            Height = 34,
            PlaceholderText = "粘贴加密授权码",
            Radius = 6,
            BorderColor = Color.FromArgb(217, 217, 217),
            BorderActive = Color.FromArgb(22, 119, 255)
        };
        var copy = Button("复制机器码", 120);
        var activate = Button("激活", 90);
        copy.Click += (_, _) => Clipboard.SetText(_machineCode);
        activate.Click += async (_, _) => await ActivateLicenseAsync();

        var inputRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 0, 0, 4)
        };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        inputRow.Controls.Add(new Label
        {
            Text = "授权码",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(38, 38, 38)
        }, 0, 0);
        inputRow.Controls.Add(_licenseCodeInput, 1, 0);
        inputRow.Controls.Add(activate, 2, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 42, BackColor = Color.White };
        buttons.Controls.Add(copy);

        var licenseBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 1,
            RowCount = 3
        };
        licenseBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        licenseBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        licenseBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        licenseBody.Controls.Add(inputRow, 0, 0);
        licenseBody.Controls.Add(buttons, 0, 1);
        licenseBody.Controls.Add(_licenseOutput, 0, 2);

        var card = Card("软件授权", licenseBody);
        _content.Controls.Add(card);
    }

    private async Task ShowUpdatesAsync()
    {
        SetPage("软件更新");
        _ = await GetUpdateRepositoryAsync();
        _updateOutput = new AntInput
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            Font = new Font("Segoe UI", 12F),
            Radius = 6,
            Text = UpdateVersionText("未检测")
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 48, BackColor = Color.White };
        var check = Button("检测最新版本", 125);
        check.Click += async (_, _) => await CheckUpdatesAsync();
        actions.Controls.Add(check);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 1,
            RowCount = 2
        };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(actions, 0, 0);
        body.Controls.Add(_updateOutput, 0, 1);

        var card = Card("版本信息", body);
        _content.Controls.Add(card);
    }

    private void ShowAbout()
    {
        SetPage("关于软件");
        var about = new AntInput
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            Font = new Font("Segoe UI", 10F),
            Radius = 6,
            Text =
                $"{BuildMetadata.DisplayName}{Environment.NewLine}" +
                $"版本：{CurrentVersionText()}{Environment.NewLine}{Environment.NewLine}" +
                $"窗巡是一款面向桌面工作场景的窗口、文字识别和任务栏闪烁监听工具。软件可以监听窗口标题、桌面或窗口中的文字内容、任务栏软件闪烁状态，并在命中规则后通过系统通知或网络回调发送提醒。适合客服、订单、业务系统、运维告警等需要及时发现关键字和窗口状态变化的工作流。{Environment.NewLine}{Environment.NewLine}" +
                $"核心能力：{Environment.NewLine}" +
                $"- 窗口标题关键词监听{Environment.NewLine}" +
                $"- 桌面/窗口文字识别和区域框选{Environment.NewLine}" +
                $"- 任务栏软件闪烁提醒{Environment.NewLine}" +
                $"- 每条规则自定义通知渠道和通知内容{Environment.NewLine}" +
                $"- 本机机器码授权和在线版本更新{Environment.NewLine}{Environment.NewLine}" +
                $"作者：tegic{Environment.NewLine}" +
                $"联系方式：35350826{Environment.NewLine}" +
                "主页：https://github.com/teg1c"
        };

        _content.Controls.Add(Card("关于软件", about));
    }

    private async Task MonitorTickAsync()
    {
        if (!IsLicenseUsable)
        {
            return;
        }

        await ScanWindowTitlesAsync();
    }

    private async Task ScanWindowTitlesAsync(bool force = false)
    {
        if (!IsLicenseUsable)
        {
            SetStatus("需要有效授权，窗口监听已锁定。");
            return;
        }

        if (!_monitoringEnabled && !force)
        {
            return;
        }

        IReadOnlyList<WindowSnapshot> windows;
        try
        {
            windows = _windowInventory.GetVisibleWindows();
        }
        catch (Exception ex)
        {
            SetStatus($"窗口扫描失败：{ex.Message}");
            return;
        }

        var rules = (_rules.Count == 0 ? await _repository.GetRulesAsync() : _rules)
            .Where(rule => rule.RuleType == MonitorRuleType.WindowTitle)
            .ToArray();
        var hitCount = 0;
        var hitRuleIds = new HashSet<Guid>();
        foreach (var window in windows)
        {
            var input = new MonitorInput(MonitorContentType.WindowTitle, window.Title, window, window.ProcessName, DateTimeOffset.Now);
            foreach (var match in _ruleMatcher.Match(rules, input))
            {
                hitRuleIds.Add(match.Rule.Id);
                if (await SaveAndNotifyMatchAsync(match))
                {
                    hitCount++;
                }
            }
        }
        ResetUnmatchedRules(rules, hitRuleIds);

        if (force)
        {
            SetStatus($"窗口标题扫描完成。窗口数：{windows.Count}，命中：{hitCount}。");
        }

        if (DateTimeOffset.Now - _lastOcrScan >= TimeSpan.FromSeconds(15))
        {
            await RunOcrScanAsync(force: false, showResult: false);
        }
    }

    private async Task RunOcrScanAsync(bool force, bool showResult)
    {
        if (!IsLicenseUsable)
        {
            SetStatus("需要有效授权，文字识别监听已锁定。");
            return;
        }

        if (!_monitoringEnabled && !force)
        {
            return;
        }

        _lastOcrScan = DateTimeOffset.Now;
        try
        {
            SetStatus("正在运行文字识别...");
            if (showResult && _ocrOutput is not null)
            {
                using var bitmap = await _captureService.CaptureDesktopAsync();
                var ocr = await _ocrEngine.RecognizeAsync(bitmap, new OcrOptions("zh-Hans,en"));
                _ocrOutput.Text = string.IsNullOrWhiteSpace(ocr.Text) ? "（未识别到文本）" : ocr.Text;
                SetStatus($"文字识别预览完成，耗时 {ocr.Duration.TotalMilliseconds:N0}ms。");
            }

            var hits = 0;
            var rules = (_rules.Count == 0 ? await _repository.GetRulesAsync() : _rules)
                .Where(rule => rule.Enabled && rule.RuleType == MonitorRuleType.Ocr)
                .ToArray();
            var hitRuleIds = new HashSet<Guid>();
            foreach (var rule in rules)
            {
                using var bitmap = await CaptureForOcrRuleAsync(rule);
                if (bitmap is null)
                {
                    continue;
                }

                using var cropped = CropForRule(bitmap, rule);
                var ocr = await _ocrEngine.RecognizeAsync(cropped, new OcrOptions("zh-Hans,en"));
                if (string.IsNullOrWhiteSpace(ocr.Text))
                {
                    continue;
                }

                var source = rule.OcrTargetType == OcrTargetType.Desktop ? "桌面" : rule.ProcessName ?? "窗口";
                var input = new MonitorInput(MonitorContentType.OcrText, ocr.Text, null, source, DateTimeOffset.Now);
                foreach (var match in _ruleMatcher.Match([rule], input))
                {
                    hitRuleIds.Add(match.Rule.Id);
                    if (await SaveAndNotifyMatchAsync(match)) hits++;
                }
            }
            ResetUnmatchedRules(rules, hitRuleIds);

            SetStatus($"文字识别规则扫描完成。命中：{hits}。");
        }
        catch (Exception ex)
        {
            SetStatus($"文字识别失败：{ex.Message}");
            if (showResult && _ocrOutput is not null)
            {
                _ocrOutput.Text = $"文字识别失败：{ex.Message}";
            }
        }
    }

    private async Task<bool> SaveAndNotifyMatchAsync(RuleMatch match)
    {
        if (!IsLicenseUsable)
        {
            return false;
        }

        var pending = MonitorEventFactory.FromMatch(match, NotificationStatus.Pending);
        if (_cooldown.Evaluate(pending, match.Rule.CooldownSeconds) == NotificationStatus.CooldownSkipped)
        {
            return false;
        }

        if (!CanSendForConsecutiveHit(match.Rule, pending.OccurredAt))
        {
            return false;
        }

        var notificationSent = await SendNotificationsAsync(pending, match.Rule);
        await _repository.AddEventAsync(pending with
        {
            NotificationStatus = notificationSent ? NotificationStatus.Sent : NotificationStatus.Failed
        });
        return true;
    }

    private async Task<bool> SendNotificationsAsync(MonitorEvent monitorEvent, MonitorRule rule)
    {
        var ok = true;
        var message = RenderMessage(rule.WindowsToastMessageTemplate, monitorEvent);
        if (rule.NotificationChannels.Contains(NotificationChannel.WindowsToast))
        {
            try
            {
                _notifyIcon.ShowBalloonTip(3000, $"规则命中：{monitorEvent.RuleName}", message, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                ok = false;
                SetStatus($"系统通知发送失败：{ex.Message}");
            }
        }

        if (rule.NotificationChannels.Contains(NotificationChannel.Webhook) && !string.IsNullOrWhiteSpace(rule.WebhookUrl))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, rule.WebhookUrl);
                var headers = ParseHeaders(rule.WebhookHeadersJson);
                foreach (var header in headers.Where(header => !string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                var body = RenderMessage(rule.WebhookBodyTemplate, monitorEvent);
                request.Content = new StringContent(body, Encoding.UTF8, LooksLikeJson(body) ? "application/json" : "text/plain");
                if (headers.TryGetValue("Content-Type", out var contentType) && !string.IsNullOrWhiteSpace(contentType))
                {
                    request.Content.Headers.Remove("Content-Type");
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }

                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                ok = false;
                SetStatus($"网络回调发送失败：{ex.Message}");
            }
        }

        return ok;
    }

    private async void OnTaskbarFlashDetected(object? sender, TaskbarFlashEvent flashEvent)
    {
        if (!IsLicenseUsable)
        {
            return;
        }

        var rules = _rules
            .Where(rule =>
                rule.Enabled &&
                rule.RuleType == MonitorRuleType.TaskbarFlash &&
                (string.IsNullOrWhiteSpace(rule.ProcessName) || string.Equals(rule.ProcessName, flashEvent.ProcessName, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(rule.WindowTitlePattern) || (flashEvent.WindowTitle?.Contains(rule.WindowTitlePattern, StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToArray();

        if (rules.Length == 0)
        {
            return;
        }

        foreach (var rule in rules)
        {
            var pending = new MonitorEvent
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                HitType = MonitorContentType.TaskbarFlash,
                Keyword = "闪烁",
                WindowTitle = flashEvent.WindowTitle,
                ProcessName = flashEvent.ProcessName,
                TextSnippet = $"{flashEvent.ProcessName} 发生任务栏闪烁，检测方式：{flashEvent.DetectionMethod}",
                NotificationStatus = NotificationStatus.Pending,
                OccurredAt = flashEvent.OccurredAt
            };
            if (_cooldown.Evaluate(pending, rule.CooldownSeconds) == NotificationStatus.CooldownSkipped)
            {
                continue;
            }

            ResetQuietFlashRule(rule, flashEvent.OccurredAt);
            if (!CanSendForConsecutiveHit(rule, pending.OccurredAt))
            {
                continue;
            }

            var notificationSent = await SendNotificationsAsync(pending, rule);
            await _repository.AddEventAsync(pending with
            {
                NotificationStatus = notificationSent ? NotificationStatus.Sent : NotificationStatus.Failed
            });
        }

        SetStatus($"检测到任务栏闪烁：{flashEvent.ProcessName}");
    }

    private async Task LoadRulesAsync()
    {
        _rules = await _repository.GetRulesAsync();
    }

    private void StartConfiguredTaskbarRules()
    {
        if (!IsLicenseUsable)
        {
            _taskbarFlashDetector.Stop();
            return;
        }

        var targets = _rules
            .Where(rule => rule.Enabled && rule.RuleType == MonitorRuleType.TaskbarFlash && !string.IsNullOrWhiteSpace(rule.ProcessName))
            .Select(rule => new TaskbarFlashTarget(rule.ProcessName!, rule.WindowTitlePattern, rule.CooldownSeconds))
            .ToArray();
        if (targets.Length > 0)
        {
            _taskbarFlashDetector.Start(targets);
            return;
        }

        _taskbarFlashDetector.Stop();
    }

    private void FillRulesGrid()
    {
        if (_rulesGrid is null) return;
        _ruleRows = _rules.Select(rule => new RuleRow(
            rule,
            rule.Enabled ? "启用" : "停用",
            RuleTypeText(rule.RuleType),
            rule.Name,
            RuleTargetText(rule),
            RuleKeywordsText(rule),
            string.Join(", ", rule.NotificationChannels.Select(ChannelText)),
            $"{rule.MaxConsecutiveNotifications}x / {rule.CooldownSeconds}s",
            new AntCellButton("toggle", rule.Enabled ? "停用" : "启用", rule.Enabled ? AntdUI.TTypeMini.Warn : AntdUI.TTypeMini.Primary) { Radius = 6 })).ToList();
        _rulesGrid.DataSource = _ruleRows;
        if (_selectedRuleId is not null && _ruleRows.All(row => row.Rule.Id != _selectedRuleId.Value))
        {
            _selectedRuleId = null;
        }
    }

    private static string RuleTargetText(MonitorRule rule)
    {
        if (rule.RuleType == MonitorRuleType.Ocr)
        {
            var target = rule.OcrTargetType == OcrTargetType.Desktop ? "整个桌面" : $"{rule.ProcessName} / {rule.WindowTitlePattern}";
            var region = rule.OcrRegionWidth > 0 && rule.OcrRegionHeight > 0
                ? $" 区域 {rule.OcrRegionX},{rule.OcrRegionY} {rule.OcrRegionWidth}x{rule.OcrRegionHeight}"
                : " 全部";
            return $"{target}{region}";
        }

        if (rule.RuleType == MonitorRuleType.TaskbarFlash)
        {
            return rule.ProcessName ?? "选择运行中的软件";
        }

        return $"{rule.ProcessName ?? "任意进程"} / {rule.WindowTitlePattern ?? "任意标题"}";
    }

    private static string RuleKeywordsText(MonitorRule rule)
    {
        if (rule.Keywords.Count > 0)
        {
            return string.Join(", ", rule.Keywords);
        }

        return rule.RuleType == MonitorRuleType.TaskbarFlash ? "任务栏闪烁" : "";
    }

    private static string RuleTypeText(MonitorRuleType type)
    {
        return type switch
        {
            MonitorRuleType.Ocr => "文字识别",
            MonitorRuleType.TaskbarFlash => "任务栏闪烁",
            _ => "窗口标题"
        };
    }

    private static string ChannelText(NotificationChannel channel)
    {
        return channel switch
        {
            NotificationChannel.WindowsToast => "系统通知",
            NotificationChannel.Webhook => "网络回调",
            _ => channel.ToString()
        };
    }

    private static string ContentTypeText(MonitorContentType type)
    {
        return type switch
        {
            MonitorContentType.WindowTitle => "窗口标题",
            MonitorContentType.OcrText => "文字识别",
            MonitorContentType.TaskbarFlash => "任务栏闪烁",
            _ => type.ToString()
        };
    }

    private static string NotificationStatusText(NotificationStatus status)
    {
        return status switch
        {
            NotificationStatus.Pending => "待发送",
            NotificationStatus.Sent => "已发送",
            NotificationStatus.Failed => "失败",
            NotificationStatus.CooldownSkipped => "冷却跳过",
            _ => status.ToString()
        };
    }

    private void FillWindowsGrid()
    {
        if (_windowsGrid is null) return;
        _windowsGrid.Rows.Clear();
        foreach (var window in _windowInventory.GetVisibleWindows())
        {
            _windowsGrid.Rows.Add(window.ProcessName, window.Title, $"0x{window.Handle.ToInt64():X}", $"{window.Bounds.X},{window.Bounds.Y} {window.Bounds.Width}x{window.Bounds.Height}");
        }
        SetStatus("窗口列表已刷新。");
    }

    private void FillFlashGrid()
    {
        if (_flashGrid is null) return;
        _flashGrid.Rows.Clear();
        foreach (var window in _windowInventory.GetVisibleWindows())
        {
            var index = _flashGrid.Rows.Add(false, window.ProcessName, window.Title, $"0x{window.Handle.ToInt64():X}");
            _flashGrid.Rows[index].Tag = window;
        }
    }

    private void FillEventsGrid(IReadOnlyList<MonitorEvent> events, bool includeStatus = false)
    {
        if (_eventsGrid is null) return;
        _eventsGrid.DataSource = events.Select(item => new EventRow(
            includeStatus ? item.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss") : item.OccurredAt.ToString("HH:mm:ss"),
            item.RuleName,
            ContentTypeText(item.HitType),
            item.WindowTitle ?? item.ProcessName ?? "",
            item.TextSnippet ?? "",
            NotificationStatusText(item.NotificationStatus))).ToList();
    }

    private async Task EditRuleAsync(MonitorRule? rule)
    {
        using var form = new RuleEditorForm(rule, _windowInventory.GetVisibleWindows(), _captureService);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await _repository.SaveRuleAsync(form.Rule);
            SelectRule(form.Rule);
            await LoadRulesAsync();
            StartConfiguredTaskbarRules();
            FillRulesGrid();
            SetStatus("规则已保存。");
        }
    }

    private async Task EditSelectedRuleAsync()
    {
        var rule = SelectedRule();
        if (rule is null)
        {
            SetStatus("请先选择一条规则。");
            return;
        }

        await EditRuleAsync(rule);
    }

    private async Task DeleteSelectedRuleAsync()
    {
        var rule = SelectedRule();
        if (rule is null)
        {
            SetStatus("请先选择一条规则。");
            return;
        }

        var confirm = MessageBox.Show(this, $"确定删除规则“{rule.Name}”吗？", "删除规则", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        await _repository.DeleteRuleAsync(rule.Id);
        await LoadRulesAsync();
        StartConfiguredTaskbarRules();
        FillRulesGrid();
        SetStatus("规则已删除。");
    }

    private async Task CopySelectedRuleAsync()
    {
        var rule = SelectedRule();
        if (rule is null)
        {
            SetStatus("请先选择一条规则。");
            return;
        }

        var now = DateTimeOffset.Now;
        var copy = rule with
        {
            Id = Guid.NewGuid(),
            Name = $"{rule.Name} 副本",
            Enabled = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _repository.SaveRuleAsync(copy);
        await LoadRulesAsync();
        FillRulesGrid();
        SetStatus("规则已复制。");
    }

    private async Task ToggleSelectedRuleAsync()
    {
        var rule = SelectedRule();
        if (rule is null)
        {
            SetStatus("请先选择一条规则。");
            return;
        }

        await ToggleRuleAsync(rule);
    }

    private async Task ToggleRuleAsync(MonitorRule rule)
    {
        await _repository.SaveRuleAsync(rule with { Enabled = !rule.Enabled, UpdatedAt = DateTimeOffset.Now });
        _hitStates.Remove(rule.Id);
        await LoadRulesAsync();
        StartConfiguredTaskbarRules();
        FillRulesGrid();
        SetStatus("规则状态已变更。");
    }

    private async Task<Bitmap?> CaptureForOcrRuleAsync(MonitorRule rule)
    {
        if (rule.OcrTargetType == OcrTargetType.Desktop)
        {
            return await _captureService.CaptureDesktopAsync();
        }

        var window = _windowInventory.GetVisibleWindows().FirstOrDefault(item =>
            (string.IsNullOrWhiteSpace(rule.ProcessName) || string.Equals(item.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(rule.WindowTitlePattern) || item.Title.Contains(rule.WindowTitlePattern, StringComparison.OrdinalIgnoreCase)));
        return window is null ? null : await _captureService.CaptureWindowAsync(window);
    }

    private static Bitmap CropForRule(Bitmap source, MonitorRule rule)
    {
        if (rule.OcrRegionWidth is not > 0 || rule.OcrRegionHeight is not > 0)
        {
            return new Bitmap(source);
        }

        var rect = Rectangle.Intersect(
            new Rectangle(rule.OcrRegionX ?? 0, rule.OcrRegionY ?? 0, rule.OcrRegionWidth.Value, rule.OcrRegionHeight.Value),
            new Rectangle(0, 0, source.Width, source.Height));
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return new Bitmap(source);
        }

        return source.Clone(rect, source.PixelFormat);
    }

    private MonitorRule? SelectedRule()
    {
        if (_selectedRuleId is { } selectedRuleId)
        {
            var selected = _rules.FirstOrDefault(rule => rule.Id == selectedRuleId);
            if (selected is not null)
            {
                return selected;
            }
        }

        if (_rulesGrid?.FocusedRow is RuleRow row)
        {
            SelectRule(row.Rule);
            return row.Rule;
        }

        if (_rulesGrid is not null && _rulesGrid.SelectedIndex > 0)
        {
            var index = _rulesGrid.SelectedIndex - 1;
            if (index >= 0 && index < _ruleRows.Count)
            {
                var selected = _ruleRows[index].Rule;
                SelectRule(selected);
                return selected;
            }
        }

        return null;
    }

    private void SelectRule(MonitorRule rule)
    {
        _selectedRuleId = rule.Id;
    }

    private async Task SaveSelectedFlashRulesAsync()
    {
        if (_flashGrid is null) return;
        var windows = _flashGrid.Rows
            .Cast<DataGridViewRow>()
            .Where(row => row.Cells[0].Value is bool selected && selected)
            .Select(row => row.Tag as WindowSnapshot)
            .Where(window => window is not null)
            .Select(window => window!)
            .GroupBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (windows.Length == 0)
        {
            SetStatus("请至少选择一个运行中的软件。");
            return;
        }

        var existing = _rules
            .Where(rule => rule.RuleType == MonitorRuleType.TaskbarFlash)
            .ToDictionary(rule => rule.ProcessName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        foreach (var window in windows)
        {
            if (existing.TryGetValue(window.ProcessName, out var rule))
            {
                if (!rule.Enabled)
                {
                    await _repository.SaveRuleAsync(rule with { Enabled = true, UpdatedAt = DateTimeOffset.Now });
                }

                continue;
            }

            await _repository.SaveRuleAsync(new MonitorRule
            {
                Name = $"任务栏闪烁：{window.ProcessName}",
                RuleType = MonitorRuleType.TaskbarFlash,
                ProcessName = window.ProcessName,
                ContentTypes = [MonitorContentType.TaskbarFlash],
                Keywords = [],
                NotificationChannels = [NotificationChannel.WindowsToast],
                NotificationMessageTemplate = "规则：{RuleName}\r\n来源：{Source}\r\n内容：{Snippet}",
                CooldownSeconds = 60
            });
        }

        await LoadRulesAsync();
        StartConfiguredTaskbarRules();
        SetStatus($"已保存闪烁规则：{string.Join(", ", windows.Select(item => item.ProcessName))}");
    }

    private async Task ImportLicenseAsync(string machineCode)
    {
        if (_licenseOutput is null) return;
        using var dialog = new OpenFileDialog { Title = "选择授权文件", Filter = "授权文件 (*.json;*.lic;*.txt)|*.json;*.lic;*.txt|所有文件 (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            await _licenseService.ImportOfflineLicenseAsync(dialog.FileName, machineCode);
            await ValidateLicenseAsync(forceRemoteCheck: true);
            if (IsLicenseUsable)
            {
                _monitoringEnabled = true;
                StartConfiguredTaskbarRules();
            }

            SetStatus("授权已导入。");
        }
        catch (Exception ex)
        {
            _licenseOutput.Text = $"{LicenseText(machineCode, _licenseValidation)}{Environment.NewLine}{Environment.NewLine}导入失败：{ex.Message}";
            SetStatus("授权导入失败。");
        }
    }

    private async Task ActivateLicenseAsync()
    {
        if (_licenseOutput is null || _licenseCodeInput is null) return;
        try
        {
            await _licenseService.ActivateAsync(_licenseCodeInput.Text, _machineCode);
            await ValidateLicenseAsync(forceRemoteCheck: true);
            if (IsLicenseUsable)
            {
                _monitoringEnabled = true;
                StartConfiguredTaskbarRules();
            }

            SetStatus("授权已激活。");
        }
        catch (Exception ex)
        {
            _licenseOutput.Text = $"{LicenseText(_machineCode, _licenseValidation)}{Environment.NewLine}{Environment.NewLine}激活失败：{ex.Message}";
            SetStatus("授权激活失败。");
        }
    }

    private async Task CheckUpdatesAsync()
    {
        if (_updateOutput is null) return;
        try
        {
            _updateOutput.Text = UpdateVersionText("正在检测...");
            var (owner, repo) = await GetUpdateRepositoryAsync();
            var release = await _updateService.GetLatestReleaseAsync(owner, repo);
            if (release is null)
            {
                _updateOutput.Text = UpdateVersionText("获取失败");
                return;
            }

            var currentVersion = CurrentVersionText();
            var latestVersion = NormalizeVersionToken(release.TagName);
            _updateOutput.Text = UpdateVersionText(release.TagName);
            if (IsNewerVersion(latestVersion, currentVersion))
            {
                SetStatus($"发现新版本 {release.TagName}，请更新到最新版。");
                MessageBox.Show(
                    this,
                    $"发现新版本：{release.TagName}{Environment.NewLine}当前版本：{currentVersion}{Environment.NewLine}{Environment.NewLine}请更新到最新版。",
                    "发现新版本",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                SetStatus("已是最新版本。");
            }
        }
        catch (Exception ex)
        {
            _updateOutput.Text = UpdateVersionText("检测失败");
            SetStatus($"检测更新失败：{ex.Message}");
        }
    }

    private async Task<(string Owner, string Repository)> GetUpdateRepositoryAsync()
    {
        var owner = await _repository.GetSettingAsync(SettingsKeys.GitHubOwner);
        var repo = await _repository.GetSettingAsync(SettingsKeys.GitHubRepository);
        return (
            string.IsNullOrWhiteSpace(owner) ? SettingsKeys.DefaultGitHubOwner : owner.Trim(),
            string.IsNullOrWhiteSpace(repo) ? SettingsKeys.DefaultGitHubRepository : repo.Trim());
    }

    private static string CurrentVersionText()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informational)
            ? assembly.GetName().Version?.ToString()
            : informational;
        return NormalizeVersionToken(version ?? "0.0.0");
    }

    private static string UpdateVersionText(string latestVersion)
    {
        return $"当前版本：{CurrentVersionText()}{Environment.NewLine}最新版本：{latestVersion}";
    }

    private static string NormalizeVersionToken(string version)
    {
        var normalized = version.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var buildIndex = normalized.IndexOf('+', StringComparison.Ordinal);
        return buildIndex >= 0 ? normalized[..buildIndex] : normalized;
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (TryParseVersion(latest, out var latestVersion) &&
            TryParseVersion(current, out var currentVersion))
        {
            return latestVersion > currentVersion;
        }

        return !string.Equals(
            NormalizeVersionToken(latest),
            NormalizeVersionToken(current),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var parts = NormalizeVersionToken(value).Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            version = new Version(0, 0, 0);
            return false;
        }

        var core = parts[0];
        return Version.TryParse(core, out version!);
    }

    private static string LicenseText(string machineCode, LicenseValidationResult? validation)
    {
        var license = validation?.License;
        var status = license is null
            ? "未激活授权。"
            : $"状态：{LicenseStatusText(validation?.Status ?? LicenseStatus.Missing)}{Environment.NewLine}" +
              $"授权ID：{license.LicenseId}{Environment.NewLine}" +
              $"授权类型：{LicenseTypeText(license.LicenseType)}{Environment.NewLine}" +
              $"版本：{license.Edition}{Environment.NewLine}" +
              $"授权时间：{license.IssuedAt:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
              $"到期日期：{license.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "永久"}{Environment.NewLine}" +
              $"最近校验：{license.LastServerTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "本地校验"}{Environment.NewLine}" +
              $"消息：{validation?.Message}";
        return $"机器码：{Environment.NewLine}{machineCode}{Environment.NewLine}{Environment.NewLine}" +
               status;
    }

    private static string BoolText(bool value)
    {
        return value ? "是" : "否";
    }

    private static string LicenseStatusText(LicenseStatus status)
    {
        return status switch
        {
            LicenseStatus.Valid => "有效",
            LicenseStatus.ExpiringSoon => "即将过期",
            LicenseStatus.Expired => "已过期",
            LicenseStatus.Invalid => "无效",
            LicenseStatus.DeviceMismatch => "机器不匹配",
            _ => "未授权"
        };
    }

    private static string LicenseTypeText(LicenseType type)
    {
        return type switch
        {
            LicenseType.Daily => "按天",
            LicenseType.Monthly => "按月",
            LicenseType.Yearly => "按年",
            LicenseType.Permanent => "永久",
            _ => "按年"
        };
    }

    private static string RenderMessage(string template, MonitorEvent monitorEvent)
    {
        var source = monitorEvent.WindowTitle ?? monitorEvent.ProcessName ?? "";
        var replacements = new Dictionary<string, string?>
        {
            ["RuleName"] = monitorEvent.RuleName,
            ["HitType"] = monitorEvent.HitType.ToString(),
            ["Keyword"] = monitorEvent.Keyword,
            ["WindowTitle"] = monitorEvent.WindowTitle,
            ["ProcessName"] = monitorEvent.ProcessName,
            ["Source"] = source,
            ["Snippet"] = monitorEvent.TextSnippet,
            ["OccurredAt"] = monitorEvent.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss")
        };

        var result = string.IsNullOrWhiteSpace(template)
            ? "规则：{RuleName}\r\n类型：{HitType}\r\n关键词：{Keyword}\r\n来源：{Source}\r\n内容：{Snippet}"
            : template;
        foreach (var item in replacements)
        {
            result = result.Replace($"{{{item.Key}}}", item.Value ?? "", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private bool CanSendForConsecutiveHit(MonitorRule rule, DateTimeOffset occurredAt)
    {
        var limit = Math.Max(1, rule.MaxConsecutiveNotifications);
        if (!_hitStates.TryGetValue(rule.Id, out var state))
        {
            state = new HitState();
            _hitStates[rule.Id] = state;
        }

        state.ConsecutiveHits++;
        state.LastHitAt = occurredAt;
        return state.ConsecutiveHits <= limit;
    }

    private void ResetUnmatchedRules(IEnumerable<MonitorRule> rules, HashSet<Guid> hitRuleIds)
    {
        foreach (var rule in rules)
        {
            if (!hitRuleIds.Contains(rule.Id))
            {
                _hitStates.Remove(rule.Id);
            }
        }
    }

    private void ResetQuietFlashRule(MonitorRule rule, DateTimeOffset hitAt)
    {
        if (_hitStates.TryGetValue(rule.Id, out var state) &&
            hitAt - state.LastHitAt > TimeSpan.FromSeconds(Math.Max(3, rule.CooldownSeconds * 2)))
        {
            _hitStates.Remove(rule.Id);
        }
    }

    private static IReadOnlyDictionary<string, string> ParseHeaders(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            return new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool LooksLikeJson(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
    }

    private static Control Metric(string label, string value)
    {
        var panel = new AntPanel { Dock = DockStyle.Fill, Back = Color.White, Radius = 8, BorderColor = Color.FromArgb(235, 238, 245), BorderWidth = 1, Shadow = 4, ShadowOpacity = 0.06F, Margin = new Padding(0, 0, 12, 0), Padding = new Padding(14) };
        panel.Controls.Add(new AntLabel { Text = value, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(17, 24, 39), BackColor = Color.White });
        panel.Controls.Add(new AntLabel { Text = label, Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(75, 85, 99), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.White });
        return panel;
    }

    private static AntButton Button(string text, int width)
    {
        return new AntButton { Text = text, Width = width, Height = 34, Margin = new Padding(0, 4, 8, 4), Type = AntdUI.TTypeMini.Primary, Radius = 6 };
    }

    private static AntPanel Card(string title, Control child)
    {
        var panel = new AntPanel { Dock = DockStyle.Fill, Back = Color.White, Radius = 8, BorderColor = Color.FromArgb(235, 238, 245), BorderWidth = 1, Shadow = 4, ShadowOpacity = 0.06F, Padding = new Padding(12), Margin = new Padding(0, 8, 0, 0) };
        panel.Controls.Add(child);
        panel.Controls.Add(new AntLabel { Text = title, Dock = DockStyle.Top, Height = 32, Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(17, 24, 39), BackColor = Color.White });
        return panel;
    }

    private static DataGridView Grid(string[] columns)
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            GridColor = Color.FromArgb(240, 240, 240),
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 38,
            RowTemplate = { Height = 34 }
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(38, 38, 38);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(250, 250, 250);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 244, 255);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(22, 119, 255);
        grid.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 250, 250);

        foreach (var column in columns)
        {
            if (column is "Select" or "Enabled" or "选择" or "启用")
            {
                grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = column, Name = column, FillWeight = 45 });
            }
            else if (column is "Action" or "操作")
            {
                grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = column, Name = column, FillWeight = 70 });
            }
            else
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column, Name = column });
            }
        }

        return grid;
    }

    private static AntTable RulesTable()
    {
        var table = StyledTable();
        table.Columns.Add(new AntColumn(nameof(RuleRow.Status), "状态") { Width = "64" });
        table.Columns.Add(new AntColumn(nameof(RuleRow.Type), "类型") { Width = "100" });
        table.Columns.Add(new AntColumn(nameof(RuleRow.Name), "名称") { Width = "150" });
        table.Columns.Add(new AntColumn(nameof(RuleRow.Target), "目标") { Width = "170", Ellipsis = true });
        table.Columns.Add(new AntColumn(nameof(RuleRow.Keywords), "关键词") { Width = "160", Ellipsis = true });
        table.Columns.Add(new AntColumn(nameof(RuleRow.Channels), "通知渠道") { Width = "170", Ellipsis = true });
        table.Columns.Add(new AntColumn(nameof(RuleRow.Limit), "限制") { Width = "95" });
        table.Columns.Add(new AntColumn(nameof(RuleRow.Action), "操作") { Width = "104" });
        return table;
    }

    private static AntTable EventTable(bool includeStatus = false)
    {
        var table = StyledTable();
        table.Columns.Add(new AntColumn(nameof(EventRow.Time), "时间") { Width = includeStatus ? "160" : "90" });
        table.Columns.Add(new AntColumn(nameof(EventRow.Rule), "规则") { Width = "160" });
        table.Columns.Add(new AntColumn(nameof(EventRow.Type), "类型") { Width = "120" });
        table.Columns.Add(new AntColumn(nameof(EventRow.Source), "来源") { Width = "220", Ellipsis = true });
        table.Columns.Add(new AntColumn(nameof(EventRow.Hit), "命中内容") { Width = "auto", Ellipsis = true });
        if (includeStatus)
        {
            table.Columns.Add(new AntColumn(nameof(EventRow.Status), "状态") { Width = "110" });
        }

        return table;
    }

    private static AntTable StyledTable()
    {
        return new AntTable
        {
            Dock = DockStyle.Fill,
            Bordered = true,
            Radius = 6,
            RowHeight = 38,
            RowHeightHeader = 40,
            BorderColor = Color.FromArgb(240, 240, 240),
            ColumnBack = Color.FromArgb(250, 250, 250),
            ColumnFore = Color.FromArgb(31, 41, 55),
            RowHoverBg = Color.FromArgb(248, 250, 252),
            RowSelectedBg = Color.FromArgb(232, 244, 255),
            RowSelectedFore = Color.FromArgb(31, 41, 55),
            EmptyText = "暂无数据"
        };
    }

    private void SetStatus(string text)
    {
        _statusText.Text = text;
    }

    private void RefreshMonitorToggle()
    {
        if (_monitorToggleButton is null || _monitorToggleButton.IsDisposed)
        {
            return;
        }

        if (_monitoringEnabled)
        {
            _monitorToggleButton.Text = _pulseOn ? "监听中\n●" : "监听中\n○";
            _monitorToggleButton.Type = AntdUI.TTypeMini.Primary;
            return;
        }

        _monitorToggleButton.Text = "已暂停\n启动";
        _monitorToggleButton.Type = AntdUI.TTypeMini.Warn;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
        menu.Items.Add("开始监听", null, (_, _) =>
        {
            _monitoringEnabled = true;
            RefreshMonitorToggle();
        });
        menu.Items.Add("暂停监听", null, (_, _) =>
        {
            _monitoringEnabled = false;
            RefreshMonitorToggle();
        });
        menu.Items.Add("退出", null, (_, _) =>
        {
            _notifyIcon.Visible = false;
            Application.Exit();
        });
        return menu;
    }

    private ContextMenuStrip BuildRuleContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, e) =>
        {
            var hasRule = SelectedRule() is not null;
            foreach (ToolStripItem item in menu.Items)
            {
                item.Enabled = hasRule;
            }

            e.Cancel = !hasRule;
        };
        menu.Items.Add("编辑", null, async (_, _) => await EditSelectedRuleAsync());
        menu.Items.Add("删除", null, async (_, _) => await DeleteSelectedRuleAsync());
        menu.Items.Add("复制", null, async (_, _) => await CopySelectedRuleAsync());
        return menu;
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing)
        {
            _notifyIcon.Dispose();
            return;
        }

        e.Cancel = true;
        Hide();
        _notifyIcon.ShowBalloonTip(2000, BuildMetadata.DisplayName, "软件仍在托盘运行。", ToolTipIcon.Info);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitorTimer.Dispose();
            _notifyIcon.Dispose();
            _taskbarFlashDetector.Dispose();
            _httpClient.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class HitState
    {
        public int ConsecutiveHits { get; set; }
        public DateTimeOffset LastHitAt { get; set; }
    }

    private sealed record RuleRow(
        MonitorRule Rule,
        string Status,
        string Type,
        string Name,
        string Target,
        string Keywords,
        string Channels,
        string Limit,
        AntCellButton Action);

    private sealed record EventRow(
        string Time,
        string Rule,
        string Type,
        string Source,
        string Hit,
        string Status);
}
