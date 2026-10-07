# Roadmap creation — stage 2

Apply the [shared workflow contracts](README.md). Read the concept, then produce the roadmap; do not implement or orchestrate work.

- Require `status: ready` unless the user explicitly requests a roadmap from a draft.
- If decomposition reveals an unresolved decision, return it to the user; do not decide scope on the concept's behalf.
- Order work by dependencies. Use one `roadmap.md` for small tasks; introduce milestones and leaf files only when they make execution manageable.
- Make each item executable without guessing, using the roadmap contract. Avoid artificial substeps.
- Show the resulting structure briefly and stop.
