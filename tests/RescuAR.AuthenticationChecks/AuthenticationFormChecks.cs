using RescuAR.App.Models;
using RescuAR.App.Services.Authentication;

internal static class AuthenticationFormChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(AuthenticationFormPolicy.PasswordError("GoodPass1!", "GoodPass1!") is null,
            "registration and recovery accept the existing dev ten-character password policy");
        check(AuthenticationFormPolicy.PasswordError("Short1!A", "Short1!A") is not null,
            "Athena's shorter password minimum cannot weaken dev's policy");
        check(AuthenticationFormPolicy.PasswordError("GoodPass1.", "GoodPass1.") is not null,
            "password symbols retain dev's existing allowed-character rule");
        check(AuthenticationFormPolicy.PasswordError("GoodPass1!", "Different1!") is not null,
            "password confirmation must match");
        check(!AuthenticationFormPolicy.ValidEmail("invalid email") && AuthenticationFormPolicy.ValidEmail(" person@example.test "),
            "recovery validates and normalizes email before contacting a provider");

        var today = new DateTime(2026, 10, 26);
        var draft = new ResidentProfileDraft { Email = "person@example.test", FirstName = "Test", MiddleName = "",
            LastName = "Resident", PhoneNumber = "test-phone", HouseNumber = "12", StreetName = "Test Street",
            Barangay = "Calumpang", Birthday = today.AddYears(-2) };
        check(AuthenticationFormPolicy.ResidentError(draft, today) is null, "middle name is optional and the age-ten restriction is not adopted");
        check(AuthenticationFormPolicy.MarikinaBarangays.Count == 16 && AuthenticationFormPolicy.MarikinaBarangays.Contains("Calumpang"),
            "the resident selector preserves dev's complete barangay catalog");
        draft.Birthday = today.AddDays(1);
        check(AuthenticationFormPolicy.ResidentError(draft, today) is not null, "a future birthdate remains invalid without adding a minimum age");
        draft.Birthday = today.AddYears(-20);
        draft.StreetName = "";
        check(AuthenticationFormPolicy.ResidentError(draft, today) is not null, "the resident step cannot omit a street address");
        draft.StreetName = "Test Street";
        draft.Barangay = "Unknown";
        check(AuthenticationFormPolicy.ResidentError(draft, today) is not null, "resident selection must come from the existing city catalog");
        draft.Barangay = "Calumpang";
        check(draft.Address == "12, Test Street, Calumpang, Marikina City, Metro Manila, 1800", "the signup draft preserves the address format used by the shared flow");
        check(!typeof(ResidentProfileDraft).GetProperties().Any(property => new[] { "Password", "ConfirmPassword", "AccessToken", "OtpCode" }.Contains(property.Name)),
            "the verification draft carries no password, token or recovery-code fields");

        var factory = new FakeFactory();
        using var flow = new PasswordRecoveryFlow(factory);
        check(await Fails<InvalidOperationException>(() => flow.RequestAsync("invalid")) && factory.Created == 0,
            "invalid recovery email makes no provider request");
        await flow.RequestAsync(" person@example.test ");
        var first = factory.Last!;
        check(first.Email == "person@example.test" && first.Sends == 1, "the recovery code request captures its normalized recipient");
        check(await Fails<InvalidOperationException>(() => flow.ConfirmAsync("abcdef", "GoodPass1!", "GoodPass1!")) && first.Verifies == 0,
            "non-numeric recovery codes are rejected before verification");
        check(await Fails<InvalidOperationException>(() => flow.ConfirmAsync("123456", "Short1!A", "Short1!A")) && first.Verifies == 0,
            "a weak recovery password is rejected before consuming a code");
        first.FailNextUpdate = true;
        check(await Fails<IOException>(() => flow.ConfirmAsync("123456", "GoodPass1!", "GoodPass1!")),
            "a failed password update is reported without claiming success");
        await flow.ConfirmAsync("123456", "GoodPass1!", "GoodPass1!");
        check(first.Verifies == 1 && first.Updates == 2, "retry after an update failure reuses the already-verified temporary identity");
        check(first.Disposed && first.Disposals == 1, "successful recovery discards its temporary client instead of opening the app");
        check(await Fails<InvalidOperationException>(() => flow.ConfirmAsync("123456", "GoodPass1!", "GoodPass1!")),
            "a completed recovery cannot reuse its discarded session");

        await flow.RequestAsync("second@example.test");
        var second = factory.Last!;
        second.VerifiedUser = null;
        check(await Fails<InvalidOperationException>(() => flow.ConfirmAsync("123456", "GoodPass1!", "GoodPass1!")) && second.Updates == 0,
            "an unverified recovery response cannot update a password");
        second.VerifiedUser = "verified-user";
        second.UpdatedUser = "another-user";
        check(await Fails<InvalidOperationException>(() => flow.ConfirmAsync("123456", "GoodPass1!", "GoodPass1!")) && second.Disposed,
            "an update for another identity cannot be shown as a successful recovery");

        await flow.RequestAsync("cancel@example.test");
        var cancelled = factory.Last!;
        cancelled.VerifyBlock = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirming = flow.ConfirmAsync("123456", "GoodPass1!", "GoodPass1!");
        await cancelled.VerifyEntered.Task;
        flow.Cancel();
        cancelled.VerifyBlock.SetResult("verified-user");
        check(await Fails<OperationCanceledException>(() => confirming) && cancelled.Updates == 0 && cancelled.Disposed,
            "closing recovery rejects a late verification and prevents a password update");

        factory.FailNextSend = true;
        check(await Fails<IOException>(() => flow.RequestAsync("failed@example.test")) && factory.Last!.Disposed,
            "a failed recovery-code request disposes its temporary client");
        await flow.RequestAsync("new@example.test");
        var replacement = factory.Last!;
        check(replacement.Email == "new@example.test" && replacement.Verifies == 0, "a new request starts with a fresh recipient and verification state");
        flow.Cancel();
        flow.Cancel();
        check(replacement.Disposals == 1, "repeated closing cannot dispose a recovery client twice");
    }

    private static async Task<bool> Fails<T>(Func<Task> action) where T : Exception
    {
        try { await action(); return false; }
        catch (Exception error) when (error is T) { return true; }
    }

    private sealed class FakeFactory : IPasswordRecoveryFactory
    {
        public int Created { get; private set; }
        public FakeClient? Last { get; private set; }
        public bool FailNextSend { get; set; }
        public Task<IPasswordRecoveryClient> CreateAsync()
        {
            Created++;
            Last = new FakeClient { FailSend = FailNextSend };
            FailNextSend = false;
            return Task.FromResult<IPasswordRecoveryClient>(Last);
        }
    }

    private sealed class FakeClient : IPasswordRecoveryClient
    {
        public string? Email { get; private set; }
        public string? VerifiedUser { get; set; } = "verified-user";
        public string? UpdatedUser { get; set; } = "verified-user";
        public int Sends { get; private set; }
        public int Verifies { get; private set; }
        public int Updates { get; private set; }
        public bool Disposed { get; private set; }
        public int Disposals { get; private set; }
        public bool FailNextUpdate { get; set; }
        public bool FailSend { get; set; }
        public TaskCompletionSource<string?>? VerifyBlock { get; set; }
        public TaskCompletionSource VerifyEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SendCodeAsync(string email)
        {
            Sends++;
            Email = email;
            return FailSend ? Task.FromException(new IOException("test send failure")) : Task.CompletedTask;
        }
        public Task<string?> VerifyCodeAsync(string email, string code)
        {
            Verifies++;
            VerifyEntered.TrySetResult();
            return VerifyBlock?.Task ?? Task.FromResult(VerifiedUser);
        }
        public Task<string?> UpdatePasswordAsync(string password)
        {
            Updates++;
            if (FailNextUpdate) { FailNextUpdate = false; return Task.FromException<string?>(new IOException("test update failure")); }
            return Task.FromResult(UpdatedUser);
        }
        public void Dispose() { if (!Disposed) { Disposed = true; Disposals++; } }
    }
}
