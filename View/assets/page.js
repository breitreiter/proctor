// What the pages add to Web Awesome's components. A page reads the same without it.
// A link to a step (a pill, a friction tag) opens that step: a wa-details is closed until told otherwise.
function openTarget() {
  const target = location.hash.length > 1 ? document.getElementById(decodeURIComponent(location.hash.slice(1))) : null;
  if (target?.localName === "wa-details") { target.open = true; target.scrollIntoView({ block: "center" }); }
}
addEventListener("hashchange", openTarget);
customElements.whenDefined("wa-details").then(openTarget);
