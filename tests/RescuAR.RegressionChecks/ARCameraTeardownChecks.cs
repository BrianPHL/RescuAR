using RescuAR.AR;

internal static class ARCameraTeardownChecks
{
    public static void Run(Action<bool, string> check)
    {
        const long generation = 33100;
        int teardownCount = 0;
        Task? duringTeardown = null;
        ARCameraTextureBridge.SetDrawThreadProcessor(generation, () => false);
        Task teardown = ARCameraTextureBridge.RequestDrawThreadTeardownAsync(generation, () =>
        {
            teardownCount++;
            duringTeardown = ARCameraTextureBridge.ExecuteDrawThreadTeardownAtSurfaceBoundaryAsync(
                generation, () => teardownCount++);
        });
        bool staleDrawSkipped = ARCameraTextureBridge.ProcessDrawThreadWork(generation - 1);
        check(staleDrawSkipped && teardownCount == 0 && !teardown.IsCompleted,
            "A stale draw callback cannot claim another graphics owner's teardown");
        bool drawSkipped = ARCameraTextureBridge.ProcessDrawThreadWork(generation);
        check(drawSkipped && teardownCount == 1 && !teardown.IsCompleted && ReferenceEquals(teardown, duringTeardown),
            "Disconnect during draw teardown waits for the active owner's frame boundary");
        Task atBoundary = ARCameraTextureBridge.ExecuteDrawThreadTeardownAtSurfaceBoundaryAsync(generation,
            () => teardownCount++);
        ARCameraTextureBridge.CompleteDrawThreadFrame(generation);
        check(ReferenceEquals(teardown, atBoundary) && teardown.IsCompletedSuccessfully && teardownCount == 1,
            "A disconnected surface acknowledges completed draw cleanup exactly once");

        const long nextGeneration = generation + 1;
        ARCameraTextureBridge.SetDrawThreadProcessor(nextGeneration, () => false);
        ARCameraTextureBridge.ResumeProcessing(nextGeneration);
        check(!ARCameraTextureBridge.ProcessDrawThreadWork(nextGeneration) && ARCameraTextureBridge.ProcessDrawThreadWork(generation),
            "A replacement graphics owner renders after retirement and rejects the old owner");

        var failure = new InvalidOperationException("GPU drain did not complete");
        Task failed = ARCameraTextureBridge.ExecuteDrawThreadTeardownAtSurfaceBoundaryAsync(nextGeneration, () => throw failure);
        Task duplicate = ARCameraTextureBridge.RequestDrawThreadTeardownAsync(nextGeneration, () => teardownCount++);
        ARCameraTextureBridge.ResumeProcessing(nextGeneration);
        check(ReferenceEquals(failed, duplicate) && ReferenceEquals(failed.Exception?.InnerException, failure) &&
            ARCameraTextureBridge.ProcessDrawThreadWork(nextGeneration) && teardownCount == 1,
            "Failed surface cleanup reaches every waiter and retired draws remain disabled");
    }
}
