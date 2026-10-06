(() => {
  // Permanent deletion requires an explicit confirmation. The server also checks
  // the confirmation value, so disabling JavaScript cannot silently delete items.
  document.querySelectorAll('form[data-confirm]').forEach(form => {
    form.addEventListener('submit', event => {
      if (!window.confirm(form.dataset.confirm)) { event.preventDefault(); return; }
      const confirmed = form.querySelector('input[name="confirmed"]');
      if (confirmed) confirmed.value = 'true';
    });
  });
  // Menus are popovers (opened by a button with popovertarget). The browser handles opening,
  // Escape and clicking away; this places each one against its button, lined up with the
  // button's right edge (or left with data-align="start"), and flips it above when there is no room below.
  const menus = [...document.querySelectorAll('.menu[popover]')];
  const triggerOf = menu => document.querySelector(`[popovertarget="${menu.id}"]:not([popovertargetaction="hide"])`);
  const place = menu => {
    const trigger = triggerOf(menu);
    if (!trigger) return;
    const anchor = trigger.getBoundingClientRect();
    const { offsetWidth: width, offsetHeight: height } = menu;
    const gap = 6, edge = 8;
    const left = menu.dataset.align === 'start' ? anchor.left : anchor.right - width;
    let top = anchor.bottom + gap;
    if (top + height > window.innerHeight - edge && anchor.top - gap - height >= edge) top = anchor.top - gap - height;
    menu.style.left = `${Math.max(edge, Math.min(left, window.innerWidth - width - edge))}px`;
    menu.style.top = `${top}px`;
  };
  menus.forEach(menu => {
    // Hidden until placed, so it never flashes at the centred fallback position.
    menu.addEventListener('beforetoggle', event => {
      if (event.newState === 'open') menu.classList.add('is-anchored', 'is-placing');
    });
    menu.addEventListener('toggle', event => {
      const open = event.newState === 'open';
      triggerOf(menu)?.classList.toggle('is-active', open);
      if (!open) return;
      place(menu);
      menu.classList.remove('is-placing');
      menu.querySelector('input:not([type="hidden"])')?.focus();
    });
  });
  const placeOpen = () => menus.filter(menu => menu.matches(':popover-open')).forEach(place);
  window.addEventListener('resize', placeOpen);
  window.addEventListener('scroll', placeOpen, true);

  // On narrow screens the folder list becomes a scrolling strip; keep the open folder in view.
  const folderPane = document.querySelector('.folder-pane');
  const openFolder = folderPane?.querySelector('[aria-current="page"]');
  if (folderPane && openFolder && folderPane.scrollWidth > folderPane.clientWidth) {
    const pane = folderPane.getBoundingClientRect();
    const link = openFolder.getBoundingClientRect();
    folderPane.scrollLeft += link.left - pane.left - (pane.width - link.width) / 2;
  }

  // Collapsible sidebar on narrow screens.
  const toggle = document.querySelector('[data-nav-toggle]');
  const sidebar = document.querySelector('[data-sidebar]');
  if (toggle && sidebar) {
    toggle.addEventListener('click', () => {
      const open = sidebar.classList.toggle('is-open');
      toggle.setAttribute('aria-expanded', String(open));
    });
  }

  // Account popout (theme and sign out). Closes on an outside click, Escape, or tabbing away.
  document.querySelectorAll('[data-account]').forEach(account => {
    const button = account.querySelector('[data-account-toggle]');
    const setOpen = open => {
      account.classList.toggle('is-open', open);
      button.setAttribute('aria-expanded', String(open));
    };
    button.addEventListener('click', () => setOpen(!account.classList.contains('is-open')));
    document.addEventListener('click', event => {
      if (!account.contains(event.target)) setOpen(false);
    });
    account.addEventListener('keydown', event => {
      if (event.key !== 'Escape' || !account.classList.contains('is-open')) return;
      setOpen(false);
      button.focus();
    });
    account.addEventListener('focusout', event => {
      if (event.relatedTarget && !account.contains(event.relatedTarget)) setOpen(false);
    });
  });

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
        // An item can narrow what search reads with data-filter-text (for example a row's summary, not its edit form).
        const text = (item.querySelector('[data-filter-text]') ?? item).textContent.toLowerCase();
        const matchesText = !query || text.includes(query);
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
      // Grouped lists (for example staff and administrators) hide a group with no matches while filtering.
      list.querySelectorAll('[data-filter-group]').forEach(group => {
        group.hidden = (query !== '' || state !== 'all') && !group.querySelector('[data-filter-item]:not([hidden])');
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

  // Selects that reveal an extra field for one option: data-reveal="#field" data-reveal-value="option".
  // The field's inputs are disabled while hidden so they are not submitted.
  document.querySelectorAll('select[data-reveal]').forEach(select => {
    const target = document.querySelector(select.dataset.reveal);
    if (!target) return;
    const inputs = [...target.querySelectorAll('input, select, textarea')];
    const apply = focus => {
      const show = select.value === select.dataset.revealValue;
      target.hidden = !show;
      inputs.forEach(input => { input.disabled = !show; input.required = show; });
      if (show && focus) inputs[0]?.focus();
    };
    select.addEventListener('change', () => apply(true));
    apply(false);
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
