namespace RescuAR.MAUI.Evergine
{
    public partial class EvergineViewHandler
    {
        public static readonly IPropertyMapper<EvergineView, EvergineViewHandler> PropertyMapper =
            new PropertyMapper<EvergineView, EvergineViewHandler>(ViewMapper)
            {
                [nameof(EvergineView.Application)] = MapApplication,
            };

        public static readonly CommandMapper<EvergineView, EvergineViewHandler> CommandMapper =
            new(ViewCommandMapper);

        public EvergineViewHandler()
            : base(PropertyMapper, CommandMapper)
        {
        }
    }
}
