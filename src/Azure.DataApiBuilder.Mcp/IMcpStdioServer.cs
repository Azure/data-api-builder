namespace Azure.DataApiBuilder.Mcp.Core
{
    public interface IMcpStdioServer
    {
        Task RunAsync(CancellationToken cancellationToken);
    }
}