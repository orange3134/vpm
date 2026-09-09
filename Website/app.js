'use strict';

const field = document.querySelector('#repository');
const status = document.querySelector('#copy-status');

document.querySelector('#copy').addEventListener('click', async () => {
  status.textContent = '';
  try {
    await navigator.clipboard.writeText(field.value);
    status.textContent = 'URLをコピーしました。';
  } catch {
    field.focus();
    field.select();
    status.textContent = 'URLを選択しました。コピーしてお使いください。';
  }
});
