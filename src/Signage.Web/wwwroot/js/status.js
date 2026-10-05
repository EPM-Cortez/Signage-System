(() => {
  const root = document.querySelector('[data-status-url]');
  if (!root) return;
  const heading = root.querySelector('[data-status-heading]');
  const message = root.querySelector('[data-status-message]');
  const preview = root.querySelector('[data-preview-link]');
  const progress = root.querySelector('.progress');
  let stopped = false;

  const poll = async () => {
    try {
      const response = await fetch(root.dataset.statusUrl, { cache: 'no-store' });
      if (!response.ok) throw new Error('Status request failed');
      const state = await response.json();
      heading.textContent = state.status;
      if (state.ready) {
        root.classList.add('is-ready');
        message.textContent = state.message || (state.published
          ? 'The new presentation is ready and has been published to the selected screens.'
          : 'The presentation is prepared but has not been published.');
        progress.classList.add('hidden');
        if (state.previewUrl) {
          preview.href = state.previewUrl;
          preview.classList.remove('hidden');
        }
        stopped = true;
      } else if (state.failed) {
        root.classList.add('is-failed');
        message.textContent = state.message;
        progress.classList.add('hidden');
        stopped = true;
      }
      if (stopped) document.title = `${state.status} · School Signage`;
    } catch {
      message.textContent = 'Status will refresh when the connection is available.';
    }
    if (!stopped) window.setTimeout(poll, 2500);
  };
  poll();
})();
