const fs = require('fs');
const path = require('path');

// 1. Rewrite Nuget.config - remove EngThrive-MCP, add nuget.org
const nugetConfig = `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="data_api_builder_build_packages" value="https://pkgs.dev.azure.com/sqldab/fcb212b3-b288-4c9e-b55a-5842a268b16d/_packaging/data_api_builder_build_packages/nuget/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="ModelContextProtocol" />
      <package pattern="ModelContextProtocol.*" />
    </packageSource>
    <packageSource key="data_api_builder_build_packages">
      <package pattern="*" />
    </packageSource>
    <!-- CI (Azure Pipelines NuGetAuthenticate) renames the source with a "feed-" prefix at runtime -->
    <packageSource key="feed-data_api_builder_build_packages">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
  <disabledPackageSources>
    <clear />
  </disabledPackageSources>
</configuration>
`;
fs.writeFileSync(path.join(__dirname, '..', 'Nuget.config'), nugetConfig.trimStart());
console.log('1. Nuget.config updated');

// 2. Update Directory.Packages.props - swap package
const dppPath = path.join(__dirname, 'Directory.Packages.props');
let dpp = fs.readFileSync(dppPath, 'utf8');
// Remove Microsoft.ModelContextProtocol.HttpServer line
dpp = dpp.replace(/\s*<PackageVersion Include="Microsoft\.ModelContextProtocol\.HttpServer"[^/]*\/>/g, '');
// Add ModelContextProtocol and ModelContextProtocol.AspNetCore if not present
if (!dpp.includes('Include="ModelContextProtocol"')) {
  // Insert after HotChocolate.ModelContextProtocol line or before closing ItemGroup
  dpp = dpp.replace(
    /(<PackageVersion Include="HotChocolate\.ModelContextProtocol"[^/]*\/>)/,
    '$1\n    <PackageVersion Include="ModelContextProtocol" Version="1.0.0" />\n    <PackageVersion Include="ModelContextProtocol.AspNetCore" Version="1.0.0" />'
  );
}
fs.writeFileSync(dppPath, dpp);
console.log('2. Directory.Packages.props updated');

// 3. Update Mcp .csproj - swap package reference
const csprojPath = path.join(__dirname, 'Azure.DataApiBuilder.Mcp', 'Azure.DataApiBuilder.Mcp.csproj');
let csproj = fs.readFileSync(csprojPath, 'utf8');
csproj = csproj.replace(
  /<PackageReference Include="Microsoft\.ModelContextProtocol\.HttpServer"\s*\/>/,
  '<PackageReference Include="ModelContextProtocol" />\n    <PackageReference Include="ModelContextProtocol.AspNetCore" />'
);
fs.writeFileSync(csprojPath, csproj);
console.log('3. Azure.DataApiBuilder.Mcp.csproj updated');

// 4. Remove nuGetServiceConnections from pipeline files (no longer needed)
const pipelineDir = path.join(__dirname, '..', '.pipelines');
const pipelineFiles = [
  'cosmos-pipelines.yml', 'dwsql-pipelines.yml', 'mssql-pipelines.yml',
  'mysql-pipelines.yml', 'pg-pipelines.yml',
  path.join('templates', 'build-pipelines.yml'),
  path.join('templates', 'static-tools.yml')
];
for (const f of pipelineFiles) {
  const fp = path.join(pipelineDir, f);
  if (!fs.existsSync(fp)) continue;
  let content = fs.readFileSync(fp, 'utf8');
  const original = content;
  // Remove the "inputs:" and "nuGetServiceConnections:" lines under NuGetAuthenticate@1
  content = content.replace(
    /(- task: NuGetAuthenticate@1\s*\n\s*displayName: 'NuGet Authenticate')\s*\n\s*inputs:\s*\n\s*nuGetServiceConnections: 'EngThriveNugetFeedAccessForSqlDab'/g,
    '$1'
  );
  if (content !== original) {
    fs.writeFileSync(fp, content);
    console.log('4. Pipeline updated: ' + f);
  }
}

console.log('\nDone! Run `dotnet restore` to verify.');
