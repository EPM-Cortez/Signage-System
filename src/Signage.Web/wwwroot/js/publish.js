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
  const errors = root.querySelector('[data-upload-errors]');
  const upload = root.querySelector('[data-upload]');
  const uploadTitle = upload.querySelector('[data-upload-title]');
  const uploadPercent = upload.querySelector('[data-upload-percent]');
  const uploadBar = upload.querySelector('[data-upload-bar]');
  const uploadDetail = upload.querySelector('[data-upload-detail]');
  const uploadCancel = upload.querySelector('[data-upload-cancel]');
  let request = null;

  const formatSize = bytes => bytes >= 1048576 ? `${(bytes / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
  const formatDuration = seconds => seconds < 60 ? `${Math.max(1, Math.round(seconds))} sec` : `${Math.round(seconds / 60)} min`;
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
  const showErrors = messages => {
    const list = document.createElement('ul');
    messages.forEach(text => {
      const item = document.createElement('li');
      item.textContent = text;
      list.append(item);
    });
    errors.replaceChildren(list);
    errors.classList.remove('hidden');
    errors.scrollIntoView({ block: 'nearest' });
  };

  const setUploadPercent = fraction => {
    const percent = Math.floor(fraction * 100);
    uploadBar.firstElementChild.style.width = `${fraction * 100}%`;
    uploadBar.setAttribute('aria-valuenow', String(percent));
    uploadPercent.textContent = `${percent}%`;
  };

  const resetUpload = () => {
    request = null;
    root.classList.remove('is-uploading');
    upload.classList.add('hidden');
    submit.classList.remove('hidden');
    submit.disabled = false;
  };

  // Post with XMLHttpRequest rather than a normal submit so large files show how far along they are.
  const startUpload = () => {
    const started = performance.now();
    errors.classList.add('hidden');
    uploadTitle.textContent = 'Uploading';
    uploadDetail.textContent = 'Starting upload…';
    uploadBar.classList.add('progress-determinate');
    uploadCancel.classList.remove('hidden');
    setUploadPercent(0);
    root.classList.add('is-uploading');
    submit.classList.add('hidden');
    upload.classList.remove('hidden');

    const xhr = new XMLHttpRequest();
    request = xhr;
    xhr.open('POST', root.action);
    xhr.setRequestHeader('X-Requested-With', 'XMLHttpRequest');
    xhr.responseType = 'json';
    xhr.upload.addEventListener('progress', event => {
      if (!event.lengthComputable) return;
      setUploadPercent(event.loaded / event.total);
      const seconds = (performance.now() - started) / 1000;
      const rate = event.loaded / seconds;
      const remaining = rate > 0 ? (event.total - event.loaded) / rate : 0;
      uploadDetail.textContent = `${formatSize(event.loaded)} of ${formatSize(event.total)}`
        + (seconds > 2 && remaining >= 1 ? ` · about ${formatDuration(remaining)} left` : '');
    });
    xhr.upload.addEventListener('load', () => {
      // Every byte has arrived; the server still saves and checks the file before it replies.
      setUploadPercent(1);
      uploadTitle.textContent = 'Checking the file';
      uploadDetail.textContent = 'Saving it on the server. This only takes a moment.';
      uploadBar.classList.remove('progress-determinate');
      uploadBar.firstElementChild.style.width = '';
      uploadCancel.classList.add('hidden');
    });
    xhr.addEventListener('load', () => {
      const status = xhr.status;
      const body = xhr.response;
      if (status === 200 && body?.redirect) {
        window.location.assign(body.redirect);
        return;
      }
      // A server that answered with a redirect instead (the browser has already followed it).
      if (status === 200 && xhr.responseURL && new URL(xhr.responseURL).pathname !== window.location.pathname) {
        window.location.assign(xhr.responseURL);
        return;
      }
      resetUpload();
      if (status === 401) {
        window.location.reload();
      } else if (status === 413) {
        showErrors(['This file is too large to upload.']);
      } else {
        showErrors(body?.errors?.length ? body.errors : ['The upload could not be completed. Refresh the page and try again.']);
      }
    });
    xhr.addEventListener('error', () => {
      resetUpload();
      showErrors(["The upload didn't finish. Check your connection and try again."]);
    });
    xhr.addEventListener('abort', resetUpload);
    xhr.send(new FormData(root));
  };

  root.addEventListener('submit', event => {
    event.preventDefault();
    if (!request) startUpload();
  });
  uploadCancel.addEventListener('click', () => request?.abort());

  // Coming back from the status page restores this page from the back/forward cache mid-upload.
  window.addEventListener('pageshow', resetUpload);

  if (starts.value || ends.value) schedule.open = true;
  refreshTargets();
  refreshTiming();
  if (fileInput.files.length) updateFile(fileInput.files[0]);
})();
