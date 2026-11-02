// Layout helpers shared by both films. All measuring happens synchronously at
// load, before any GSAP transform is applied, so camera targets come from the
// real CSS layout instead of hand-typed coordinates.
window.ST = {
  // Untransformed size factor of an element (the Studio preview may scale the page).
  _k(el) {
    return el.getBoundingClientRect().width / el.offsetWidth || 1;
  },

  // Point inside `el` (fractions fx, fy) in `world` coordinates.
  pos(el, world, fx = 0.5, fy = 0.5) {
    const k = ST._k(world);
    const r = el.getBoundingClientRect();
    const w = world.getBoundingClientRect();
    return { x: (r.left - w.left + r.width * fx) / k, y: (r.top - w.top + r.height * fy) / k };
  },

  // Point inside `el` in the unscaled coordinate space of a scaled `container`.
  local(el, container, fx = 0.5, fy = 0.5) {
    const k = container.getBoundingClientRect().width / container.offsetWidth;
    const r = el.getBoundingClientRect();
    const c = container.getBoundingClientRect();
    return { x: (r.left - c.left + r.width * fx) / k, y: (r.top - c.top + r.height * fy) / k };
  },

  // Camera vars that put world point p at frame point (fx, fy) at the given scale.
  cam(scale, p, fx = 960, fy = 540) {
    return { scale, x: fx - scale * p.x, y: fy - scale * p.y };
  },

  // Split the words of an element into spans for staggered reveals.
  words(el) {
    el.querySelectorAll("[data-words]").forEach((node) => {
      const parts = node.textContent.trim().split(/\s+/);
      node.textContent = "";
      parts.forEach((p, i) => {
        const s = document.createElement("span");
        s.className = "w" + (node.dataset.brand === p ? " brand-word" : "");
        s.textContent = p;
        node.appendChild(s);
        if (i < parts.length - 1) node.appendChild(document.createTextNode(" "));
      });
    });
  },
};
