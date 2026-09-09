using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using RescuAR.App.ViewModels.Profile;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RescuAR.App.Views.Profile
{
    public partial class HealthInformationPage : ContentPage, INotifyPropertyChanged
    {
        private int _currentStep = 1;
        private bool _isTutorialVisible;
        private string _tutorialMessage = "";
        private string _nextButtonText = "Next";
        private Color _step1Color = Colors.LightGray;
        private Color _step2Color = Colors.LightGray;
        private Color _step3Color = Colors.LightGray;

        public bool IsTutorialVisible
        {
            get => _isTutorialVisible;
            set { _isTutorialVisible = value; OnPropertyChanged(); }
        }

        public string TutorialMessage
        {
            get => _tutorialMessage;
            set { _tutorialMessage = value; OnPropertyChanged(); }
        }

        public string NextButtonText
        {
            get => _nextButtonText;
            set { _nextButtonText = value; OnPropertyChanged(); }
        }

        public Color Step1Color { get => _step1Color; set { _step1Color = value; OnPropertyChanged(); } }
        public Color Step2Color { get => _step2Color; set { _step2Color = value; OnPropertyChanged(); } }
        public Color Step3Color { get => _step3Color; set { _step3Color = value; OnPropertyChanged(); } }

        public HealthInformationPage()
        {
            InitializeComponent();
            BindingContext = new ProfileViewModel();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            
            bool hasSeenTutorial = Preferences.Default.Get("HasSeenHealthTutorial", false);
            if (!hasSeenTutorial)
            {
                IsTutorialVisible = true;
                UpdateTutorialStep(1);
            }
            else
            {
                IsTutorialVisible = false;
            }
        }

        private void OnSkipTutorialClicked(object sender, System.EventArgs e)
        {
            FinishTutorial();
        }

        private void OnNextTutorialClicked(object sender, System.EventArgs e)
        {
            if (_currentStep < 3)
            {
                UpdateTutorialStep(_currentStep + 1);
            }
            else
            {
                FinishTutorial();
            }
        }

        private void UpdateTutorialStep(int step)
        {
            _currentStep = step;
            Step1Color = step == 1 ? Color.FromArgb("#0A8491") : Colors.LightGray;
            Step2Color = step == 2 ? Color.FromArgb("#0A8491") : Colors.LightGray;
            Step3Color = step == 3 ? Color.FromArgb("#0A8491") : Colors.LightGray;

            switch (step)
            {
                case 1:
                    TutorialMessage = "First, enter your Health Card Number if you have one. This is crucial for medical responders.";
                    NextButtonText = "Next";
                    break;
                case 2:
                    TutorialMessage = "Next, select your Blood Type. This helps hospitals prepare the right resources instantly.";
                    NextButtonText = "Next";
                    break;
                case 3:
                    TutorialMessage = "Finally, list any allergies or maintenance medications to prevent adverse reactions during treatment.";
                    NextButtonText = "Finish";
                    break;
            }
        }

        private void FinishTutorial()
        {
            IsTutorialVisible = false;
            Preferences.Default.Set("HasSeenHealthTutorial", true);
        }
    }
}
