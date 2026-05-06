using System;
using System.Drawing;
using System.Windows.Forms;

namespace TPL
{
    public class LicenseForm : Form
    {
        private LicenseInfo _info;
        private Label lblTitle;
        private Label lblStatus;
        private Label lblHwIdLabel;
        private TextBox txtHwId;
        private Button btnCopy;
        private Label lblKeyLabel;
        private TextBox txtKey;
        private Button btnActivate;
        private Button btnClose;

        public LicenseForm(LicenseInfo info)
        {
            _info = info;
            InitializeComponent();
            // ThemeManager tạm bỏ — sẽ thêm lại sau
            // ThemeManager.Apply(this, ThemeManager.IsDarkMode());
            LoadLicenseData();
        }

        private void InitializeComponent()
        {
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblHwIdLabel = new System.Windows.Forms.Label();
            this.txtHwId = new System.Windows.Forms.TextBox();
            this.btnCopy = new System.Windows.Forms.Button();
            this.lblKeyLabel = new System.Windows.Forms.Label();
            this.txtKey = new System.Windows.Forms.TextBox();
            this.btnActivate = new System.Windows.Forms.Button();
            this.btnClose = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Location = new System.Drawing.Point(20, 20);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(200, 25);
            this.lblTitle.TabIndex = 8;
            this.lblTitle.Text = "Bản Quyền Kích Hoạt";
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.lblStatus.Location = new System.Drawing.Point(20, 60);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(79, 19);
            this.lblStatus.TabIndex = 7;
            this.lblStatus.Text = "Trạng thái...";
            // 
            // lblHwIdLabel
            // 
            this.lblHwIdLabel.AutoSize = true;
            this.lblHwIdLabel.Location = new System.Drawing.Point(20, 110);
            this.lblHwIdLabel.Name = "lblHwIdLabel";
            this.lblHwIdLabel.Size = new System.Drawing.Size(87, 15);
            this.lblHwIdLabel.TabIndex = 6;
            this.lblHwIdLabel.Text = "Mã phần cứng:";
            this.lblHwIdLabel.Click += new System.EventHandler(this.lblHwIdLabel_Click);
            // 
            // txtHwId
            // 
            this.txtHwId.Location = new System.Drawing.Point(20, 130);
            this.txtHwId.Name = "txtHwId";
            this.txtHwId.ReadOnly = true;
            this.txtHwId.Size = new System.Drawing.Size(300, 23);
            this.txtHwId.TabIndex = 5;
            // 
            // btnCopy
            // 
            this.btnCopy.Location = new System.Drawing.Point(330, 129);
            this.btnCopy.Name = "btnCopy";
            this.btnCopy.Size = new System.Drawing.Size(80, 25);
            this.btnCopy.TabIndex = 4;
            this.btnCopy.Text = "Copy";
            this.btnCopy.Click += new System.EventHandler(this.btnCopy_Click);
            // 
            // lblKeyLabel
            // 
            this.lblKeyLabel.AutoSize = true;
            this.lblKeyLabel.Location = new System.Drawing.Point(20, 170);
            this.lblKeyLabel.Name = "lblKeyLabel";
            this.lblKeyLabel.Size = new System.Drawing.Size(79, 15);
            this.lblKeyLabel.TabIndex = 3;
            this.lblKeyLabel.Text = "Mã kích hoạt:";
            // 
            // txtKey
            // 
            this.txtKey.Location = new System.Drawing.Point(20, 190);
            this.txtKey.Name = "txtKey";
            this.txtKey.Size = new System.Drawing.Size(300, 23);
            this.txtKey.TabIndex = 2;
            // 
            // btnActivate
            // 
            this.btnActivate.Location = new System.Drawing.Point(330, 189);
            this.btnActivate.Name = "btnActivate";
            this.btnActivate.Size = new System.Drawing.Size(80, 25);
            this.btnActivate.TabIndex = 1;
            this.btnActivate.Text = "Kích Hoạt";
            this.btnActivate.Click += new System.EventHandler(this.btnActivate_Click);
            // 
            // btnClose
            // 
            this.btnClose.Location = new System.Drawing.Point(330, 240);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(80, 30);
            this.btnClose.TabIndex = 0;
            this.btnClose.Text = "Đóng";
            this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
            // 
            // LicenseForm
            // 
            this.ClientSize = new System.Drawing.Size(434, 291);
            this.Controls.Add(this.btnClose);
            this.Controls.Add(this.btnActivate);
            this.Controls.Add(this.txtKey);
            this.Controls.Add(this.lblKeyLabel);
            this.Controls.Add(this.btnCopy);
            this.Controls.Add(this.txtHwId);
            this.Controls.Add(this.lblHwIdLabel);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.lblTitle);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "LicenseForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Kích Hoạt TPL";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void LoadLicenseData()
        {
            txtHwId.Text = _info.HardwareId;
            
            if (_info.IsHardwareChanged)
            {
                lblStatus.Text = "⚠️ Phát hiện thay đổi linh kiện phần cứng!\nBản quyền đã bị vô hiệu hoá.";
                lblStatus.ForeColor = Color.Red;
            }
            else if (_info.ExpirationDate == DateTime.MaxValue)
            {
                lblStatus.Text = "✅ Đã kích hoạt vĩnh viễn.";
                lblStatus.ForeColor = Color.LimeGreen;
            }
            else if (DateTime.Now > _info.ExpirationDate)
            {
                lblStatus.Text = $"❌ Đã hết hạn sử dụng vào ngày: {_info.ExpirationDate:dd/MM/yyyy}.\nVui lòng liên hệ tác giả để nhận mã kích hoạt.";
                lblStatus.ForeColor = Color.Red;
            }
            else if (DateTime.Now < _info.LastRunDate)
            {
                lblStatus.Text = "❌ Thời gian hệ thống bị sai lệch!\nVui lòng đồng bộ lại đồng hồ Windows.";
                lblStatus.ForeColor = Color.Red;
            }
            else
            {
                int daysLeft = (int)(_info.ExpirationDate - DateTime.Now).TotalDays;
                lblStatus.Text = $"✅ Đang dùng thử. Còn lại: {daysLeft} ngày.\n(Hết hạn: {_info.ExpirationDate:dd/MM/yyyy})";
                lblStatus.ForeColor = Color.Orange;
            }
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtHwId.Text))
            {
                Clipboard.SetText(txtHwId.Text);
                MessageBox.Show("Đã copy mã phần cứng vào bộ nhớ tạm!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void btnActivate_Click(object sender, EventArgs e)
        {
            string key = txtKey.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show("Vui lòng nhập mã kích hoạt!", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (LicenseManager.ActivateLicense(key, out string message))
            {
                MessageBox.Show(message, "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                // Reload info
                _info = LicenseManager.GetLicenseInfo();
                LoadLicenseData();
                this.DialogResult = DialogResult.OK; // Signal success
            }
            else
            {
                MessageBox.Show(message, "Lỗi Kích Hoạt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            if (_info.IsValid)
                this.DialogResult = DialogResult.OK;
            else
                this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void lblHwIdLabel_Click(object sender, EventArgs e)
        {

        }
    }
}
