(() => {
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;

  // Revelação das seções (uma vez só)
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); }
    }
  }, { rootMargin: "0px 0px -10% 0px" });
  document.querySelectorAll(".reveal").forEach((el) => io.observe(el));

  // Frase grande: cada palavra acende ao cruzar a linha de gatilho
  const tagline = document.querySelector("[data-words]");
  if (tagline) {
    const words = tagline.textContent.trim().split(/\s+/);
    tagline.textContent = "";
    words.forEach((w, i) => {
      const s = document.createElement("span");
      s.className = "w";
      s.textContent = w;
      tagline.append(s);
      if (i < words.length - 1) tagline.append(" ");
    });
    const spans = [...tagline.querySelectorAll(".w")];
    if (reduce) {
      spans.forEach((s) => s.classList.add("on"));
    } else {
      let ticking = false;
      const update = () => {
        ticking = false;
        const line = innerHeight * 0.62;
        spans.forEach((s) => s.classList.toggle("on", s.getBoundingClientRect().top < line));
      };
      addEventListener("scroll", () => { if (!ticking) { ticking = true; requestAnimationFrame(update); } }, { passive: true });
      update();
    }
  }

  // Menu móvel
  const toggle = document.querySelector(".menu-toggle");
  const menu = document.getElementById("menu");
  if (toggle && menu) {
    const items = [...menu.querySelectorAll("li")];
    const setOpen = (open) => {
      toggle.setAttribute("aria-expanded", String(open));
      toggle.setAttribute("aria-label", open ? "Fechar menu" : "Abrir menu");
      document.body.style.overflow = open ? "hidden" : "";
      if (open) {
        menu.hidden = false;
        items.forEach((li, i) => { li.style.transitionDelay = `${Math.min(i * 60, 400)}ms`; });
        requestAnimationFrame(() => menu.classList.add("open"));
      } else {
        menu.classList.remove("open");
        items.forEach((li) => { li.style.transitionDelay = "0ms"; });
        setTimeout(() => { if (!menu.classList.contains("open")) menu.hidden = true; }, 300);
      }
    };
    toggle.addEventListener("click", () => setOpen(toggle.getAttribute("aria-expanded") !== "true"));
    menu.addEventListener("click", (e) => { if (e.target.closest("a")) setOpen(false); });
    addEventListener("keydown", (e) => { if (e.key === "Escape" && !menu.hidden) setOpen(false); });
  }

  // Item atual da navegação conforme a seção visível
  const links = [...document.querySelectorAll(".nav-links a")];
  const sections = links.map((a) => document.querySelector(a.getAttribute("href"))).filter(Boolean);
  const navIo = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      links.forEach((a) => {
        const on = a.getAttribute("href") === `#${e.target.id}`;
        a.classList.toggle("active", on);
        if (on) a.setAttribute("aria-current", "location"); else a.removeAttribute("aria-current");
      });
    }
  }, { rootMargin: "-45% 0px -50% 0px" });
  sections.forEach((s) => navIo.observe(s));
})();
