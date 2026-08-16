using EvergineApplication = global::Evergine.Framework.Application;

namespace RescuAR.MAUI.Evergine
{
    public class EvergineView : View
    {
        public static readonly BindableProperty ApplicationProperty =
            BindableProperty.Create(
                nameof(Application),
                typeof(EvergineApplication),
                typeof(EvergineView),
                default(EvergineApplication));

        public static readonly BindableProperty DisplayNameProperty =
            BindableProperty.Create(
                nameof(DisplayName),
                typeof(string),
                typeof(EvergineView),
                string.Empty);

        public EvergineApplication? Application
        {
            get => (EvergineApplication?)GetValue(ApplicationProperty);
            set => SetValue(ApplicationProperty, value);
        }

        public string DisplayName
        {
            get => (string)GetValue(DisplayNameProperty);
            set => SetValue(DisplayNameProperty, value);
        }

        public event EventHandler<EventArgs>? PointerPressed;

        public event EventHandler<EventArgs>? PointerMoved;

        public event EventHandler<EventArgs>? PointerReleased;

        internal void StartInteraction() =>
            PointerPressed?.Invoke(this, EventArgs.Empty);

        internal void MovedInteraction() =>
            PointerMoved?.Invoke(this, EventArgs.Empty);

        internal void EndInteraction() =>
            PointerReleased?.Invoke(this, EventArgs.Empty);
    }
}
