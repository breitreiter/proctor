import { closeSync, openSync, readSync } from "node:fs";

const POLL_MS = 250;
const GIVE_UP_MS = Number(process.env.JOT_CONFIRM_TIMEOUT_MS ?? 30_000);

/**
 * Ask before doing something destructive. The answer is read from the
 * terminal itself rather than stdin, so a stray redirect can't say yes on
 * the user's behalf.
 */
export function confirm(question: string): boolean {
  process.stderr.write(`${question} [y/N] `);
  const key = readKeyFromTerminal();
  process.stderr.write("\n");
  return key === "y" || key === "Y";
}

function readKeyFromTerminal(): string | null {
  const fd = openTerminal();
  if (fd === null) return null;
  const buf = Buffer.alloc(16);
  const n = readSync(fd, buf, 0, buf.length, null);
  closeSync(fd);
  return n > 0 ? buf.toString("utf8", 0, 1) : null;
}

// No terminal yet (started from a script, say): wait for one to show up.
function openTerminal(): number | null {
  const deadline = Date.now() + GIVE_UP_MS;
  for (;;) {
    try {
      return openSync("/dev/tty", "r");
    } catch {
      if (Date.now() >= deadline) return null;
      sleep(POLL_MS);
    }
  }
}

function sleep(ms: number): void {
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
}
