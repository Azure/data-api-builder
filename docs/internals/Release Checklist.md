Release Checklist
- [ ] Make sure rolling build after the latest desired commit required for the release is green. Then create a new branch `release/Mxx` where xx is the release version.
[] Modify `.pipelines/build-pipelines.yml` to update the major, minor and patch version numbers to match xx. Example, for `M1.5`, set minor to 1 and patch to 5 (major is implicitly 0 here.) This new branch is now frozen at `v0.1.5`
- [ ] Trigger a release [build](https://msdata.visualstudio.com/CosmosDB/_build?definitionId=18014) targeting the newly created branch by setting the pipeline variables `isNugetRelease` to `true` and `releaseName` to `Mxx`.
- [ ] After a successful build,
- Verify the [nuget feed](https://msdata.visualstudio.com/CosmosDB/_artifacts/feed/DataApiBuilder) is updated with the desired release version for the `dab` package. Download the nuget and do some smoke tests following the instructions [here.](../getting-started/getting-started-dab-cli.md)
- Verify the [`dab`](https://ms.portal.azure.com/#view/Microsoft_Azure_ContainerRegistries/RepositoryBlade/id/%2Fsubscriptions%2Fb9c77f10-b438-4c32-9819-eef8a654e478%2FresourceGroups%2Fhawaii-demo-rg%2Fproviders%2FMicrosoft.ContainerRegistry%2Fregistries%2Fhawaiiacr/repository/dab) repository under the [`hawaiiacr`](https://ms.portal.azure.com/#@microsoft.onmicrosoft.com/resource/subscriptions/b9c77f10-b438-4c32-9819-eef8a654e478/resourceGroups/hawaii-demo-rg/providers/Microsoft.ContainerRegistry/registries/hawaiiacr/repository) container registry has an image tagged with `Mxx` and a matching `BuildDate-Build.SourceVersion` tag. Follow the instructions at [GettingStartedWithDocker.md](GetStartedWithDocker.md) and smoke test the docker scenario.

- [ ] Go to [creating a new release page](https://github.com/Azure/data-api-builder/releases/new). Follow the step by step instructions.
- Tag the release with the correct version, and branch name. E.g. `v0.1.5-alpha`, `M1.5`
- Provide a description to the release.
- Download the `dab` nuget package from the internal feed, and upload attach it to the release page.