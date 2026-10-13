# LabOps v26.16.0

Sample prep and data acquisition could be done in batches, each batch a step of its own under
its step, and a screen reader read the rows of the app's lists by their names.

## New Features

- **Sample prep and data acquisition could be done in batches.** A **Batches** button split the
  step into a number of batches, each a step of its own shown indented under it, which was started,
  finished, assigned and planned on its own. The step's row said how many batches were done and
  kept the step's protocol, notebook and tools. Importing an Octopus layout made sample prep one
  batch per plate, with each plate's number of samples. A step split after it was done kept its
  start on the first batch and its finish on the last. The overview, the lab dashboard and the
  wiki pages named each batch ("Sample prep, batch 2"), and the instrument schedule showed each
  acquisition batch's plan. `labops projects split <item> <step> --batches N` did the same for
  Claude and in a terminal (`src/LabOps.Engines/Projects/ProjectsEngine.Write.cs`,
  `src/LabOps.App/Views/ProjectsView.xaml`, `ViewModels/ProjectItems.cs`).

## Bug Fixes

- **A screen reader read a project, quote or notebook in a list by its name.** It had read each row
  of the Projects list, the quotes list and the notebook picker as every field the app kept for it,
  nested lists and all. A project read as its name and title, a quote as its number and title, and
  a notebook as its ID and title (`src/LabOps.App/ViewModels/ProjectItems.cs`,
  `src/LabOps.Core/Quotes/QuoteSummary.cs`, `src/LabOps.Core/Panorama/PanoramaClient.cs`).
