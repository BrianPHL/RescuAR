using RescuAR.MAUI.Platforms.Android.Services;

namespace RescuAR.Tests;

public sealed class ArCameraImportFailurePolicyTests
{
    [Theory]
    [InlineData(typeof(DllNotFoundException), ArCameraImportFailureDisposition.TerminalNativeBridge)]
    [InlineData(typeof(EntryPointNotFoundException), ArCameraImportFailureDisposition.TerminalNativeBridge)]
    [InlineData(typeof(BadImageFormatException), ArCameraImportFailureDisposition.TerminalNativeBridge)]
    [InlineData(typeof(NotSupportedException), ArCameraImportFailureDisposition.TerminalUnsupportedFormat)]
    [InlineData(typeof(InvalidOperationException), ArCameraImportFailureDisposition.RetryWithBackoff)]
    public void ClassifiesFailures(Type exceptionType, ArCameraImportFailureDisposition expected) =>
        Assert.Equal(expected, ArCameraImportFailurePolicy.Classify((Exception)Activator.CreateInstance(exceptionType)!));

    [Fact]
    public void DeviceLostIsTerminal() =>
        Assert.Equal(ArCameraImportFailureDisposition.TerminalRenderer,
            ArCameraImportFailurePolicy.Classify(new InvalidOperationException("VK_ERROR_DEVICE_LOST")));

    [Fact]
    public void RetryBudgetIsBounded()
    {
        Assert.False(ArCameraImportFailurePolicy.ShouldEnterTerminalState(ArCameraImportFailureDisposition.RetryWithBackoff, 2));
        Assert.True(ArCameraImportFailurePolicy.ShouldEnterTerminalState(ArCameraImportFailureDisposition.RetryWithBackoff, 3));
        Assert.True(ArCameraImportFailurePolicy.ShouldEnterTerminalState(ArCameraImportFailureDisposition.TerminalNativeBridge, 1));
    }
}
