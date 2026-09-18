#!/usr/bin/env bash
# Sample setup: a fresh copy of the case's fixture in $PROCTOR_WORK, committed so the
# teardown can diff against it. fixture.path is copied; fixture.git/rev is cloned.
set -euo pipefail
path=$(jq -r '.fixture.path // empty' <<<"$PROCTOR_CASE_JSON")
git_url=$(jq -r '.fixture.git // empty' <<<"$PROCTOR_CASE_JSON")
rev=$(jq -r '.fixture.rev // "HEAD"' <<<"$PROCTOR_CASE_JSON")
rm -rf "$PROCTOR_WORK"; mkdir -p "$PROCTOR_WORK"
if [ -n "$path" ]; then
  cp -R "$PROCTOR_EVAL_DIR/$path/." "$PROCTOR_WORK/"
  rm -rf "$PROCTOR_WORK"/{.git,bin,obj} "$PROCTOR_WORK"/*/bin "$PROCTOR_WORK"/*/obj "$PROCTOR_WORK"/*/*/bin "$PROCTOR_WORK"/*/*/obj
  cd "$PROCTOR_WORK" && git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm "fixture $path"
  echo "copied $path"
elif [ -n "$git_url" ]; then
  git clone -q "$git_url" "$PROCTOR_WORK" && cd "$PROCTOR_WORK" && git checkout -q "$rev"
  echo "cloned $git_url at $rev"
else
  echo "case $PROCTOR_CASE has neither fixture.path nor fixture.git" >&2; exit 1
fi
