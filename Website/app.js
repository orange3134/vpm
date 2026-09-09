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
  const catalog = document.querySelector('#catalog');
  catalog.replaceChildren();
  const compareVersions = (a, b) => {
    const left = a.split('+')[0].split('.').map(Number), right = b.split('+')[0].split('.').map(Number);
    return right[0] - left[0] || right[1] - left[1] || right[2] - left[2];
  };
  for (const [id, entry] of Object.entries(listing.packages)) {
    const latest = Object.keys(entry.versions).sort(compareVersions)[0];
    const info = entry.versions[latest];
    const card = document.createElement('article');
    card.className = 'package';
    const title = document.createElement('h3');
    title.textContent = info.displayName || id;
    const version = document.createElement('p');
    version.className = 'package-version';
    version.textContent = `v${latest} · ${id}`;
    const description = document.createElement('p');
    description.textContent = info.description || '';
    const details = document.createElement('a');
    const candidate = info.documentationUrl || info.url;
    details.href = candidate.startsWith('https://') ? candidate : info.url;
    details.textContent = info.documentationUrl ? '使い方・導入方法' : 'VPM用ZIP';
    card.append(title, version, description, details);
    catalog.append(card);
  }
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
  document.querySelector('#catalog').textContent = '一覧を取得できませんでした。時間をおいて再読み込みしてください。';
  document.querySelector('#version').textContent = 'バージョン情報を取得できませんでした。GitHubのリリースページをご利用ください。';
});
