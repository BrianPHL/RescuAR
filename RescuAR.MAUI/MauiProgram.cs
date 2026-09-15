using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp.Views.Maui.Controls.Hosting;
using RescuAR.MAUI.Evergine;
using RescuAR.MAUI.Services;
using RescuAR.MAUI.Services.Location;
using RescuAR.App.Services.Authentication;
using RescuAR.App.ViewModels.Authentication;
using RescuAR.App.Views.Authentication;
using RescuAR.App.Services.Dashboard;
using RescuAR.App.Services.Reports;
using RescuAR.App.Services.Weather;
using RescuAR.App.ViewModels.Dashboard;
using RescuAR.App.ViewModels.Prepare;
using RescuAR.App.ViewModels.Summary;
using RescuAR.App.ViewModels.Profile;
using RescuAR.App.ViewModels.Reports;
using RescuAR.App.Views.Dashboard;
using RescuAR.App.Views.Prepare;
using RescuAR.App.Views.Summary;
using RescuAR.App.Views.Profile;
using RescuAR.App.Views.Reports;

namespace RescuAR.MAUI;

public static class MauiProgram
{
    public static IServiceProvider Services
    {
        get;
        private set;
    } = default!;
    public static MauiApp CreateMauiApp()
    {
        var builder =
            MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiEvergine()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont(
                    "OpenSans-Regular.ttf",
                    "OpenSansRegular");

                fonts.AddFont(
                    "OpenSans-Semibold.ttf",
                    "OpenSansSemibold");
            });

#if ANDROID

        builder.Services.AddSingleton<
            IArCoreService,
            Platforms.Android.Services.ArCoreService>();

        builder.Services.AddSingleton<
            ILocationService,
            MauiLocationService>();

#endif

        // RescuAR authentication service
        builder.Services.AddSingleton<AuthenticationService>();

        // Authentication Views + ViewModels
        builder.Services.AddTransient<SplashPage>();
        builder.Services.AddTransient<SplashViewModel>();

        builder.Services.AddTransient<OnboardingPage>();
        builder.Services.AddTransient<OnboardingViewModel>();

        builder.Services.AddTransient<RegistrationPage>();
        builder.Services.AddTransient<RegistrationViewModel>();

        builder.Services.AddTransient<RegistrationSuccessPage>();
        builder.Services.AddTransient<RegistrationSuccessViewModel>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<LoginViewModel>();

        builder.Services.AddTransient<GoogleAuthPage>();
        builder.Services.AddTransient<GoogleAuthViewModel>();

        builder.Services.AddTransient<PermissionsPage>();
        builder.Services.AddTransient<PermissionsViewModel>();

        // Batch 3: updated registration / verification / address flow
        builder.Services.AddTransient<OtpVerificationPage>();
        builder.Services.AddTransient<OtpVerificationViewModel>();

        builder.Services.AddTransient<AddressInputPage>();
        builder.Services.AddTransient<AddressInputViewModel>();

        builder.Services.AddTransient<TermsAndConditionsPage>();
        builder.Services.AddTransient<RescuAR.App.Views.Authentication.PrivacyPolicyPage>();

        // Batch 4: Dashboard shared services
        builder.Services.AddSingleton<IDashboardDataService, DashboardDataService>();
        builder.Services.AddSingleton<CommunityReportService>();
        builder.Services.AddSingleton<IOsmGeocodingService, OsmGeocodingService>();
        builder.Services.AddSingleton<IWeatherService, WeatherService>();

        // Batch 4: Dashboard pages / ViewModels
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<AreaStatusOverviewPage>();
        builder.Services.AddTransient<AreaStatusOverviewViewModel>();
        builder.Services.AddTransient<AdvisoriesActivePage>();
        builder.Services.AddTransient<CommunityReportsOverviewPage>();
        builder.Services.AddTransient<CommunityReportsOverviewViewModel>();
        builder.Services.AddTransient<DisasterInformationPage>();
        builder.Services.AddTransient<DisasterInformationViewModel>();
        builder.Services.AddTransient<EvacuationOverviewPage>();
        builder.Services.AddTransient<EvacuationOverviewViewModel>();
        builder.Services.AddTransient<PASSOverviewPage>();
        builder.Services.AddTransient<PASSOverviewViewModel>();
        builder.Services.AddTransient<PreparednessOverviewPage>();
        builder.Services.AddTransient<PreparednessOverviewViewModel>();
        builder.Services.AddTransient<QuickActionsPage>();
        builder.Services.AddTransient<QuickActionsViewModel>();
        builder.Services.AddTransient<SafetyCircleOverviewPage>();
        builder.Services.AddTransient<SafetyCircleOverviewViewModel>();
        builder.Services.AddTransient<WeatherInformationPage>();
        builder.Services.AddTransient<WeatherInformationViewModel>();

        // Batch 4: Prepare pages / ViewModels
        builder.Services.AddTransient<PreparePage>();
        builder.Services.AddTransient<PrepareViewModel>();
        builder.Services.AddTransient<ChecklistPage>();
        builder.Services.AddTransient<ChecklistViewModel>();
        builder.Services.AddTransient<PASSPage>();
        builder.Services.AddTransient<PASSViewModel>();
        builder.Services.AddTransient<AssessmentPage>();
        builder.Services.AddTransient<AssessmentViewModel>();
        builder.Services.AddTransient<EvacuationCenterInfoPage>();
        builder.Services.AddTransient<EvacuationCenterInfoViewModel>();
        builder.Services.AddTransient<HotlineDirectoryPage>();
        builder.Services.AddTransient<HotlineDirectoryViewModel>();
        builder.Services.AddTransient<FloodHistoryPage>();
        builder.Services.AddTransient<FloodHistoryViewModel>();
        builder.Services.AddTransient<HistoricalPhotosPage>();
        builder.Services.AddTransient<HistoricalPhotosViewModel>();
        builder.Services.AddTransient<DocumentaryVideosPage>();
        builder.Services.AddTransient<DocumentaryVideosViewModel>();
        builder.Services.AddTransient<FloodTimelinePage>();
        builder.Services.AddTransient<FloodTimelineViewModel>();
        builder.Services.AddTransient<PreparednessGuidePage>();
        builder.Services.AddTransient<PreparednessGuideViewModel>();
        builder.Services.AddTransient<PracticeARPage>();
        builder.Services.AddTransient<PracticeARViewModel>();

        // Batch 4: Summary
        builder.Services.AddTransient<SummaryPage>();
        builder.Services.AddTransient<SummaryViewModel>();

        // Batch 5: Reports
        builder.Services.AddTransient<ReportsViewModel>();
        builder.Services.AddTransient<ReportsPage>();
        builder.Services.AddTransient<ReportDetailsViewModel>();
        builder.Services.AddTransient<ReportDetailsPage>();
        builder.Services.AddTransient<RescuAR.App.ViewModels.Reports.AdvisoryFeedViewModel>();
        builder.Services.AddTransient<RescuAR.App.Views.Reports.AdvisoryFeedPage>();
        builder.Services.AddTransient<NotificationsPage>();

        // Batch 6: Map + Safety Circle
        builder.Services.AddSingleton<RescuAR.App.Services.Cloud.SafetyCircleService>();
        builder.Services.AddTransient<RescuAR.App.ViewModels.Map.MapViewModel>();
        builder.Services.AddTransient<RescuAR.App.Views.Map.MapPage>();
        builder.Services.AddTransient<RescuAR.App.ViewModels.Map.SafetyCircleViewModel>();
        builder.Services.AddTransient<RescuAR.App.Views.Map.SafetyCirclePage>();
        builder.Services.AddTransient<RescuAR.App.ViewModels.Map.CircleChatViewModel>();
        builder.Services.AddTransient<RescuAR.App.Views.Map.CircleChatPage>();

        // Batch 5: Profile
        builder.Services.AddTransient<ProfileViewModel>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<PersonalInformationPage>();
        builder.Services.AddTransient<HealthInformationPage>();
        builder.Services.AddTransient<SafetyCircleSettingsPage>();
        builder.Services.AddTransient<EmergencyContactsPage>();
        builder.Services.AddTransient<AppSettingsPage>();
        builder.Services.AddTransient<HelpCenterPage>();
        builder.Services.AddTransient<RescuAR.App.Views.Profile.PrivacyPolicyPage>();
        builder.Services.AddTransient<TermsConditionsPage>();
        builder.Services.AddTransient<SystemInformationPage>();


        // Final composition: preserve the source Entry presentation behavior
        // without importing its Unity/WebView permission configuration.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping(
            "NoUnderline",
            (handler, view) =>
            {
#if ANDROID
                handler.PlatformView.BackgroundTintList =
                    global::Android.Content.Res.ColorStateList.ValueOf(
                        global::Android.Graphics.Color.Transparent);
#endif
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        Services = app.Services;

        // Several migrated ViewModels still expose the source's static
        // Instance accessors. Point those accessors at the same DI singletons
        // so the application does not create parallel service instances.
        DashboardDataService.Instance =
            app.Services.GetRequiredService<IDashboardDataService>();

        WeatherService.Instance =
            app.Services.GetRequiredService<IWeatherService>();

        return app;
    }
}
