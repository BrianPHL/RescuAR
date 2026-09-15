using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace RescuAR.App.ViewModels.Prepare;

public class AssessmentQuestion
{
    public int Number { get; set; }
    public string Category { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public int CorrectOptionIndex { get; set; } = 0; // 0-based index for 'positive/prepared' answer
    public int SelectedOptionIndex { get; set; } = -1;
    public string SelectedOption { get; set; } = string.Empty;
}

public partial class AssessmentViewModel : ObservableObject
{
    private readonly List<AssessmentQuestion> _questions = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    [NotifyPropertyChangedFor(nameof(QuestionProgressText))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(IsLastQuestion))]
    [NotifyPropertyChangedFor(nameof(IsNotLastQuestion))]
    private int _currentIndex = 0;

    [ObservableProperty]
    private AssessmentQuestion _currentQuestion = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotFinished))]
    private bool _isFinished = false;

    public bool IsNotFinished => !IsFinished;

    [ObservableProperty]
    private int _finalScorePercentage = 0;

    [ObservableProperty]
    private string _finalScoreStatus = "Prepared";

    // Option Properties for UI binding
    [ObservableProperty]
    private string _option1Text = string.Empty;

    [ObservableProperty]
    private string _option2Text = string.Empty;

    [ObservableProperty]
    private string _option3Text = string.Empty;

    [ObservableProperty]
    private bool _isOption1Selected;

    [ObservableProperty]
    private bool _isOption2Selected;

    [ObservableProperty]
    private bool _isOption3Selected;

    [ObservableProperty]
    private bool _isOption3Visible;

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    private bool _canSubmit;

    public double ProgressValue => (CurrentIndex + 1) / (double)_questions.Count;

    public string PercentText
    {
        get
        {
            double pct = (CurrentIndex + 1) * 100.0 / _questions.Count;
            return $"{Math.Min(100, (int)pct)}%";
        }
    }

    public string QuestionProgressText => $"Question {CurrentIndex + 1} of {_questions.Count}";

    public bool CanGoPrevious => CurrentIndex > 0;

    public bool IsLastQuestion => CurrentIndex == _questions.Count - 1;
    public bool IsNotLastQuestion => CurrentIndex < _questions.Count - 1;

    public AssessmentViewModel()
    {
        InitializeQuestions();
        LoadCurrentQuestion();
    }

    private void InitializeQuestions()
    {
        var masterBank = new List<AssessmentQuestion>
        {
            // ==========================================
            // CATEGORY 1: Emergency Supplies (10 Questions)
            // ==========================================
            new AssessmentQuestion
            {
                Category = "Emergency Supplies",
                QuestionText = "Do you currently have at least a 3-day supply of drinking water (1 gallon per person per day) for your household?",
                Options = new List<string> { "Yes, fully stocked", "Partially stocked", "No supply" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Supplies",
                QuestionText = "Do you have non-perishable food supplies (canned goods, energy bars) that can last for at least 3 days?",
                Options = new List<string> { "Yes, 3+ days", "1-2 days only", "None" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Supplies",
                QuestionText = "Do you have a fully stocked first-aid kit with unexpired essential medications available at home?",
                Options = new List<string> { "Yes, complete & fresh", "Basic items only", "No kit" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Supplies",
                QuestionText = "Do you have working flashlights, spare batteries, and fully charged backup power banks ready?",
                Options = new List<string> { "Yes, all charged & ready", "Flashlight only", "Neither" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Supplies",
                QuestionText = "Does each member of your household have a dedicated, packed emergency Go-Bag?",
                Options = new List<string> { "Yes, all members have one", "Shared bag only", "No Go-Bags" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Supplies",
                QuestionText = "Do you have a portable battery-powered or hand-crank radio to monitor emergency broadcasts during power outages?",
                Options = new List<string> { "Yes, ready with batteries", "Needs fresh batteries", "No radio" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Supplies",
                QuestionText = "Do you have personal hygiene and sanitation supplies (soap, wipes, masks, heavy-duty trash bags) packed for emergencies?",
                Options = new List<string> { "Yes, fully packed", "Partial supplies", "None" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Supplies",
                QuestionText = "Do you keep an emergency cash reserve in small denominations stored securely in your emergency kit?",
                Options = new List<string> { "Yes, cash prepared", "Card/E-wallet only", "No reserve" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Supplies",
                QuestionText = "Do you have a manual can opener, multi-purpose tool (Swiss knife/pliers), and heavy-duty duct tape in your kit?",
                Options = new List<string> { "Yes, all available", "Tools only", "None" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Supplies",
                QuestionText = "Do you have protective face masks (N95/kn95) or dust coverings packed for volcanic ash, smoke, or dust exposure?",
                Options = new List<string> { "Yes, packed for all", "Partial supply", "No masks" },
                CorrectOptionIndex = 0
            },

            // ==========================================
            // CATEGORY 2: Evacuation Readiness (10 Questions)
            // ==========================================
            new AssessmentQuestion
            {
                Category = "Evacuation Readiness",
                QuestionText = "Do you know the exact location and primary access path to your nearest designated LGU evacuation center?",
                Options = new List<string> { "Yes, fully aware", "Vaguely know", "Don't know" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Evacuation Readiness",
                QuestionText = "Have you identified and physically walked or driven an emergency evacuation route with your family?",
                Options = new List<string> { "Yes, practiced route", "Discussed only", "No route planned" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Evacuation Readiness",
                QuestionText = "Can your household gather emergency Go-Bags and evacuate your residence within 15 minutes of an alert?",
                Options = new List<string> { "Yes, ready in 15 mins", "Needs 30+ mins", "Not ready" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Evacuation Readiness",
                QuestionText = "Is your primary vehicle maintained with at least a half-tank of fuel ready for immediate evacuation?",
                Options = new List<string> { "Yes, always maintained", "Sometimes", "No vehicle / Rarely filled" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Evacuation Readiness",
                QuestionText = "Are emergency identification tags or cards prepared for young children, elderly, or family members with special needs?",
                Options = new List<string> { "Yes, all ID tags ready", "Partial IDs", "No ID tags" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Evacuation Readiness",
                QuestionText = "Have you tested alternative secondary evacuation routes in case primary roads are blocked or flooded?",
                Options = new List<string> { "Yes, alternate routes mapped", "One route only", "No route mapped" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Evacuation Readiness",
                QuestionText = "Do you have a pre-arranged evacuation plan or sturdy carrier for household pets?",
                Options = new List<string> { "Yes, pet carrier & food ready", "Basic plan", "No pet plan" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Evacuation Readiness",
                QuestionText = "Are you aware of the historical flood water levels in your immediate neighborhood and street?",
                Options = new List<string> { "Yes, fully informed", "Somewhat aware", "Unaware" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Evacuation Readiness",
                QuestionText = "Do you have sturdy waterproof boots or closed protective shoes ready for evacuating through water or debris?",
                Options = new List<string> { "Yes, for all members", "For adults only", "No suitable shoes" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Evacuation Readiness",
                QuestionText = "Have you established a buddy system with neighbors to assist elderly or vulnerable residents during evacuation?",
                Options = new List<string> { "Yes, neighbor agreement", "Informal plan", "No arrangement" },
                CorrectOptionIndex = 0
            },

            // ==========================================
            // CATEGORY 3: Emergency Communication (10 Questions)
            // ==========================================
            new AssessmentQuestion
            {
                Category = "Emergency Communication",
                QuestionText = "Do you have emergency hotlines (RescuAR, Marikina DRRMO Rescue 161, BFP, PNP) saved on all household mobile devices?",
                Options = new List<string> { "Yes, all saved", "Some saved", "None saved" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Communication",
                QuestionText = "Is there an agreed-upon emergency meeting point outside your neighborhood if cellular networks fail?",
                Options = new List<string> { "Yes, clearly defined", "Roughly agreed", "Not defined" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Communication",
                QuestionText = "Have you designated an out-of-town contact person to coordinate family communications during localized disasters?",
                Options = new List<string> { "Yes, contact assigned", "Discussed", "No contact" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Communication",
                QuestionText = "Do you have emergency signal devices such as loud whistles or high-visibility signal mirrors in your kit?",
                Options = new List<string> { "Yes, whistles/signals ready", "Whistle only", "None" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Communication",
                QuestionText = "Do you keep a printed physical list of important contacts inside your emergency Go-Bag in case phone batteries die?",
                Options = new List<string> { "Yes, printed list in bag", "Digital only", "No contact list" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Communication",
                QuestionText = "Do you understand the local siren alarm levels for Marikina River water levels (1st, 2nd, 3rd alarm)?",
                Options = new List<string> { "Yes, know all levels", "Basic idea", "Don't know" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Communication",
                QuestionText = "Do you have a solar-powered charger or hand-crank generator for mobile devices during prolonged blackouts?",
                Options = new List<string> { "Yes, solar/hand-crank ready", "Power bank only", "None" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Communication",
                QuestionText = "Does your family have an agreed-upon SMS check-in protocol or group chat for rapid emergency status updates?",
                Options = new List<string> { "Yes, protocol established", "Informal chat", "No protocol" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Communication",
                QuestionText = "Do you know the location of your local Barangay Hall or Emergency Operations Center for in-person reporting?",
                Options = new List<string> { "Yes, know exact spot", "Vaguely know", "Don't know" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Emergency Communication",
                QuestionText = "Are you subscribed to official LGU emergency SMS warning alerts and NDRRMC disaster notifications?",
                Options = new List<string> { "Yes, subscribed", "Uncertain", "Not subscribed" },
                CorrectOptionIndex = 0
            },

            // ==========================================
            // CATEGORY 4: Household Preparedness (10 Questions)
            // ==========================================
            new AssessmentQuestion
            {
                Category = "Household Preparedness",
                QuestionText = "Are important personal documents (IDs, land titles, insurance, birth certificates) stored in a waterproof emergency pouch?",
                Options = new List<string> { "Yes, waterproofed & ready", "In regular drawer", "Unorganized" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Household Preparedness",
                QuestionText = "Do you have a working fire extinguisher and functional smoke detectors installed in key areas of your home?",
                Options = new List<string> { "Yes, inspected & working", "Extinguisher only", "Neither" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Household Preparedness",
                QuestionText = "Do adult household members know how to shut off the main electricity circuit breaker, gas valve, and water main?",
                Options = new List<string> { "Yes, all adults know how", "One person knows", "No one knows" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Household Preparedness",
                QuestionText = "Are heavy furniture items, water heaters, and top-heavy appliances securely anchored to walls to prevent earthquake tipping?",
                Options = new List<string> { "Yes, anchored", "Partially anchored", "Not anchored" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Household Preparedness",
                QuestionText = "Do you have a specific assistance plan and supplies prepared for infants, elderly, or family members requiring special care?",
                Options = new List<string> { "Yes, fully prepared", "Basic plan", "No plan" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Household Preparedness",
                QuestionText = "Does at least one person in your household have formal basic First Aid and CPR training certification?",
                Options = new List<string> { "Yes, trained/certified", "Self-taught basic", "No training" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Household Preparedness",
                QuestionText = "Do you regularly monitor official weather advisories, PAGASA typhoons, and river level alerts via RescuAR or official apps?",
                Options = new List<string> { "Yes, daily/frequently", "Only during storms", "Rarely" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Household Preparedness",
                QuestionText = "Do you have sandbags or flood barriers ready to prevent floodwater from entering low-lying doorways?",
                Options = new List<string> { "Yes, barriers ready", "Improvised only", "None" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Household Preparedness",
                QuestionText = "Do you have clean secondary water storage containers sanitized and ready for sudden municipal water cutoffs?",
                Options = new List<string> { "Yes, sanitized & stored", "Some containers", "No storage" },
                CorrectOptionIndex = 0
            },
            new AssessmentQuestion
            {
                Category = "Household Preparedness",
                QuestionText = "Do you perform a bi-annual (every 6 months) inspection and rotation of emergency food, water, and medicine stocks?",
                Options = new List<string> { "Yes, strict schedule", "Occasional check", "Never inspect" },
                CorrectOptionIndex = 0
            }
        };

        // Pick 2 questions from each category (8 total) for a balanced sample, plus 2 extra random ones = 10 total
        var selected = new List<AssessmentQuestion>();
        var grouped = masterBank.GroupBy(q => q.Category).ToList();

        foreach (var group in grouped)
        {
            selected.AddRange(group.OrderBy(_ => Random.Shared.Next()).Take(2));
        }

        var remainingPool = masterBank.Except(selected).OrderBy(_ => Random.Shared.Next()).Take(2);
        selected.AddRange(remainingPool);

        // Shuffle the final 10 selected questions
        var randomPicked = selected.OrderBy(_ => Random.Shared.Next()).ToList();

        _questions.Clear();
        for (int i = 0; i < randomPicked.Count; i++)
        {
            var q = randomPicked[i];
            _questions.Add(new AssessmentQuestion
            {
                Number = i + 1,
                Category = q.Category,
                QuestionText = q.QuestionText,
                Options = new List<string>(q.Options),
                CorrectOptionIndex = q.CorrectOptionIndex,
                SelectedOptionIndex = -1,
                SelectedOption = string.Empty
            });
        }
    }

    private void LoadCurrentQuestion()
    {
        if (CurrentIndex < 0 || CurrentIndex >= _questions.Count) return;

        CurrentQuestion = _questions[CurrentIndex];

        Option1Text = CurrentQuestion.Options.Count > 0 ? CurrentQuestion.Options[0] : string.Empty;
        Option2Text = CurrentQuestion.Options.Count > 1 ? CurrentQuestion.Options[1] : string.Empty;
        Option3Text = CurrentQuestion.Options.Count > 2 ? CurrentQuestion.Options[2] : string.Empty;
        IsOption3Visible = CurrentQuestion.Options.Count > 2;

        UpdateSelectedOptionUI();
        ValidateNavigation();
    }

    private void UpdateSelectedOptionUI()
    {
        IsOption1Selected = CurrentQuestion.SelectedOptionIndex == 0;
        IsOption2Selected = CurrentQuestion.SelectedOptionIndex == 1;
        IsOption3Selected = CurrentQuestion.SelectedOptionIndex == 2;
    }

    private void ValidateNavigation()
    {
        bool hasSelection = CurrentQuestion.SelectedOptionIndex != -1;
        CanGoNext = hasSelection && IsNotLastQuestion;
        CanSubmit = hasSelection && IsLastQuestion;
    }

    [RelayCommand]
    private void SelectOption(string optionIndexStr)
    {
        if (int.TryParse(optionIndexStr, out int optNumber))
        {
            int zeroIndex = optNumber - 1;
            CurrentQuestion.SelectedOptionIndex = zeroIndex;
            CurrentQuestion.SelectedOption = optNumber <= CurrentQuestion.Options.Count ? CurrentQuestion.Options[zeroIndex] : string.Empty;

            UpdateSelectedOptionUI();
            ValidateNavigation();
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (CurrentIndex < _questions.Count - 1)
        {
            CurrentIndex++;
            LoadCurrentQuestion();
        }
    }

    [RelayCommand]
    private void Previous()
    {
        if (CurrentIndex > 0)
        {
            CurrentIndex--;
            LoadCurrentQuestion();
        }
    }

    [RelayCommand]
    private void Submit()
    {
        // Compute score
        int total = _questions.Count;
        int earnedPoints = 0;

        foreach (var q in _questions)
        {
            if (q.SelectedOptionIndex == 0) earnedPoints += 10;
            else if (q.SelectedOptionIndex == 1) earnedPoints += 5;
            else earnedPoints += 0;
        }

        int maxPossible = total * 10;
        FinalScorePercentage = (earnedPoints * 100) / maxPossible;

        if (FinalScorePercentage >= 80) FinalScoreStatus = "Highly Prepared";
        else if (FinalScorePercentage >= 60) FinalScoreStatus = "Prepared";
        else FinalScoreStatus = "Needs Improvement";

        // Save to Preferences
        Preferences.Set("PASS_Score", FinalScorePercentage);
        Preferences.Set("PASS_Status", FinalScoreStatus);
        Preferences.Set("PASS_LastDate", DateTime.Now.ToString("MMMM dd, yyyy"));

        IsFinished = true;
    }

    [RelayCommand]
    private async Task BackAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("..");
        }
    }

    [RelayCommand]
    private async Task GoToPreparationAssessmentAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}
