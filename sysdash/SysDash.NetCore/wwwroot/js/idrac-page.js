(function initIdracPage() {
  function checkAccessKey() {
    const params = new URLSearchParams(window.location.search);
    const key = params.get("key");
    if (!key) {
      document.body.innerHTML = '<div style="display:flex;justify-content:center;align-items:center;height:100vh;background:#0f1113;color:#e6e6e6;"><div style="text-align:center;"><h1 style="color:#FF0000;margin:0 0 20px 0;">Access Denied</h1><p style="font-size:16px;margin:0;">Missing access key.</p></div></div>';
      document.body.style.margin = "0";
      document.body.style.padding = "0";
      throw new Error("Access denied");
    }
  }

  function syncLinks() {
    const params = new URLSearchParams(window.location.search);
    const key = params.get("key");
    const back = document.getElementById("back-link");
    if (back && key) {
      back.href = "/?key=" + encodeURIComponent(key);
    }
  }

  function setDark(on) {
    if (on) document.body.classList.add("dark"); else document.body.classList.remove("dark");
    try { localStorage.setItem("sysdash:dark", on ? "1" : "0"); } catch (e) {}
  }

  function initDark() {
    try {
      const stored = localStorage.getItem("sysdash:dark");
      const prefer = stored === null ? true : stored === "1";
      setDark(prefer);
    } catch (e) { setDark(true); }
    const btn = document.getElementById("dark-toggle");
    if (btn) btn.addEventListener("click", () => setDark(!document.body.classList.contains("dark")));
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
    return escHtml(value + (suffix || ""));
  }

  function fmtDate(ts) {
    if (!ts) return "-";
    const d = new Date(Number(ts) * 1000);
    if (Number.isNaN(d.getTime())) return "-";
    return d.toLocaleString();
  }

  function row(label, value) {
    return `<div class="row"><span class="label">${escHtml(label)}</span><span class="value">${value}</span></div>`;
  }

  function setError(message) {
    const banner = document.getElementById("error-banner");
    if (!banner) return;
    if (!message) {
      banner.classList.add("hidden");
      banner.textContent = "";
      return;
    }

    banner.classList.remove("hidden");
    banner.textContent = message;
  }

  function renderSystem(system) {
    const target = document.getElementById("system-content");
    if (!target) return;
    if (!system) {
      target.innerHTML = "No system data returned.";
      return;
    }

    target.innerHTML = [
      row("Name", fmt(system.name)),
      row("Manufacturer", fmt(system.manufacturer)),
      row("Model", fmt(system.model)),
      row("Service Tag", fmt(system.service_tag)),
      row("Power State", fmt(system.power_state)),
      row("Health", fmt(system.health_rollup || system.health)),
      row("BIOS", fmt(system.bios_version)),
      row("Host", fmt(system.hostname))
    ].join("");
  }

  function renderManager(manager) {
    const target = document.getElementById("manager-content");
    if (!target) return;
    if (!manager) {
      target.innerHTML = "No controller data returned.";
      return;
    }

    target.innerHTML = [
      row("Name", fmt(manager.name)),
      row("Model", fmt(manager.model)),
      row("Firmware", fmt(manager.firmware_version)),
      row("Health", fmt(manager.health)),
      row("Date/Time", fmt(manager.date_time))
    ].join("");
  }

  function renderThermal(thermal) {
    const target = document.getElementById("thermal-content");
    if (!target) return;
    if (!thermal) {
      target.innerHTML = "No thermal data returned.";
      return;
    }

    const cpuTemps = Array.isArray(thermal.cpu_temps_c) ? thermal.cpu_temps_c : [];
    const fans = Array.isArray(thermal.fans) ? thermal.fans : [];

    const tempRows = [
      row("CPU Avg", fmt(thermal.cpu_temp_avg_c, " C")),
      row("CPU Sensors", cpuTemps.length ? escHtml(cpuTemps.map(v => v + " C").join(", ")) : "-")
    ];

    const fanRows = fans.slice(0, 8).map(f => {
      const name = f && f.name ? f.name : "Fan";
      const rpm = f && (f.rpm !== null && f.rpm !== undefined) ? `${f.rpm} ${f.units || "RPM"}` : "-";
      const state = f && (f.state || f.health) ? ` (${f.state || f.health})` : "";
      return row(String(name), escHtml(String(rpm + state)));
    });

    target.innerHTML = tempRows.concat(fanRows.length ? fanRows : [row("Fans", "-")]).join("");
  }

  function renderPower(power) {
    const target = document.getElementById("power-content");
    if (!target) return;
    if (!power) {
      target.innerHTML = "No power data returned.";
      return;
    }

    const supplies = Array.isArray(power.power_supplies) ? power.power_supplies : [];
    const rows = supplies.slice(0, 6).map(p => {
      const name = p && (p.name || p.model) ? (p.name || p.model) : "PSU";
      const out = p && (p.last_output_w !== null && p.last_output_w !== undefined) ? `${p.last_output_w} W` : "-";
      const state = p && (p.state || p.health) ? `${p.state || ""} ${p.health || ""}`.trim() : "-";
      return row(String(name), escHtml(`${out} | ${state}`));
    });

    target.innerHTML = rows.length ? rows.join("") : "No power supply details returned.";
  }

  function formatBytes(bytes) {
    const value = Number(bytes);
    if (!Number.isFinite(value) || value <= 0) return "-";
    const units = ["B", "KB", "MB", "GB", "TB", "PB"];
    let unit = 0;
    let n = value;
    while (n >= 1024 && unit < units.length - 1) {
      n /= 1024;
      unit += 1;
    }
    return `${n.toFixed(unit < 3 ? 0 : 1)} ${units[unit]}`;
  }

  function renderDisks(disks) {
    const target = document.getElementById("disks-content");
    if (!target) return;
    const items = Array.isArray(disks) ? disks : [];
    if (!items.length) {
      target.innerHTML = "No disk data returned.";
      return;
    }

    function trimBeforeLocation(details) {
      if (!details || typeof details !== "object" || Array.isArray(details)) {
        return details;
      }

      const keys = Object.keys(details);
      const locationIndex = keys.findIndex(k => k.toLowerCase() === "location");
      if (locationIndex < 0) {
        return details;
      }

      const trimmed = {};
      for (let i = locationIndex; i < keys.length; i += 1) {
        const key = keys[i];
        trimmed[key] = details[key];
      }

      return trimmed;
    }

    const rows = items.slice(0, 24).map((d, index) => {
      const name = d && (d.name || d.model) ? (d.name || d.model) : `Disk ${index + 1}`;
      const type = d && (d.media_type || d.protocol) ? `${d.media_type || ""} ${d.protocol || ""}`.trim() : "-";
      const cap = formatBytes(d ? d.capacity_bytes : null);
      const health = d && (d.health || d.state) ? `${d.health || ""} ${d.state || ""}`.trim() : "-";
      const details = trimBeforeLocation(d && d.all_info ? d.all_info : d);
      const detailsJson = escHtml(JSON.stringify(details, null, 2));
      return `<div class="disk-entry">
        ${row(String(name), escHtml(`${cap} | ${type} | ${health}`))}
        <details class="disk-details">
          <summary>Show all drive fields</summary>
          <pre class="disk-json">${detailsJson}</pre>
        </details>
      </div>`;
    });

    target.innerHTML = rows.join("");
  }

  async function loadIdrac() {
    const params = new URLSearchParams(window.location.search);
    const key = params.get("key");

    try {
      setError("");
      const res = await fetch(`/api/idrac?key=${encodeURIComponent(key)}`);
      const data = await res.json();
      if (!res.ok) {
        setError(data && data.error ? data.error : "Failed to fetch iDRAC data.");
        return;
      }

      if (data.error) {
        setError(data.error);
      }

      renderSystem(data.system);
      renderManager(data.manager);
      renderThermal(data.thermal);
      renderPower(data.power);
      renderDisks(data.disks);

      const updated = document.getElementById("last-updated");
      if (updated) {
        updated.textContent = `Last updated: ${fmtDate(data.fetched_at)}`;
      }
    } catch (err) {
      console.error("loadIdrac error", err);
      setError("Unable to load iDRAC data from server.");
    }
  }

  checkAccessKey();
  syncLinks();
  initDark();

  const refreshBtn = document.getElementById("refresh-btn");
  if (refreshBtn) {
    refreshBtn.addEventListener("click", loadIdrac);
  }

  loadIdrac();
  setInterval(loadIdrac, 30000);
})();
