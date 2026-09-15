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
        _questions.Add(new AssessmentQuestion
        {
            Number = 1,
            Category = "Emergency Supplies",
            QuestionText = "Do you currently have at least a 3-day supply of drinking water for your household?",
            Options = new List<string> { "Yes, fully stocked", "Partially", "No supply" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 2,
            Category = "Emergency Supplies",
            QuestionText = "Do you have non-perishable food supplies that can last for at least 3 days?",
            Options = new List<string> { "Yes, 3+ days", "1-2 days only", "None" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 3,
            Category = "Emergency Supplies",
            QuestionText = "Do you have a fully stocked first-aid kit available at home?",
            Options = new List<string> { "Yes, complete", "Basic items only", "No kit" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 4,
            Category = "Emergency Supplies",
            QuestionText = "Do you have working flashlights and backup power banks ready for use?",
            Options = new List<string> { "Yes, all charged", "Flashlight only", "Neither" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 5,
            Category = "Evacuation Readiness",
            QuestionText = "Do you know the exact location of the nearest designated evacuation center?",
            Options = new List<string> { "Yes, fully aware", "Vaguely know", "Don't know" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 6,
            Category = "Evacuation Readiness",
            QuestionText = "Have you identified and practiced an evacuation route with your family?",
            Options = new List<string> { "Yes, practiced", "Discussed only", "No plan" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 7,
            Category = "Evacuation Readiness",
            QuestionText = "Can your household gather emergency Go-Bags and evacuate within 15 minutes?",
            Options = new List<string> { "Yes, ready to go", "Needs 30+ mins", "Not ready" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 8,
            Category = "Emergency Communication",
            QuestionText = "Do you have emergency local hotlines (RescuAR / LGU / BFP) saved on your mobile device?",
            Options = new List<string> { "Yes, all saved", "Some saved", "None saved" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 9,
            Category = "Emergency Communication",
            QuestionText = "Is there an agreed emergency meeting point for your family if communication is lost?",
            Options = new List<string> { "Yes, defined", "Roughly agreed", "Not defined" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 10,
            Category = "Household Preparedness",
            QuestionText = "Are important personal documents stored in a waterproof emergency folder?",
            Options = new List<string> { "Yes, waterproofed", "In regular drawer", "Unorganized" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 11,
            Category = "Fire Safety",
            QuestionText = "Do you have a working fire extinguisher and smoke detectors installed in your home?",
            Options = new List<string> { "Yes, both", "Only one", "Neither" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 12,
            Category = "Household Preparedness",
            QuestionText = "Do you have a specific backup plan for assisting pets, elderly, or disabled family members?",
            Options = new List<string> { "Yes, fully planned", "Discussed only", "No plan" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 13,
            Category = "First Aid Knowledge",
            QuestionText = "Does anyone in your household have basic first aid and CPR training?",
            Options = new List<string> { "Yes, certified", "Basic knowledge", "None" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 14,
            Category = "Evacuation Readiness",
            QuestionText = "Is your primary vehicle maintained with at least a half tank of gas for sudden evacuations?",
            Options = new List<string> { "Yes, always", "Sometimes", "Rarely/No vehicle" },
            CorrectOptionIndex = 0
        });
        _questions.Add(new AssessmentQuestion
        {
            Number = 15,
            Category = "Emergency Supplies",
            QuestionText = "Do you have a portable battery-powered or hand-crank radio to monitor emergency broadcasts?",
            Options = new List<string> { "Yes, ready", "Needs batteries", "No radio" },
            CorrectOptionIndex = 0
        });
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
