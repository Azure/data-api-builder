# Data API Builder Docker Container

Production-ready Data API Builder service container.

## Quick Start

### Pull from GitHub Container Registry

```bash
docker pull ghcr.io/karo-data-management-limited/wellbeing-os-module-dab:latest
```

### Run with Configuration File

```bash
docker run -d \
  --name dab \
  -p 5000:5000 \
  -v $(pwd)/dab-config.json:/App/config/dab-config.json:ro \
  -e DATABASE_CONNECTION_STRING="Server=host.docker.internal,1433;Database=mydb;User ID=sa;Password=YourPassword;TrustServerCertificate=true" \
  ghcr.io/karo-data-management-limited/wellbeing-os-module-dab:latest
```

## Environment Variables

| Variable | Description | Default | Required |
|----------|-------------|---------|----------|
| `ASPNETCORE_URLS` | URLs to bind | `http://+:5000` | No |
| `ASPNETCORE_ENVIRONMENT` | Environment name | `Production` | No |
| `DATABASE_CONNECTION_STRING` | Database connection string | - | Yes |

### Additional Configuration

Mount your `dab-config.json` file to `/App/config/dab-config.json` in the container.

## Docker Compose Example

```yaml
version: '3.8'

services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      SA_PASSWORD: "YourStrong@Passw0rd"
    ports:
      - "1433:1433"
    volumes:
      - sqldata:/var/opt/mssql

  dab:
    image: ghcr.io/karo-data-management-limited/wellbeing-os-module-dab:latest
    ports:
      - "5000:5000"
    volumes:
      - ./dab-config.json:/App/config/dab-config.json:ro
    environment:
      DATABASE_CONNECTION_STRING: "Server=sqlserver,1433;Database=mydb;User ID=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=true"
    depends_on:
      - sqlserver
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5000/health"]
      interval: 30s
      timeout: 3s
      retries: 3

volumes:
  sqldata:
```

## Building Locally

```bash
docker build -t dab:local .
```

## Endpoints

- **Health Check**: `http://localhost:5000/health`
- **REST API**: `http://localhost:5000/api`
- **GraphQL**: `http://localhost:5000/graphql`
- **Swagger** (dev mode): `http://localhost:5000/swagger`

## Configuration File Structure

The `dab-config.json` file should follow the Data API Builder schema. See [config/dab-config.template.json](../config/dab-config.template.json) for a template.

Key sections:
- `data-source`: Database connection and type
- `runtime`: REST, GraphQL, host settings
- `entities`: Entity definitions with permissions

## Troubleshooting

### Container won't start

Check logs:
```bash
docker logs dab
```

### Database connection fails

Ensure:
1. Connection string is correct
2. Database server is accessible from container
3. Use `host.docker.internal` for localhost databases on Windows/Mac
4. Firewall allows connections
5. TrustServerCertificate=true is set if using self-signed certs

### Configuration file not found

Verify:
1. Config file is mounted to `/App/config/dab-config.json`
2. File path on host is correct
3. File has proper permissions (readable)

## Security Considerations

1. **Never commit secrets** to version control
2. Use **Azure Key Vault** or similar for production secrets
3. Set `host.mode` to `production` in production environments
4. Configure appropriate CORS origins
5. Enable authentication provider (StaticWebApps, EasyAuth, etc.)
6. Use **managed identities** when running in Azure

## Support

For issues and questions:
- GitHub Issues: https://github.com/Karo-Data-Management-Limited/wellbeing-os-module-dab/issues
- Documentation: See [docs/](../docs/) folder
