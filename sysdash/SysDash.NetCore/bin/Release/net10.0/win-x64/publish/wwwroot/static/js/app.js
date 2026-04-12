console.log('[app.js] Script loaded');

async function fetchStatus() {
  try {
    const params = new URLSearchParams(window.location.search);
    const key = params.get('key');
    const res = await fetch(`/api/status?key=${encodeURIComponent(key)}`);
    const data = await res.json();
    const container = document.getElementById('hosts');
    container.innerHTML = '';
    // compute online/offline counts
    let online = 0, offline = 0;
    const now = Date.now() / 1000;
    data.forEach(item => {
      // determine online status: use report_interval when available, otherwise 90s fallback
      const last = item.last_seen || 0;
      const interval = item.report_interval || item.reportInterval || null;
      const timeSinceSeen = now - last;
      let isOnline = true;
      if (interval && isFinite(interval)) {
        isOnline = timeSinceSeen < Number(interval);
      } else {
        isOnline = timeSinceSeen < 90;
      }
      if (isOnline) online++; else offline++;
      
      // Determine offline color and icon
      let hostnameClass = '';
      let exclamationIcon = '';
      if (!isOnline) {
        if (timeSinceSeen >= 60) {
          hostnameClass = 'offline-60';
          exclamationIcon = '<span class="offline-icon">❗</span>';
        } else if (timeSinceSeen >= 30) {
          hostnameClass = 'offline-30';
          exclamationIcon = '<span class="offline-icon">❗</span>';
        }
      }
      
      const card = document.createElement('div');
      card.className = 'card';
      const displayName = item.friendly_name || item.hostname;
      card.innerHTML = `
        <h2 class="${hostnameClass}">${exclamationIcon}${displayName}</h2>
        <div class="meta">IP: <span class="value">${item.ip}</span></div>
        <div class="meta">Uptime: <span class="value">${formatSeconds(item.uptime)}</span></div>
        <div class="meta">CPU: <span class="value">${item.cpu_percent === null || item.cpu_percent === undefined ? '—' : item.cpu_percent + ' %'}</span></div>
        <div class="meta">Memory: <span class="value">${item.memory_percent === null || item.memory_percent === undefined ? '—' : item.memory_percent + ' %'}</span></div>
        <div class="meta">Ping: <span class="value">${item.ping_ms === null ? '—' : item.ping_ms + ' ms'}</span></div>
        <div class="meta">Last seen: <span class="value">${formatAgo(item.last_seen)}</span></div>
      `;

      const rdpMeta = document.createElement('div');
      rdpMeta.className = 'meta';
      const rdpBtn = document.createElement('button');
      rdpBtn.textContent = item.rdp_url ? 'Open RDP' : 'RDP Not Configured';
      rdpBtn.style.marginTop = '2px';
      rdpBtn.style.padding = '6px 10px';
      rdpBtn.style.borderRadius = '4px';
      rdpBtn.style.border = '1px solid #5BCEFA';
      rdpBtn.style.background = item.rdp_url ? '#5BCEFA' : 'transparent';
      rdpBtn.style.color = item.rdp_url ? '#000' : '#888';
      rdpBtn.style.fontWeight = '600';
      rdpBtn.style.cursor = item.rdp_url ? 'pointer' : 'not-allowed';
      rdpBtn.disabled = !item.rdp_url;
      if (item.rdp_url) {
        rdpBtn.addEventListener('click', () => {
          const win = window.open(item.rdp_url, '_blank', 'noopener,noreferrer');
          if (!win) {
            alert('Popup blocked. Please allow popups for this site.');
          }
        });
      }
      rdpMeta.appendChild(rdpBtn);
      card.appendChild(rdpMeta);
      container.appendChild(card);
    });
    // update summary display (centered)
    const summary = document.getElementById('status-summary');
    if (summary) {
      summary.querySelector('.online-count').textContent = String(online);
      summary.querySelector('.offline-count').textContent = String(offline);
    }
  } catch (err) {
    console.error('fetchStatus error', err);
  }
}

// Dark mode toggle
function setDark(on) {
  if (on) document.body.classList.add('dark'); else document.body.classList.remove('dark');
  try { localStorage.setItem('sysdash:dark', on ? '1' : '0'); } catch (e) {}
}

function initDark() {
  try {
    const stored = localStorage.getItem('sysdash:dark');
    const prefer = stored === null ? true : stored === '1';
    setDark(prefer);
  } catch (e) { setDark(true); }
  const btn = document.getElementById('dark-toggle');
  if (btn) btn.addEventListener('click', () => setDark(!document.body.classList.contains('dark')));
}

initDark();

function formatSeconds(s) {
  if (s === null || s === undefined) return '—';
  s = Math.floor(s);
  const days = Math.floor(s / 86400); s %= 86400;
  const hrs = Math.floor(s / 3600); s %= 3600;
  const mins = Math.floor(s / 60); const secs = s % 60;
  let out = '';
  if (days) out += days + 'd ';
  if (hrs) out += hrs + 'h ';
  out += mins + 'm ' + secs + 's';
  return out;
}

function formatAgo(ts) {
  if (!ts) return '—';
  const diff = Math.floor(Date.now() / 1000 - ts);
  if (diff < 5) return 'just now';
  if (diff < 60) return diff + 's ago';
  if (diff < 3600) return Math.floor(diff / 60) + 'm ago';
  if (diff < 86400) return Math.floor(diff / 3600) + 'h ago';
  return Math.floor(diff / 86400) + 'd ago';
}

fetchStatus();
setInterval(fetchStatus, 5000);

async function fetchServiceStatus() {
  try {
    const params = new URLSearchParams(window.location.search);
    const key = params.get('key');
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 15000);
    console.log('[fetchServiceStatus] Fetching /api/services');
    const res = await fetch(`/api/services?key=${encodeURIComponent(key)}`, { signal: controller.signal });
    clearTimeout(timeoutId);
    console.log('[fetchServiceStatus] Response status:', res.status);
    const data = await res.json();
    console.log('[fetchServiceStatus] Response data:', data);

    const panel = document.getElementById('status-panel');
    const loading = document.getElementById('status-loading');
    if (!data || !data.services?.length) {
      console.warn('[fetchServiceStatus] No services or empty array');
      if (loading) loading.textContent = 'No services available';
      return;
    }

    let html = '';
    data.services.forEach(service => {
      let icon = '⚪';
      if (service.status === 'ok') icon = '🟢';
      if (service.status === 'warn') icon = '🟡';
      if (service.status === 'down') icon = '🔴';
      html += `<div class="status-item"><span>${service.name}</span><span style="font-size:1.2rem;">${icon}</span></div>`;
    });
    console.log('[fetchServiceStatus] Rendering', data.services.length, 'services');

    if (panel) panel.innerHTML = `<h3>Services</h3>${html}`;

  } catch (err) {
    console.error('[fetchServiceStatus] Error:', err);
    const panel = document.getElementById('status-panel');
    if (panel) panel.innerHTML = '<h3>Services</h3><p style="color:#d9534f;">Error loading services</p>';
  }
}

fetchServiceStatus();
setInterval(fetchServiceStatus, 5000);

// Ping external IP every minute
async function pingExternal() {
  try {
    const params = new URLSearchParams(window.location.search);
    const key = params.get('key');
    const res = await fetch(`/api/ping/9.9.9.9?key=${encodeURIComponent(key)}`);
    const data = await res.json();
    const el = document.getElementById('external-ping');
    if (el) {
      el.textContent = data.ping_ms === null ? '—' : data.ping_ms + ' ms';
    }
  } catch (err) {
    console.error('pingExternal error', err);
    const el = document.getElementById('external-ping');
    if (el) el.textContent = '—';
  }
}

pingExternal();
setInterval(pingExternal, 60000);

function escHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function renderUnraid(snapshot) {
  const root = document.querySelector('#unraid-panel .unraid-scroll');
  if (!root) return;

  if (!snapshot) {
    root.innerHTML = '<p style="font-size:0.85rem;">No Unraid data available.</p>';
    return;
  }

  if (snapshot.error) {
    root.innerHTML = `<p style="color:#ff7b7b;font-size:0.85rem;">${escHtml(snapshot.error)}</p>`;
    return;
  }

  if (!snapshot.gql_available) {
    root.innerHTML = '<p style="font-size:0.85rem;">Connecting to Unraid...</p>';
    return;
  }

  const array = snapshot.array || {};
  const disks = Array.isArray(array.disks) ? array.disks : [];
  const docker = snapshot.docker || {};
  const containers = Array.isArray(docker.containers) ? docker.containers : [];
  const running = containers.filter((c) => c.state === 'RUNNING');
  const stopped = containers.filter((c) => c.state !== 'RUNNING');
  const shares = Array.isArray(snapshot.shares) ? snapshot.shares : [];

  let html = '';

  // CPU temperature summary
  if (Array.isArray(snapshot.cpu_temps) && snapshot.cpu_temps.length) {
    const cpu0 = snapshot.cpu_temps[0];
    const cpu1 = snapshot.cpu_temps[1];
    const cpu0Color = cpu0 >= 80 ? '#ff5c5c' : cpu0 >= 65 ? '#ffb347' : '#5BCEFA';
    const cpu1Color = cpu1 >= 80 ? '#ff5c5c' : cpu1 >= 65 ? '#ffb347' : '#5BCEFA';
    html += `<div class="unraid-section" style="padding-bottom:6px;">`;
    html += `<div class="unraid-section-title">CPU Temps</div>`;
    if (typeof cpu0 === 'number') {
      html += `<div class="unraid-row"><span class="unraid-row-name">CPU0</span><span class="unraid-row-val" style="color:${cpu0Color};">${cpu0}°C</span></div>`;
    }
    if (typeof cpu1 === 'number') {
      html += `<div class="unraid-row"><span class="unraid-row-name">CPU1</span><span class="unraid-row-val" style="color:${cpu1Color};">${cpu1}°C</span></div>`;
    }
    html += `</div>`;
  }

  if (array && !array.error) {
    const astate = array.state || 'unknown';
    const stateClass = astate === 'STARTED' ? 'uarray-started' : (astate === 'STOPPED' ? 'uarray-stopped' : 'uarray-other');
    html += '<div class="unraid-section">';
    html += '<div class="unraid-section-title">Array</div>';
    html += `<div class="unraid-row"><span class="unraid-row-name">State</span><span class="unraid-row-val ${stateClass}">${escHtml(astate)}</span></div>`;
    disks.forEach((disk) => {
      const ds = disk.status || '';
      const dClass = ds === 'DISK_OK' ? 'udisk-ok' : (ds.includes('WARN') ? 'udisk-warn' : 'udisk-err');
      const label = ds === 'DISK_OK' ? 'OK' : ds;
      html += `<div class="unraid-row"><span class="unraid-row-name">${escHtml(disk.name || '')}</span><span class="unraid-row-val ${dClass}">${escHtml(label)}</span></div>`;
    });
    html += '</div>';
  }

  if (docker && !docker.error) {
    html += '<div class="unraid-section">';
    html += `<div class="unraid-section-title">Docker - <span class="uct-running">${running.length} running</span> / <span class="uct-stopped">${stopped.length} stopped</span></div>`;
    running.forEach((ct) => {
      const name = (ct.names && ct.names[0] ? ct.names[0] : '').replace('/', '');
      html += `<div class="unraid-row"><span class="unraid-row-name uct-running">${escHtml(name)}</span><span class="unraid-row-val" style="font-size:0.75rem;color:#7a8a99;">${escHtml(ct.status || '')}</span></div>`;
    });
    stopped.forEach((ct) => {
      const name = (ct.names && ct.names[0] ? ct.names[0] : '').replace('/', '');
      html += `<div class="unraid-row"><span class="unraid-row-name uct-stopped">${escHtml(name)}</span><span class="unraid-row-val" style="font-size:0.75rem;color:#666;">${escHtml(ct.state || '')}</span></div>`;
    });
    html += '</div>';
  }

  if (shares.length) {
    html += '<div class="unraid-section">';
    html += '<div class="unraid-section-title">Shares</div>';
    shares.forEach((share) => {
      const free = Number(share.free || 0);
      const freeText = free > 0 ? `${(free / 1073741824).toFixed(1)} GB free` : '—';
      html += `<div class="unraid-row"><span class="unraid-row-name">${escHtml(share.name || '')}</span><span class="unraid-row-val" style="font-size:0.78rem;">${escHtml(freeText)}</span></div>`;
    });
    html += '</div>';
  }

  root.innerHTML = html || '<p style="font-size:0.85rem;">No Unraid API data available.</p>';
}

async function fetchUnraidSnapshot() {
  try {
    const params = new URLSearchParams(window.location.search);
    const key = params.get('key');
    const res = await fetch(`/api/unraid?key=${encodeURIComponent(key)}`);
    const data = await res.json();
    renderUnraid(data);
  } catch (err) {
    console.error('fetchUnraidSnapshot error', err);
    renderUnraid({ error: 'Error loading Unraid snapshot.' });
  }
}

fetchUnraidSnapshot();
setInterval(fetchUnraidSnapshot, 10000);
