using PlaywrightHub.Services;

namespace PlaywrightHub;

/// <summary>
/// Entry point of the PlaywrightHub application.
/// </summary>
/// <remarks>
/// The <c>Program</c> class is responsible for initializing and invoking the
/// <c>HubServiceRunner</c> to start the application. This is done via the <c>Main</c> method,
/// which takes any arguments provided during execution and passes them to the service.
/// </remarks>
public static class Program
{
    /// <summary>
    /// The entry point of the application.
    /// </summary>
    /// <param name="args">An array of command-line arguments passed to the application.</param>
    /// <returns>A task that represents the asynchronous operation of initializing and starting the application.</returns>
    public static Task Main(string[] args)
    {
        return HubServiceRunner.RunAsync(args);
    }
}
