# LabOps v26.13.0

A free-text column someone had read for identifiers could be marked as read, so `check` stopped
warning about it until its wording changed.

## New Features

- **A free-text column could be marked as read.** `check` warns about every notes or comments column,
  since no rule can tell a note from a name. Once someone had read one and found nothing identifying,
  `labops projects review <project> <file> [column...] --by LOGIN` recorded it in the project's
  record under `reviewed:`, with who, when, and a fingerprint of the column's wording. `check` (and
  the pre-commit check) then left that column alone, and warned again, naming who read it and when,
  as soon as its wording changed; more rows saying the same things needed no new review.
