using System.Drawing.Drawing2D;
using WeekNumber.Helpers;

namespace WeekNumber.Forms;

public class AboutForm : Form
{
    private static AboutForm? _instance;

    public static void ShowInstance()
    {
        if (_instance != null)
        {
            _instance.BringToFront();
            _instance.Focus();
            return;
        }
        _instance = new AboutForm();
        _instance.Show();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _instance = null;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        InitializeForm();
    }

    private void InitializeForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = "WeekNumber";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        DoubleBuffered = true;

        float scale = DeviceDpi / 96f;

        int clientW = Scale(380, scale);
        int clientH = Scale(175, scale);
        ClientSize = new Size(clientW, clientH);

        var screen = Screen.FromPoint(Cursor.Position);
        Location = new Point(
            screen.WorkingArea.Left + (screen.WorkingArea.Width  - Width)  / 2,
            screen.WorkingArea.Top  + (screen.WorkingArea.Height - Height) / 2
        );

        BuildUI(scale, clientW, clientH);
    }

    private void BuildUI(float scale, int clientW, int clientH)
    {
        int pad             = Scale(28, scale);
        int iconSize        = Scale(80, scale);
        int gap             = Scale(20, scale);
        int btnH            = Scale(36, scale);
        int btnMarginBottom = Scale(16, scale);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.ico");
        Image iconImage = File.Exists(iconPath)
            ? LoadIconImage(iconPath, iconSize)
            : SystemIcons.Application.ToBitmap();

        var picture = new PictureBox
        {
            Size      = new Size(iconSize, iconSize),
            SizeMode  = PictureBoxSizeMode.Zoom,
            Image     = iconImage,
            BackColor = Color.Transparent,
            Location  = new Point(pad, (clientH - btnH - btnMarginBottom - iconSize) / 2)
        };
        Controls.Add(picture);

        int textX    = pad + iconSize + gap;
        int textTopY = picture.Location.Y;

        var nameFont    = new Font("Segoe UI", 15f, FontStyle.Bold);
        var versionFont = new Font("Segoe UI", 9f,  FontStyle.Regular);

        var nameLabel = new Label
        {
            Text      = "WeekNumber",
            Font      = nameFont,
            ForeColor = Color.FromArgb(20, 20, 20),
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(textX, textTopY)
        };
        Controls.Add(nameLabel);

        var versionLabel = new Label
        {
            Text      = $"Version {VersionHelper.GetAppVersion()}",
            Font      = versionFont,
            ForeColor = Color.FromArgb(130, 130, 140),
            BackColor = Color.Transparent,
            AutoSize  = true,
            Location  = new Point(textX + Scale(2, scale), textTopY + nameLabel.PreferredHeight + Scale(4, scale))
        };
        Controls.Add(versionLabel);

        var githubBtn = new FlatButton
        {
            Text     = "View on GitHub  ↗",
            Size     = new Size(clientW - pad * 2, btnH),
            Location = new Point(pad, clientH - btnH - btnMarginBottom),
            Font     = new Font("Segoe UI", 9f, FontStyle.Regular),
            Cursor   = Cursors.Hand
        };
        githubBtn.Click += (_, _) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = GitHubUrl,
                UseShellExecute = true
            });
        Controls.Add(githubBtn);
    }

    private static Bitmap LoadIconImage(string iconPath, int size)
    {
        using var icon = new Icon(iconPath, new Size(size, size));
        return icon.ToBitmap();
    }

    private static int Scale(int value, float scale) => (int)Math.Round(value * scale);

    private const string GitHubUrl = "https://github.com/anthony0tran/week-number";
}

internal class FlatButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public FlatButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        DoubleBuffered = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true;  Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e)   { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        Color fill = _pressed ? Color.FromArgb(225, 225, 230)
                   : _hovered ? Color.FromArgb(240, 240, 245)
                   :            Color.FromArgb(246, 246, 248);

        using var path = RoundedRect(rect, 8);
        using var fillBrush = new SolidBrush(fill);
        g.FillPath(fillBrush, path);

        using var borderPen = new Pen(Color.FromArgb(210, 210, 218), 1f);
        g.DrawPath(borderPen, path);

        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var textBrush = new SolidBrush(Color.FromArgb(30, 30, 35));
        g.DrawString(Text, Font, textBrush, (RectangleF)rect, sf);
    }

    private static GraphicsPath RoundedRect(Rectangle b, int r)
    {
        var p = new GraphicsPath();
        p.AddArc(b.X,             b.Y,              r * 2, r * 2, 180, 90);
        p.AddArc(b.Right - r * 2, b.Y,              r * 2, r * 2, 270, 90);
        p.AddArc(b.Right - r * 2, b.Bottom - r * 2, r * 2, r * 2, 0,   90);
        p.AddArc(b.X,             b.Bottom - r * 2, r * 2, r * 2, 90,  90);
        p.CloseFigure();
        return p;
    }
}