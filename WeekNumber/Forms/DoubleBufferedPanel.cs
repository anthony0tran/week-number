using System.Windows.Forms;

namespace WeekNumber.Forms;

internal sealed class DoubleBufferedPanel : Panel
{
    // Enables double-buffering at the panel level to reduce flicker.
    public DoubleBufferedPanel()
    {
        DoubleBuffered = true;
    }
}
