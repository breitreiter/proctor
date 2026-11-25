---
source: https://orbit.dtu.dk/files/55857860/ris_m_1582.pdf (Risø-M-1582, February 1973; the report behind Rasmussen and Jensen, "Mental procedures in real-life tasks: a case study of electronic trouble shooting", Ergonomics 17(3), 1974). Copy supplied by the user in `/srv/aiwork/inbox/ris_m_1582.pdf`
read: 2026-11-14, the OCR text layer of a 73-page typescript scan. Read: abstract, experimental conditions (3), general features (5.2 to 5.4), and the whole discussion and conclusion (7, 8). Section 6, the detailed search routines, and the appendix of annotated records were skimmed for headings only. Quotes have obvious scan errors corrected; the two percentages are marked where the scan is doubtful
take: the user's (2026-11-14, with Suchman): the work is a technician's, not a line worker's. Claude's: this is the empirical half of that. Skilled people on a familiar kind of system choose many cheap checks over a few reasoned ones, it is rational by their own criterion, and the designer who calls it inefficient is using the wrong one. It changes what the friction share means
---

# A study of mental procedures in electronic trouble shooting

Jens Rasmussen and Aage Jensen, Danish Atomic Energy Commission, Risø,
1973. Think-aloud records of repair technicians finding faults in
laboratory instruments.

## The study

Technicians at a nuclear research establishment repairing its ordinary
instruments, in their own workshop, asked "to verbalize his procedures
during a repair work", on tape.

- "A total of 45 cases were recorded, comprising 6 individuals performing
  fault-finding in 8 different types of instruments, each case having its
  particular fault." 30 were analysed in detail. Instruments included
  multichannel analysers, oscilloscopes, TV receivers and digital
  voltmeters.
- Qualitative on purpose: "reliable quantitative information was not to
  be expected."
- Each man worked on instruments "the use of which, and their overall
  design, were familiar to him", but never as "a highly trained expert in
  the specific type of instrument".
- **Faults were planted to offer a short cut.** When it turned out that
  "information contained in the external system response was not used to
  any great extent", the experimenters introduced simulated faults that
  "could be located rather exactly by short-cut methods if the
  information available from the external response was taken into careful
  consideration."

## What they found

**Two ways to find a fault.** The designer "used only a few observations,
but employed a complex data handling in his decision procedure ... his
procedure is informationally economic." The technician "uses many
observations in a sequence of simple decisions. His method is a general
search procedure which is not dependent upon the actual system or
specific fault. He treats the observations individually in a stream of
good/bad judgements which is informationally uneconomic, but fast."

**The task is where, not why.** "The man defines his task primarily as a
search to find where the faulty component is located in the system. He
does not consider his task a problem-solving one, which is to explain why
the system has the observed faulty response".

**Three routines**, in increasing need for knowledge of the specific
system:

- functional search: scan familiar features of the system's response,
  judge each good or bad, go to the subsystem responsible;
- topographic search: follow the main signal path through a subsystem "by
  a rapid sequence of good/bad judgements", with the circuit diagram
  "viewed as a topographic map";
- search by evaluation: reason from how exactly the function is failing.
  Nearly 80% of these were "rapid statements ... based upon general
  professional background or experience", and only about 20% "more
  careful reasoning based upon a mental model related to the internal
  functioning of the specific system" (both figures doubtful in the
  scan).

**The short cut was not taken.** "Even when such faults were simulated so
that the system response to the planners of the experiments clearly
indicated possible short-cut methods if the internal functioning of the
system was considered, the men normally used their general search
routines." And: "Scanning a high number of observations by simple
procedures is clearly preferred to the preparation of specific procedures
worked out by studying or memorizing the internal functioning of the
system."

**Redundant observations, local decisions.** "During the search routines,
no attention is paid to whether a measuring point will be informationally
redundant or not." "Instead of making overall plans for the search, the
tendency is to make rapid or impulsive decisions all along the search
based only upon the information observed at the moment. This, of course,
gives a very individual pattern to the different overall procedures found
in our cases." The rule is to follow "the way of least resistance" to
the next field for which a familiar routine is at hand.

**Information noticed and passed over.** There is "a point of no return":
once the man has decided where to look next, hints that are "clearly
available from the observation and ... recorded by the man" have no
influence, and "the next search is a routine, starting from scratch."
Later, "in some cases important information has been recorded several
times during routine search without triggering his attention until a
period of confusion sets in."

**When a routine fails, repeat it.** A topographic search fails to reach
a local search "in more than half of the attempts"; the preferred
response is "to repeat the search by another parameter", and then "a
pronounced tendency to return to a search performed earlier ... with more
careful judgements of the observations rather than a more careful
evaluation of the actual faulty function."

**When stuck, they still do not read the manual.** "When in trouble,
there seems to be no tendency to consider it worth-while studying by
means of manuals or diagrams the functioning of the circuitry in greater
detail." The technicians' own advice: "better take a break, wait until
next day, or discuss the problem with a colleague."

**Cost of an observation changes the procedure.** Asked when studying the
internals first would be worth it, they said when "measurements or
manipulations could have serious consequences, as in live warning
systems - 'when a siren is at the end of the wire'", or when the site is
unpleasant. A test on a radioactive waste tank's level control "resulted
in a very 'rational' procedure based upon a careful functional evaluation
of the system response in advance and very few measurements on site."

**Telling them did not help.** "We also suggested to the technicians that
they use functional reasoning when in trouble ... This, however, did not
cause any significant changes in the procedures". To change, "the man
has to consider the task in advance as one calling for special
treatment."

**Load in one subtask simplifies the others.** "difficulties in one of
the subtasks tend to cause simpler procedures to be used in others", and
under strain the man "seems to be very insensitive to hints in the
observations which would normally be familiar to him."

**Is it rational?** "What is rational depends upon the performance
criteria adopted by the men." Their criterion is "to locate the fault as
quickly as possible", and by it "the procedures found in our records are
rational". The designer "may quite naturally value as rational the
'elegant' deductive procedure which is informationally very efficient ...
but this criterion is not the appropriate one". The comparison offered:
"most car drivers ... prefer, when moving around in a big city, to drive
along familiar main streets rather than preparing individual short-cut
routes by means of a city map."

**Conclusion.** "procedures found unsystematic in a behaviouristic study
may turn out to be rational and systematic in the light of the man's
verbalizations", and "rapid sequences of simple decisions based upon
informationally redundant observations may be more characteristic of
human problem solving than one would suspect".

## Worth taking

- **Search is not friction by itself.** Our readers and rules file a
  recursive grep, a `find`, a look at source under friction. Rasmussen's
  technicians did the equivalent as their method, by choice, with the
  short cut in plain view. An agent grepping proctor's tree for a check
  name is running a topographic search: cheap observations, one bit each.
  The ledger should price it, and should not assume the reasoned path
  (read the docs, form a model, act once) was the cheaper one.
- **The criterion decides, so state it.** The technician minimises time
  and mental load, the designer counts observations. For an agent the
  costs are tokens, wall-clock and context. A cheap check that returns
  little is rational when a call is cheap; it stops being so when each
  result is large and stays in context for the rest of the run. That is
  the downstream cost `regimes.md` leaves open, and it is the thing to
  measure before calling a search wasteful.
- **Make the observation expensive and the procedure changes.** The waste
  tank and the siren are intended friction that worked: the men planned
  first when a measurement had consequences. It predicts an agent will
  reason before acting where a call is costly or risky, and probe where
  it is free. A surface can choose which.
- **"Just read the docs" will not change behaviour.** Suggesting the
  better procedure did nothing, and being stuck did not send them to the
  manual. A treatment that adds advice to a prompt should be expected to
  do little; one that changes what the first cheap check returns should
  do more. Testable on `agent-onboarding`.
- **Passed-over hints are a symptom we do not look for.** Information
  "recorded several times ... without triggering his attention": a result
  that contained the answer, followed by more search for it. This is
  rule-checkable where the eventual answer is known (the string the run
  finally acts on appeared in an earlier result), and it is the
  garden-path side of the surprise table seen from the result.
- **Repeat with another parameter, then go back.** The published form of
  the rework and re-read lines, and of a search retried with a new term.
- **Individual pattern from local decisions.** Their explanation of why
  no two records look alike is ours for why the alignment failed.
- **Verbalised records make an unsystematic trace systematic.** The case
  for reading the agent's own words beside its calls, and for thinking
  tokens where they are recorded. Suchman's caution still applies to
  accounts given afterwards; these were given during the work.

## Not to take

- **The numbers.** 30 records, six men, one shop, no counts meant to
  generalise; the authors say so.
- **An equivalence between a technician and a model.** The technicians'
  limit is short-term memory and their cheap resource is a probe. An
  agent's limit is context and its cheap resource is a tool call. The
  shape of the trade carries; the constants do not.
- **A licence to call all search fine.** Their routines were "normally
  successful in the end" on familiar kinds of system. Ko's developers on
  unfamiliar code had 88% of searches come to nothing
  (`programmer-time-studies.md`). Which case an agent is in depends on
  the surface, and that is the measurement.

## Not read

- Section 6 in detail and the annotated records in the appendix.
- The 1974 Ergonomics version, and Rasmussen's 1983 paper on skill, rule
  and knowledge levels, which generalises from this study.
