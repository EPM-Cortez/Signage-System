(() => {
  const viewer = document.querySelector('[data-viewer]');
  if (!viewer || typeof viewer.showModal !== 'function') return;
  const slides = [...document.querySelectorAll('[data-slide]')].map(element => ({
    element,
    type: element.dataset.type,
    src: element.dataset.src,
    video: element.dataset.video,
    duration: Number(element.dataset.duration) || 10000,
    label: element.dataset.label
  }));
  if (!slides.length) return;

  const image = viewer.querySelector('[data-viewer-image]');
  const video = viewer.querySelector('[data-viewer-video]');
  const note = viewer.querySelector('[data-viewer-note]');
  const count = viewer.querySelector('[data-viewer-count]');
  const progress = viewer.querySelector('[data-viewer-progress]');
  const playButton = viewer.querySelector('[data-viewer-play]');
  const youtubeHoldMs = 5000;
  let index = 0;
  let playing = false;
  let timer = 0;

  const runProgress = milliseconds => {
    progress.style.transition = 'none';
    progress.style.width = '0';
    if (!milliseconds) return;
    progress.getBoundingClientRect();
    progress.style.transition = `width ${milliseconds}ms linear`;
    progress.style.width = '100%';
  };

  const stopTimers = () => {
    window.clearTimeout(timer);
    video.onended = null;
  };

  const stopVideo = () => {
    video.pause();
    video.removeAttribute('src');
    video.load();
  };

  const show = target => {
    stopTimers();
    index = (target + slides.length) % slides.length;
    const slide = slides[index];
    count.textContent = `${index + 1} / ${slides.length}`;
    note.classList.add('hidden');

    if (slide.type === 'video' && slide.video) {
      image.classList.add('hidden');
      video.classList.remove('hidden');
      video.poster = slide.src;
      video.src = slide.video;
      video.onended = () => { if (playing) show(index + 1); };
      if (playing) video.play().catch(() => {});
      runProgress(0);
      return;
    }

    stopVideo();
    video.classList.add('hidden');
    image.classList.remove('hidden');
    image.src = slide.src;
    image.alt = slide.label;
    if (slide.type === 'youtube') {
      note.textContent = 'YouTube video · plays on the display while it is online';
      note.classList.remove('hidden');
    }
    const duration = slide.type === 'youtube' ? youtubeHoldMs : slide.duration;
    if (playing) {
      runProgress(duration);
      timer = window.setTimeout(() => show(index + 1), duration);
    } else {
      runProgress(0);
    }
  };

  const setPlaying = value => {
    playing = value;
    playButton.setAttribute('aria-label', playing ? 'Pause' : 'Play');
    playButton.querySelector('.icon-pause').classList.toggle('hidden', !playing);
    playButton.querySelector('.icon-play').classList.toggle('hidden', playing);
  };

  const open = (start, autoplay) => {
    setPlaying(autoplay);
    if (!viewer.open) viewer.showModal();
    show(start);
  };

  const togglePlay = () => {
    setPlaying(!playing);
    if (!video.classList.contains('hidden')) {
      if (playing) video.play().catch(() => {}); else video.pause();
    } else {
      show(index);
    }
  };

  slides.forEach((slide, position) => slide.element.addEventListener('click', () => open(position, false)));
  document.querySelector('[data-viewer-start]')?.addEventListener('click', () => open(0, true));
  viewer.querySelector('[data-viewer-prev]').addEventListener('click', () => show(index - 1));
  viewer.querySelector('[data-viewer-next]').addEventListener('click', () => show(index + 1));
  viewer.querySelector('[data-viewer-close]').addEventListener('click', () => viewer.close());
  viewer.querySelector('[data-viewer-fullscreen]').addEventListener('click', () => {
    if (document.fullscreenElement) document.exitFullscreen();
    else viewer.requestFullscreen?.().catch(() => {});
  });
  playButton.addEventListener('click', togglePlay);
  viewer.addEventListener('keydown', event => {
    if (event.key === 'ArrowRight') { event.preventDefault(); show(index + 1); }
    else if (event.key === 'ArrowLeft') { event.preventDefault(); show(index - 1); }
    else if (event.key === ' ' && event.target === viewer) { event.preventDefault(); togglePlay(); }
  });
  viewer.addEventListener('close', () => {
    stopTimers();
    stopVideo();
    runProgress(0);
    if (document.fullscreenElement) document.exitFullscreen();
    slides[index].element.focus();
  });
})();
