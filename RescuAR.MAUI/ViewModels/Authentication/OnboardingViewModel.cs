using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using RescuAR.App.Views.Authentication;

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

        public bool IsBackButtonVisible => CurrentSlideIndex == 1 || CurrentSlideIndex == 2;
        public bool IsSkipButtonVisible => CurrentSlideIndex < 3;
        public bool IsOnboardingVisible => CurrentSlideIndex < 3;
        public bool IsEntryVisible => CurrentSlideIndex == 3;

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
            OnPropertyChanged(nameof(IsBackButtonVisible));
            OnPropertyChanged(nameof(IsSkipButtonVisible));
            OnPropertyChanged(nameof(IsOnboardingVisible));
            OnPropertyChanged(nameof(IsEntryVisible));
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
                NavigateToSplash();
            }
        }

        [RelayCommand]
        private void Back()
        {
            if (CurrentSlideIndex > 0)
            {
                CurrentSlideIndex--;
                UpdateSlideData();
            }
        }

        [RelayCommand]
        private void Skip()
        {
            NavigateToSplash();
        }

        private void NavigateToSplash()
        {
            var splashPage = _serviceProvider.GetRequiredService<SplashPage>();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    Application.Current.MainPage = splashPage;
                }
            });
        }

        [RelayCommand]
        private void CreateAccount()
        {
            var registrationPage = _serviceProvider.GetRequiredService<RegistrationPage>();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    Application.Current.MainPage = registrationPage;
                }
            });
        }

        [RelayCommand]
        private void SignIn()
        {
            var loginPage = _serviceProvider.GetRequiredService<LoginPage>();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Application.Current != null)
                {
                    Application.Current.MainPage = loginPage;
                }
            });
        }
    }
}

