(() => {
  const root = document.querySelector('[data-publish-form]');
  if (!root) return;
  const inputs = [...root.querySelectorAll('[data-group-name]')];
  const targetList = root.querySelector('[data-target-list]');
  const fileInput = root.querySelector('[data-file-input]');
  const fileLabel = root.querySelector('[data-file-label]');
  const fileHint = root.querySelector('[data-file-hint]');
  const dropZone = root.querySelector('[data-drop-zone]');
  const summaryFile = root.querySelector('[data-summary-file]');
  const summaryTiming = root.querySelector('[data-summary-timing]');
  const nameInput = root.querySelector('#Input_Name');
  const publishWhenReady = root.querySelector('#Input_PublishWhenReady');
  const starts = root.querySelector('#Input_StartsLocal');
  const ends = root.querySelector('#Input_EndsLocal');
  const schedule = root.querySelector('[data-schedule]');
  const submit = root.querySelector('[data-submit]');

  const formatSize = bytes => bytes >= 1048576 ? `${(bytes / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
  const formatDate = value => new Date(value).toLocaleString(undefined, { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });

  const refreshTargets = () => {
    const selected = inputs.filter(input => input.checked).map(input => input.dataset.groupName);
    targetList.textContent = selected.length ? selected.join(', ') : 'None selected';
    targetList.classList.toggle('is-empty', selected.length === 0);
  };

  const refreshTiming = () => {
    let text;
    if (!publishWhenReady.checked) text = "Prepare only, don't publish yet";
    else if (starts.value && ends.value) text = `${formatDate(starts.value)} until ${formatDate(ends.value)}`;
    else if (starts.value) text = `From ${formatDate(starts.value)}`;
    else if (ends.value) text = `As soon as it's ready, until ${formatDate(ends.value)}`;
    else text = "As soon as it's ready";
    summaryTiming.textContent = text;
  };

  const updateFile = file => {
    if (!file) return;
    fileLabel.textContent = file.name;
    fileHint.textContent = `${formatSize(file.size)} · choose a different file`;
    dropZone.classList.add('has-file');
    summaryFile.textContent = file.name;
    summaryFile.classList.remove('is-empty');
    if (!nameInput.value) nameInput.value = file.name.replace(/\.pptx$/i, '');
  };

  inputs.forEach(input => input.addEventListener('change', refreshTargets));
  [publishWhenReady, starts, ends].forEach(input => input.addEventListener('change', refreshTiming));
  fileInput.addEventListener('change', () => updateFile(fileInput.files[0]));
  ['dragenter', 'dragover'].forEach(name => dropZone.addEventListener(name, event => {
    event.preventDefault();
    dropZone.classList.add('is-dragging');
  }));
  ['dragleave', 'drop'].forEach(name => dropZone.addEventListener(name, event => {
    event.preventDefault();
    dropZone.classList.remove('is-dragging');
  }));
  dropZone.addEventListener('drop', event => {
    const file = event.dataTransfer.files[0];
    if (!file || !file.name.toLowerCase().endsWith('.pptx')) return;
    const transfer = new DataTransfer();
    transfer.items.add(file);
    fileInput.files = transfer.files;
    updateFile(file);
  });
  root.addEventListener('submit', () => {
    // Large files take a while to upload; show that the click registered.
    window.setTimeout(() => {
      submit.disabled = true;
      submit.textContent = 'Uploading…';
    });
  });

  window.addEventListener('pageshow', () => {
    submit.disabled = false;
    submit.textContent = 'Publish';
  });

  if (starts.value || ends.value) schedule.open = true;
  refreshTargets();
  refreshTiming();
  if (fileInput.files.length) updateFile(fileInput.files[0]);
})();
