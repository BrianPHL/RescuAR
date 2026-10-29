using System.Xml.Linq;
using RescuAR.MAUI.Services.Navigation;

internal static class NavigationPresentationChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(RouteStartupRetryPolicy.CanRetryInView(true, true, false, false),
            "GPS recovery retries from 2D Map with ARCore paused or uninitialized");
        check(RouteStartupRetryPolicy.CanRetryInView(true, false, true, false),
            "GPS recovery in AR does not wait for an AR tracking session");
        check(!RouteStartupRetryPolicy.CanRetryInView(false, true, false, false),
            "2D Map does not repeatedly restart an accepted route while waiting only for heading");
        check(RouteStartupRetryPolicy.CanRetryInView(false, false, true, true) &&
            !RouteStartupRetryPolicy.CanRetryInView(false, false, true, false),
            "heading-only recovery still requires a ready AR view");
        check(!RouteStartupRetryPolicy.CanRetryInView(true, false, false, true),
            "the flood view cannot initiate navigation retries");

        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RescuAR.MAUI", "Views", "Camera", "CameraPage.xaml")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("Run checks inside the repository.");
        var document = XDocument.Load(Path.Combine(root.FullName, "RescuAR.MAUI", "Views", "Camera", "CameraPage.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2009/xaml";
        var stack = document.Descendants().Single(node => (string?)node.Attribute(x + "Name") == "arNavigationOverlays");
        check(stack.Name.LocalName == "VerticalStackLayout" && (string?)stack.Attribute("Spacing") == "6",
            "AR instruction and status cards use natural stacked layout with explicit spacing");
        foreach (string name in new[] { "cameraModeStatusBanner", "arTrackingStatusBanner", "lowLightFallbackBanner", "turnGuidancePanel", "routeLocatorPanel" })
        {
            var card = document.Descendants().Single(node => (string?)node.Attribute(x + "Name") == name);
            check(card.Parent == stack && (string?)card.Attribute("Margin") == "0",
                $"{name}: changing visibility or wrapped height cannot overlap a sibling through absolute margins");
        }
        check((string?)stack.Attribute("CascadeInputTransparent") == "False",
            "stacking overlays retains interaction with instruction and low-light buttons");
    }
}
