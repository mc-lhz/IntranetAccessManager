using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CampusNetLogin
{
    public class SettingsForm : Form
    {
        private readonly AppConfig _config;
        private readonly bool _firstRun;
        private TextBox _txtPortal;
        private TextBox _txtUsername;
        private TextBox _txtPassword;
        private CheckBox _chkShowPassword;
        private CheckBox _chkAutoStart;
        private Label _lblStatus;
        private Button _btnTest;
        private Button _btnSave;
        private Button _btnCancel;
        private bool _testing;

        public AppConfig Config
        {
            get { return _config; }
        }

        public SettingsForm(AppConfig config, bool firstRun)
        {
            _config = config.Clone();
            _firstRun = firstRun;
            InitializeComponent();
            LoadValues();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            Text = _firstRun ? "校园网自动登录 - 首次设置" : "校园网自动登录 - 设置";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(640, 470);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.White;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 86;
            header.BackColor = Color.FromArgb(30, 111, 217);
            Controls.Add(header);

            Label title = new Label();
            title.Text = "校园网自动登录";
            title.ForeColor = Color.White;
            title.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold, GraphicsUnit.Point);
            title.AutoSize = true;
            title.Location = new Point(24, 16);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "厦门市教育城域网 · 入网认证";
            subtitle.ForeColor = Color.FromArgb(220, 235, 255);
            subtitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(26, 55);
            header.Controls.Add(subtitle);

            Label badge = new Label();
            badge.Text = "安全登录";
            badge.ForeColor = Color.White;
            badge.BackColor = Color.FromArgb(72, 143, 232);
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            badge.Size = new Size(76, 26);
            badge.Location = new Point(536, 28);
            header.Controls.Add(badge);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Location = new Point(24, 110);
            table.Size = new Size(592, 236);
            table.ColumnCount = 3;
            table.RowCount = 5;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            table.BackColor = Color.White;
            Controls.Add(table);

            table.Controls.Add(MakeLabel("认证地址"), 0, 0);
            _txtPortal = MakeTextBox();
            _txtPortal.Width = 350;
            _txtPortal.Text = _config.PortalUrl;
            table.Controls.Add(_txtPortal, 1, 0);
            table.Controls.Add(MakeHint("默认已填好"), 2, 0);

            table.Controls.Add(MakeLabel("手机号"), 0, 1);
            _txtUsername = MakeTextBox();
            _txtUsername.Width = 350;
            _txtUsername.Text = _config.Username;
            table.Controls.Add(_txtUsername, 1, 1);
            table.Controls.Add(MakeHint("I教育平台账号"), 2, 1);

            table.Controls.Add(MakeLabel("密码"), 0, 2);
            Panel passwordPanel = new Panel();
            passwordPanel.Dock = DockStyle.Fill;
            passwordPanel.Margin = new Padding(0, 8, 0, 8);
            _txtPassword = MakeTextBox();
            _txtPassword.Width = 290;
            _txtPassword.UseSystemPasswordChar = true;
            _txtPassword.Text = _config.Password;
            passwordPanel.Controls.Add(_txtPassword);
            _chkShowPassword = new CheckBox();
            _chkShowPassword.Text = "显示";
            _chkShowPassword.AutoSize = true;
            _chkShowPassword.Location = new Point(300, 7);
            _chkShowPassword.CheckedChanged += OnShowPasswordChanged;
            passwordPanel.Controls.Add(_chkShowPassword);
            table.Controls.Add(passwordPanel, 1, 2);
            table.Controls.Add(MakeHint("仅保存在本机"), 2, 2);

            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "登录 Windows 后自动启动";
            _chkAutoStart.AutoSize = true;
            _chkAutoStart.Checked = _config.AutoStart;
            table.Controls.Add(_chkAutoStart, 1, 3);
            table.Controls.Add(MakeHint("推荐开启"), 2, 3);

            _lblStatus = new Label();
            _lblStatus.Text = _firstRun
                ? "首次使用：请输入 I教育平台 的手机号和密码，然后点击“保存”。"
                : "修改后点击“保存”，程序会在需要时自动登录。";
            _lblStatus.ForeColor = Color.FromArgb(90, 90, 90);
            _lblStatus.AutoSize = false;
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Padding = new Padding(0, 4, 0, 4);
            table.Controls.Add(_lblStatus, 0, 4);
            table.SetColumnSpan(_lblStatus, 3);

            Label security = new Label();
            security.Text = "密码使用 Windows DPAPI 加密后保存在本机，不会上传到其他服务器。";
            security.ForeColor = Color.FromArgb(130, 130, 130);
            security.AutoSize = false;
            security.Location = new Point(24, 354);
            security.Size = new Size(592, 22);
            Controls.Add(security);

            Label note = new Label();
            note.Text = "本程序仅自动提交学校官方入网页面，不绕过认证、设备检查或任何安全策略。";
            note.ForeColor = Color.FromArgb(130, 130, 130);
            note.AutoSize = false;
            note.Location = new Point(24, 378);
            note.Size = new Size(592, 22);
            Controls.Add(note);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 62;
            footer.BackColor = Color.FromArgb(248, 249, 251);
            Controls.Add(footer);

            _btnTest = new Button();
            _btnTest.Text = "测试登录";
            _btnTest.Size = new Size(108, 32);
            _btnTest.Location = new Point(24, 14);
            _btnTest.Click += OnTestClick;
            footer.Controls.Add(_btnTest);

            _btnSave = new Button();
            _btnSave.Text = "保存";
            _btnSave.Size = new Size(92, 32);
            _btnSave.Location = new Point(416, 14);
            _btnSave.BackColor = Color.FromArgb(30, 111, 217);
            _btnSave.ForeColor = Color.White;
            _btnSave.FlatStyle = FlatStyle.Flat;
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += OnSaveClick;
            footer.Controls.Add(_btnSave);

            _btnCancel = new Button();
            _btnCancel.Text = "取消";
            _btnCancel.Size = new Size(92, 32);
            _btnCancel.Location = new Point(516, 14);
            _btnCancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(_btnCancel);

            AcceptButton = _btnSave;
            CancelButton = _btnCancel;

            ResumeLayout(false);
        }

        private static Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = Color.FromArgb(45, 55, 70);
            return label;
        }

        private static Label MakeHint(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = Color.FromArgb(150, 155, 165);
            label.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
            return label;
        }

        private static TextBox MakeTextBox()
        {
            TextBox box = new TextBox();
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Height = 28;
            box.Margin = new Padding(0, 8, 0, 8);
            box.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            return box;
        }

        private void LoadValues()
        {
            if (!AutoStart.IsEnabled && !_firstRun)
            {
                _chkAutoStart.Checked = false;
            }
            if (!string.IsNullOrEmpty(_config.PasswordError))
            {
                SetStatus(_config.PasswordError, Color.Firebrick);
                _txtPassword.Text = "";
            }
            else if (_firstRun)
            {
                _chkAutoStart.Checked = true;
            }
        }

        private void OnShowPasswordChanged(object sender, EventArgs e)
        {
            _txtPassword.UseSystemPasswordChar = !_chkShowPassword.Checked;
        }

        private string CurrentPassword()
        {
            if (!string.IsNullOrEmpty(_txtPassword.Text))
            {
                return _txtPassword.Text;
            }
            if (!string.IsNullOrEmpty(_config.Password))
            {
                return _config.Password;
            }
            return "";
        }

        private bool ValidateFields(out string message)
        {
            message = "";
            if (string.IsNullOrWhiteSpace(_txtUsername.Text))
            {
                message = "请输入 I教育平台手机号。";
                _txtUsername.Focus();
                return false;
            }
            if (string.IsNullOrEmpty(CurrentPassword()))
            {
                message = "请输入 I教育平台密码。";
                _txtPassword.Focus();
                return false;
            }
            string portal = _txtPortal.Text.Trim();
            if (!string.IsNullOrEmpty(portal))
            {
                if (!portal.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !portal.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    portal = "http://" + portal;
                }
                Uri uri;
                if (!Uri.TryCreate(portal, UriKind.Absolute, out uri))
                {
                    message = "认证地址格式不正确。";
                    _txtPortal.Focus();
                    return false;
                }
            }
            return true;
        }

        private void OnTestClick(object sender, EventArgs e)
        {
            if (_testing)
            {
                return;
            }
            string message;
            if (!ValidateFields(out message))
            {
                SetStatus(message, Color.Firebrick);
                return;
            }

            AppConfig testConfig = _config.Clone();
            testConfig.PortalUrl = _txtPortal.Text.Trim();
            testConfig.Username = _txtUsername.Text.Trim();
            testConfig.Password = CurrentPassword();
            testConfig.TryUnprotectPassword();

            _testing = true;
            _btnTest.Enabled = false;
            _btnSave.Enabled = false;
            SetStatus("正在连接认证服务器，请稍候...", Color.FromArgb(30, 111, 217));

            ThreadPool.QueueUserWorkItem(delegate
            {
                LoginResult result;
                try
                {
                    OnlineCheckResult online = OnlineChecker.Check(testConfig.PortalUrl);
                    if (online.Online)
                    {
                        result = LoginResult.Ok("", "");
                        result.Message = "当前网络已经在线，无需重复登录。";
                    }
                    else
                    {
                        PortalClient client = new PortalClient(testConfig);
                        result = client.Login(testConfig.Username, testConfig.Password);
                    }
                }
                catch (Exception ex)
                {
                    result = LoginResult.Fail("测试失败：" + ex.Message, "EXCEPTION");
                }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _testing = false;
                        _btnTest.Enabled = true;
                        _btnSave.Enabled = true;
                        if (result.Success)
                        {
                            SetStatus(result.Message, Color.FromArgb(22, 140, 80));
                        }
                        else
                        {
                            SetStatus(result.Message, Color.Firebrick);
                        }
                    }));
                }
                catch
                {
                }
            });
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            string message;
            if (!ValidateFields(out message))
            {
                SetStatus(message, Color.Firebrick);
                return;
            }

            _config.PortalUrl = _txtPortal.Text.Trim();
            if (string.IsNullOrEmpty(_config.PortalUrl))
            {
                _config.PortalUrl = "http://10.110.125.20";
            }
            _config.Username = _txtUsername.Text.Trim();
            string password = CurrentPassword();
            if (!string.IsNullOrEmpty(password))
            {
                _config.SetPassword(password);
            }
            _config.AutoStart = _chkAutoStart.Checked;

            try
            {
                _config.Save();
                AutoStart.SetEnabled(_config.AutoStart);
            }
            catch (Exception ex)
            {
                SetStatus("保存失败：" + ex.Message, Color.Firebrick);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void SetStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }
    }
}