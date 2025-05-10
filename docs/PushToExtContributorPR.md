## How to Complete an External Contributor PR from their fork

In case it is not possible to either git switch or git fetch

1. Add the contributor's repo `.git` as a remote, you can find the `.git` by clicking on their branch which should bring you to their fork and then clicking on the Green Code button, the .git address will be listed under the option to clone using web URL. Copy this `.git` address for future steps.
2. `git remote add <name> <.git address>`

For example:
`git remote add thomasfroehle https://github.com/thomasfroehle/data-api-builder.git`

3. Run `git fetch <name>` for the remote you just added

For example:
`git fetch thomasfroehle`

4. You now have the ability to either rebase their branch onto a working branch you've already made changes to, or you can start your changes fresh on their branch.

If you are trying to add changes you've already made you can use the following example, note that `thomasfroehle` is the name of the remote we added, so you would use whatever remote name is relevant. Suppose I have a branch locally with the fix named `dev/aaronburtle/CompleteQuotedTableSupport`

`git checkout -b fix-case-sensitive-table-names thomasfroehle/fix-case-sensitive-table-names`
`git rebase dev/aaronburtle/CompleteQuotedTableSupport` 
`git push thomasfroehle fix-case-sensitive-table-names`

If you just want to start fresh from their branch you can do
`git switch <branchname>` without the need to include the name of the remote you added since you've fetched it already in step 3.

5. You can now commit and push but make sure you use
`git push <name of remote> <name of branch>`

For example:
`git push --set-upstream thomasfroehle fix-case-sensitive-table-names`