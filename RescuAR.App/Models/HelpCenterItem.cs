namespace RescuAR.App.Models
{
    /// <summary>
    /// Represents a single help/FAQ item in the Help Center.
    /// </summary>
    public class HelpCenterItem
    {
        /// <summary>
        /// Display number within its section (1, 2, 3...).
        /// </summary>
        public int Number { get; set; }

        /// <summary>
        /// The title/question of the help item.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// A brief description or summary of the help item.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The category/section this item belongs to.
        /// </summary>
        public string Category { get; set; } = string.Empty;
    }
}
