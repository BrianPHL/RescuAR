using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using RescuAR.App.Views.Authentication;

using RescuAR.App.Services.Authentication;

namespace RescuAR.App.ViewModels.Authentication
{
    public partial class OnboardingViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        [ObservableProperty]
        private int _currentSlideIndex = 0;

        [ObservableProperty]
        private string _titlePart1 = string.Empty;

        [ObservableProperty]
        private string _titleHighlight = string.Empty;

        [ObservableProperty]
        private string _titlePart2 = string.Empty;

        [ObservableProperty]
        private string _description = string.Empty;

        [ObservableProperty]
        private string _currentImage = string.Empty;

        [ObservableProperty]
        private string _nextButtonText = "Next >";

        [ObservableProperty]
        private string _versionText = "v0.0.1a";

        public bool IsOnboardingVisible => true;

        public bool IsDot1Active => CurrentSlideIndex == 0;
        public bool IsDot2Active => CurrentSlideIndex == 1;
        public bool IsDot3Active => CurrentSlideIndex == 2;

        public OnboardingViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            UpdateSlideData();
        }

        public void SetSlideIndex(int index)
        {
            CurrentSlideIndex = index;
            UpdateSlideData();
        }

        private void UpdateSlideData()
        {
            switch (CurrentSlideIndex)
            {
                case 0:
                    TitlePart1 = "Evacuation guidance when it ";
                    TitleHighlight = "matters";
                    TitlePart2 = " most.";
                    Description = "RescuAR uses Augmented Reality to guide you to safe zones during emergencies.";
                    CurrentImage = "onboarding_flood.jpg";
                    NextButtonText = "Next >";
                    break;
                case 1:
                    TitlePart1 = "";
                    TitleHighlight = "Guidance";
                    TitlePart2 = " before, during, and after.";
                    Description = "Explore nearby evacuation centers and receive real-time safety instructions.";
                    CurrentImage = "onboarding_phone.jpg";
                    NextButtonText = "Next >";
                    break;
                case 2:
                    TitlePart1 = "Official data. ";
                    TitleHighlight = "Verified";
                    TitlePart2 = " alerts.";
                    Description = "All hazard alerts are generated from verified monitoring agencies and official disaster data. Internet connection required.";
                    CurrentImage = "onboarding_flag.jpg";
                    NextButtonText = "Get Started >";
                    break;
                case 3:
                    // Entry state - handled in UI
                    break;
            }

            // Notify UI of visibility changes
            OnPropertyChanged(nameof(IsDot1Active));
            OnPropertyChanged(nameof(IsDot2Active));
            OnPropertyChanged(nameof(IsDot3Active));
        }

        [RelayCommand]
        private void Next()
        {
            if (CurrentSlideIndex < 2)
            {
                CurrentSlideIndex++;
                UpdateSlideData();
            }
            else
            {
                NavigateToLogin();
            }
        }

        [RelayCommand]
        private void Skip()
        {
            NavigateToLogin();
        }

        private void NavigateToLogin()
        {
            var loginPage = _serviceProvider.GetRequiredService<LoginPage>();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    AuthenticationNavigation.TrySetRootPage(new NavigationPage(loginPage));
                }
            });
        }
    }
}

