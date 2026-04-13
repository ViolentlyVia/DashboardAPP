(function initOmadaPage() {
  var selectedSiteId = null;
  var selectedSiteMap = {};
  var currentOverview = null;
  var currentClients = [];
  var sortField = "name";
  var sortDir = "asc";

  function checkAccessKey() {
    var params = new URLSearchParams(window.location.search);
    var key = params.get("key");
    if (!key) {
      document.body.innerHTML = '<div style="display:flex;justify-content:center;align-items:center;height:100vh;background:#0f1113;color:#e6e6e6;"><div style="text-align:center;"><h1 style="color:#FF0000;margin:0 0 20px 0;">Access Denied</h1><p style="font-size:16px;margin:0;">Missing access key.</p></div></div>';
      document.body.style.margin = "0";
      document.body.style.padding = "0";
      throw new Error("Access denied");
    }
  }

  function syncLinks() {
    var params = new URLSearchParams(window.location.search);
    var key = params.get("key");
    var back = document.getElementById("back-link");
    if (back && key) {
      back.href = "/?key=" + encodeURIComponent(key);
    }
  }

  function setDark(on) {
    if (on) {
      document.body.classList.add("dark");
    } else {
      document.body.classList.remove("dark");
    }
    try { localStorage.setItem("sysdash:dark", on ? "1" : "0"); } catch (e) {}
  }

  function initDark() {
    try {
      var stored = localStorage.getItem("sysdash:dark");
      var prefer = stored === null ? true : stored === "1";
      setDark(prefer);
    } catch (e) { setDark(true); }
    var btn = document.getElementById("dark-toggle");
    if (btn) {
      btn.addEventListener("click", function () {
        setDark(!document.body.classList.contains("dark"));
      });
    }
  }

  function escHtml(value) {
    return String(value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/\"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  function fmt(value, suffix) {
    if (value === null || value === undefined || value === "") return "-";
    return escHtml(String(value) + (suffix || ""));
  }

  function fmtDate(ts) {
    if (!ts) return "-";
    var d = new Date(Number(ts) * 1000);
    if (Number.isNaN(d.getTime())) return "-";
    return d.toLocaleString();
  }

  function fmtUptime(seconds) {
    if (seconds === null || seconds === undefined) return "-";
    var s = Math.floor(Number(seconds));
    if (s < 0) return "-";
    var d = Math.floor(s / 86400);
    var h = Math.floor((s % 86400) / 3600);
    var m = Math.floor((s % 3600) / 60);
    if (d > 0) return d + "d " + h + "h " + m + "m";
    if (h > 0) return h + "h " + m + "m";
    return m + "m";
  }

  function fmtRate(bps) {
    if (bps === null || bps === undefined) return "-";
    var n = Number(bps);
    if (!Number.isFinite(n) || n < 0) return "-";
    if (n >= 1000000000) return (n / 1000000000).toFixed(1) + " Gbps";
    if (n >= 1000000) return (n / 1000000).toFixed(1) + " Mbps";
    if (n >= 1000) return (n / 1000).toFixed(0) + " Kbps";
    return n + " bps";
  }

  function row(label, value) {
    return '<div class="row"><span class="label">' + escHtml(label) + '</span><span class="value">' + value + '</span></div>';
  }

  function statusBadge(status) {
    var s = Number(status);
    if (status === null || status === undefined) {
      return '<span class="status-badge status-unknown">Unknown</span>';
    }
    if (s === 0) {
      return '<span class="status-badge status-disconnected">Disconnected</span>';
    }
    return '<span class="status-badge status-connected">Connected</span>';
  }

  function deviceTypeLabel(type) {
    if (!type) return "Device";
    var t = String(type).toLowerCase();
    if (t === "ap") return "AP";
    if (t === "switch") return "Switch";
    if (t === "gateway") return "Gateway";
    if (t === "eap") return "AP";
    return escHtml(type.charAt(0).toUpperCase() + type.slice(1));
  }

  function signalIcon(dbm) {
    if (dbm === null || dbm === undefined) return "";
    var n = Number(dbm);
    var cls = n >= -65 ? "signal-good" : n >= -80 ? "signal-fair" : "signal-poor";
    return '<span class="signal-bar ' + cls + '">' + escHtml(String(n)) + " dBm</span>";
  }

  function cmpValues(a, b) {
    if (a === b) return 0;
    if (a === null || a === undefined || a === "") return 1;
    if (b === null || b === undefined || b === "") return -1;

    var an = Number(a);
    var bn = Number(b);
    var aIsNum = Number.isFinite(an);
    var bIsNum = Number.isFinite(bn);
    if (aIsNum && bIsNum) {
      if (an < bn) return -1;
      if (an > bn) return 1;
      return 0;
    }

    var as = String(a).toLowerCase();
    var bs = String(b).toLowerCase();
    if (as < bs) return -1;
    if (as > bs) return 1;
    return 0;
  }

  function getClientSortValue(client, field) {
    if (!client || typeof client !== "object") return null;
    if (field === "name") return client.name || client.mac || "";
    if (field === "ip") return client.ip || "";
    if (field === "type") return client.wireless === true ? "wireless" : "wired";
    if (field === "ssid") return client.ssid || "";
    if (field === "quality") return client.wireless === true ? client.signal_level : (client.wired_link_speed || client.rx_rate || 0);
    if (field === "uptime") return client.uptime || 0;
    return null;
  }

  function sortClients(clients) {
    return clients.slice().sort(function (left, right) {
      var a = getClientSortValue(left, sortField);
      var b = getClientSortValue(right, sortField);
      var cmp = cmpValues(a, b);
      return sortDir === "asc" ? cmp : -cmp;
    });
  }

  function wireSpeedText(client) {
    if (!client || client.wireless === true) return "-";

    if (client.wired_link_speed_text !== null && client.wired_link_speed_text !== undefined) {
      var speedText = String(client.wired_link_speed_text).trim();
      if (speedText) {
        if (/[a-zA-Z]/.test(speedText)) {
          return escHtml(speedText);
        }

        var textNumber = Number(speedText);
        if (Number.isFinite(textNumber) && textNumber > 0) {
          return escHtml(textNumber + " Mbps");
        }
      }
    }

    var speed = client.wired_link_speed;
    if (speed !== null && speed !== undefined) {
      var n = Number(speed);
      if (Number.isFinite(n) && n > 0) {
        if (n >= 1000000) return escHtml(fmtRate(n));
        return escHtml(n + " Mbps");
      }
    }

    // Fallback if Omada doesn't expose a dedicated wired speed field.
    var rx = client.rx_rate;
    var tx = client.tx_rate;
    if ((rx !== null && rx !== undefined) || (tx !== null && tx !== undefined)) {
      var rxText = rx !== null && rx !== undefined ? fmtRate(rx) : "-";
      var txText = tx !== null && tx !== undefined ? fmtRate(tx) : "-";
      return escHtml(rxText + " / " + txText);
    }

    return "-";
  }

  function setError(message) {
    var banner = document.getElementById("error-banner");
    if (!banner) return;
    if (!message) {
      banner.classList.add("hidden");
      banner.textContent = "";
      return;
    }
    banner.classList.remove("hidden");
    banner.textContent = message;
  }

  function renderOverview(data, siteOverride, detail) {
    var target = document.getElementById("overview-content");
    if (!target) return;

    var connected = data && data.connected;
    var site = siteOverride || (data && data.selected_site);
    var controller = data && data.controller;
    var deviceTotal = detail && Array.isArray(detail.devices)
      ? detail.devices.length
      : (data && data.device_count_total !== null && data.device_count_total !== undefined ? data.device_count_total : null);
    var clientTotal = detail && Array.isArray(detail.clients)
      ? detail.clients.length
      : (data && data.client_count_total !== null && data.client_count_total !== undefined ? data.client_count_total : null);

    target.innerHTML = [
      row("Status", connected
        ? '<span class="status-badge status-connected">Connected</span>'
        : '<span class="status-badge status-disconnected">Disconnected</span>'),
      row("Active Site", site && site.name ? escHtml(site.name) : "-"),
      row("Scenario", site && site.scenario ? fmt(site.scenario) : "-"),
      row("Region", site && site.region ? fmt(site.region) : "-"),
      row("Controller", controller && controller.base_url ? fmt(controller.base_url) : "-"),
      row("Devices", deviceTotal !== null && deviceTotal !== undefined ? fmt(deviceTotal) : "-"),
      row("Clients", clientTotal !== null && clientTotal !== undefined ? fmt(clientTotal) : "-"),
    ].join("");
  }

  function renderSites(data) {
    var target = document.getElementById("sites-content");
    if (!target) return;

    var sites = data && Array.isArray(data.sites) ? data.sites : [];
    var selectedId = selectedSiteId || (data && data.selected_site ? data.selected_site.site_id : null);
    selectedSiteMap = {};

    if (!sites.length) {
      target.innerHTML = "No sites returned.";
      return;
    }

    var cards = sites.map(function (s) {
      if (s && s.site_id) {
        selectedSiteMap[s.site_id] = s;
      }
      var isActive = s.site_id && s.site_id === selectedId;
      return '<button class="site-card site-select' + (isActive ? " active-site" : "") + '" data-site-id="' + escHtml(s.site_id || "") + '">'
        + '<div class="site-name">' + escHtml(s.name || "–") + (isActive ? '<span class="active-pill">Active</span>' : "") + '</div>'
        + '<div class="site-meta">'
        + (s.scenario ? escHtml(s.scenario) : "")
        + (s.region ? " · " + escHtml(s.region) : "")
        + (s.time_zone ? " · " + escHtml(s.time_zone) : "")
        + '</div>'
        + '</button>';
    });
    target.innerHTML = cards.join("");

    var buttons = target.querySelectorAll(".site-select");
    buttons.forEach(function (btn) {
      btn.addEventListener("click", async function () {
        var siteId = btn.getAttribute("data-site-id");
        if (!siteId || siteId === selectedSiteId) return;
        selectedSiteId = siteId;
        renderSites(currentOverview);
        await loadDetail(siteId);
      });
    });
  }

  function renderDevices(detail) {
    var target = document.getElementById("devices-content");
    var countEl = document.getElementById("device-count");
    if (!target) return;

    var devices = detail && Array.isArray(detail.devices) ? detail.devices : [];
    if (countEl) countEl.textContent = devices.length || "";

    if (!devices.length) {
      target.innerHTML = "<span>No devices returned.</span>";
      return;
    }

    var cards = devices.map(function (d) {
      var name = d.name || d.model || d.mac || "Unknown";
      var ip = d.ip || "-";
      var uptime = fmtUptime(d.uptime);
      var clients = d.clients !== null && d.clients !== undefined ? String(Math.round(d.clients)) + " clients" : "";
      var fw = d.firmware_version || "";
      var dl = d.download !== null && d.download !== undefined ? fmtRate(d.download) : "";
      var ul = d.upload !== null && d.upload !== undefined ? fmtRate(d.upload) : "";

      return '<div class="device-card">'
        + '<div class="device-card-header">'
        + '<span class="device-name">' + escHtml(name) + '</span>'
        + '<span class="device-type">' + deviceTypeLabel(d.type) + '</span>'
        + '</div>'
        + '<div class="device-meta">'
        + statusBadge(d.status)
        + '<span>' + escHtml(ip) + '</span>'
        + (d.model ? '<span>' + escHtml(d.model) + '</span>' : '')
        + (fw ? '<span>FW: ' + escHtml(fw) + '</span>' : '')
        + (uptime !== "-" ? '<span>Up: ' + escHtml(uptime) + '</span>' : '')
        + (clients ? '<span>' + escHtml(clients) + '</span>' : '')
        + (dl || ul ? '<span>↓ ' + escHtml(dl) + ' ↑ ' + escHtml(ul) + '</span>' : '')
        + '</div>'
        + '</div>';
    });

    target.innerHTML = cards.join("");
  }

  function renderClients(detail) {
    var target = document.getElementById("clients-content");
    var countEl = document.getElementById("client-count");
    if (!target) return;

    var clients = detail && Array.isArray(detail.clients) ? detail.clients : [];
    currentClients = clients.slice();
    if (countEl) countEl.textContent = clients.length || "";

    if (!clients.length) {
      target.innerHTML = "No clients returned.";
      return;
    }

    var sortedClients = sortClients(clients);

    var rows = sortedClients.map(function (c) {
      var name = c.name || c.mac || "Unknown";
      var ip = c.ip || "-";
      var isWireless = c.wireless === true;
      var typeIcon = isWireless
        ? '<span class="wifi-icon">📶 WiFi</span>'
        : '<span class="wired-icon">🔌 Wired</span>';
      var ssid = isWireless && c.ssid ? escHtml(c.ssid) : "-";
      var signalOrSpeed = isWireless
        ? (c.signal_level !== null && c.signal_level !== undefined ? signalIcon(c.signal_level) : "-")
        : wireSpeedText(c);
      var uptime = fmtUptime(c.uptime);

      return '<tr>'
        + '<td>' + escHtml(name) + '</td>'
        + '<td>' + escHtml(ip) + '</td>'
        + '<td>' + typeIcon + '</td>'
        + '<td>' + ssid + '</td>'
        + '<td>' + signalOrSpeed + '</td>'
        + '<td>' + escHtml(uptime) + '</td>'
        + '</tr>';
    });

    function sortLabel(base, field) {
      if (sortField !== field) return base;
      return base + (sortDir === "asc" ? " ▲" : " ▼");
    }

    target.innerHTML = '<table class="clients-table">'
      + '<thead><tr>'
      + '<th><button class="sort-btn" data-sort="name">' + sortLabel("Name", "name") + '</button></th>'
      + '<th><button class="sort-btn" data-sort="ip">' + sortLabel("IP", "ip") + '</button></th>'
      + '<th><button class="sort-btn" data-sort="type">' + sortLabel("Type", "type") + '</button></th>'
      + '<th><button class="sort-btn" data-sort="ssid">' + sortLabel("SSID", "ssid") + '</button></th>'
      + '<th><button class="sort-btn" data-sort="quality">' + sortLabel("Signal / Link", "quality") + '</button></th>'
      + '<th><button class="sort-btn" data-sort="uptime">' + sortLabel("Uptime", "uptime") + '</button></th>'
      + '</tr></thead>'
      + '<tbody>' + rows.join("") + '</tbody>'
      + '</table>';

    var sortButtons = target.querySelectorAll(".sort-btn");
    sortButtons.forEach(function (btn) {
      btn.addEventListener("click", function () {
        var field = btn.getAttribute("data-sort");
        if (!field) return;
        if (sortField === field) {
          sortDir = sortDir === "asc" ? "desc" : "asc";
        } else {
          sortField = field;
          sortDir = "asc";
        }
        renderClients({ clients: currentClients });
      });
    });
  }

  async function loadOverview() {
    var params = new URLSearchParams(window.location.search);
    var key = params.get("key");

    try {
      var res = await fetch("/api/omada?key=" + encodeURIComponent(key));
      var data = await res.json();

      if (!res.ok) {
        setError(data && data.error ? data.error : "Failed to fetch Omada overview.");
        return null;
      }

      if (data.error) {
        setError(data.error);
      } else {
        setError("");
      }

      currentOverview = data;
      if (!selectedSiteId && data && data.selected_site && data.selected_site.site_id) {
        selectedSiteId = data.selected_site.site_id;
      }

      renderOverview(data, selectedSiteMap[selectedSiteId], null);
      renderSites(data);

      var updated = document.getElementById("last-updated");
      if (updated) {
        updated.textContent = "Last updated: " + fmtDate(data.fetched_at);
      }

      return data;
    } catch (err) {
      console.error("loadOverview error", err);
      setError("Unable to load Omada overview from server.");
      return null;
    }
  }

  async function loadDetail(siteId) {
    var params = new URLSearchParams(window.location.search);
    var key = params.get("key");
    var detailSiteId = siteId || selectedSiteId;
    var url = "/api/omada/detail?key=" + encodeURIComponent(key);
    if (detailSiteId) {
      url += "&site_id=" + encodeURIComponent(detailSiteId);
    }

    try {
      var res = await fetch(url);
      var data = await res.json();

      if (!res.ok) {
        var errMsg = data && data.error ? data.error : "Failed to fetch Omada device/client detail.";
        document.getElementById("devices-content").innerHTML = "<span>" + escHtml(errMsg) + "</span>";
        document.getElementById("clients-content").innerHTML = "<span>" + escHtml(errMsg) + "</span>";
        return;
      }

      if (data.error) {
        document.getElementById("devices-content").innerHTML = "<span>" + escHtml(data.error) + "</span>";
        document.getElementById("clients-content").innerHTML = "<span>" + escHtml(data.error) + "</span>";
        return;
      }

      if (data.selected_site && data.selected_site.site_id) {
        selectedSiteId = data.selected_site.site_id;
      }

      renderDevices(data);
      renderClients(data);
      if (currentOverview) {
        renderOverview(currentOverview, data.selected_site, data);
        renderSites(currentOverview);
      }
    } catch (err) {
      console.error("loadDetail error", err);
      var msg = "Unable to load device/client data from server.";
      document.getElementById("devices-content").innerHTML = "<span>" + escHtml(msg) + "</span>";
      document.getElementById("clients-content").innerHTML = "<span>" + escHtml(msg) + "</span>";
    }
  }

  async function loadAll() {
    var overview = await loadOverview();
    if (!overview) return;
    await loadDetail(selectedSiteId);
  }

  checkAccessKey();
  syncLinks();
  initDark();

  var refreshBtn = document.getElementById("refresh-btn");
  if (refreshBtn) {
    refreshBtn.addEventListener("click", loadAll);
  }

  loadAll();
  setInterval(loadAll, 60000);
})();
