import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";

export interface Task {
  id: number;
  text: string;
  done: boolean;
}

export function storePath(): string {
  return process.env.JOT_FILE ?? resolve(process.cwd(), ".jot.json");
}

export function load(path = storePath()): Task[] {
  if (!existsSync(path)) return [];
  const text = readFileSync(path, "utf8").trim();
  if (text === "") return [];
  const parsed: unknown = JSON.parse(text);
  return Array.isArray(parsed) ? (parsed as Task[]) : [];
}

export function save(tasks: Task[], path = storePath()): void {
  writeFileSync(path, JSON.stringify(tasks, null, 2) + "\n");
}

export function add(tasks: Task[], text: string): Task {
  const id = tasks.reduce((max, t) => Math.max(max, t.id), 0) + 1;
  const task = { id, text, done: false };
  tasks.push(task);
  return task;
}

export function markDone(tasks: Task[], id: number): Task | undefined {
  const task = tasks.find((t) => t.id === id);
  if (task) task.done = true;
  return task;
}

export function format(task: Task): string {
  return `#${task.id} [${task.done ? "x" : " "}] ${task.text}`;
}
