using WindowsMonitor.Core.Models;
using WindowsMonitor.Core.Services;
using WindowsMonitor.Infrastructure;
using System.Text.Json;
using AntButton = AntdUI.Button;
using AntCheckbox = AntdUI.Checkbox;
using AntInput = AntdUI.Input;
using AntInputNumber = AntdUI.InputNumber;
using AntSelect = AntdUI.Select;

namespace WindowsMonitor.App;

public sealed class RuleEditorForm : Form
{
    private readonly IReadOnlyList<WindowSnapshot> _windows;
    private readonly ICaptureService _captureService;

    private readonly FlowLayoutPanel _body = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(22),
        BackColor = Color.White
    };

    private readonly AntInput _name = new() { Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntSelect _ruleType = new() { Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntInput _keywords = new() { Multiline = true, Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntInput _processName = new() { Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntInput _windowTitle = new() { Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntInputNumber _cooldown = new() { Minimum = 1, Maximum = 3600, Value = 60, Radius = 6 };
    private readonly AntInputNumber _maxConsecutiveNotifications = new() { Minimum = 1, Maximum = 100, Value = 1, Radius = 6 };
    private readonly AntCheckbox _enabled = new() { Text = "启用", Checked = true, AutoSize = true };
    private readonly AntCheckbox _toast = new() { Text = "系统通知", Checked = true, AutoSize = true };
    private readonly AntCheckbox _webhook = new() { Text = "网络回调", AutoSize = true };
    private readonly AntInput _toastMessageTemplate = new() { Multiline = true, Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntInput _webhookUrl = new() { Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntInput _webhookHeadersJson = new() { Multiline = true, Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntInput _webhookBodyTemplate = new() { Multiline = true, Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntSelect _ocrTarget = new() { Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntSelect _ocrWindow = new() { Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly AntSelect _flashWindow = new() { Radius = 6, BorderColor = Color.FromArgb(217, 217, 217), BorderActive = Color.FromArgb(22, 119, 255) };
    private readonly Label _region = new() { Text = "全部区域", AutoSize = true };
    private readonly AntButton _pickRegion = new() { Text = "预览/框选区域", Width = 140, Type = AntdUI.TTypeMini.Primary, Radius = 6 };
    private readonly AntButton _clearRegion = new() { Text = "清除区域", Width = 90, Radius = 6 };

    private readonly List<(string Label, WindowSnapshot Window)> _windowItems = [];
    private readonly List<(string Label, WindowSnapshot Window)> _flashItems = [];
    private readonly Dictionary<string, Panel> _rows = [];
    private Rectangle? _ocrRegion;

    public MonitorRule Rule { get; private set; }

    public RuleEditorForm(MonitorRule? rule, IReadOnlyList<WindowSnapshot> windows, ICaptureService captureService)
    {
        Rule = rule ?? new MonitorRule();
        _windows = windows;
        _captureService = captureService;

        Text = rule is null ? "新增规则" : "编辑规则";
        Size = new Size(760, 760);
        MinimumSize = new Size(720, 620);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.White;

        BuildLayout();
        LoadRule(Rule);
        UpdateVisibility();
    }

    private void BuildLayout()
    {
        _ruleType.Items.AddRange(new object[] { "窗口标题", "文字识别", "任务栏闪烁" });
        _ocrTarget.Items.AddRange(new object[] { "整个桌面", "窗口" });
        _ruleType.SelectedIndexChanged += (_, _) => ScheduleVisibilityUpdate();
        _ruleType.SelectedValueChanged += (_, _) => ScheduleVisibilityUpdate();
        _ruleType.TextChanged += (_, _) => ScheduleVisibilityUpdate();
        _ocrTarget.SelectedIndexChanged += (_, _) => ScheduleVisibilityUpdate();
        _ocrTarget.SelectedValueChanged += (_, _) => ScheduleVisibilityUpdate();
        _ocrTarget.TextChanged += (_, _) => ScheduleVisibilityUpdate();
        _toast.CheckedChanged += (_, _) => ScheduleVisibilityUpdate();
        _webhook.CheckedChanged += (_, _) => ScheduleVisibilityUpdate();
        _pickRegion.Click += async (_, _) => await PickOcrRegionAsync();
        _clearRegion.Click += (_, _) =>
        {
            _ocrRegion = null;
            UpdateRegionText();
        };

        foreach (var window in _windows)
        {
            var label = $"{window.ProcessName} - {window.Title}";
            _windowItems.Add((label, window));
            _ocrWindow.Items.Add(label);
        }

        foreach (var window in _windows
                     .GroupBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            var label = string.IsNullOrWhiteSpace(window.Title)
                ? window.ProcessName
                : $"{window.ProcessName} - {window.Title}";
            _flashItems.Add((label, window));
            _flashWindow.Items.Add(label);
        }

        AddRow("RuleType", "规则类型", _ruleType, 38);
        AddRow("Name", "规则名称", _name, 38);
        AddRow("Keywords", "关键词", _keywords, 112);
        AddRow("ProcessFilter", "进程过滤", _processName, 38);
        AddRow("WindowTitleFilter", "窗口标题过滤", _windowTitle, 38);
        AddRow("OcrTarget", "识别目标", _ocrTarget, 38);
        AddRow("OcrWindow", "识别窗口", _ocrWindow, 38);
        AddRow("OcrRegion", "识别区域", Flow(_region, _pickRegion, _clearRegion), 42);
        AddRow("FlashWindow", "运行中的软件", _flashWindow, 38);
        AddRow("Cooldown", "冷却秒数", _cooldown, 38);
        AddRow("MaxConsecutive", "连续发送上限", _maxConsecutiveNotifications, 38);
        AddRow("State", "状态", Flow(_enabled), 34);
        AddRow("Channels", "通知渠道", Flow(_toast, _webhook), 34);
        AddRow("ToastMessage", "系统通知内容", _toastMessageTemplate, 96);
        AddRow("WebhookUrl", "回调地址", _webhookUrl, 34);
        AddRow("WebhookHeaders", "回调请求头", _webhookHeadersJson, 86);
        AddRow("WebhookBody", "回调请求体", _webhookBodyTemplate, 126);

        _body.ClientSizeChanged += (_, _) => ResizeRows();

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            BackColor = Color.White
        };
        var save = new AntButton { Text = "保存", Width = 92, Height = 32, Type = AntdUI.TTypeMini.Primary, Radius = 6 };
        var cancel = new AntButton { Text = "取消", Width = 92, Height = 32, DialogResult = DialogResult.Cancel, Radius = 6 };
        save.Click += (_, _) => SaveRuleAndClose();
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);

        Controls.Add(_body);
        Controls.Add(actions);
        AcceptButton = save;
        CancelButton = cancel;
        ResizeRows();
    }

    private void LoadRule(MonitorRule rule)
    {
        SelectItemText(_ruleType, RuleTypeText(rule.RuleType));
        SelectItemText(_ocrTarget, OcrTargetText(rule.OcrTargetType));
        _name.Text = rule.Name;
        _keywords.Text = string.Join(Environment.NewLine, rule.Keywords);
        _processName.Text = rule.ProcessName ?? string.Empty;
        _windowTitle.Text = rule.WindowTitlePattern ?? string.Empty;
        _cooldown.Value = Math.Clamp(rule.CooldownSeconds, 1, 3600);
        _maxConsecutiveNotifications.Value = Math.Clamp(rule.MaxConsecutiveNotifications, 1, 100);
        _enabled.Checked = rule.Enabled;
        _toast.Checked = rule.NotificationChannels.Contains(NotificationChannel.WindowsToast);
        _webhook.Checked = rule.NotificationChannels.Contains(NotificationChannel.Webhook);
        _toastMessageTemplate.Text = string.IsNullOrWhiteSpace(rule.WindowsToastMessageTemplate)
            ? new MonitorRule().WindowsToastMessageTemplate
            : rule.WindowsToastMessageTemplate;
        _webhookUrl.Text = rule.WebhookUrl ?? string.Empty;
        _webhookHeadersJson.Text = string.IsNullOrWhiteSpace(rule.WebhookHeadersJson) ? "{}" : rule.WebhookHeadersJson;
        _webhookBodyTemplate.Text = string.IsNullOrWhiteSpace(rule.WebhookBodyTemplate)
            ? new MonitorRule().WebhookBodyTemplate
            : rule.WebhookBodyTemplate;
        _ocrRegion = rule.OcrRegionWidth > 0 && rule.OcrRegionHeight > 0
            ? new Rectangle(rule.OcrRegionX ?? 0, rule.OcrRegionY ?? 0, rule.OcrRegionWidth.Value, rule.OcrRegionHeight.Value)
            : null;

        if (!string.IsNullOrWhiteSpace(rule.ProcessName))
        {
            var ocrIndex = _windowItems.FindIndex(item =>
                string.Equals(item.Window.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(rule.WindowTitlePattern) ||
                 item.Window.Title.Contains(rule.WindowTitlePattern, StringComparison.OrdinalIgnoreCase)));
            if (ocrIndex >= 0)
            {
                _ocrWindow.SelectedIndex = ocrIndex;
            }

            var flashIndex = _flashItems.FindIndex(item =>
                string.Equals(item.Window.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase));
            if (flashIndex >= 0)
            {
                _flashWindow.SelectedIndex = flashIndex;
            }
        }

        UpdateRegionText();
    }

    private void UpdateVisibility()
    {
        if (IsDisposed)
        {
            return;
        }

        var type = SelectedRuleType();
        var isWindowTitle = type == MonitorRuleType.WindowTitle;
        var isOcr = type == MonitorRuleType.Ocr;
        var isFlash = type == MonitorRuleType.TaskbarFlash;
        var isOcrWindow = isOcr && SelectedOcrTarget() == OcrTargetType.Window;

        SetRowVisible("Keywords", isWindowTitle || isOcr);
        SetRowVisible("ProcessFilter", isWindowTitle);
        SetRowVisible("WindowTitleFilter", isWindowTitle);
        SetRowVisible("OcrTarget", isOcr);
        SetRowVisible("OcrWindow", isOcrWindow);
        SetRowVisible("OcrRegion", isOcr);
        SetRowVisible("FlashWindow", isFlash);
        SetRowVisible("ToastMessage", _toast.Checked);
        SetRowVisible("WebhookUrl", _webhook.Checked);
        SetRowVisible("WebhookHeaders", _webhook.Checked);
        SetRowVisible("WebhookBody", _webhook.Checked);

        if (isOcrWindow && _ocrWindow.SelectedIndex < 0 && _ocrWindow.Items.Count > 0)
        {
            _ocrWindow.SelectedIndex = 0;
        }

        if (isFlash && _flashWindow.SelectedIndex < 0 && _flashWindow.Items.Count > 0)
        {
            _flashWindow.SelectedIndex = 0;
        }

        _ocrTarget.Enabled = isOcr;
        _ocrWindow.Enabled = isOcrWindow;
        _pickRegion.Enabled = isOcr;
        _clearRegion.Enabled = isOcr;
        ResizeRows();
    }

    private void ScheduleVisibilityUpdate()
    {
        if (IsDisposed)
        {
            return;
        }

        if (!IsHandleCreated)
        {
            UpdateVisibility();
            return;
        }

        BeginInvoke((MethodInvoker)UpdateVisibility);
    }

    private async Task PickOcrRegionAsync()
    {
        if (SelectedRuleType() != MonitorRuleType.Ocr)
        {
            return;
        }

        var hideForDesktopCapture = SelectedOcrTarget() == OcrTargetType.Desktop;
        try
        {
            if (hideForDesktopCapture)
            {
                Hide();
                await Task.Delay(180);
            }

            using var bitmap = await CaptureSelectedOcrTargetAsync();
            if (bitmap is null)
            {
                MessageBox.Show(this, "请先选择识别窗口。", "文字识别预览", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppLogger.Info($"打开文字识别区域预览。target={SelectedOcrTarget()}, image={bitmap.Width}x{bitmap.Height}");
            using var picker = new OcrRegionPickerForm(bitmap, _ocrRegion);
            if (picker.ShowDialog(hideForDesktopCapture ? null : this) == DialogResult.OK)
            {
                _ocrRegion = picker.SelectedRegion;
                UpdateRegionText();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("打开文字识别区域预览失败。", ex);
            MessageBox.Show(this, $"打开文字识别预览失败：{ex.Message}", "文字识别预览", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (hideForDesktopCapture && !IsDisposed)
            {
                Show();
                Activate();
            }
        }
    }

    private async Task<Bitmap?> CaptureSelectedOcrTargetAsync()
    {
        if (SelectedOcrTarget() == OcrTargetType.Desktop)
        {
            return await _captureService.CaptureDesktopAsync();
        }

        var window = SelectedOcrWindow();
        return window is null ? null : await _captureService.CaptureWindowAsync(window);
    }

    private void SaveRuleAndClose()
    {
        UpdateVisibility();
        var ruleType = SelectedRuleType();
        var processName = string.IsNullOrWhiteSpace(_processName.Text) ? null : _processName.Text.Trim();
        var windowTitlePattern = string.IsNullOrWhiteSpace(_windowTitle.Text) ? null : _windowTitle.Text.Trim();

        if (ruleType == MonitorRuleType.Ocr)
        {
            if (SelectedOcrTarget() == OcrTargetType.Desktop)
            {
                processName = null;
                windowTitlePattern = null;
            }
            else if (SelectedOcrWindow() is { } ocrWindow)
            {
                processName = ocrWindow.ProcessName;
                windowTitlePattern = ocrWindow.Title;
            }
        }
        else if (ruleType == MonitorRuleType.TaskbarFlash && SelectedFlashWindow() is { } flashWindow)
        {
            processName = flashWindow.ProcessName;
            windowTitlePattern = null;
        }

        if (ruleType is MonitorRuleType.WindowTitle or MonitorRuleType.Ocr && string.IsNullOrWhiteSpace(_keywords.Text))
        {
            MessageBox.Show(this, "窗口标题和文字识别规则至少需要一个关键词。", "规则校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (ruleType == MonitorRuleType.Ocr &&
            SelectedOcrTarget() == OcrTargetType.Window &&
            string.IsNullOrWhiteSpace(processName))
        {
            MessageBox.Show(this, "请先选择识别窗口。", "规则校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (ruleType == MonitorRuleType.TaskbarFlash && string.IsNullOrWhiteSpace(processName))
        {
            MessageBox.Show(this, "请先选择运行中的软件。", "规则校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_webhook.Checked && string.IsNullOrWhiteSpace(_webhookUrl.Text))
        {
            MessageBox.Show(this, "网络回调通知渠道需要填写地址。", "规则校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_webhook.Checked && !IsValidWebhookUrl(_webhookUrl.Text))
        {
            MessageBox.Show(this, "回调地址必须是完整的 http 或 https 地址。", "规则校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_webhook.Checked && !IsValidJsonObject(_webhookHeadersJson.Text))
        {
            MessageBox.Show(this, "回调请求头必须是 JSON 对象，例如：{\"Authorization\":\"Bearer token\"}。", "规则校验", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var contentType = ruleType switch
        {
            MonitorRuleType.Ocr => MonitorContentType.OcrText,
            MonitorRuleType.TaskbarFlash => MonitorContentType.TaskbarFlash,
            _ => MonitorContentType.WindowTitle
        };

        var channels = new List<NotificationChannel>();
        if (_toast.Checked) channels.Add(NotificationChannel.WindowsToast);
        if (_webhook.Checked) channels.Add(NotificationChannel.Webhook);

        Rule = Rule with
        {
            Name = string.IsNullOrWhiteSpace(_name.Text) ? "未命名规则" : _name.Text.Trim(),
            Enabled = _enabled.Checked,
            RuleType = ruleType,
            ProcessName = processName,
            WindowTitlePattern = windowTitlePattern,
            OcrTargetType = SelectedOcrTarget(),
            OcrRegionX = _ocrRegion?.X,
            OcrRegionY = _ocrRegion?.Y,
            OcrRegionWidth = _ocrRegion?.Width,
            OcrRegionHeight = _ocrRegion?.Height,
            ContentTypes = [contentType],
            Keywords = _keywords.Text
                .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            NotificationChannels = channels.Count == 0 ? [NotificationChannel.WindowsToast] : channels,
            NotificationMessageTemplate = string.IsNullOrWhiteSpace(_toastMessageTemplate.Text)
                ? new MonitorRule().NotificationMessageTemplate
                : _toastMessageTemplate.Text.Trim(),
            WindowsToastMessageTemplate = string.IsNullOrWhiteSpace(_toastMessageTemplate.Text)
                ? new MonitorRule().WindowsToastMessageTemplate
                : _toastMessageTemplate.Text.Trim(),
            WebhookUrl = _webhook.Checked ? _webhookUrl.Text.Trim() : null,
            WebhookHeadersJson = _webhook.Checked ? NormalizeJsonObject(_webhookHeadersJson.Text) : "{}",
            WebhookBodyTemplate = _webhook.Checked && !string.IsNullOrWhiteSpace(_webhookBodyTemplate.Text)
                ? _webhookBodyTemplate.Text.Trim()
                : new MonitorRule().WebhookBodyTemplate,
            CooldownSeconds = (int)_cooldown.Value,
            MaxConsecutiveNotifications = (int)_maxConsecutiveNotifications.Value,
            UpdatedAt = DateTimeOffset.Now
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private MonitorRuleType SelectedRuleType()
    {
        var text = CurrentSelectText(_ruleType);
        if (text is "文字识别" or "OCR识别")
        {
            return MonitorRuleType.Ocr;
        }

        if (text == "任务栏闪烁")
        {
            return MonitorRuleType.TaskbarFlash;
        }

        if (text == "窗口标题")
        {
            return MonitorRuleType.WindowTitle;
        }

        if (_ruleType.SelectedIndex == 1)
        {
            return MonitorRuleType.Ocr;
        }

        if (_ruleType.SelectedIndex == 2)
        {
            return MonitorRuleType.TaskbarFlash;
        }

        if (_ruleType.SelectedIndex == 0)
        {
            return MonitorRuleType.WindowTitle;
        }

        return Enum.TryParse<MonitorRuleType>(text, out var type)
            ? type
            : MonitorRuleType.WindowTitle;
    }

    private OcrTargetType SelectedOcrTarget()
    {
        var text = CurrentSelectText(_ocrTarget);
        if (text == "窗口")
        {
            return OcrTargetType.Window;
        }

        if (text == "整个桌面")
        {
            return OcrTargetType.Desktop;
        }

        if (_ocrTarget.SelectedIndex == 1)
        {
            return OcrTargetType.Window;
        }

        if (_ocrTarget.SelectedIndex == 0)
        {
            return OcrTargetType.Desktop;
        }

        return Enum.TryParse<OcrTargetType>(text, out var type)
            ? type
            : OcrTargetType.Desktop;
    }

    private WindowSnapshot? SelectedOcrWindow()
    {
        return _ocrWindow.SelectedIndex >= 0 && _ocrWindow.SelectedIndex < _windowItems.Count
            ? _windowItems[_ocrWindow.SelectedIndex].Window
            : null;
    }

    private WindowSnapshot? SelectedFlashWindow()
    {
        return _flashWindow.SelectedIndex >= 0 && _flashWindow.SelectedIndex < _flashItems.Count
            ? _flashItems[_flashWindow.SelectedIndex].Window
            : null;
    }

    private void UpdateRegionText()
    {
        _region.Text = _ocrRegion is null
            ? "全部区域"
            : $"{_ocrRegion.Value.X},{_ocrRegion.Value.Y} {_ocrRegion.Value.Width}x{_ocrRegion.Value.Height}";
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

    private static string OcrTargetText(OcrTargetType type)
    {
        return type == OcrTargetType.Window ? "窗口" : "整个桌面";
    }

    private static void SelectItemText(AntSelect select, string text)
    {
        select.Text = text;
        for (var index = 0; index < select.Items.Count; index++)
        {
            if (string.Equals(select.Items[index]?.ToString(), text, StringComparison.Ordinal))
            {
                select.SelectedIndex = index;
                return;
            }
        }
    }

    private static string CurrentSelectText(AntSelect select)
    {
        if (select.SelectedValue is { } selectedValue)
        {
            var value = selectedValue.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(select.Text))
        {
            return select.Text.Trim();
        }

        if (select.SelectedIndex >= 0 && select.SelectedIndex < select.Items.Count)
        {
            return select.Items[select.SelectedIndex]?.ToString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private void AddRow(string key, string label, Control editor, int height)
    {
        var row = new Panel
        {
            Height = height,
            Width = Math.Max(640, _body.ClientSize.Width - 36),
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Color.White
        };
        var labelControl = new Label
        {
            Text = label,
            Dock = DockStyle.Left,
            Width = 166,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(75, 85, 99),
            BackColor = Color.White
        };
        editor.Left = labelControl.Width;
        editor.Top = 2;
        editor.Width = row.Width - labelControl.Width - 8;
        editor.Height = Math.Max(28, height - 4);
        editor.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

        row.Controls.Add(editor);
        row.Controls.Add(labelControl);
        _body.Controls.Add(row);
        _rows[key] = row;
    }

    private void SetRowVisible(string key, bool visible)
    {
        if (_rows.TryGetValue(key, out var row))
        {
            row.Visible = visible;
        }
    }

    private void ResizeRows()
    {
        var width = Math.Max(640, _body.ClientSize.Width - 36);
        foreach (var row in _rows.Values)
        {
            row.Width = width;
            foreach (Control control in row.Controls)
            {
                if (control is Label)
                {
                    continue;
                }

                control.Width = width - 174;
            }
        }
    }

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        flow.Controls.AddRange(controls);
        return flow;
    }

    private static bool IsValidJsonObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeJsonObject(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? "{}" : text.Trim();
    }

    private static bool IsValidWebhookUrl(string text)
    {
        return Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https";
    }
}
