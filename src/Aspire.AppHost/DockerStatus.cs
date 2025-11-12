using System.Diagnostics;

public static class DockerStatus
{
    public static async Task<bool> IsDockerRunningAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "info",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        try
        {
            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
