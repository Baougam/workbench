// Workbench for Chrome, Edge and Brave.
// Right-click a link, image, video or audio › Download with Workbench › how long.
// The browser downloads the file as usual; Workbench (via native messaging) gets the path and the time,
// so no card pops up afterwards and the file expires on schedule.

const HOST = 'com.workbench.host';
const TIMES = [
  ['1 hour', 'For an hour'],
  ['1 day', 'For a day'],
  ['1 week', 'For a week'],
  ['1 month', 'For a month'],
  ['Forever', 'Keep forever']
];
const CONTEXTS = ['link', 'image', 'video', 'audio'];

function tell(msg) {
  try {
    chrome.runtime.sendNativeMessage(HOST, msg, () => void chrome.runtime.lastError);
  } catch (e) { /* Workbench not installed */ }
}

function menus() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: 'wb', title: 'Download with Workbench', contexts: CONTEXTS });
    for (const [key, title] of TIMES) {
      chrome.contextMenus.create({ id: 'wb:' + key, parentId: 'wb', title, contexts: CONTEXTS });
    }
  });
}

chrome.runtime.onInstalled.addListener(() => { menus(); tell({ op: 'hello', browser: navigator.userAgent }); });
chrome.runtime.onStartup.addListener(() => { menus(); tell({ op: 'hello', browser: navigator.userAgent }); });

async function remember(id, time) { await chrome.storage.session.set({ ['d' + id]: time }); }
async function recall(id) { const r = await chrome.storage.session.get('d' + id); return r['d' + id]; }
async function forget(id) { await chrome.storage.session.remove('d' + id); }

chrome.contextMenus.onClicked.addListener(async (info) => {
  const id = String(info.menuItemId);
  if (!id.startsWith('wb:')) return;
  const time = id.slice(3);
  const url = info.linkUrl || info.srcUrl;
  if (!url) return;
  let dl;
  try { dl = await chrome.downloads.download({ url }); } catch (e) { return; }
  if (dl === undefined) return;
  await remember(dl, time);
  // the file name may already be known (fast downloads)
  const [item] = await chrome.downloads.search({ id: dl });
  if (item && item.filename) tell({ op: item.state === 'complete' ? 'done' : 'plan', time, path: item.filename });
});

chrome.downloads.onChanged.addListener(async (d) => {
  const time = await recall(d.id);
  if (!time) return;
  if (d.filename && d.filename.current) tell({ op: 'plan', time, path: d.filename.current });
  if (d.state && d.state.current === 'complete') {
    const [item] = await chrome.downloads.search({ id: d.id });
    if (item && item.filename) tell({ op: 'done', time, path: item.filename });
    await forget(d.id);
  }
  if (d.state && d.state.current === 'interrupted') await forget(d.id);
});
