using System.Reflection;

namespace WeekNumber
{
    public class AboutWindow
    {
        /// <summary>
        /// Opens a window that displays the application version and a GitHub link.
        /// </summary>
        public static string GetAppVersion()
        {
            // Prefer AssemblyFileVersionAttribute if present.
            var fileVer = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyFileVersionAttribute>()
                ?.Version;

            if (!string.IsNullOrWhiteSpace(fileVer))
            {
                var parts = fileVer.Split('.');
                if (parts.Length >= 3)
                    return string.Join('.', parts[0], parts[1], parts[2]);
                return fileVer;
            }

            // Fallback to AssemblyInformationalVersionAttribute (strip any +commit metadata).
            var infoVer = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(infoVer))
            {
                var plusIndex = infoVer.IndexOf('+');
                var ver = plusIndex >= 0 ? infoVer.Substring(0, plusIndex) : infoVer;
                var parts = ver.Split('.');
                if (parts.Length >= 3)
                    return string.Join('.', parts[0], parts[1], parts[2]);
                return ver;
            }

            // Final fallback to Application.ProductVersion.
            var prodVerParts = Application.ProductVersion.Split('.');
            if (prodVerParts.Length >= 3)
                return string.Join('.', prodVerParts[0], prodVerParts[1], prodVerParts[2]);
            return Application.ProductVersion;
        }

        // Holds the current About form instance so repeated clicks reuse the window.
        public static Form? _aboutForm;

        /// <summary>
        /// Shows the About window. Reuses existing instance if already open.
        /// </summary>
        public static void ShowAboutWindow(object sender, EventArgs e)
        {
            // If an instance exists and is visible, bring it to front and return.
            if (_aboutForm is { IsDisposed: false } && _aboutForm.Visible)
            {
                if (_aboutForm.WindowState == FormWindowState.Minimized)
                    _aboutForm.WindowState = FormWindowState.Normal;

                _aboutForm.Activate();
                _aboutForm.BringToFront();
                return;
            }

            // Build form metadata.
            string version = GetAppVersion();
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.ico");

            // Create the About dialog.
            var about = new Form
            {
                Text = $"WeekNumber {version}",
                Size = new Size(360, 220),                        // Base size; layout will scale in the second column
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowIcon = false,
                ShowInTaskbar = false
            };

            // Track this instance.
            _aboutForm = about;

            // Cleanup reference when the form closes.
            about.FormClosed += (_, __) =>
            {
                about.Dispose();
                if (ReferenceEquals(_aboutForm, about))
                    _aboutForm = null;
            };

            // Resolve icon (prefer app icon, fallback to system icon).
            Image bodyImage = File.Exists(iconPath)
                ? new Icon(iconPath, new Size(128, 128)).ToBitmap()
                : SystemIcons.Application.ToBitmap();

            // Left column: fixed-size image.
            var picture = new PictureBox
            {
                Size = new Size(128, 128),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = bodyImage,
                Margin = new Padding(12, 12, 16, 12)              // Extra right margin for spacing to text column
            };

            // Right column: link text; allow it to grow/shrink naturally.
            float baseSize = SystemFonts.MessageBoxFont.Size;
            var link = new LinkLabel
            {
                Text = "Check out our GitHub page!",
                AutoSize = true,                                  // Natural size within table cell
                Tag = "https://github.com/anthony0tran/week-number",
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, baseSize + 4f, FontStyle.Bold),
                Margin = new Padding(0, 12, 12, 12),
                LinkBehavior = LinkBehavior.HoverUnderline,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true                               // If space tightens, show ellipsis instead of overflow
            };

            // Open the URL using the default browser.
            link.LinkClicked += (s, args) =>
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = (string)link.Tag!,
                    UseShellExecute = true
                });

            // Layout: two-column table
            // Column 0: fixed width for image (128px)
            // Column 1: remaining width for link (fills horizontally)
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),                        // Overall content padding inside the form
                ColumnCount = 2,
                RowCount = 1
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128F));   // Fixed image column
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));    // Flexible text column
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Place controls into table.
            table.Controls.Add(picture, 0, 0);
            table.Controls.Add(link, 1, 0);

            // Make the link fill its cell horizontally so it wraps/ellipsizes properly.
            link.Dock = DockStyle.Fill;

            // Prevent the window from being resized too small to display content reasonably.
            about.MinimumSize = new Size(360, about.MinimumSize.Height);

            // Apply layout to form.
            about.Controls.Add(table);

            // Show modal.
            about.ShowDialog();
        }
    }
}
