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
