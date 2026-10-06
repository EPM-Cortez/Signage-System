(() => {
  const root = document.querySelector('[data-status-url]');
  if (!root) return;
  const heading = root.querySelector('[data-status-heading]');
  const stage = root.querySelector('[data-status-stage]');
  const message = root.querySelector('[data-status-message]');
  const preview = root.querySelector('[data-preview-link]');
  const ring = root.querySelector('[data-progress-ring]');
  const ringValue = root.querySelector('[data-ring-value]');
  const ringLabel = root.querySelector('[data-ring-label]');
  const delivery = root.querySelector('[data-delivery]');
  const deliveryTitle = delivery.querySelector('[data-delivery-title]');
  const deliveryBar = delivery.querySelector('[data-delivery-bar]');
  const deliveryNote = delivery.querySelector('[data-delivery-note]');
  const preparingMessage = message.textContent;

  const preparingPollMs = 1500;
  const deliveryPollMs = 5000;
  // Screens pick up new content within a couple of player polls; stop watching well after that.
  const deliveryWatchMs = 10 * 60 * 1000;

  const plural = (count, one, many) => `${count} ${count === 1 ? one : many}`;
  const formatDate = value => new Date(value).toLocaleString(undefined, { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
  const every = seconds => seconds === 60 ? 'minute' : seconds % 60 === 0 ? `${seconds / 60} minutes` : `${seconds} seconds`;

  // The server reports progress after each slide. In between, ease towards the next report so a slow
  // slide still visibly moves, without ever showing more than has actually been done.
  let shown = 0;
  let ceiling = 0;
  let secondsPerStep = 4;
  let lastStepAt = performance.now();
  let lastSlides = 0;
  let creep = null;

  const drawRing = value => {
    ringValue.style.strokeDashoffset = String(100 - value);
    ringLabel.textContent = `${Math.floor(value)}%`;
  };

  const startCreep = () => {
    if (creep) return;
    let last = performance.now();
    creep = window.setInterval(() => {
      const now = performance.now();
      if (shown < ceiling) {
        shown += (ceiling - shown) * (1 - Math.exp(-(now - last) / 1000 / (secondsPerStep * 0.6)));
        drawRing(shown);
      }
      last = now;
    }, 200);
  };

  const stopCreep = () => {
    window.clearInterval(creep);
    creep = null;
  };

  const showProgress = progress => {
    stage.textContent = progress.stage;
    if (progress.percent === null) {
      ring.classList.add('is-indeterminate');
      ring.removeAttribute('aria-valuenow');
      ringLabel.textContent = '';
      return;
    }
    if (progress.slidesRendered > lastSlides) {
      const now = performance.now();
      secondsPerStep = Math.max(1, (now - lastStepAt) / 1000 / (progress.slidesRendered - lastSlides));
      lastStepAt = now;
      lastSlides = progress.slidesRendered;
    }
    ring.classList.remove('is-indeterminate');
    ring.setAttribute('aria-valuenow', String(progress.percent));
    shown = Math.max(shown, progress.percent);
    ceiling = Math.max(shown, progress.nextPercent - 1);
    drawRing(shown);
    startCreep();
  };

  // Returns true once every online screen that should switch has reported playing this version.
  const showScreens = screens => {
    const { total, showing, offline } = screens;
    const waiting = total - showing - offline;
    const notes = [];
    if (total > 0) {
      deliveryTitle.textContent = `Showing on ${showing} of ${plural(total, 'screen', 'screens')}`;
      deliveryBar.firstElementChild.style.width = `${(showing / total) * 100}%`;
      if (waiting > 0) notes.push(`Screens check for new content every ${every(screens.checkSeconds)}.`);
      if (offline > 0) notes.push(`${plural(offline, 'screen is', 'screens are')} offline and will update when ${offline === 1 ? 'it reconnects' : 'they reconnect'}.`);
      if (screens.scheduledGroups > 0) notes.push(`More screens switch at ${formatDate(screens.nextStartUtc)}.`);
      if (screens.overriddenGroups > 0) notes.push(`A higher-priority presentation is showing on ${plural(screens.overriddenGroups, 'other screen group', 'other screen groups')}.`);
    } else if (screens.liveGroups > 0) {
      deliveryTitle.textContent = 'No screens are in the selected groups yet';
    } else if (screens.scheduledGroups > 0) {
      deliveryTitle.textContent = `Screens switch at ${formatDate(screens.nextStartUtc)}`;
    } else if (screens.overriddenGroups > 0) {
      deliveryTitle.textContent = 'Another presentation has priority right now';
    } else {
      deliveryTitle.textContent = 'Not showing on any screens right now';
    }
    deliveryBar.classList.toggle('hidden', total === 0);
    delivery.classList.toggle('is-complete', total > 0 && showing === total);
    deliveryNote.textContent = notes.join(' ');
    deliveryNote.classList.toggle('hidden', notes.length === 0);
    delivery.classList.remove('hidden');
    return waiting <= 0;
  };

  let readyAt = null;
  const settle = (state, className) => {
    root.classList.add(className);
    stopCreep();
    ring.classList.remove('is-indeterminate');
    stage.classList.add('hidden');
    document.title = `${state.status} · School Signage`;
  };

  const poll = async () => {
    let next = preparingPollMs;
    try {
      const response = await fetch(root.dataset.statusUrl, { cache: 'no-store' });
      if (!response.ok) throw new Error('Status request failed');
      const state = await response.json();
      heading.textContent = state.status;
      if (state.ready) {
        if (readyAt === null) readyAt = Date.now();
        settle(state, 'is-ready');
        ring.setAttribute('aria-valuenow', '100');
        message.textContent = state.message || (state.published
          ? 'The new presentation is ready and has been published to the selected screens.'
          : 'The presentation is prepared but has not been published.');
        if (state.previewUrl) {
          preview.href = state.previewUrl;
          preview.classList.remove('hidden');
        }
        const delivered = !state.screens || showScreens(state.screens);
        next = delivered || Date.now() - readyAt > deliveryWatchMs ? null : deliveryPollMs;
      } else if (state.failed) {
        settle(state, 'is-failed');
        message.textContent = state.message;
        next = null;
      } else {
        message.textContent = preparingMessage;
        if (state.progress) showProgress(state.progress);
      }
    } catch {
      if (readyAt === null) message.textContent = 'Status will refresh when the connection is available.';
      else next = Date.now() - readyAt > deliveryWatchMs ? null : deliveryPollMs;
    }
    if (next) window.setTimeout(poll, next);
  };
  poll();
})();
