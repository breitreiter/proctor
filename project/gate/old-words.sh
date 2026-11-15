#!/usr/bin/env bash
# The old model's words, gated: suite, arm, experiment, baseline, sample, cell and resume, with their plurals, in
# any case, as whole words or as a part of an identifier (PROCTOR_ARM, ArmStats, cellDir). Every tracked file is
# searched; a hit is allowed only when old-words.allow says so. Exit 0 when nothing else is left, 1 otherwise.
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
  my $word = qr/(?:suite|arm|experiment|baseline|sample|cell|resume)/i;
  my $cap  = qr/(?:Suite|Arm|Experiment|Baseline|Sample|Cell|Resume)/;
  my $old  = qr/(?:(?<![A-Za-z])$word|(?<=[a-z])$cap)(?:e?s)?(?![a-z])/;
  my $hits = 0;
  FILE: for my $f (@ARGV) {
    for (@files) { next FILE if $f =~ $_ }
    open my $in, "<", $f or next;
    next if -B $f;
    my @here = grep { $f =~ $_->[0] } @lines;
    while (my $l = <$in>) {
      (my $t = $l) =~ s{archive/[\w./-]+}{}g;   # a link into project/archive/ names a file there, kept as written
      next unless $t =~ $old;
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
