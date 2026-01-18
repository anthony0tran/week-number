using WeekNumber.Helpers;

namespace WeekNumber.Forms;

public class AboutForm : Form
{
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        InitializeForm();
    }

    private void InitializeForm()
    {
        Text = $"WeekNumber {VersionHelper.GetAppVersion()}";
        Size = new Size(FormWidth, FormHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(FormWidth, MinimumSize.Height);

        var table = CreateLayoutTable();
        Controls.Add(table);
    }

    private TableLayoutPanel CreateLayoutTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(TablePadding),
            ColumnCount = 2,
            RowCount = 1
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IconSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        table.Controls.Add(CreatePictureBox(), 0, 0);
        var link = CreateGitHubLinkLabel();
        table.Controls.Add(link, 1, 0);
        link.Dock = DockStyle.Fill;
        return table;
    }

    private PictureBox CreatePictureBox()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.ico");
        Image bodyImage = File.Exists(iconPath)
            ? LoadIconImage(iconPath)
            : SystemIcons.Application.ToBitmap();

        return new PictureBox
        {
            Size = new Size(IconSize, IconSize),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = bodyImage,
            Margin = new Padding(PictureMarginLeft, PictureMarginTop, PictureMarginRight, PictureMarginBottom)
        };
    }

    private static Bitmap LoadIconImage(string iconPath)
    {
        using var icon = new Icon(iconPath, new Size(IconSize, IconSize));
        return icon.ToBitmap();
    }

    private static LinkLabel CreateGitHubLinkLabel()
    {
        var baseSize = (SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).Size;
        var link = new LinkLabel
        {
            Text = "Check out our GitHub page!",
            AutoSize = true,
            Tag = GitHubUrl,
            Font = new Font((SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont).FontFamily, baseSize + LinkFontSizeDelta, FontStyle.Bold),
            Margin = new Padding(LinkMarginLeft, LinkMarginTop, LinkMarginRight, LinkMarginBottom),
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
        return link;
    }

    private const int FormWidth = 360;
    private const int FormHeight = 220;
    private const int IconSize = 128;
    private const int TablePadding = 12;
    private const int PictureMarginLeft = 12;
    private const int PictureMarginTop = 12;
    private const int PictureMarginRight = 16;
    private const int PictureMarginBottom = 12;
    private const int LinkMarginLeft = 0;
    private const int LinkMarginTop = 12;
    private const int LinkMarginRight = 12;
    private const int LinkMarginBottom = 12;
    private const float LinkFontSizeDelta = 4f;
    private const string GitHubUrl = "https://github.com/anthony0tran/week-number";
}