// Test-only MAUI boundaries. The actual service and lease-manager sources are
// compiled into this runner; these fakes do not exercise Android hardware.
using System.Numerics;

namespace Microsoft.Maui.Devices
{
    public static class DeviceDisplay
    {
        public static (string Rotation, int Unused) MainDisplayInfo => ("Portrait", 0);
    }
}

namespace Microsoft.Maui.Devices.Sensors
{
    public enum SensorSpeed { UI }
    public sealed class OrientationSensorChangedEventArgs(Quaternion orientation) : EventArgs
    {
        public (Quaternion Orientation, int Unused) Reading => (orientation, 0);
    }
    public sealed class AccelerometerChangedEventArgs(Vector3 acceleration) : EventArgs
    {
        public (Vector3 Acceleration, int Unused) Reading => (acceleration, 0);
    }
    public static class OrientationSensor
    {
        public static FakeOrientationSensor Default { get; } = new();
    }
    public static class Accelerometer
    {
        public static FakeAccelerometer Default { get; } = new();
    }
    public sealed class FakeOrientationSensor
    {
        public bool IsSupported => true;
        public bool IsMonitoring { get; private set; }
        public Action? BeforeStart { get; set; }
        public event EventHandler<OrientationSensorChangedEventArgs>? ReadingChanged;
        public void Start(SensorSpeed speed) { BeforeStart?.Invoke(); IsMonitoring = true; }
        public void Stop() => IsMonitoring = false;
        public void Emit(Quaternion value) => ReadingChanged?.Invoke(this, new(value));
    }
    public sealed class FakeAccelerometer
    {
        public bool IsSupported => true;
        public bool IsMonitoring { get; private set; }
        public event EventHandler<AccelerometerChangedEventArgs>? ReadingChanged;
        public void Start(SensorSpeed speed) => IsMonitoring = true;
        public void Stop() => IsMonitoring = false;
        public void Emit(Vector3 value) => ReadingChanged?.Invoke(this, new(value));
    }
}
