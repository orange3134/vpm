'use strict';
const field = document.querySelector('#repository');
document.querySelector('#copy').addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText(field.value);
    document.querySelector('#copy-status').textContent = 'URLをコピーしました。';
  } catch {
    field.select();
    document.querySelector('#copy-status').textContent = 'URLを選択しました。コピーしてお使いください。';
  }
});
fetch('./vpm.json').then(response => {
  if (!response.ok) throw new Error('Listing unavailable');
  return response.json();
}).then(listing => {
  const versions = Object.keys(listing.packages['com.avatarnamecard.exporter'].versions)
    .filter(version => /^\d+\.\d+\.\d+$/.test(version))
    .sort((a, b) => {
      const left = a.split('.').map(Number), right = b.split('.').map(Number);
      return right[0] - left[0] || right[1] - left[1] || right[2] - left[2];
    });
  if (!versions.length) throw new Error('No versions');
  const version = versions[0];
  document.querySelector('#version').textContent = `最新版 v${version}`;
  document.querySelector('#unitypackage').href = `https://github.com/orange3134/vpm/releases/download/v${version}/MEISHI-Pop-Exporter-${version}.unitypackage`;
}).catch(() => {
  document.querySelector('#version').textContent = 'バージョン情報を取得できませんでした。GitHubのリリースページをご利用ください。';
});
