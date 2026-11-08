# Adding a provider

Every job that needs a model hands off to this one flow and gets back a
named, checked provider. The job's own screen says only what it needs and
what it writes afterwards; nothing about a vendor is drawn twice.

1. `pick.txt`: the provider kinds the job can use, from the catalogue's
   `serves`. Providers already set up that fit come first.
2. `<kind>.txt`: the kind's form, the same layout for every kind. One file
   per kind in the catalogue.
3. `check.txt`: the key check lists the provider's models (free, no
   tokens). That proves the key and records which models it can reach.

**Setup picks no models.** Which model an arm, a judge or a classifier uses
is the suite's decision, made when the suite is planned; setup only
connects. The one exception is a provider where the model is something the
user provisioned and setup cannot discover: Azure's deployment name.

The flow returns: the provider's name, its kind, the env variable holding
its key, the model ids the key check found (recorded in `setup.json`, so a
coding agent planning a suite chooses from models the user can actually
reach), and whether the check passed. The job writes its own
config entry from that (nb.json for the built-in harness, proctor.json
`agents` or `judges` for the others).

No form has optional fields. Anything unusual (a gateway, extra headers, an
organization id, a context size) is the user's coding agent's to write into
the config afterwards; setup's job is the secret, which must not pass
through the agent. `openai-compatible.txt` is the extreme case: a name and a
key, nothing else.

Form layout, every kind:

```
<label>

  Where to get a key: https://breitreiter.github.io/proctor/providers.html#<kind>
                      (<where words>)
  <notice, if the kind has one>

  <required fields, in catalogue order; a model only for Azure>
  [key check]

  Name for this provider in your suites [<default>]:
```
