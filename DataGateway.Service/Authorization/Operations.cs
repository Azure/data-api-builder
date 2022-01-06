using Microsoft.AspNetCore.Authorization.Infrastructure;

public static class Operations
{
    public static OperationAuthorizationRequirement POST =
        new () { Name = nameof(POST) };
    public static OperationAuthorizationRequirement GET =
        new () { Name = nameof(GET) };
}
