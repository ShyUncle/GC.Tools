namespace ChatMsg
{
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
        private void InitializeComponent()
        {
            btnFindWeChat = new Button();
            txtLog = new TextBox();
            btnStop = new Button();
            SuspendLayout();
            // 
            // btnFindWeChat
            // 
            btnFindWeChat.Location = new Point(10, 10);
            btnFindWeChat.Name = "btnFindWeChat";
            btnFindWeChat.Size = new Size(140, 26);
            btnFindWeChat.TabIndex = 0;
            btnFindWeChat.Text = "查找微信窗体句柄";
            btnFindWeChat.UseVisualStyleBackColor = true;
            btnFindWeChat.Click += btnFindWeChat_Click;
            // 
            // txtLog
            // 
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            txtLog.Location = new Point(10, 48);
            txtLog.Multiline = true;
            txtLog.Name = "txtLog";
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Size = new Size(680, 325);
            txtLog.TabIndex = 1;
            // 
            // btnStop
            // 
            btnStop.Location = new Point(240, 10);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(140, 26);
            btnStop.TabIndex = 2;
            btnStop.Text = "停止接受消息";
            btnStop.UseVisualStyleBackColor = true;
            btnStop.Click += btnStop_Click;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(700, 382);
            Controls.Add(btnStop);
            Controls.Add(txtLog);
            Controls.Add(btnFindWeChat);
            Name = "Form1";
            Text = "微信消息监听示例";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Button btnFindWeChat;
        private System.Windows.Forms.TextBox txtLog;
        private Button btnStop;
    }
}
