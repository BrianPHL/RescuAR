using EvergineApplication = global::Evergine.Framework.Application;

namespace RescuAR.MAUI.Evergine
{
    public class EvergineView : View
    {
#if ANDROID
        private global::RescuAR.MyApplication? claimedApplication;

        internal global::RescuAR.MyApplication ClaimApplicationForAndroidHandler(global::RescuAR.MyApplication application)
        {
            // A replacement handler needs a new container. The pinned engine
            // only registers its surface, display and graphics device once.
            if (ReferenceEquals(claimedApplication, application)) return new global::RescuAR.MyApplication();
            claimedApplication = application;
            return application;
        }
#endif

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
