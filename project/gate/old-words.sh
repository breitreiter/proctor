#!/usr/bin/env bash
# The old model's words, gated: suite, arm, experiment, baseline, sample, cell, resume and (since the bank,
# project/plans/bank.md) batch, with their plurals, in
# any case, as whole words or as a part of an identifier (PROCTOR_ARM, ArmStats, cellDir). The layer before those,
# eval for exam and case for task, is gated narrowly, since both are ordinary English: case only inside an
# identifier (fromCase, caseDir, PROCTOR_CASE; IgnoreCase and its kind are not hits), eval inside an identifier or
# as the word eval or evals. Every tracked file is searched; a hit is allowed only when old-words.allow says so.
# Exit 0 when nothing else is left, 1 otherwise.
#   usage: project/gate/old-words.sh [--all]   (--all also prints the hits the allow-list lets through)
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
allow="project/gate/old-words.allow"
git ls-files -z | xargs -0 perl -e '
  my ($allow, $all) = (shift, shift);
  my (@files, @lines);
  open my $a, "<", $allow or die "$allow: $!\n";
  while (<$a>) {
    chomp; next if /^\s*(#|$)/;
    my ($glob, @rest) = split /\t+/;
    my $pattern = (grep { !/^#/ } @rest)[0];
    die "$allow:$.: an entry says why: a tab, then # and the reason\n" unless grep { /^#/ } @rest;
    (my $re = quotemeta $glob) =~ s/\\\*\\\*/.*/g; $re =~ s/\\\*/[^\/]*/g;
    if (defined $pattern && length $pattern) { push @lines, [qr/^$re$/, qr/$pattern/i] } else { push @files, qr/^$re$/ }
  }
  my $word = qr/(?:suite|arm|experiment|baseline|sample|cell|resume|batch)/i;
  my $cap  = qr/(?:Suite|Arm|Experiment|Baseline|Sample|Cell|Resume|Batch)/;
  my $old  = qr/(?:(?<![A-Za-z])$word|(?<=[a-z])$cap)(?:e?s)?(?![a-z])/;
  my $older = qr/(?<=[a-z])(?:Case|Eval)s?(?![a-z])|(?<![A-Za-z])(?:case|eval)s?(?=[A-Z])|(?<![A-Za-z])(?:CASE|EVAL)S?_|_(?:CASE|EVAL)S?(?![A-Za-z])|(?<![A-Za-z])[Ee]vals?(?![A-Za-z])/;
  my $notCase = qr/(?:Ignore|Camel|Pascal|Snake|Kebab|Upper|Lower|Title|Name|Test)Case/;
  my $hits = 0;
  FILE: for my $f (@ARGV) {
    for (@files) { next FILE if $f =~ $_ }
    open my $in, "<", $f or next;
    next if -B $f;
    my @here = grep { $f =~ $_->[0] } @lines;
    while (my $l = <$in>) {
      (my $t = $l) =~ s{archive/[\w./-]+}{}g;   # a link into project/archive/ names a file there, kept as written
      (my $u = $t) =~ s/$notCase//g;
      next unless $t =~ $old || $u =~ $older;
      next if $l =~ /Shape inherited from experiments; tolerated, not endorsed/;
      my $ok = grep { $l =~ $_->[1] } @here;
      next if $ok && !$all;
      chomp $l; $l =~ s/^\s+//;
      printf "%s%s:%d: %s\n", ($ok ? "allowed " : ""), $f, $., substr($l, 0, 200);
      $hits++ unless $ok;
    }
  }
  print STDERR "old-words: $hits line(s) outside the allow-list\n";
  exit($hits ? 1 : 0);
' "$allow" "${1:+1}"
