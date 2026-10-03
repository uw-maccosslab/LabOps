# ChargeState v26.2.0

ChargeState now finds new releases while it is open, not only when it starts.

## New Features

- **Updates are found while the app is open.** ChargeState looked for a new release only when it
  started, so a copy left open all day never offered one. It now also checks every four hours, as
  PanoramaBridge does, downloads in the background, and shows **Update ready: restart to install**.
  It never checks again once an update is waiting, and installing still waits for you
  (`src/ChargeState.App/Services/UpdateService.cs`).
