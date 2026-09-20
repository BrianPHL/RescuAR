using RescuAR.MAUI.Services;

namespace RescuAR.Tests;

public sealed class ArCoreLifecyclePolicyTests
{
    [Theory]
    [InlineData(ArCoreLifecycleState.Uninitialized, ArCoreLifecycleState.Initializing)]
    [InlineData(ArCoreLifecycleState.Initializing, ArCoreLifecycleState.Uninitialized)]
    [InlineData(ArCoreLifecycleState.Initializing, ArCoreLifecycleState.Running)]
    [InlineData(ArCoreLifecycleState.Running, ArCoreLifecycleState.Pausing)]
    [InlineData(ArCoreLifecycleState.Pausing, ArCoreLifecycleState.Paused)]
    [InlineData(ArCoreLifecycleState.Paused, ArCoreLifecycleState.Initializing)]
    [InlineData(ArCoreLifecycleState.Running, ArCoreLifecycleState.Disposing)]
    [InlineData(ArCoreLifecycleState.Disposing, ArCoreLifecycleState.Disposed)]
    [InlineData(ArCoreLifecycleState.Faulted, ArCoreLifecycleState.Uninitialized)]
    public void LegalTransitionsAreAccepted(ArCoreLifecycleState from, ArCoreLifecycleState to) =>
        Assert.True(ArCoreLifecyclePolicy.IsLegalTransition(from, to));

    [Theory]
    [InlineData(ArCoreLifecycleState.Running)]
    [InlineData(ArCoreLifecycleState.Paused)]
    [InlineData(ArCoreLifecycleState.Disposed)]
    public void RepeatedStateIsIdempotent(ArCoreLifecycleState state) =>
        Assert.True(ArCoreLifecyclePolicy.IsLegalTransition(state, state));

    [Theory]
    [InlineData(ArCoreLifecycleState.Running, ArCoreLifecycleState.Uninitialized)]
    [InlineData(ArCoreLifecycleState.Disposed, ArCoreLifecycleState.Running)]
    [InlineData(ArCoreLifecycleState.Uninitialized, ArCoreLifecycleState.Running)]
    public void IllegalTransitionsAreRejected(ArCoreLifecycleState from, ArCoreLifecycleState to) =>
        Assert.False(ArCoreLifecyclePolicy.IsLegalTransition(from, to));

    [Fact]
    public void OnlyLatestGenerationAndTargetAreAccepted()
    {
        Assert.True(ArCoreLifecyclePolicy.IsLatestRequest(8, ArCoreLifecycleTarget.Running, 8, ArCoreLifecycleTarget.Running));
        Assert.False(ArCoreLifecyclePolicy.IsLatestRequest(7, ArCoreLifecycleTarget.Running, 8, ArCoreLifecycleTarget.Running));
        Assert.False(ArCoreLifecyclePolicy.IsLatestRequest(8, ArCoreLifecycleTarget.Paused, 8, ArCoreLifecycleTarget.Running));
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public void ExceptionsMapToTypedFailures(Exception exception, ArCoreFailureCode expected) =>
        Assert.Equal(expected, ArCoreLifecyclePolicy.ClassifyFailureCode(new InvalidOperationException("outer", exception), ArCoreFailureCode.Unknown));

    [Fact]
    public void PausedSessionRemainsPausedOnFailure()
    {
        Assert.Equal(ArCoreLifecycleState.Paused, ArCoreLifecyclePolicy.GetFailureState(true, true));
        Assert.Equal(ArCoreLifecycleState.Faulted, ArCoreLifecyclePolicy.GetFailureState(true, false));
    }

    public static TheoryData<Exception, ArCoreFailureCode> FailureCases => new()
    {
        { new DeviceNotCompatibleException(), ArCoreFailureCode.UnsupportedDevice },
        { new ArCoreNotInstalledException(), ArCoreFailureCode.InstallationRequired },
        { new CameraNotAvailableException(), ArCoreFailureCode.CameraUnavailable },
        { new DllNotFoundException(), ArCoreFailureCode.NativeBridgeUnavailable },
        { new TimeoutException(), ArCoreFailureCode.TimedOut },
    };

    private sealed class DeviceNotCompatibleException : Exception { }
    private sealed class ArCoreNotInstalledException : Exception { }
    private sealed class CameraNotAvailableException : Exception { }
}
