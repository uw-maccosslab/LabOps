# ChargeState v26.4.0

Projects grow to fit the lab's work: labs hold projects, each project's samples can be measured
in several experiments (DIA, then PRM), every step can be assigned to a person, and closing the
app no longer shows an error.

## New Features

### Projects: labs, projects and experiments

- **A project can hold several experiments.** The Projects list now shows projects: one body of
  work with one set of samples, inside a lab. Selecting one shows the samples' timeline (received,
  metadata, plate layout, prep) and then one section per experiment, each a measurement and
  analysis of those samples with its own instrument, Panorama folders and steps. MJFF BioFIND, for
  example, has a DIA experiment on the Orbitrap Astral now, and a PRM experiment on the Stellar
  can follow with its own unblinding and assay development steps.
- **New project** (in the toolbar, formerly New experiment) starts Claude on the lab, the project
  and its experiments. **New experiment** on a project's page adds another measurement of its
  samples; Claude asks what it is.
- **Timelines fit the work.** Each section has **Add a step** for anything its timeline does not
  show yet, such as a second shipment, unblinded metadata or assay development, placed anywhere
  in the timeline. A step added by mistake has **Remove** until someone records anything on it.
- **Each step is assigned to a person.** **Assign** on a step gives it to someone listed in
  lab-projects' `config/people.yaml` (optionally with the later steps nobody has), and the step
  shows who has it. Starting a step nobody had assigns it to you. The project list has an
  **Assigned** column: who has the step its Progress column names. Search finds people too.
- The list's **Progress** column reads "Samples: Plate layout", "DIA: Data analysis" or
  "Complete", and sorts in that order. The **Lab** column, in place of Collaborator, reads
  institution and PI: "UW - MacCoss", "CincinnatiZoo - Curry".
- Organize metadata, Open in Octopus and Import Octopus layout work on the project, since the
  samples belong to it. Originals go in `inbox/<project>/`.

## Bug Fixes

- **Closing the app no longer shows "Something went wrong".** When no Claude conversation was
  open, closing the window showed an error about a window closing, and the app stayed open and
  disabled until closed again. It now closes at once.
- **Claude keeps collection dates and ages in sample tables.** The app told Claude never to copy
  full dates or ages over 89 from a human study, so organizing a manifest such as MJFF's dropped
  them even after lab-projects stopped flagging them (its engine 26.2.0). Claude now follows the
  lab-projects CLAUDE.md for what counts as identifying, so a change to that rule no longer needs
  an app release. The message shown when the check does find something no longer mentions dates.

## Breaking Changes

- **Needs lab-projects engine 26.2.0.** The Projects area reads the labs, projects and
  experiments layout. A copy of lab-projects from before it shows a message asking for a sync;
  lab-projects itself requires this version (`min_app_version` 26.4.0), so older apps say they
  need an update.
