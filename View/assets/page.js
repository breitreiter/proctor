// What the pages add to Web Awesome's components. A page reads the same without it.
// A link to a step (a pill, a friction tag) opens that step: a wa-details is closed until told otherwise.
function openTarget() {
  const target = location.hash.length > 1 ? document.getElementById(decodeURIComponent(location.hash.slice(1))) : null;
  if (target?.localName === "wa-details") { target.open = true; target.scrollIntoView({ block: "center" }); }
}
addEventListener("hashchange", openTarget);
customElements.whenDefined("wa-details").then(openTarget);

// Search on a stage: the steps whose text holds every word typed stay, the rest are hidden, and the count is said.
const search = document.getElementById("search");
if (search) {
  const steps = [...document.querySelectorAll("wa-details.step")];
  const count = document.getElementById("search-count");
  search.addEventListener("input", () => {
    const words = search.value.toLowerCase().split(/\s+/).filter(Boolean);
    let shown = 0;
    for (const step of steps) {
      const text = step.textContent.toLowerCase();
      step.hidden = !words.every(word => text.includes(word));
      if (!step.hidden) shown++;
    }
    count.textContent = words.length ? `${shown} of ${steps.length} steps` : "";
  });
}

// Any two configurations compared: tick two on the exam's page and the button names the pair, "X vs Y", and goes there.
// X is the one ticked first, unless that one is live: what is live is what the other is compared with.
const compare = document.getElementById("compare");
if (compare) {
  const hint = document.getElementById("compare-hint");
  let picked = [];
  document.addEventListener("change", event => {
    const box = event.target.closest?.("wa-checkbox[data-pick]");
    if (!box) return;
    picked = picked.filter(other => other !== box);
    if (box.checked) picked.push(box);
    if (picked.length === 2) {
      const [x, y] = picked[0].hasAttribute("data-live") ? [picked[1], picked[0]] : picked;
      compare.textContent = `Compare ${x.dataset.pick} vs ${y.dataset.pick}`;
      compare.href = `${compare.dataset.to}?configuration=${encodeURIComponent(x.dataset.pick)}&against=${encodeURIComponent(y.dataset.pick)}`;
      compare.disabled = false;
      hint.textContent = "";
    } else {
      compare.textContent = "Compare";
      compare.removeAttribute("href");
      compare.disabled = true;
      hint.textContent = picked.length > 2 ? "Two at a time: untick one." : picked.length === 1 ? "Tick one more." : "Tick two configurations to compare them.";
    }
  });
}
