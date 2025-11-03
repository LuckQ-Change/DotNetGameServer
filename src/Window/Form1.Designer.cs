namespace Window;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private System.Windows.Forms.TabControl tabServices;
    private System.Windows.Forms.TabPage tabLogin;
    private System.Windows.Forms.TabPage tabBattle;
    private System.Windows.Forms.TabPage tabResource;
    private System.Windows.Forms.TabPage tabDirectory;
    private System.Windows.Forms.Label lblLoginPort;
    private System.Windows.Forms.TextBox txtLoginPort;
    private System.Windows.Forms.Button btnLoginStart;
    private System.Windows.Forms.Button btnLoginStop;
    private System.Windows.Forms.Button btnLoginRestart;
    private System.Windows.Forms.Label lblBattlePort;
    private System.Windows.Forms.TextBox txtBattlePort;
    private System.Windows.Forms.Button btnBattleStart;
    private System.Windows.Forms.Button btnBattleStop;
    private System.Windows.Forms.Button btnBattleRestart;
    private System.Windows.Forms.Label lblResourcePort;
    private System.Windows.Forms.TextBox txtResourcePort;
    private System.Windows.Forms.Button btnResourceStart;
    private System.Windows.Forms.Button btnResourceStop;
    private System.Windows.Forms.Button btnResourceRestart;
    private System.Windows.Forms.Label lblResourceRoot;
    private System.Windows.Forms.TextBox txtResourceRoot;
    private System.Windows.Forms.Button btnResourceBrowse;
    private System.Windows.Forms.Label lblDirectoryPort;
    private System.Windows.Forms.TextBox txtDirectoryPort;
    private System.Windows.Forms.Button btnDirectoryStart;
    private System.Windows.Forms.Button btnDirectoryStop;
    private System.Windows.Forms.Button btnDirectoryRestart;
    private System.Windows.Forms.ComboBox cmbLogFilter;
    private System.Windows.Forms.Button btnClearLog;
    private System.Windows.Forms.Button btnSaveLog;
    private System.Windows.Forms.TextBox txtLog;
    private System.Windows.Forms.Button btnStartAll;
    private System.Windows.Forms.Button btnStopAll;

    private void InitializeComponent()
    {
        this.tabServices = new System.Windows.Forms.TabControl();
        this.tabLogin = new System.Windows.Forms.TabPage();
        this.tabBattle = new System.Windows.Forms.TabPage();
        this.tabDirectory = new System.Windows.Forms.TabPage();
        this.lblLoginPort = new System.Windows.Forms.Label();
        this.txtLoginPort = new System.Windows.Forms.TextBox();
        this.btnLoginStart = new System.Windows.Forms.Button();
        this.btnLoginStop = new System.Windows.Forms.Button();
        this.btnLoginRestart = new System.Windows.Forms.Button();
        this.lblBattlePort = new System.Windows.Forms.Label();
        this.txtBattlePort = new System.Windows.Forms.TextBox();
        this.btnBattleStart = new System.Windows.Forms.Button();
        this.btnBattleStop = new System.Windows.Forms.Button();
        this.btnBattleRestart = new System.Windows.Forms.Button();
        this.tabResource = new System.Windows.Forms.TabPage();
        this.lblResourcePort = new System.Windows.Forms.Label();
        this.txtResourcePort = new System.Windows.Forms.TextBox();
        this.btnResourceStart = new System.Windows.Forms.Button();
        this.btnResourceStop = new System.Windows.Forms.Button();
        this.btnResourceRestart = new System.Windows.Forms.Button();
        this.lblResourceRoot = new System.Windows.Forms.Label();
        this.txtResourceRoot = new System.Windows.Forms.TextBox();
        this.btnResourceBrowse = new System.Windows.Forms.Button();
        this.lblDirectoryPort = new System.Windows.Forms.Label();
        this.txtDirectoryPort = new System.Windows.Forms.TextBox();
        this.btnDirectoryStart = new System.Windows.Forms.Button();
        this.btnDirectoryStop = new System.Windows.Forms.Button();
        this.btnDirectoryRestart = new System.Windows.Forms.Button();
        this.cmbLogFilter = new System.Windows.Forms.ComboBox();
        this.btnClearLog = new System.Windows.Forms.Button();
        this.btnSaveLog = new System.Windows.Forms.Button();
        this.txtLog = new System.Windows.Forms.TextBox();
        this.btnStartAll = new System.Windows.Forms.Button();
        this.btnStopAll = new System.Windows.Forms.Button();
        this.tabServices.SuspendLayout();
        this.tabDirectory.SuspendLayout();
        this.tabLogin.SuspendLayout();
        this.tabBattle.SuspendLayout();
        this.tabResource.SuspendLayout();
        this.SuspendLayout();
        // 
        // tabServices
        // 
        this.tabServices.Controls.Add(this.tabLogin);
        this.tabServices.Controls.Add(this.tabBattle);
        this.tabServices.Controls.Add(this.tabDirectory);
        this.tabServices.Controls.Add(this.tabResource);
        this.tabServices.Location = new System.Drawing.Point(12, 12);
        this.tabServices.Name = "tabServices";
        this.tabServices.SelectedIndex = 0;
        this.tabServices.Size = new System.Drawing.Size(776, 200);
        this.tabServices.TabIndex = 0;
        this.tabServices.MouseDown += new System.Windows.Forms.MouseEventHandler(this.tabServices_MouseDown);
        this.tabServices.MouseMove += new System.Windows.Forms.MouseEventHandler(this.tabServices_MouseMove);
        this.tabServices.MouseUp += new System.Windows.Forms.MouseEventHandler(this.tabServices_MouseUp);
        // 
        // tabLogin
        // 
        this.tabLogin.Controls.Add(this.lblLoginPort);
        this.tabLogin.Controls.Add(this.txtLoginPort);
        this.tabLogin.Controls.Add(this.btnLoginStart);
        this.tabLogin.Controls.Add(this.btnLoginStop);
        this.tabLogin.Controls.Add(this.btnLoginRestart);
        this.tabLogin.Location = new System.Drawing.Point(4, 26);
        this.tabLogin.Name = "tabLogin";
        this.tabLogin.Padding = new System.Windows.Forms.Padding(3);
        this.tabLogin.Size = new System.Drawing.Size(768, 170);
        this.tabLogin.TabIndex = 0;
        this.tabLogin.Text = "Login";
        this.tabLogin.UseVisualStyleBackColor = true;
        // 
        // lblLoginPort
        // 
        this.lblLoginPort.AutoSize = true;
        this.lblLoginPort.Location = new System.Drawing.Point(10, 15);
        this.lblLoginPort.Name = "lblLoginPort";
        this.lblLoginPort.Size = new System.Drawing.Size(44, 17);
        this.lblLoginPort.TabIndex = 0;
        this.lblLoginPort.Text = "端口";
        // 
        // txtLoginPort
        // 
        this.txtLoginPort.Location = new System.Drawing.Point(60, 12);
        this.txtLoginPort.Name = "txtLoginPort";
        this.txtLoginPort.Size = new System.Drawing.Size(80, 23);
        this.txtLoginPort.TabIndex = 1;
        this.txtLoginPort.Text = "5000";
        // 
        // btnLoginStart
        // 
        this.btnLoginStart.Location = new System.Drawing.Point(160, 11);
        this.btnLoginStart.Name = "btnLoginStart";
        this.btnLoginStart.Size = new System.Drawing.Size(90, 25);
        this.btnLoginStart.TabIndex = 2;
        this.btnLoginStart.Text = "启动";
        this.btnLoginStart.UseVisualStyleBackColor = true;
        this.btnLoginStart.Click += new System.EventHandler(this.btnLoginStart_Click);
        // 
        // btnLoginStop
        // 
        this.btnLoginStop.Location = new System.Drawing.Point(260, 11);
        this.btnLoginStop.Name = "btnLoginStop";
        this.btnLoginStop.Size = new System.Drawing.Size(90, 25);
        this.btnLoginStop.TabIndex = 3;
        this.btnLoginStop.Text = "停止";
        this.btnLoginStop.UseVisualStyleBackColor = true;
        this.btnLoginStop.Enabled = false;
        this.btnLoginStop.Click += new System.EventHandler(this.btnLoginStop_Click);
        // 
        // btnLoginRestart
        // 
        this.btnLoginRestart.Location = new System.Drawing.Point(360, 11);
        this.btnLoginRestart.Name = "btnLoginRestart";
        this.btnLoginRestart.Size = new System.Drawing.Size(90, 25);
        this.btnLoginRestart.TabIndex = 4;
        this.btnLoginRestart.Text = "重启";
        this.btnLoginRestart.UseVisualStyleBackColor = true;
        this.btnLoginRestart.Enabled = false;
        this.btnLoginRestart.Click += new System.EventHandler(this.btnLoginRestart_Click);
        // 
        // tabBattle
        // 
        this.tabBattle.Controls.Add(this.lblBattlePort);
        this.tabBattle.Controls.Add(this.txtBattlePort);
        this.tabBattle.Controls.Add(this.btnBattleStart);
        this.tabBattle.Controls.Add(this.btnBattleStop);
        this.tabBattle.Controls.Add(this.btnBattleRestart);
        this.tabBattle.Location = new System.Drawing.Point(4, 26);
        this.tabBattle.Name = "tabBattle";
        this.tabBattle.Padding = new System.Windows.Forms.Padding(3);
        this.tabBattle.Size = new System.Drawing.Size(768, 170);
        this.tabBattle.TabIndex = 1;
        this.tabBattle.Text = "Battle";
        this.tabBattle.UseVisualStyleBackColor = true;
        // 
        // tabDirectory
        // 
        this.tabDirectory.Controls.Add(this.lblDirectoryPort);
        this.tabDirectory.Controls.Add(this.txtDirectoryPort);
        this.tabDirectory.Controls.Add(this.btnDirectoryStart);
        this.tabDirectory.Controls.Add(this.btnDirectoryStop);
        this.tabDirectory.Controls.Add(this.btnDirectoryRestart);
        this.tabDirectory.Location = new System.Drawing.Point(4, 26);
        this.tabDirectory.Name = "tabDirectory";
        this.tabDirectory.Padding = new System.Windows.Forms.Padding(3);
        this.tabDirectory.Size = new System.Drawing.Size(768, 170);
        this.tabDirectory.TabIndex = 2;
        this.tabDirectory.Text = "Directory";
        this.tabDirectory.UseVisualStyleBackColor = true;
        // 
        // tabResource
        // 
        this.tabResource.Controls.Add(this.lblResourcePort);
        this.tabResource.Controls.Add(this.txtResourcePort);
        this.tabResource.Controls.Add(this.btnResourceStart);
        this.tabResource.Controls.Add(this.btnResourceStop);
        this.tabResource.Controls.Add(this.btnResourceRestart);
        this.tabResource.Controls.Add(this.lblResourceRoot);
        this.tabResource.Controls.Add(this.txtResourceRoot);
        this.tabResource.Controls.Add(this.btnResourceBrowse);
        this.tabResource.Location = new System.Drawing.Point(4, 26);
        this.tabResource.Name = "tabResource";
        this.tabResource.Padding = new System.Windows.Forms.Padding(3);
        this.tabResource.Size = new System.Drawing.Size(768, 170);
        this.tabResource.TabIndex = 2;
        this.tabResource.Text = "Resource";
        this.tabResource.UseVisualStyleBackColor = true;
        // 
        // lblResourcePort
        // 
        this.lblResourcePort.AutoSize = true;
        this.lblResourcePort.Location = new System.Drawing.Point(10, 15);
        this.lblResourcePort.Name = "lblResourcePort";
        this.lblResourcePort.Size = new System.Drawing.Size(44, 17);
        this.lblResourcePort.TabIndex = 0;
        this.lblResourcePort.Text = "端口";
        // 
        // txtResourcePort
        // 
        this.txtResourcePort.Location = new System.Drawing.Point(60, 12);
        this.txtResourcePort.Name = "txtResourcePort";
        this.txtResourcePort.Size = new System.Drawing.Size(80, 23);
        this.txtResourcePort.TabIndex = 1;
        this.txtResourcePort.Text = "7000";
        // 
        // btnResourceStart
        // 
        this.btnResourceStart.Location = new System.Drawing.Point(160, 11);
        this.btnResourceStart.Name = "btnResourceStart";
        this.btnResourceStart.Size = new System.Drawing.Size(90, 25);
        this.btnResourceStart.TabIndex = 2;
        this.btnResourceStart.Text = "启动";
        this.btnResourceStart.UseVisualStyleBackColor = true;
        this.btnResourceStart.Click += new System.EventHandler(this.btnResourceStart_Click);
        // 
        // btnResourceStop
        // 
        this.btnResourceStop.Location = new System.Drawing.Point(260, 11);
        this.btnResourceStop.Name = "btnResourceStop";
        this.btnResourceStop.Size = new System.Drawing.Size(90, 25);
        this.btnResourceStop.TabIndex = 3;
        this.btnResourceStop.Text = "停止";
        this.btnResourceStop.UseVisualStyleBackColor = true;
        this.btnResourceStop.Enabled = false;
        this.btnResourceStop.Click += new System.EventHandler(this.btnResourceStop_Click);
        // 
        // btnResourceRestart
        // 
        this.btnResourceRestart.Location = new System.Drawing.Point(360, 11);
        this.btnResourceRestart.Name = "btnResourceRestart";
        this.btnResourceRestart.Size = new System.Drawing.Size(90, 25);
        this.btnResourceRestart.TabIndex = 4;
        this.btnResourceRestart.Text = "重启";
        this.btnResourceRestart.UseVisualStyleBackColor = true;
        this.btnResourceRestart.Enabled = false;
        this.btnResourceRestart.Click += new System.EventHandler(this.btnResourceRestart_Click);
        // 
        // lblResourceRoot
        // 
        this.lblResourceRoot.AutoSize = true;
        this.lblResourceRoot.Location = new System.Drawing.Point(10, 50);
        this.lblResourceRoot.Name = "lblResourceRoot";
        this.lblResourceRoot.Size = new System.Drawing.Size(68, 17);
        this.lblResourceRoot.TabIndex = 4;
        this.lblResourceRoot.Text = "资源目录";
        // 
        // txtResourceRoot
        // 
        this.txtResourceRoot.Location = new System.Drawing.Point(84, 47);
        this.txtResourceRoot.Name = "txtResourceRoot";
        this.txtResourceRoot.Size = new System.Drawing.Size(480, 23);
        this.txtResourceRoot.TabIndex = 5;
        // 
        // btnResourceBrowse
        // 
        this.btnResourceBrowse.Location = new System.Drawing.Point(580, 46);
        this.btnResourceBrowse.Name = "btnResourceBrowse";
        this.btnResourceBrowse.Size = new System.Drawing.Size(80, 25);
        this.btnResourceBrowse.TabIndex = 6;
        this.btnResourceBrowse.Text = "浏览...";
        this.btnResourceBrowse.UseVisualStyleBackColor = true;
        this.btnResourceBrowse.Click += new System.EventHandler(this.btnResourceBrowse_Click);
        // 
        // lblBattlePort
        // 
        this.lblBattlePort.AutoSize = true;
        this.lblBattlePort.Location = new System.Drawing.Point(10, 15);
        this.lblBattlePort.Name = "lblBattlePort";
        this.lblBattlePort.Size = new System.Drawing.Size(44, 17);
        this.lblBattlePort.TabIndex = 0;
        this.lblBattlePort.Text = "端口";
        // 
        // txtBattlePort
        // 
        this.txtBattlePort.Location = new System.Drawing.Point(60, 12);
        this.txtBattlePort.Name = "txtBattlePort";
        this.txtBattlePort.Size = new System.Drawing.Size(80, 23);
        this.txtBattlePort.TabIndex = 1;
        this.txtBattlePort.Text = "6000";
        // 
        // btnBattleStart
        // 
        this.btnBattleStart.Location = new System.Drawing.Point(160, 11);
        this.btnBattleStart.Name = "btnBattleStart";
        this.btnBattleStart.Size = new System.Drawing.Size(90, 25);
        this.btnBattleStart.TabIndex = 2;
        this.btnBattleStart.Text = "启动";
        this.btnBattleStart.UseVisualStyleBackColor = true;
        this.btnBattleStart.Click += new System.EventHandler(this.btnBattleStart_Click);
        // 
        // btnBattleStop
        // 
        this.btnBattleStop.Location = new System.Drawing.Point(260, 11);
        this.btnBattleStop.Name = "btnBattleStop";
        this.btnBattleStop.Size = new System.Drawing.Size(90, 25);
        this.btnBattleStop.TabIndex = 3;
        this.btnBattleStop.Text = "停止";
        this.btnBattleStop.UseVisualStyleBackColor = true;
        this.btnBattleStop.Enabled = false;
        this.btnBattleStop.Click += new System.EventHandler(this.btnBattleStop_Click);
        // 
        // btnBattleRestart
        // 
        this.btnBattleRestart.Location = new System.Drawing.Point(360, 11);
        this.btnBattleRestart.Name = "btnBattleRestart";
        this.btnBattleRestart.Size = new System.Drawing.Size(90, 25);
        this.btnBattleRestart.TabIndex = 4;
        this.btnBattleRestart.Text = "重启";
        this.btnBattleRestart.UseVisualStyleBackColor = true;
        this.btnBattleRestart.Enabled = false;
        this.btnBattleRestart.Click += new System.EventHandler(this.btnBattleRestart_Click);
        // 
        // lblDirectoryPort
        // 
        this.lblDirectoryPort.AutoSize = true;
        this.lblDirectoryPort.Location = new System.Drawing.Point(10, 15);
        this.lblDirectoryPort.Name = "lblDirectoryPort";
        this.lblDirectoryPort.Size = new System.Drawing.Size(44, 17);
        this.lblDirectoryPort.TabIndex = 0;
        this.lblDirectoryPort.Text = "端口";
        // 
        // txtDirectoryPort
        // 
        this.txtDirectoryPort.Location = new System.Drawing.Point(60, 12);
        this.txtDirectoryPort.Name = "txtDirectoryPort";
        this.txtDirectoryPort.Size = new System.Drawing.Size(80, 23);
        this.txtDirectoryPort.TabIndex = 1;
        this.txtDirectoryPort.Text = "5500";
        // 
        // btnDirectoryStart
        // 
        this.btnDirectoryStart.Location = new System.Drawing.Point(160, 11);
        this.btnDirectoryStart.Name = "btnDirectoryStart";
        this.btnDirectoryStart.Size = new System.Drawing.Size(90, 25);
        this.btnDirectoryStart.TabIndex = 2;
        this.btnDirectoryStart.Text = "启动";
        this.btnDirectoryStart.UseVisualStyleBackColor = true;
        this.btnDirectoryStart.Click += new System.EventHandler(this.btnDirectoryStart_Click);
        // 
        // btnDirectoryStop
        // 
        this.btnDirectoryStop.Location = new System.Drawing.Point(260, 11);
        this.btnDirectoryStop.Name = "btnDirectoryStop";
        this.btnDirectoryStop.Size = new System.Drawing.Size(90, 25);
        this.btnDirectoryStop.TabIndex = 3;
        this.btnDirectoryStop.Text = "停止";
        this.btnDirectoryStop.UseVisualStyleBackColor = true;
        this.btnDirectoryStop.Enabled = false;
        this.btnDirectoryStop.Click += new System.EventHandler(this.btnDirectoryStop_Click);
        // 
        // btnDirectoryRestart
        // 
        this.btnDirectoryRestart.Location = new System.Drawing.Point(360, 11);
        this.btnDirectoryRestart.Name = "btnDirectoryRestart";
        this.btnDirectoryRestart.Size = new System.Drawing.Size(90, 25);
        this.btnDirectoryRestart.TabIndex = 4;
        this.btnDirectoryRestart.Text = "重启";
        this.btnDirectoryRestart.UseVisualStyleBackColor = true;
        this.btnDirectoryRestart.Enabled = false;
        this.btnDirectoryRestart.Click += new System.EventHandler(this.btnDirectoryRestart_Click);
        // 
        // txtLog
        // 
        this.txtLog.Location = new System.Drawing.Point(12, 260);
        this.txtLog.Multiline = true;
        this.txtLog.Name = "txtLog";
        this.txtLog.ReadOnly = true;
        this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
        this.txtLog.Size = new System.Drawing.Size(776, 280);
        this.txtLog.TabIndex = 4;
        // 
        // cmbLogFilter
        // 
        this.cmbLogFilter.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        this.cmbLogFilter.Items.AddRange(new object[] {"All", "Login", "Battle", "Resource", "Directory"});
        this.cmbLogFilter.Location = new System.Drawing.Point(12, 225);
        this.cmbLogFilter.Name = "cmbLogFilter";
        this.cmbLogFilter.Size = new System.Drawing.Size(120, 23);
        this.cmbLogFilter.TabIndex = 1;
        this.cmbLogFilter.SelectedIndex = 0;
        this.cmbLogFilter.SelectedIndexChanged += new System.EventHandler(this.cmbLogFilter_SelectedIndexChanged);
        // 
        // btnClearLog
        // 
        this.btnClearLog.Location = new System.Drawing.Point(150, 225);
        this.btnClearLog.Name = "btnClearLog";
        this.btnClearLog.Size = new System.Drawing.Size(90, 25);
        this.btnClearLog.TabIndex = 2;
        this.btnClearLog.Text = "清空日志";
        this.btnClearLog.UseVisualStyleBackColor = true;
        this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
        // 
        // btnSaveLog
        // 
        this.btnSaveLog.Location = new System.Drawing.Point(250, 225);
        this.btnSaveLog.Name = "btnSaveLog";
        this.btnSaveLog.Size = new System.Drawing.Size(90, 25);
        this.btnSaveLog.TabIndex = 3;
        this.btnSaveLog.Text = "保存日志";
        this.btnSaveLog.UseVisualStyleBackColor = true;
        this.btnSaveLog.Click += new System.EventHandler(this.btnSaveLog_Click);
        // 
        // btnStartAll
        // 
        this.btnStartAll.Location = new System.Drawing.Point(350, 225);
        this.btnStartAll.Name = "btnStartAll";
        this.btnStartAll.Size = new System.Drawing.Size(100, 25);
        this.btnStartAll.TabIndex = 5;
        this.btnStartAll.Text = "一键启动";
        this.btnStartAll.UseVisualStyleBackColor = true;
        this.btnStartAll.Click += new System.EventHandler(this.btnStartAll_Click);
        // 
        // btnStopAll
        // 
        this.btnStopAll.Location = new System.Drawing.Point(460, 225);
        this.btnStopAll.Name = "btnStopAll";
        this.btnStopAll.Size = new System.Drawing.Size(100, 25);
        this.btnStopAll.TabIndex = 6;
        this.btnStopAll.Text = "一键停止";
        this.btnStopAll.UseVisualStyleBackColor = true;
        this.btnStopAll.Click += new System.EventHandler(this.btnStopAll_Click);
        // 
        // Form1
        // 
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(800, 560);
        this.Controls.Add(this.btnStopAll);
        this.Controls.Add(this.btnStartAll);
        this.Controls.Add(this.txtLog);
        this.Controls.Add(this.btnSaveLog);
        this.Controls.Add(this.btnClearLog);
        this.Controls.Add(this.cmbLogFilter);
        this.Controls.Add(this.tabServices);
        this.Name = "Form1";
        this.Text = "Window - .NET Game Server";
        this.tabServices.ResumeLayout(false);
        this.tabLogin.ResumeLayout(false);
        this.tabLogin.PerformLayout();
        this.tabBattle.ResumeLayout(false);
        this.tabBattle.PerformLayout();
        this.tabDirectory.ResumeLayout(false);
        this.tabDirectory.PerformLayout();
        this.tabResource.ResumeLayout(false);
        this.tabResource.PerformLayout();
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion
}
