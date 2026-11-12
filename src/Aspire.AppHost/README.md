# Aspire Instructions

This project allows you to run DAB in debug mode using [Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview).

## Prerequisites
- [.NET SDK](https://dotnet.microsoft.com/download) (10.0 or later)
- [Docker](https://www.docker.com/products/docker-desktop) (optional, for containerized development)

The AppHost SDK and hosting integrations use Aspire 13.6.5, which includes the fix for
[CVE-2026-120006](https://github.com/advisories/GHSA-f7vh-h5g3-7jrh). The PostgreSQL container
stays on major version 17; upgrading the hosting libraries does not opt existing development
databases into PostgreSQL 18.

## Database Configuration

In the `launchProfile.json` file, you can configure the database connection string. If you don't, Aspire will start for you a local instance in a Docker container.

Simply provide a value for the `ASPIRE_DATABASE_CONNECTION_STRING` environment variable.

You can select to run Aspire with different databases selecting the appropriate launch profile:
- `aspire-sql`
- `aspire-postgres`
