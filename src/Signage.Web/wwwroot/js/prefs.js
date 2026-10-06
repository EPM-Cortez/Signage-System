(() => {
  // Saved display preferences: colour theme and the pinned admin sidebar.
  // Loaded in <head> without defer so both are applied before the page first paints.
  const root = document.documentElement;
  const themeKey = 'signage-theme';
  const navKey = 'signage-nav';
  const system = window.matchMedia('(prefers-color-scheme: dark)');

  const read = key => {
    try { return localStorage.getItem(key); } catch { return null; }
  };
  const write = (key, value) => {
    try {
      if (value === null) localStorage.removeItem(key);
      else localStorage.setItem(key, value);
    } catch { /* Storage blocked: the change still applies to this page. */ }
  };

  // Theme: light, dark, or follow the system setting (the default).
  const themeChoice = () => {
    const value = read(themeKey);
    return value === 'light' || value === 'dark' ? value : 'system';
  };
  const applyTheme = () => {
    const choice = themeChoice();
    root.dataset.theme = choice === 'system' ? (system.matches ? 'dark' : 'light') : choice;
    document.querySelectorAll('[data-theme-choice]').forEach(button =>
      button.setAttribute('aria-pressed', String(button.dataset.themeChoice === choice)));
  };

  // Sidebar: a thin icon rail that widens on hover, unless pinned open.
  const applyNav = () => {
    const pinned = read(navKey) === 'pinned';
    root.dataset.nav = pinned ? 'pinned' : 'rail';
    document.querySelectorAll('[data-nav-pin]').forEach(button => {
      button.setAttribute('aria-pressed', String(pinned));
      button.title = pinned ? 'Unpin sidebar' : 'Pin sidebar open';
    });
  };

  applyTheme();
  applyNav();
  system.addEventListener('change', applyTheme);
  // Keep other open tabs in step.
  window.addEventListener('storage', () => { applyTheme(); applyNav(); });

  document.addEventListener('DOMContentLoaded', () => {
    applyTheme();
    applyNav();
    document.querySelectorAll('[data-theme-choice]').forEach(button => button.addEventListener('click', () => {
      const choice = button.dataset.themeChoice;
      write(themeKey, choice === 'system' ? null : choice);
      applyTheme();
    }));
    document.querySelectorAll('[data-nav-pin]').forEach(button => button.addEventListener('click', () => {
      write(navKey, root.dataset.nav === 'pinned' ? null : 'pinned');
      applyNav();
    }));
  });
})();
