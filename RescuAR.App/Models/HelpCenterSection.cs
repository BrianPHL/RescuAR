using System.Collections.ObjectModel;

namespace RescuAR.App.Models
{
    /// <summary>
    /// Represents a grouped section of help items (e.g., "Getting Started", "Troubleshooting").
    /// </summary>
    public class HelpCenterSection
    {
        /// <summary>
        /// The display title for this section.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The help items within this section.
        /// </summary>
        public ObservableCollection<HelpCenterItem> Items { get; set; } = new();
    }
}
