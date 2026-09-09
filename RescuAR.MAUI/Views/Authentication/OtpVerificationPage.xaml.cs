using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Authentication;

namespace RescuAR.App.Views.Authentication
{
    public partial class OtpVerificationPage : ContentPage
    {
        public OtpVerificationPage(OtpVerificationViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }

        private void OnEntryTextChanged(object sender, TextChangedEventArgs e)
        {
            var entry = sender as Entry;
            if (entry == null) return;

            // If text is entered, move to the next
            if (e.NewTextValue.Length == 1)
            {
                if (entry == entry1) entry2.Focus();
                else if (entry == entry2) entry3.Focus();
                else if (entry == entry3) entry4.Focus();
                else if (entry == entry4) entry5.Focus();
                else if (entry == entry5) entry6.Focus();
                else if (entry == entry6) entry6.Unfocus(); // Done
            }
            // If text is deleted (backspace), move to the previous
            else if (e.NewTextValue.Length == 0 && e.OldTextValue?.Length == 1)
            {
                if (entry == entry6) entry5.Focus();
                else if (entry == entry5) entry4.Focus();
                else if (entry == entry4) entry3.Focus();
                else if (entry == entry3) entry2.Focus();
                else if (entry == entry2) entry1.Focus();
            }
        }
    }
}
