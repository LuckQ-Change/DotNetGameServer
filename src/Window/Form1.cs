using DotNetGameServer;
using LoginServer;
using BattleServer;
using ResourceServer;
using LogicServer;
using Core;
using DirectoryServer;
using Microsoft.Extensions.Logging;
using Window.Logging;

namespace Window;

public partial class Form1 : Form
{
    private LoginGameServer? _loginServer;
    private BattleGameServer? _battleServer;
    private ResourceGameServer? _resourceServer;
    private DirectoryGameServer? _directoryServer;
    private LogManager _logManager = new LogManager();
    private FolderBrowserDialog _folderBrowser = new FolderBrowserDialog();
    private bool _draggingTab = false;
    private TabPage? _dragTab = null;
    private Point _dragStart;
    private ILoggerFactory _loggerFactory;

    public Form1()
    {
        InitializeComponent();
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new UiLoggerProvider(message => Log(message)));
        });
    }

    private async void btnLoginStart_Click(object? sender, EventArgs e)
    {
        if (!int.TryParse(txtLoginPort.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("请输入有效端口(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        
        try
        {
            _loginServer = new LoginGameServer(_loggerFactory.CreateLogger("Login"));
            await _loginServer.StartAsync(port);
            btnLoginStart.Enabled = false;
            btnLoginStop.Enabled = true;
            btnLoginRestart.Enabled = true;
            Log($"[Login] 服务启动 @ {port}");
        }
        catch (Exception ex)
        {
            Log($"[Login] 启动失败: {ex.Message}");
            MessageBox.Show($"登录服务启动失败: {ex.Message}", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnLoginStop_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_loginServer != null)
            {
                await _loginServer.StopAsync();
            }
            _loginServer = null;
            btnLoginStart.Enabled = true;
            btnLoginStop.Enabled = false;
            btnLoginRestart.Enabled = false;
            Log("[Login] 服务停止");
        }
        catch (Exception ex)
        {
            Log($"[Login] 停止失败: {ex.Message}");
        }
    }

    private async void btnBattleStart_Click(object? sender, EventArgs e)
    {
        if (!int.TryParse(txtBattlePort.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("请输入有效端口(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        
        try
        {
            _battleServer = new BattleGameServer(_loggerFactory.CreateLogger("Battle"));
            await _battleServer.StartAsync(port);
            btnBattleStart.Enabled = false;
            btnBattleStop.Enabled = true;
            btnBattleRestart.Enabled = true;
            Log($"[Battle] 服务启动 @ {port}");
        }
        catch (Exception ex)
        {
            Log($"[Battle] 启动失败: {ex.Message}");
            MessageBox.Show($"战斗服务启动失败: {ex.Message}", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnBattleStop_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_battleServer != null)
            {
                await _battleServer.StopAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"[Battle] 停止失败: {ex.Message}");
        }
        btnBattleStart.Enabled = true;
        btnBattleStop.Enabled = false;
        btnBattleRestart.Enabled = false;
        Log("[Battle] 服务停止");
    }

    private async void btnDirectoryStart_Click(object? sender, EventArgs e)
    {
        if (!int.TryParse(txtDirectoryPort.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("请输入有效端口(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            _directoryServer = new DirectoryGameServer(_loggerFactory.CreateLogger("Directory"));
            await _directoryServer.StartAsync(port);
            btnDirectoryStart.Enabled = false;
            btnDirectoryStop.Enabled = true;
            btnDirectoryRestart.Enabled = true;
            Log($"[Directory] 服务启动 @ {port}");
        }
        catch (Exception ex)
        {
            Log($"[Directory] 启动失败: {ex.Message}");
            MessageBox.Show($"目录服务启动失败: {ex.Message}", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnDirectoryStop_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_directoryServer != null)
            {
                await _directoryServer.StopAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"[Directory] 停止失败: {ex.Message}");
        }
        btnDirectoryStart.Enabled = true;
        btnDirectoryStop.Enabled = false;
        btnDirectoryRestart.Enabled = false;
        Log("[Directory] 服务停止");
    }

    private void Log(string message)
    {
        var service = "System";
        var text = message;
        if (message.StartsWith("["))
        {
            var idx = message.IndexOf(']');
            if (idx > 1)
            {
                service = message.Substring(1, idx - 1);
                text = message.Substring(idx + 1).TrimStart();
            }
        }
        _logManager.Append(service, text);
        RefreshLogText();
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        txtLog.AppendText(line + Environment.NewLine);
    }

    private void RefreshLogText()
    {
        if (txtLog.InvokeRequired)
        {
            txtLog.BeginInvoke(new Action(RefreshLogText));
            return;
        }
        var filter = cmbLogFilter.SelectedItem?.ToString() ?? "All";
        txtLog.Text = _logManager.ToDisplayText(filter);
        txtLog.SelectionStart = txtLog.Text.Length;
        txtLog.ScrollToCaret();
    }

    private void cmbLogFilter_SelectedIndexChanged(object? sender, EventArgs e)
    {
        RefreshLogText();
    }

    private void btnClearLog_Click(object? sender, EventArgs e)
    {
        _logManager.Clear();
        RefreshLogText();
    }

    private void btnSaveLog_Click(object? sender, EventArgs e)
    {
        var filter = cmbLogFilter.SelectedItem?.ToString() ?? "All";
        var path = _logManager.SaveToFile(filter);
        MessageBox.Show($"日志已保存: {path}", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private int GetTabIndexAt(Point clientPoint)
    {
        for (int i = 0; i < tabServices.TabPages.Count; i++)
        {
            if (tabServices.GetTabRect(i).Contains(clientPoint)) return i;
        }
        return -1;
    }

    private void tabServices_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var idx = GetTabIndexAt(e.Location);
        if (idx >= 0)
        {
            _draggingTab = true;
            _dragTab = tabServices.TabPages[idx];
            _dragStart = e.Location;
        }
    }

    private void tabServices_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_draggingTab || _dragTab == null || e.Button != MouseButtons.Left) return;
        var overIndex = GetTabIndexAt(e.Location);
        if (overIndex >= 0)
        {
            var currentIndex = tabServices.TabPages.IndexOf(_dragTab);
            if (overIndex != currentIndex)
            {
                tabServices.TabPages.Remove(_dragTab);
                tabServices.TabPages.Insert(overIndex, _dragTab);
                tabServices.SelectedTab = _dragTab;
            }
        }
    }

    private void tabServices_MouseUp(object? sender, MouseEventArgs e)
    {
        if (!_draggingTab) return;
        _draggingTab = false;
        // 不再支持拖出分离窗口，仅结束拖拽并清理状态
        _dragTab = null;
    }

    private void DetachTabToWindow(TabPage page, Point screenPoint)
    {
        // 迁移控件到浮动窗口
        var hostPanel = new Panel { Dock = DockStyle.Fill };
        var controls = page.Controls.Cast<Control>().ToArray();
        page.Controls.Clear();
        foreach (var c in controls) hostPanel.Controls.Add(c);

        var floatForm = new Form
        {
            Text = page.Text,
            StartPosition = FormStartPosition.Manual,
            Location = screenPoint,
            Size = new Size(600, 300)
        };
        var dockButton = new Button { Text = "停靠", Dock = DockStyle.Top, Height = 28 };

        // 抽取回停靠逻辑，供按钮与关闭事件复用
        Action dockBack = () =>
        {
            if (hostPanel.Controls.Count == 0) return;
            var newPage = new TabPage(page.Text);
            var movedControls = hostPanel.Controls.Cast<Control>().ToArray();
            hostPanel.Controls.Clear();
            foreach (var c in movedControls) newPage.Controls.Add(c);
            tabServices.TabPages.Add(newPage);
            tabServices.SelectedTab = newPage;
        };

        dockButton.Click += (s, e) =>
        {
            dockBack();
            floatForm.Close();
        };

        // 用户关闭浮动窗口时自动停靠回主界面
        floatForm.FormClosing += (s, e) =>
        {
            // 若已被停靠（hostPanel为空），不重复处理
            if (hostPanel.Controls.Count > 0)
            {
                dockBack();
            }
        };
        floatForm.Controls.Add(hostPanel);
        floatForm.Controls.Add(dockButton);
        floatForm.Show();

        // 从主 TabControl 移除原标签页
        tabServices.TabPages.Remove(page);
    }

    private async void btnResourceStart_Click(object? sender, EventArgs e)
    {
        if (!int.TryParse(txtResourcePort.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("请输入有效端口(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var root = txtResourceRoot.Text?.Trim();
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "Resources");
        }
        if (!Directory.Exists(root))
        {
            Log($"[Resource] 资源目录不存在，将使用空清单: {root}");
        }
        try
        {
            _resourceServer = new ResourceGameServer(_loggerFactory.CreateLogger("Resource"), root);
            await _resourceServer.StartAsync(port);
            btnResourceStart.Enabled = false;
            btnResourceStop.Enabled = true;
            btnResourceRestart.Enabled = true;
            Log($"[Resource] 服务启动 @ {port}, 根目录={root}");
        }
        catch (Exception ex)
        {
            Log($"[Resource] 启动失败: {ex.Message}");
            MessageBox.Show($"资源服务启动失败: {ex.Message}", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnResourceStop_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_resourceServer != null)
            {
                await _resourceServer.StopAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"[Resource] 停止失败: {ex.Message}");
        }
        btnResourceStart.Enabled = true;
        btnResourceStop.Enabled = false;
        btnResourceRestart.Enabled = false;
        Log("[Resource] 服务停止");

    }

    private async void btnLoginRestart_Click(object? sender, EventArgs e)
    {
        if (!int.TryParse(txtLoginPort.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("请输入有效端口(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            btnLoginRestart.Enabled = false;
            if (_loginServer != null)
            {
                await _loginServer.StopAsync();
            }
            _loginServer = new LoginGameServer(msg => Log($"[Login] {msg}"));
            await _loginServer.StartAsync(port);
            btnLoginStart.Enabled = false;
            btnLoginStop.Enabled = true;
            btnLoginRestart.Enabled = true;
            Log($"[Login] 服务重启 @ {port}");

        }
        catch (Exception ex)
        {
            Log($"[Login] 重启失败: {ex.Message}");
            MessageBox.Show($"登录服务重启失败: {ex.Message}", "重启失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnBattleRestart_Click(object? sender, EventArgs e)
    {
        if (!int.TryParse(txtBattlePort.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("请输入有效端口(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            btnBattleRestart.Enabled = false;
            if (_battleServer != null)
            {
                await _battleServer.StopAsync();
            }
            _battleServer = new BattleGameServer(msg => Log($"[Battle] {msg}"));
            await _battleServer.StartAsync(port);
            btnBattleStart.Enabled = false;
            btnBattleStop.Enabled = true;
            btnBattleRestart.Enabled = true;
            Log($"[Battle] 服务重启 @ {port}");

        }
        catch (Exception ex)
        {
            Log($"[Battle] 重启失败: {ex.Message}");
            MessageBox.Show($"战斗服务重启失败: {ex.Message}", "重启失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnResourceRestart_Click(object? sender, EventArgs e)
    {
        if (!int.TryParse(txtResourcePort.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("请输入有效端口(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var root = txtResourceRoot.Text?.Trim();
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "Resources");
        }
        try
        {
            btnResourceRestart.Enabled = false;
            if (_resourceServer != null)
            {
                await _resourceServer.StopAsync();
            }
            _resourceServer = new ResourceGameServer(msg => Log($"[Resource] {msg}"), root);
            await _resourceServer.StartAsync(port);
            btnResourceStart.Enabled = false;
            btnResourceStop.Enabled = true;
            btnResourceRestart.Enabled = true;
            Log($"[Resource] 服务重启 @ {port}, 根目录={root}");

        }
        catch (Exception ex)
        {
            Log($"[Resource] 重启失败: {ex.Message}");
            MessageBox.Show($"资源服务重启失败: {ex.Message}", "重启失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnDirectoryRestart_Click(object? sender, EventArgs e)
    {
        if (!int.TryParse(txtDirectoryPort.Text, out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("请输入有效端口(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            btnDirectoryRestart.Enabled = false;
            if (_directoryServer != null)
            {
                await _directoryServer.StopAsync();
            }
            _directoryServer = new DirectoryGameServer(msg => Log($"[Directory] {msg}"));
            await _directoryServer.StartAsync(port);
            btnDirectoryStart.Enabled = false;
            btnDirectoryStop.Enabled = true;
            btnDirectoryRestart.Enabled = true;
            Log($"[Directory] 服务重启 @ {port}");
        }
        catch (Exception ex)
        {
            Log($"[Directory] 重启失败: {ex.Message}");
            MessageBox.Show($"目录服务重启失败: {ex.Message}", "重启失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnResourceBrowse_Click(object? sender, EventArgs e)
    {
        _folderBrowser.Description = "选择资源根目录";
        _folderBrowser.UseDescriptionForTitle = true;
        if (_folderBrowser.ShowDialog(this) == DialogResult.OK)
        {
            txtResourceRoot.Text = _folderBrowser.SelectedPath;
        }
    }

    // 目录心跳现已由各服务内置的 HeartbeatClientComponent 负责发起和停止

    private async void btnStartAll_Click(object? sender, EventArgs e)
    {
        // 校验端口输入
        if (!int.TryParse(txtDirectoryPort.Text, out var dirPort) || dirPort <= 0 || dirPort > 65535)
        {
            MessageBox.Show("目录端口无效(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!int.TryParse(txtLoginPort.Text, out var loginPort) || loginPort <= 0 || loginPort > 65535)
        {
            MessageBox.Show("登录端口无效(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!int.TryParse(txtBattlePort.Text, out var battlePort) || battlePort <= 0 || battlePort > 65535)
        {
            MessageBox.Show("战斗端口无效(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!int.TryParse(txtResourcePort.Text, out var resourcePort) || resourcePort <= 0 || resourcePort > 65535)
        {
            MessageBox.Show("资源端口无效(1-65535)", "无效端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var root = txtResourceRoot.Text?.Trim();
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "Resources");
        }
        if (!Directory.Exists(root))
        {
            Log($"[Resource] 资源目录不存在，将使用空清单: {root}");
        }

        // 依次启动：Directory → Login → Battle → Resource
        try
        {
            if (_directoryServer == null || _directoryServer.ListeningPort == null)
            {
                _directoryServer = new DirectoryGameServer(_loggerFactory.CreateLogger("Directory"));
                await _directoryServer.StartAsync(dirPort);
                btnDirectoryStart.Enabled = false;
                btnDirectoryStop.Enabled = true;
                btnDirectoryRestart.Enabled = true;
                Log($"[Directory] 服务启动 @ {dirPort}");
            }
        }
        catch (Exception ex)
        {
            Log($"[Directory] 启动失败: {ex.Message}");
            MessageBox.Show($"目录服务启动失败: {ex.Message}", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        try
        {
            if (_loginServer == null || _loginServer.ListeningPort == null)
            {
                _loginServer = new LoginGameServer(_loggerFactory.CreateLogger("Login"));
                await _loginServer.StartAsync(loginPort);
                btnLoginStart.Enabled = false;
                btnLoginStop.Enabled = true;
                btnLoginRestart.Enabled = true;
                Log($"[Login] 服务启动 @ {loginPort}");
            }
        }
        catch (Exception ex)
        {
            Log($"[Login] 启动失败: {ex.Message}");
            MessageBox.Show($"登录服务启动失败: {ex.Message}", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        try
        {
            if (_battleServer == null || _battleServer.ListeningPort == null)
            {
                _battleServer = new BattleGameServer(_loggerFactory.CreateLogger("Battle"));
                await _battleServer.StartAsync(battlePort);
                btnBattleStart.Enabled = false;
                btnBattleStop.Enabled = true;
                btnBattleRestart.Enabled = true;
                Log($"[Battle] 服务启动 @ {battlePort}");
            }
        }
        catch (Exception ex)
        {
            Log($"[Battle] 启动失败: {ex.Message}");
            MessageBox.Show($"战斗服务启动失败: {ex.Message}", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        try
        {
            if (_resourceServer == null || _resourceServer.ListeningPort == null)
            {
                _resourceServer = new ResourceGameServer(_loggerFactory.CreateLogger("Resource"), root);
                await _resourceServer.StartAsync(resourcePort);
                btnResourceStart.Enabled = false;
                btnResourceStop.Enabled = true;
                btnResourceRestart.Enabled = true;
                Log($"[Resource] 服务启动 @ {resourcePort}, 根目录={root}");
            }
        }
        catch (Exception ex)
        {
            Log($"[Resource] 启动失败: {ex.Message}");
            MessageBox.Show($"资源服务启动失败: {ex.Message}", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnStopAll_Click(object? sender, EventArgs e)
    {
        // 依次停止：Resource → Battle → Login → Directory（顺序随意，逐个尝试）
        try
        {
            if (_resourceServer != null && _resourceServer.ListeningPort != null)
            {
                await _resourceServer.StopAsync();
                Log("[Resource] 服务停止");
            }
            btnResourceStart.Enabled = true;
            btnResourceStop.Enabled = false;
            btnResourceRestart.Enabled = false;
        }
        catch (Exception ex)
        {
            Log($"[Resource] 停止失败: {ex.Message}");
        }

        try
        {
            if (_battleServer != null && _battleServer.ListeningPort != null)
            {
                await _battleServer.StopAsync();
                Log("[Battle] 服务停止");
            }
            btnBattleStart.Enabled = true;
            btnBattleStop.Enabled = false;
            btnBattleRestart.Enabled = false;
        }
        catch (Exception ex)
        {
            Log($"[Battle] 停止失败: {ex.Message}");
        }

        try
        {
            if (_loginServer != null && _loginServer.ListeningPort != null)
            {
                await _loginServer.StopAsync();
                Log("[Login] 服务停止");
            }
            _loginServer = null;
            btnLoginStart.Enabled = true;
            btnLoginStop.Enabled = false;
            btnLoginRestart.Enabled = false;
        }
        catch (Exception ex)
        {
            Log($"[Login] 停止失败: {ex.Message}");
        }

        try
        {
            if (_directoryServer != null && _directoryServer.ListeningPort != null)
            {
                await _directoryServer.StopAsync();
                Log("[Directory] 服务停止");
            }
            btnDirectoryStart.Enabled = true;
            btnDirectoryStop.Enabled = false;
            btnDirectoryRestart.Enabled = false;
        }
        catch (Exception ex)
        {
            Log($"[Directory] 停止失败: {ex.Message}");
        }
    }
}
