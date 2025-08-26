namespace WorkerService;

/// <summary>
/// The Program class serves as the entry point for the WorkerService application.
/// This class invokes the asynchronous execution of the service.
/// </summary>
public static class Program
{
    public static Task Main(string[] args)
    {
        return new Services.WorkerServiceRunner().RunAsync(args);
    }
}
