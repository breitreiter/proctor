# Shared by the script checks: the repository as the run left it.
# $PROCTOR_WORK is used when it still exists; otherwise the fixture plus diff.patch
# is rebuilt in a temp directory, so a regrade works after the checkout is gone.
work_dir() {
  if [ -d "$PROCTOR_WORK" ] && [ -n "$(ls -A "$PROCTOR_WORK")" ]; then echo "$PROCTOR_WORK"; return; fi
  local tmp; tmp=$(mktemp -d)
  local path; path=$(jq -r '.fixture.path // empty' <<<"$PROCTOR_CASE_JSON")
  [ -n "$path" ] || { echo "cannot rebuild a git fixture without a checkout" >&2; exit 3; }
  cp -R "$PROCTOR_EVAL_DIR/$path/." "$tmp/"
  [ -s "$PROCTOR_DIFF" ] && (cd "$tmp" && git apply --allow-empty "$PROCTOR_DIFF")
  echo "$tmp"
}
test_project() { find "$1" -name '*.Tests.csproj' | head -1; }
