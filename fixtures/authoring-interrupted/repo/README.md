# exams

This repository keeps proctor exams: `exams/` for the exams, `fixtures/` for
the repositories they run against, `attempts/` for what proctor made. proctor
itself is not installed here; see the task for where its documentation and
binary are. `exams/proctor.json` points proctor at nb, whose providers in
`exams/nb.json` are its scripted Mock, so a run here costs nothing and needs
no network.
