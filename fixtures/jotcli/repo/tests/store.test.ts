import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { add, format, load, markDone, save } from "../src/store.js";

test("add numbers tasks from one and keeps counting past gaps", () => {
  const tasks = [{ id: 3, text: "old", done: true }];
  const task = add(tasks, "new");
  assert.equal(task.id, 4);
  assert.equal(tasks.length, 2);
});

test("markDone ticks the task and returns it, or undefined", () => {
  const tasks = [{ id: 1, text: "a", done: false }];
  assert.equal(markDone(tasks, 1)?.done, true);
  assert.equal(markDone(tasks, 2), undefined);
});

test("format shows the number, the tick and the text", () => {
  assert.equal(format({ id: 7, text: "walk", done: false }), "#7 [ ] walk");
  assert.equal(format({ id: 7, text: "walk", done: true }), "#7 [x] walk");
});

test("save then load round-trips, and a missing or empty file is an empty list", () => {
  const path = join(mkdtempSync(join(tmpdir(), "jot-")), "tasks.json");
  assert.deepEqual(load(path), []);
  writeFileSync(path, "");
  assert.deepEqual(load(path), []);
  save([{ id: 1, text: "a", done: false }], path);
  assert.deepEqual(load(path), [{ id: 1, text: "a", done: false }]);
});
