using System.Collections.ObjectModel;

namespace RescuAR.App.Models
{
    /// <summary>
    /// Represents a single section in the Privacy Policy page.
    /// </summary>
    public class PrivacyPolicyItem
    {
        /// <summary>
        /// Display number within the policy (1, 2, 3...).
        /// </summary>
        public int Number { get; set; }

        /// <summary>
        /// The title of the policy section.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The main body text of the policy section.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Optional bullet points listed under the description.
        /// </summary>
        public ObservableCollection<string> BulletPoints { get; set; } = new();

        /// <summary>
        /// Whether this item has bullet points to display.
        /// </summary>
        public bool HasBulletPoints => BulletPoints.Count > 0;
    }
}
