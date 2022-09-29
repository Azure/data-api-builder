## Release Checklist
- [ ] Make sure that the rolling build after the latest desired commit required for the release is green. You can do so by navigating to the root of the repo in GitHub to check the commit banner for successful pipeline execution: ![Main Branch Pipeline Status](MainRepoPipelineExecutionStatus.png)
- [ ] Create a new branch `release/MonthNameYear` where MonthNameYear matches the milestone name, e.g. `Oct2022` The version number for this release will be the current major.minor numbers as in the `main` branch. The patch number will be the final build number that will be used to generate the binaries to be released.
- [ ] After creating the release branch, increase the value of the `minor` variable in `.pipelines/build-pipelines.yml` and open a pull request on `main` with the update. This update to the minor version number can be reviewed and merged asychronously from the release so you may continue on to the next checklist items.
- [ ] Trigger a release [build](https://msdata.visualstudio.com/CosmosDB/_build?definitionId=18014) targeting the newly created branch by setting the pipeline variables `isNugetRelease` to `true` and `releaseName` to `MonthNameYear`. ![Update Pipeline Variables](PipelineVariables.png)
- [ ] After a successful build,
  - Verify the [nuget feed](https://msdata.visualstudio.com/CosmosDB/_artifacts/feed/DataApiBuilder) is updated with the desired release version for the `dab` package. Download the nuget and do some smoke tests following the instructions [here.](../getting-started/getting-started-dab-cli.md)
  - Verify the [`dab`](https://ms.portal.azure.com/#view/Microsoft_Azure_ContainerRegistries/RepositoryBlade/id/%2Fsubscriptions%2Fb9c77f10-b438-4c32-9819-eef8a654e478%2FresourceGroups%2Fhawaii-demo-rg%2Fproviders%2FMicrosoft.ContainerRegistry%2Fregistries%2Fhawaiiacr/repository/dab) repository under the [`hawaiiacr`](https://ms.portal.azure.com/#@microsoft.onmicrosoft.com/resource/subscriptions/b9c77f10-b438-4c32-9819-eef8a654e478/resourceGroups/hawaii-demo-rg/providers/Microsoft.ContainerRegistry/registries/hawaiiacr/repository) container registry has an image tagged with `MonthNameYear` and a matching `BuildDate-Build.SourceVersion` tag. Follow the instructions at [GettingStartedWithDocker.md](GetStartedWithDocker.md) and smoke test the docker scenario.
  - Verify that a new [Draft Release](https://github.com/Azure/data-api-builder/releases) is created containing the nuget package, manifest file and the zip files for all the 3 OS platforms. This Draft release will already be tagged with a versionID. E.g. `v.0.3.1-alpha`

- [ ] Go to the [release page](https://github.com/Azure/data-api-builder/releases). Follow the step by step instructions.
  - Check the target branch name. E.g. `Oct2022`
  - Provide a description to the release.
  - Publish the release.

### Guidelines for versioning

- Major – incremented to 1 when we do a public release. Currently it is zero. Any releases not backwards compatible should result in a major version number increment.
- Minor – Incremented every time we do a snap (e.g. creating a branch for a monthly release).
- Patch – Automatically set by the pipeline
