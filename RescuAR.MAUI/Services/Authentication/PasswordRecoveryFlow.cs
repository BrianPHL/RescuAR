namespace RescuAR.App.Services.Authentication;

internal interface IPasswordRecoveryClient : IDisposable
{
    Task SendCodeAsync(string email);
    Task<string?> VerifyCodeAsync(string email, string code);
    Task<string?> UpdatePasswordAsync(string password);
}

internal interface IPasswordRecoveryFactory
{
    Task<IPasswordRecoveryClient> CreateAsync();
}

internal sealed class PasswordRecoveryFlow(IPasswordRecoveryFactory factory) : IDisposable
{
    private IPasswordRecoveryClient? client;
    private string? email;
    private string? verifiedUser;
    private long version;

    public async Task RequestAsync(string address)
    {
        if (!AuthenticationFormPolicy.ValidEmail(address)) throw new InvalidOperationException("Enter a valid email address.");
        Cancel();
        long current = Volatile.Read(ref version);
        var temporary = await factory.CreateAsync();
        if (current != Volatile.Read(ref version)) { temporary.Dispose(); throw new OperationCanceledException(); }
        client = temporary;
        try
        {
            await temporary.SendCodeAsync(address.Trim());
            RequireCurrent(current, temporary);
            email = address.Trim();
        }
        catch
        {
            if (ReferenceEquals(client, temporary)) Cancel();
            throw;
        }
    }

    public async Task ConfirmAsync(string code, string password, string confirmation)
    {
        if (code.Trim().Length != 6 || code.Trim().Any(character => character is < '0' or > '9'))
            throw new InvalidOperationException("Enter the six-digit recovery code.");
        if (AuthenticationFormPolicy.PasswordError(password, confirmation) is { } error)
            throw new InvalidOperationException(error);
        var temporary = client ?? throw new InvalidOperationException("Request a recovery code first.");
        string recipient = email ?? throw new InvalidOperationException("Request a recovery code first.");
        long current = Volatile.Read(ref version);
        if (verifiedUser is null)
        {
            string? user = await temporary.VerifyCodeAsync(recipient, code.Trim());
            RequireCurrent(current, temporary);
            if (string.IsNullOrWhiteSpace(user)) throw new InvalidOperationException("The recovery code could not be verified.");
            verifiedUser = user;
        }
        string expected = verifiedUser;
        string? updated = await temporary.UpdatePasswordAsync(password);
        RequireCurrent(current, temporary);
        if (updated != expected)
        {
            Cancel();
            throw new InvalidOperationException("The password update could not be confirmed. Request a new code.");
        }
        Cancel();
    }

    public void Cancel()
    {
        Interlocked.Increment(ref version);
        var previous = client;
        client = null;
        email = null;
        verifiedUser = null;
        previous?.Dispose();
    }

    private void RequireCurrent(long current, IPasswordRecoveryClient temporary)
    {
        if (current != Volatile.Read(ref version) || !ReferenceEquals(client, temporary)) throw new OperationCanceledException();
    }

    public void Dispose() => Cancel();
}
