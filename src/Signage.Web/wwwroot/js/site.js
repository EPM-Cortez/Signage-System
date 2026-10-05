(() => {
  // Collapsible sidebar on narrow screens.
  const toggle = document.querySelector('[data-nav-toggle]');
  const sidebar = document.querySelector('[data-sidebar]');
  if (toggle && sidebar) {
    toggle.addEventListener('click', () => {
      const open = sidebar.classList.toggle('is-open');
      toggle.setAttribute('aria-expanded', String(open));
    });
  }

  // Text search and state filters for lists. Controls point at their list with
  // data-filter-controls="#list"; items carry data-filter-item and data-state.
  document.querySelectorAll('[data-filter-controls]').forEach(controls => {
    const list = document.querySelector(controls.dataset.filterControls);
    if (!list) return;
    const search = controls.querySelector('[data-filter-search]');
    const buttons = [...controls.querySelectorAll('[data-filter-state]')];
    const empty = controls.dataset.filterEmpty ? document.querySelector(controls.dataset.filterEmpty) : null;
    let state = 'all';

    const apply = () => {
      const query = (search?.value ?? '').trim().toLowerCase();
      let visible = 0;
      list.querySelectorAll('[data-filter-item]').forEach(item => {
        const matchesState = state === 'all' || (item.dataset.state ?? '').split(' ').includes(state);
        const matchesText = !query || item.textContent.toLowerCase().includes(query);
        item.hidden = !(matchesState && matchesText);
        if (!item.hidden) visible++;
      });
      // Group headings (for example audit days) hide when everything under them is hidden.
      list.querySelectorAll('[data-filter-head]').forEach(head => {
        let sibling = head.nextElementSibling;
        let any = false;
        while (sibling && !sibling.hasAttribute('data-filter-head')) {
          if (!sibling.hidden) { any = true; break; }
          sibling = sibling.nextElementSibling;
        }
        head.hidden = !any;
      });
      if (empty) empty.hidden = visible > 0;
    };

    search?.addEventListener('input', apply);
    buttons.forEach(button => button.addEventListener('click', () => {
      state = button.dataset.filterState;
      buttons.forEach(other => other.setAttribute('aria-pressed', String(other === button)));
      apply();
    }));
  });

  // Copy-to-clipboard buttons.
  document.querySelectorAll('[data-copy]').forEach(button => {
    if (!navigator.clipboard) { button.hidden = true; return; }
    const label = button.textContent;
    button.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(button.dataset.copy);
        button.textContent = 'Copied';
        window.setTimeout(() => { button.textContent = label; }, 1500);
      } catch {
        button.hidden = true;
      }
    });
  });

  // Schedule timeline: bars and the "now" marker carry fractional positions.
  document.querySelectorAll('[data-bar-start]').forEach(bar => {
    const start = Number(bar.dataset.barStart);
    const end = Number(bar.dataset.barEnd);
    bar.style.left = `${start * 100}%`;
    bar.style.width = `${Math.max(0, end - start) * 100}%`;
    bar.classList.add('is-placed');
  });
  document.querySelectorAll('[data-now]').forEach(line => {
    line.style.left = `${Number(line.dataset.now) * 100}%`;
    line.classList.add('is-placed');
  });
})();
