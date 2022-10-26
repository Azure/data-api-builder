# Security Considerations

## Disable Legacy Versions of TLS at the Server Level

### Background

Data sent between a client and Data API Builder should occur over a secure connection to protect sensitive or valuable information. A secure connection is typically established using Transport Layer Security (TLS) protocols.

Detailed in [OWASP's Transport Layer Protection](https://cheatsheetseries.owasp.org/cheatsheets/Transport_Layer_Protection_Cheat_Sheet.html) guidance, TLS provides numerous security benefits when implemented correctly:

>- **Confidentiality** - protection against an attacker from reading the contents of traffic.
>- **Integrity** - protection against an attacker modifying traffic.
>- **Replay prevention** - protection against an attacker replaying requests against the server.
>- **Authentication** - allowing the client to verify that they are connected to the real server (note that the identity of the client is not verified unless client certificates are used).

### Recommendation

One way to help configure TLS securely is **to disable usage of legacy versions of TLS at the server level**. Data API Builder is built on Kestrel, a [cross-platform web server for ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-6.0) and is configured by default to defer to the operating system's TLS version configuration. Microsoft's [TLS best practices for .NET guidance](https://learn.microsoft.com/dotnet/framework/network-programming/tls) describe the motivation behind such behavior:
> TLS 1.2 is a standard that provides security improvements over previous versions. TLS 1.2 will eventually be replaced by the newest released standard TLS 1.3 which is faster and has improved security.

> To ensure .NET Framework applications remain secure, the TLS version should not be hardcoded. .NET Framework applications should use the TLS version the operating system (OS) supports.

While explicitly defining supported TLS protocol versions for Kestrel is supported, doing so is not recommended because such definitions translate to an allow-list which prevents support for future TLS versions as they become available. More information about Kestrel's default TLS protocol version behavior can be found [here](https://learn.microsoft.com/dotnet/core/compatibility/aspnet-core/5.0/kestrel-default-supported-tls-protocol-versions-changed).
