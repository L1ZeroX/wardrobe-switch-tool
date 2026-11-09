const viewer = document.getElementById('image-viewer');
const viewerImage = document.getElementById('viewer-image');
document.querySelectorAll('[data-image]').forEach(button => {
  button.addEventListener('click', () => {
    viewerImage.src = button.dataset.image;
    document.getElementById('original-image').href = button.dataset.image;
    viewerImage.alt = button.querySelector('img').alt;
    document.getElementById('viewer-caption').textContent = button.dataset.caption;
    viewer.showModal();
  });
});
document.getElementById('close-viewer').addEventListener('click', () => viewer.close());
viewer.addEventListener('click', event => {
  if (event.target !== viewer) return;
  const bounds = viewer.getBoundingClientRect();
  if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) viewer.close();
});
document.getElementById('copy-url').addEventListener('click', async () => {
  const input = document.getElementById('listing-url');
  const status = document.getElementById('copy-status');
  try {
    await navigator.clipboard.writeText(input.value);
    status.textContent = 'URLをコピーしました。VCCに貼り付けてください。';
  } catch {
    input.focus(); input.select();
    status.textContent = 'URLを選択しました。コピーしてVCCに貼り付けてください。';
  }
});
