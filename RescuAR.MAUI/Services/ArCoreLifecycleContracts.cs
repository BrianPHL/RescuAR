namespace RescuAR.MAUI.Services;

/// <summary>
/// Authoritative lifecycle states for the single ARCore session owned by
/// <see cref="IArCoreService"/>.
/// </summary>
public enum ArCoreLifecycleState
{
    Uninitialized,
    Initializing,
    Running,
    Pausing,
    Paused,
    Disposing,
    Disposed,
    Faulted,
}

public enum ArCoreLifecycleTarget
{
    Paused,
    Running,
    Disposed,
}

/// <summary>
/// Stable failure identifiers used by UI and diagnostics. Messages may
/// change; callers must branch on the code and classification.
/// </summary>
public enum ArCoreFailureCode
{
    None,
    Cancelled,
    TimedOut,
    Superseded,
    ActivityUnavailable,
    CameraPermissionDenied,
    AvailabilityPending,
    UnsupportedDevice,
    InstallationRequired,
    InstallationFailed,
    GraphicsUnavailable,
    SessionInitializationFailed,
    CameraUnavailable,
    SessionResumeFailed,
    SessionPauseFailed,
    SessionShutdownFailed,
    NativeBridgeUnavailable,
    RendererUnavailable,
    TrackingLost,
    Unknown,
}

public enum ArCoreFailureClassification
{
    None,
    Recoverable,
    Terminal,
}

public sealed record ArCoreFailure(
    ArCoreFailureCode Code,
    ArCoreFailureClassification Classification,
    string Message)
{
    public static ArCoreFailure None { get; } =
        new(
            ArCoreFailureCode.None,
            ArCoreFailureClassification.None,
            string.Empty);
}

/// <summary>
/// One versioned view of the prerequisites used for a lifecycle decision.
/// A null native-bridge value means that the Batch 3 startup self-test has
/// not supplied evidence yet; it is deliberately not treated as success.
/// </summary>
public sealed record ArCoreCapabilitySnapshot(
    long Version,
    DateTimeOffset CapturedAtUtc,
    bool IsCurrent,
    bool ActivityAvailable,
    bool CameraPermissionGranted,
    bool? CameraAvailable,
    string Availability,
    bool ArCoreSupported,
    bool ArCoreInstalled,
    string Abi,
    bool? DepthSupported,
    bool GraphicsReady,
    bool? NativeBridgeReady,
    long GraphicsGeneration,
    string Reason);

public sealed record ArCoreLifecycleSnapshot(
    ArCoreLifecycleState State,
    ArCoreLifecycleTarget DesiredState,
    long RequestGeneration,
    long SessionGeneration,
    DateTimeOffset ChangedAtUtc,
    ArCoreCapabilitySnapshot Capabilities,
    ArCoreFailure Failure);

public sealed record ArCoreLifecycleResult(
    bool Success,
    ArCoreLifecycleSnapshot Snapshot)
{
    public ArCoreFailure Failure =>
        Snapshot.Failure;
}
