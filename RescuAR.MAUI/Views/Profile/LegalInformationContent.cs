using RescuAR.App.Services.Profile;

namespace RescuAR.App.Views.Profile;

internal static class LegalInformationContent
{
    public static View Create(bool privacy)
    {
        var stack = new VerticalStackLayout { Padding = new Thickness(20), Spacing = 16 };
        stack.Add(new Label { Text = privacy ? "Privacy Policy" : "Terms & Conditions", FontSize = 28,
            FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F172A") });
        foreach (var section in privacy ? AppInformationContent.Privacy : AppInformationContent.Terms)
        {
            var text = new VerticalStackLayout { Spacing = 8 };
            text.Add(new Label { Text = section.Number + ". " + section.Title, FontSize = 16,
                FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0A8491") });
            text.Add(new Label { Text = section.Description, FontSize = 14, TextColor = Color.FromArgb("#334155"), LineHeight = 1.2 });
            foreach (string bullet in section.BulletPoints)
                text.Add(new Label { Text = "• " + bullet, FontSize = 13, TextColor = Color.FromArgb("#475569"), LineHeight = 1.2 });
            stack.Add(new Border { Content = text, Padding = 16, BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#E2E8F0"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 } });
        }
        return stack;
    }
}
