// The stage page's one script (StagePage.cs). The page reads correctly without it: the server selects the first
// failed check and lights its steps. This adds choosing another check or friction point, and search.

const selectable = ".card, .fric, .pass-row";
const steps = () => [...document.querySelectorAll(".transcript .step")];

function select(el, scroll) {
  for (const other of document.querySelectorAll(selectable)) {
    other.classList.remove("sel");
    other.setAttribute("aria-pressed", "false");
  }
  for (const step of steps()) step.classList.remove("lit");
  el.classList.add("sel");
  el.setAttribute("aria-pressed", "true");
  const ids = (el.dataset.steps || "").split(" ").filter(Boolean);
  const lit = ids.map((id) => document.getElementById("s" + id)).filter(Boolean);
  for (const step of lit) {
    step.classList.add("lit");
    step.open = true;
  }
  if (scroll && lit.length) lit[0].scrollIntoView({ block: "nearest", behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" });
}

document.addEventListener("click", (e) => {
  const mark = e.target.closest(".gm");
  if (mark) {
    // A gutter mark selects the check that cites the step.
    e.preventDefault();
    const check = [...document.querySelectorAll(".card, .pass-row")].find((c) => c.dataset.check === mark.dataset.check);
    if (check) select(check, false);
    return;
  }
  const el = e.target.closest(selectable);
  if (el && !e.target.closest("a")) select(el, true);
});

document.addEventListener("keydown", (e) => {
  const card = e.target.closest && e.target.closest(".card");
  if (card && (e.key === "Enter" || e.key === " ")) {
    e.preventDefault();
    select(card, true);
  }
});

// Search within this stage: hide the steps whose text doesn't contain the query, open the ones that do.
const search = document.getElementById("search");
if (search) {
  search.addEventListener("input", () => {
    const q = (search.value || "").trim().toLowerCase();
    for (const step of steps()) {
      const hit = !q || step.textContent.toLowerCase().includes(q);
      step.hidden = !hit;
      if (q && hit) step.open = true;
    }
  });
}

// The selection is in the address (?check=name or ?point=id), so a link can point at one verdict or one friction
// point. The server already selects a failed check named there; this covers a passed check and a point, and keeps the
// address in step as the selection changes.
function address(el) {
  const url = new URL(location.href);
  url.searchParams.delete("check");
  url.searchParams.delete("point");
  if (el.dataset.check) url.searchParams.set("check", el.dataset.check);
  else if (el.id) url.searchParams.set("point", el.id);
  history.replaceState(null, "", url);
}
document.addEventListener("click", (e) => {
  const el = e.target.closest(selectable);
  if (el && !e.target.closest("a")) address(el);
});
{
  const asked = new URL(location.href).searchParams;
  const wanted = asked.get("point")
    ? document.getElementById(asked.get("point"))
    : asked.get("check") && [...document.querySelectorAll(".card, .pass-row")].find((c) => c.dataset.check === asked.get("check"));
  if (wanted && !wanted.classList.contains("sel")) {
    const hidden = wanted.closest("wa-details");
    if (hidden) hidden.open = true;
    select(wanted, true);
  }
}
