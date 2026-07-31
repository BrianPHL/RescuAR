using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;

namespace RescuAR.App.ViewModels.Profile
{
    /// <summary>
    /// ViewModel for the Help Center page.
    /// Manages FAQ sections, search/filter, and navigation.
    /// </summary>
    public partial class HelpCenterViewModel : ObservableObject
    {
        private readonly List<HelpCenterSection> _allSections;

        [ObservableProperty]
        private ObservableCollection<HelpCenterSection> _sections = new();

        [ObservableProperty]
        private string _searchText = string.Empty;

        public HelpCenterViewModel()
        {
            _allSections = BuildSections();
            Sections = new ObservableCollection<HelpCenterSection>(_allSections);
        }

        /// <summary>
        /// Called when SearchText changes — filters sections and items.
        /// </summary>
        partial void OnSearchTextChanged(string value)
        {
            FilterSections(value);
        }

        [RelayCommand]
        private async Task GoBack()
        {
            await Shell.Current.GoToAsync("..");
        }

        [RelayCommand]
        private async Task OpenContactEmail()
        {
            try
            {
                if (Email.Default.IsComposeSupported)
                {
                    var message = new EmailMessage
                    {
                        Subject = "RescuAR Help Center Inquiry",
                        To = new List<string> { "support@rescuar.ph" }
                    };
                    await Email.Default.ComposeAsync(message);
                }
            }
            catch (Exception)
            {
                // Email client not available — silently handle
            }
        }

        private void FilterSections(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                Sections = new ObservableCollection<HelpCenterSection>(_allSections);
                return;
            }

            var filtered = new ObservableCollection<HelpCenterSection>();
            var lowerQuery = query.ToLowerInvariant();

            foreach (var section in _allSections)
            {
                var matchingItems = section.Items
                    .Where(item =>
                        item.Title.ToLowerInvariant().Contains(lowerQuery) ||
                        item.Description.ToLowerInvariant().Contains(lowerQuery))
                    .ToList();

                if (matchingItems.Count > 0)
                {
                    filtered.Add(new HelpCenterSection
                    {
                        Title = section.Title,
                        Items = new ObservableCollection<HelpCenterItem>(matchingItems)
                    });
                }
            }

            Sections = filtered;
        }

        /// <summary>
        /// Builds all the static FAQ data matching the reference design.
        /// </summary>
        private static List<HelpCenterSection> BuildSections()
        {
            return new List<HelpCenterSection>
            {
                new()
                {
                    Title = "Getting Started",
                    Items = new ObservableCollection<HelpCenterItem>
                    {
                        new()
                        {
                            Number = 1,
                            Title = "What is RescuAR?",
                            Description = "Learn how RescuAR helps residents prepare for emergencies, receive advisories, report hazards, and navigate to evacuation centers.",
                            Category = "Getting Started"
                        },
                        new()
                        {
                            Number = 2,
                            Title = "How to use RescuAR?",
                            Description = "Learn the main features of the application and how to access them during emergencies.",
                            Category = "Getting Started"
                        }
                    }
                },
                new()
                {
                    Title = "Navigation & Evacuation",
                    Items = new ObservableCollection<HelpCenterItem>
                    {
                        new()
                        {
                            Number = 1,
                            Title = "How AR Evacuation Guidance Works",
                            Description = "Learn how augmented reality guidance helps users navigate to evacuation centers.",
                            Category = "Navigation & Evacuation"
                        },
                        new()
                        {
                            Number = 2,
                            Title = "How to Practice Evacuation Routes",
                            Description = "Learn how to familiarize yourself with evacuation routes before an emergency occurs.",
                            Category = "Navigation & Evacuation"
                        },
                        new()
                        {
                            Number = 3,
                            Title = "How to View Evacuation Centers",
                            Description = "Learn how to find nearby evacuation centers and view detailed information about them.",
                            Category = "Navigation & Evacuation"
                        }
                    }
                },
                new()
                {
                    Title = "Disaster Preparedness",
                    Items = new ObservableCollection<HelpCenterItem>
                    {
                        new()
                        {
                            Number = 1,
                            Title = "How to Use PASS",
                            Description = "Learn how the Preparation Assessment Screen evaluates your emergency preparedness.",
                            Category = "Disaster Preparedness"
                        },
                        new()
                        {
                            Number = 2,
                            Title = "How to Complete the Emergency Checklist",
                            Description = "Learn how to track essential emergency supplies and preparedness tasks.",
                            Category = "Disaster Preparedness"
                        },
                        new()
                        {
                            Number = 3,
                            Title = "How Preparedness Scores Are Calculated",
                            Description = "Understand how PASS generates your preparedness level and recommendations.",
                            Category = "Disaster Preparedness"
                        }
                    }
                },
                new()
                {
                    Title = "Community Features",
                    Items = new ObservableCollection<HelpCenterItem>
                    {
                        new()
                        {
                            Number = 1,
                            Title = "How to Submit a Report",
                            Description = "Learn how to report flooding, obstructions, and other hazards.",
                            Category = "Community Features"
                        },
                        new()
                        {
                            Number = 2,
                            Title = "Community Posting Guidelines",
                            Description = "Understand acceptable community posts and reporting practices.",
                            Category = "Community Features"
                        },
                        new()
                        {
                            Number = 3,
                            Title = "Report Verification Process",
                            Description = "Learn how reports are reviewed and validated by administrators.",
                            Category = "Community Features"
                        }
                    }
                },
                new()
                {
                    Title = "Safety Circle",
                    Items = new ObservableCollection<HelpCenterItem>
                    {
                        new()
                        {
                            Number = 1,
                            Title = "How Safety Circle Works",
                            Description = "Learn how to create and manage your Safety Circle.",
                            Category = "Safety Circle"
                        },
                        new()
                        {
                            Number = 2,
                            Title = "How to Add Members",
                            Description = "Learn how to invite family members and trusted contacts.",
                            Category = "Safety Circle"
                        },
                        new()
                        {
                            Number = 3,
                            Title = "Safety Status Indicators",
                            Description = "Understand the meaning of Safe, Needs Assistance, and Unknown statuses.",
                            Category = "Safety Circle"
                        }
                    }
                },
                new()
                {
                    Title = "Account & Settings",
                    Items = new ObservableCollection<HelpCenterItem>
                    {
                        new()
                        {
                            Number = 1,
                            Title = "Managing Emergency Contacts",
                            Description = "Learn how to add, edit, and remove emergency contacts.",
                            Category = "Account & Settings"
                        },
                        new()
                        {
                            Number = 2,
                            Title = "App Settings",
                            Description = "Learn how to customize notifications, accessibility, and location settings.",
                            Category = "Account & Settings"
                        },
                        new()
                        {
                            Number = 3,
                            Title = "Privacy & Data Usage",
                            Description = "Understand how your information is used within RescuAR.",
                            Category = "Account & Settings"
                        }
                    }
                },
                new()
                {
                    Title = "Troubleshooting",
                    Items = new ObservableCollection<HelpCenterItem>
                    {
                        new()
                        {
                            Number = 1,
                            Title = "AR Camera Not Working",
                            Description = "Steps to resolve common camera and AR issues.",
                            Category = "Troubleshooting"
                        },
                        new()
                        {
                            Number = 2,
                            Title = "Location Not Updating",
                            Description = "Troubleshooting for GPS and navigation issues.",
                            Category = "Troubleshooting"
                        },
                        new()
                        {
                            Number = 3,
                            Title = "Unable to Receive Advisories",
                            Description = "Check notification permissions and connectivity settings.",
                            Category = "Troubleshooting"
                        }
                    }
                }
            };
        }
    }
}
