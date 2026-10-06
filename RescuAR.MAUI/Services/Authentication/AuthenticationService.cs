using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Supabase.Gotrue;
using RescuAR.Services;

namespace RescuAR.App.Services.Authentication
{
    public class AuthenticationService
    {
        private async Task<Supabase.Client> GetClientAsync() =>
            await SupabaseService.Instance.GetClientAsync()
                ?? throw new InvalidOperationException("Supabase is not configured.");

        public async Task<Session> SignUpWithEmailAsync(string email, string password, string firstName, string lastName, string? middleName, string? contactNumber, RescuAR.App.Models.ResidentProfileDraft? draft = null)
        {
            var client = await GetClientAsync();

            var options = new SignUpOptions
            {
                Data = new Dictionary<string, object>
                {
                    { "first_name", firstName },
                    { "last_name", lastName }
                }
            };

            if (!string.IsNullOrWhiteSpace(middleName))
            {
                options.Data.Add("middle_name", middleName);
            }

            if (!string.IsNullOrWhiteSpace(contactNumber))
            {
                // Supabase standard user attribute for phone number
                options.Data.Add("phone", contactNumber);
                options.Data.Add("contact_number", contactNumber);
            }

            if (draft != null)
            {
                options.Data["address"] = draft.Address;
                options.Data["birthday"] = draft.Birthday.ToString("yyyy-MM-dd");
            }
            var session = await client.Auth.SignUp(email, password, options);
            return session
                ?? throw new InvalidOperationException("Supabase sign-up completed without returning an authentication session.");
        }

        public async Task<Session> SignInWithEmailAsync(string email, string password)
        {
            var client = await GetClientAsync();
            var session = await client.Auth.SignIn(email, password);
            return session
                ?? throw new InvalidOperationException("Supabase sign-in completed without returning an authentication session.");
        }

        public async Task<Session> SignInWithGoogleAsync()
        {
            var client = await GetClientAsync();

            // Set up OAuth sign-in options.
            // In modern OAuth, PKCE flow is highly recommended for mobile clients.
            var options = new SignInOptions
            {
                RedirectTo = "rescuar://callback",
                FlowType = Supabase.Gotrue.Constants.OAuthFlowType.PKCE
            };

            // Get Google Sign-In state from Supabase
            ProviderAuthState state = await client.Auth.SignIn(Supabase.Gotrue.Constants.Provider.Google, options);

            if (state == null || state.Uri == null)
            {
                throw new InvalidOperationException("Failed to generate Google Sign-In URL from Supabase.");
            }

            // Trigger MAUI WebAuthenticator
            var authResult = await WebAuthenticator.Default.AuthenticateAsync(
                state.Uri,
                new Uri("rescuar://callback")
            );

            if (authResult == null || authResult.CallbackUri == null)
            {
                throw new OperationCanceledException("Google Authentication was cancelled by the user.");
            }

            // Extract code for PKCE flow exchange
            if (!authResult.Properties.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            {
                throw new InvalidOperationException("No authorization code returned from Google Authentication.");
            }

            // Exchange the code and the original PKCE verifier for a session.
            if (string.IsNullOrWhiteSpace(state.PKCEVerifier))
            {
                throw new InvalidOperationException("Supabase did not return the PKCE verifier required to complete Google authentication.");
            }

            var session = await client.Auth.ExchangeCodeForSession(state.PKCEVerifier, code);

            if (session == null)
            {
                throw new InvalidOperationException("Failed to retrieve authentication session from callback URL.");
            }

            return session;
        }

        public async Task SignOutAsync()
        {
            try
            {
                var client = await GetClientAsync();
                await client.Auth.SignOut();
            }
            finally
            {
                await SupabaseService.Instance.ClearSessionAsync();
                RescuAR.App.Services.Profile.UserProfileService.ClearIdentity();
            }
        }
    }
}
