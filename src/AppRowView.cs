using System.Drawing;

namespace TrayStartup
{
    internal enum StatusTone
    {
        On,
        Off,
        Warning,
        Error
    }

    internal sealed class AppRowView
    {
        public AppEntry Entry { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
        public bool Enabled { get; set; }
        public bool CanToggle { get; set; }
        public bool HasWarning { get; set; }
        public StatusTone Tone { get; set; }
        public Image Icon { get; set; }
    }
}
