#!/usr/bin/env node
import { confirm } from "./confirm.js";
import { add, format, load, markDone, save } from "./store.js";

const USAGE = `Usage: jot <command> [args]

  add <text>   add a task
  list         show every task
  done <n>     tick task n
  clear        delete every task

Exit codes: 0 ok, 1 task not found, 2 usage error.`;

export function main(argv: string[]): number {
  const [command, ...args] = argv;
  switch (command) {
    case undefined:
    case "-h":
    case "--help":
    case "help":
      console.log(USAGE);
      return 0;
    case "add":
      return addCommand(args.join(" ").trim());
    case "list":
      return listCommand();
    case "done":
      return doneCommand(args[0]);
    case "clear":
      return clearCommand();
    default:
      console.error(`jot: unknown command '${command}'\n\n${USAGE}`);
      return 2;
  }
}

function addCommand(text: string): number {
  if (!text) {
    console.error("jot: add needs some text");
    return 2;
  }
  const tasks = load();
  const task = add(tasks, text);
  save(tasks);
  console.log(`Added ${format(task)}`);
  return 0;
}

function listCommand(): number {
  const tasks = load();
  if (tasks.length === 0) {
    console.log("Nothing to do.");
    return 0;
  }
  for (const task of tasks) console.log(format(task));
  return 0;
}

function doneCommand(arg: string | undefined): number {
  const id = Number(arg);
  if (!arg || !Number.isInteger(id) || id < 1) {
    console.error("jot: done needs a task number");
    return 2;
  }
  const tasks = load();
  const task = markDone(tasks, id);
  if (!task) {
    console.error(`jot: no task #${id}`);
    return 1;
  }
  save(tasks);
  console.log(`Done ${format(task)}`);
  return 0;
}

function clearCommand(): number {
  const tasks = load();
  if (tasks.length === 0) {
    console.log("Nothing to clear.");
    return 0;
  }
  if (!confirm(`Delete ${tasks.length} task${tasks.length === 1 ? "" : "s"}?`)) {
    console.log("Aborted.");
    return 0;
  }
  save([]);
  console.log(`Cleared ${tasks.length} task${tasks.length === 1 ? "" : "s"}.`);
  return 0;
}

process.exitCode = main(process.argv.slice(2));
