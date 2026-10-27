# LabOps v26.17.1

Setup lets you switch to a different GitHub account, and set your name on saved changes again,
after those steps are done.

## Bug Fixes

- **Setup could switch GitHub accounts.** When LabOps was installed on a computer where GitHub was
  already signed in, Setup took that account as done and offered no way to change it, even when it
  was someone else's. The GitHub sign-in step now keeps a **Use a different account** button after
  it is done, and the Git identity step keeps **Use my GitHub name**, so the changes you save carry
  your name after switching. When a GH_TOKEN or GITHUB_TOKEN environment variable picks the
  account, which no sign-in can override, the step says so and names the variable to remove.
