using Microsoft.Extensions.Logging;
using RescuAR.App.ViewModels.Profile;
using RescuAR.App.ViewModels.Summary;
using RescuAR.App.Views.Profile;
using RescuAR.App.Views.Summary;

namespace RescuAR.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");

                    // Geist font family
                    fonts.AddFont("Geist-Regular.ttf", "GeistRegular");
                    fonts.AddFont("Geist-Medium.ttf", "GeistMedium");
                    fonts.AddFont("Geist-SemiBold.ttf", "GeistSemiBold");
                    fonts.AddFont("Geist-Bold.ttf", "GeistBold");
                    fonts.AddFont("Geist-Light.ttf", "GeistLight");
                });

            // Register ViewModels
            builder.Services.AddTransient<HelpCenterViewModel>();
            builder.Services.AddTransient<PrivacyPolicyViewModel>();
            builder.Services.AddTransient<TermsConditionsViewModel>();
            builder.Services.AddTransient<SystemInformationViewModel>();
            builder.Services.AddTransient<SummaryViewModel>();
            builder.Services.AddTransient<ActivityHistoryViewModel>();
            builder.Services.AddTransient<NearbySafeLocationViewModel>();
            builder.Services.AddTransient<RecommendedActionViewModel>();
            builder.Services.AddTransient<SituationAssessmentViewModel>();
            builder.Services.AddTransient<AreaStatusViewModel>();

            // Register Pages
            builder.Services.AddTransient<HelpCenterPage>();
            builder.Services.AddTransient<PrivacyPolicyPage>();
            builder.Services.AddTransient<TermsConditionsPage>();
            builder.Services.AddTransient<SystemInformationPage>();
            builder.Services.AddTransient<SummaryPage>();
            builder.Services.AddTransient<ActivityHistoryPage>();
            builder.Services.AddTransient<NearbySafeLocationsPage>();
            builder.Services.AddTransient<RecommendedActionPage>();
            builder.Services.AddTransient<SituationAsessmentPage>();
            builder.Services.AddTransient<AreaStatusPage>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
