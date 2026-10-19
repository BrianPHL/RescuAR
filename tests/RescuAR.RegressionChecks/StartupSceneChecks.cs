using System.Reflection;
using Evergine.Framework.Services;
using RescuAR;

internal static class StartupSceneChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Metadata checks require no graphics device or application startup.
        var factory = new CompiledMyScene(new AssetsService());
        check(factory.AssetId == EvergineContent.Scenes.MyScene_wescene,
            "generated startup factory resolves the same scene identifier used by the application");
        check(factory.AssetPath == $"{EvergineContent.Scenes.MyScene_wescene}.wepsc",
            "generated startup factory supplies the compiled scene omitted from exported assets");
        var registration = typeof(MyApplication).GetMethod("RegisterSceneSourceFactories",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        check(registration is { IsVirtual: true } &&
            registration.GetBaseDefinition().DeclaringType == typeof(Evergine.Framework.Application),
            "the application overrides scene factory registration instead of inheriting the empty default");
    }
}
