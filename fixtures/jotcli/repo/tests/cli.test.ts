import test from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const cli = join(dirname(fileURLToPath(import.meta.url)), "..", "src", "cli.js");

function jot(file: string, ...args: string[]): { code: number; out: string; err: string } {
  try {
    const out = execFileSync(process.execPath, [cli, ...args], {
      env: { ...process.env, JOT_FILE: file },
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
    });
    return { code: 0, out, err: "" };
  } catch (e) {
    const err = e as { status: number; stdout: string; stderr: string };
    return { code: err.status, out: err.stdout, err: err.stderr };
  }
}

function freshFile(): string {
  return join(mkdtempSync(join(tmpdir(), "jot-")), "tasks.json");
}

test("add, list and done work together", () => {
  const file = freshFile();
  assert.equal(jot(file, "list").out, "Nothing to do.\n");
  assert.equal(jot(file, "add", "buy", "milk").out, "Added #1 [ ] buy milk\n");
  assert.equal(jot(file, "add", "walk").out, "Added #2 [ ] walk\n");
  assert.equal(jot(file, "done", "1").out, "Done #1 [x] buy milk\n");
  assert.equal(jot(file, "list").out, "#1 [x] buy milk\n#2 [ ] walk\n");
});

test("done on a missing task exits 1", () => {
  const file = freshFile();
  const r = jot(file, "done", "9");
  assert.equal(r.code, 1);
  assert.match(r.err, /no task #9/);
});

test("usage errors exit 2", () => {
  const file = freshFile();
  assert.equal(jot(file, "add").code, 2);
  assert.equal(jot(file, "done", "zero").code, 2);
  assert.equal(jot(file, "frobnicate").code, 2);
});

test("help exits 0 and prints the usage", () => {
  const file = freshFile();
  const r = jot(file, "--help");
  assert.equal(r.code, 0);
  assert.match(r.out, /^Usage: jot/);
});

test("clear on an empty list has nothing to ask", () => {
  const file = freshFile();
  assert.equal(jot(file, "clear").out, "Nothing to clear.\n");
});
