---
type: bug
title: A null entry in a suite's arms crashes validation with a NullReferenceException
created: 2026-10-09
status: fixed
fixed: 2026-10-10, 0.2.4
found-by: a downstream suite, building a throwaway shakedown suite with jq
---

# A null entry in a suite's arms crashes validation with a NullReferenceException

If `suite.json` has `null` in its `arms` array, `run` (and presumably `list`)
dies with an unhandled `NullReferenceException` and a stack trace, not a
validation problem naming the field.

## What happens

A shakedown suite was cut down from a five-arm suite with
`jq '.arms=[.arms[0],.arms[4]]'` against a file that by then had only one arm,
so the second entry came out as `null`:

```json
"arms": [ { "id": "a1", ... }, null ]
```

`proctor run shake --root .`:

```
Unhandled exception. System.NullReferenceException: Object reference not set to an instance of an object.
   at Proctor.Suite.ValidateDef(...) in …/proctor/Suite/Suite.cs:line 333
   at Proctor.Suite.Load(...) in …/proctor/Suite/Suite.cs:line 235
   at Proctor.Verbs.Run(...) in …/proctor/Verbs.cs:line 38
   at Proctor.Program.Main(...) in …/proctor/Program.cs:line 63
```

`Suite/Suite.cs`, `ValidateDef`, dereferences each entry without a null check:

```csharp
foreach (var (arm, i) in (def.Arms ?? []).Select((a, i) => (a, i)))
{
    var f = $"arms[{i}]";
    if (string.IsNullOrWhiteSpace(arm.Id)) Add($"{f}.id", "required");
```

The rest of the validator's contract is that a malformed suite produces a
`Problem` (file, field, message) and an exit, never a crash.

## Why it matters

Small. The input is malformed and the stack trace does point at the arm loop.
But a crash reads as a proctor defect rather than a suite mistake. It also
stops at the first bad entry, where a `Problem` would be reported alongside
every other problem in the suite.

Other arrays that `ValidateDef` walks may have the same gap (tasks, checks,
hooks); not checked.

## What we want instead

**A `null` arm is a problem, not a crash:** `arms[1]: an arm is an object, not
null`, collected with the others. Skip the entry's remaining checks.

## Resolution

Fixed in 0.2.4. A `null` arm is `arms[i]: an arm is an object, not null`,
and the entry's remaining checks are skipped. The other places a null could
come from JSON were tested too: a `null` in `grading.pass` or
`grading.validity` crashed the same way and is now a problem; a `null`
check spec (suite, fixture or task) was already a problem, but the judges
pass that `list`, `run` and `grade` make would still dereference it, which
is fixed in `Checks.ModelChecks`.
