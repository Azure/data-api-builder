# Security Considerations

## Disable Legacy Versions of TLS at the Server Level

### Background

Data sent between a client and Data API builder should occur over a secure connection to protect sensitive or valuable information. A secure connection is typically established using Transport Layer Security (TLS) protocols.

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

### Platform Resources

TLS 1.2 is enabled by default on the latest versions of .NET and many of the latest operating system versions.

#### Windows

- Install .NET on Windows - [Microsoft Learn](https://learn.microsoft.com/dotnet/core/install/windows?tabs=net60)
- Enable support for TLS 1.2 in your environment - [Azure AD Guidance](https://learn.microsoft.com/troubleshoot/azure/active-directory/enable-support-tls-environment?tabs=azure-monitor#enable-support-for-tls-12-in-your-environment)
- TLS 1.2 support at Microsoft - [Microsoft Security Blog](https://www.microsoft.com/security/blog/2017/06/20/tls-1-2-support-at-microsoft/)

#### macOS

- Install .NET on macOS - [Microsoft Learn](https://learn.microsoft.com/dotnet/core/install/macos)
- TLS Security - [Apple Platform Security](https://support.apple.com/guide/security/tls-security-sec100a75d12/web)
- TLS 1.2 is enabled starting with OS X Mavericks(10.9) - [About the security content of OS X Mavericks v10.9](https://support.apple.com/HT202854)

#### Linux

- Install .NET on Linux - [Microsoft Learn](https://learn.microsoft.com/dotnet/core/install/linux)
- Linux .NET Dependencies - [GitHub](https://github.com/dotnet/core/blob/main/release-notes/6.0/linux-packages.md)
  - Includes [OpenSSL](https://www.openssl.org/) where the latest versions support TLS protocol versions up through TLS 1.3. [OpenSSL Wiki](https://wiki.openssl.org/index.php/TLS1.3)
