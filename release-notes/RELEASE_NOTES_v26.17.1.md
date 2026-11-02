# LabOps v26.17.1

Setup let you switch to a different GitHub account and set your name on saved changes again after
those steps were done, showed its progress while it prepared an engine, and no longer opened on the
Claude settings alone.

## Bug Fixes

- **Setup could switch GitHub accounts.** When LabOps was installed on a computer where GitHub was
  already signed in, Setup took that account as done and offered no way to change it, even when it
  was someone else's. The GitHub sign-in step now keeps a **Use a different account** button after
  it is done, and the Git identity step keeps **Use my GitHub name**, so the changes you save carry
  your name after switching. When a GH_TOKEN or GITHUB_TOKEN environment variable picks the
  account, which no sign-in can override, the step says so and names the variable to remove, also
  when GitHub no longer accepts that token.
- **Preparing an engine in Setup showed how far it had got.** The first **Prepare** on a new computer
  downloads Python and every package the protocol or quote engine needs, which can take minutes,
  and Setup had shown one line that never changed, so it looked stuck and was closed partway. The
  line now names each stage as uv reports it ("downloading Python (21.0 MiB)", "installing 17
  packages") beside a clock that counts up, and **Done** waits until the step finishes
  (`src/LabOps.Core/Engines/UvProgress.cs`, `src/LabOps.App/ViewModels/SetupViewModel.cs`).
- **Setup said "Everything is ready" only when nothing was left.** With an optional step still open,
  such as an engine not yet prepared or the quotes not downloaded, it said everything was ready, and
  people closed it before reaching the quotes. It now says "Everything LabOps needs is ready. The
  steps still open are optional." (`src/LabOps.Core/Setup/SetupService.cs`).
- **Setup opened on its checklist, not the Claude settings.** For the few seconds Setup spends
  checking the sign-ins, the window had shown only the Claude model and effort choices. It now
  says it is checking what is installed and signed in, and shows the Claude settings under the
  checklist once it appears (`src/LabOps.App/Views/SetupWindow.xaml`).
