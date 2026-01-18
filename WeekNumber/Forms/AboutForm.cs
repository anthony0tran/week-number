using WeekNumber.Helpers;

namespace WeekNumber.Forms;

public class AboutForm : Form
{
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var version = VersionHelper.GetAppVersion();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.ico");

        Text = $"WeekNumber {version}";
        Size = new Size(360, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;

        Image bodyImage;
        if (File.Exists(iconPath))
        {
            using var icon = new Icon(iconPath, new Size(128, 128));
            bodyImage = icon.ToBitmap();
        }
        else
        {
            bodyImage = SystemIcons.Application.ToBitmap();
        }

        var picture = new PictureBox
        {
            Size = new Size(128, 128),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = bodyImage,
            Margin = new Padding(12, 12, 16, 12)
        };

        float baseSize = (SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).Size;
        var link = new LinkLabel
        {
            Text = "Check out our GitHub page!",
            AutoSize = true,
            Tag = "https://github.com/anthony0tran/week-number",
            Font = new Font((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, baseSize + 4f, FontStyle.Bold),
            Margin = new Padding(0, 12, 12, 12),
            LinkBehavior = LinkBehavior.HoverUnderline,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        link.LinkClicked += (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = (string)link.Tag!,
                UseShellExecute = true
            });

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 1
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(picture, 0, 0);
        table.Controls.Add(link, 1, 0);

        link.Dock = DockStyle.Fill;
        MinimumSize = MinimumSize with { Width = 360 };

        Controls.Add(table);
    }
}