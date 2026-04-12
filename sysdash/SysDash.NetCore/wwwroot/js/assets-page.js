(function initAssetsPage() {
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

  function formatSeconds(s) {
    if (s === null || s === undefined) return "-";
    s = Math.floor(s);
    const days = Math.floor(s / 86400); s %= 86400;
    const hrs = Math.floor(s / 3600); s %= 3600;
    const mins = Math.floor(s / 60); const secs = s % 60;
    let out = "";
    if (days) out += days + "d ";
    if (hrs) out += hrs + "h ";
    out += mins + "m " + secs + "s";
    return out;
  }

  function formatAgo(ts) {
    if (!ts) return "-";
    const diff = Math.floor(Date.now() / 1000 - ts);
    if (diff < 5) return "just now";
    if (diff < 60) return diff + "s ago";
    if (diff < 3600) return Math.floor(diff / 60) + "m ago";
    if (diff < 86400) return Math.floor(diff / 3600) + "h ago";
    return Math.floor(diff / 86400) + "d ago";
  }

  function escHtml(value) {
    return String(value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/\"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  function shortRdpLabel(url) {
    if (!url) return "-";
    if (url.length <= 40) return url;
    return url.slice(0, 37) + "...";
  }

  let currentEditingHostname = null;

  function editAsset(hostname, friendlyName, ip, rdpUrl) {
    currentEditingHostname = hostname;
    document.getElementById("edit-friendly-name").value = friendlyName || "";
    document.getElementById("edit-ip").value = ip || "";
    document.getElementById("edit-rdp-url").value = rdpUrl || "";
    document.getElementById("edit-modal").style.display = "block";
  }

  function closeEditModal() {
    document.getElementById("edit-modal").style.display = "none";
    currentEditingHostname = null;
    document.getElementById("edit-friendly-name").value = "";
    document.getElementById("edit-ip").value = "";
    document.getElementById("edit-rdp-url").value = "";
  }

  async function loadAssets() {
    try {
      const params = new URLSearchParams(window.location.search);
      const key = params.get("key");
      const res = await fetch(`/api/assets?key=${encodeURIComponent(key)}`);
      const assets = await res.json();
      const tbody = document.getElementById("assets-tbody");
      tbody.innerHTML = "";

      if (!assets.length) {
        tbody.innerHTML = '<tr><td colspan="9" class="empty-message">No assets in database</td></tr>';
        return;
      }

      assets.forEach(asset => {
        const row = document.createElement("tr");
        row.innerHTML = `
          <td>${escHtml(asset.friendly_name || "-")}</td>
          <td>${escHtml(asset.hostname || "")}</td>
          <td>${escHtml(asset.ip || "")}</td>
          <td title="${escHtml(asset.rdp_launch_url || "")}">${escHtml(shortRdpLabel(asset.rdp_launch_url || asset.rdp_url || "-"))}</td>
          <td>${formatSeconds(asset.uptime)}</td>
          <td>${formatAgo(asset.last_seen)}</td>
          <td>${asset.cpu_percent === null || asset.cpu_percent === undefined ? "-" : escHtml(asset.cpu_percent + " %")}</td>
          <td>${asset.memory_percent === null || asset.memory_percent === undefined ? "-" : escHtml(asset.memory_percent + " %")}</td>
          <td></td>
        `;

        const actionsCell = row.querySelector("td:last-child");

        const editBtn = document.createElement("button");
        editBtn.className = "edit-button";
        editBtn.textContent = "Edit";
        editBtn.addEventListener("click", () => {
          editAsset(asset.hostname, asset.friendly_name || "", asset.ip || "", asset.rdp_url || "");
        });

        const deleteBtn = document.createElement("button");
        deleteBtn.className = "delete-button";
        deleteBtn.textContent = "Delete";
        deleteBtn.addEventListener("click", () => deleteAsset(asset.hostname));

        actionsCell.appendChild(editBtn);
        actionsCell.appendChild(deleteBtn);
        tbody.appendChild(row);
      });
    } catch (err) {
      console.error("loadAssets error", err);
      const tbody = document.getElementById("assets-tbody");
      tbody.innerHTML = '<tr><td colspan="9" class="empty-message">Error loading assets</td></tr>';
    }
  }

  async function deleteAsset(hostname) {
    if (!confirm(`Are you sure you want to delete "${hostname}"?`)) return;
    try {
      const params = new URLSearchParams(window.location.search);
      const key = params.get("key");
      const res = await fetch(`/api/assets/${hostname}?key=${encodeURIComponent(key)}`, {
        method: "DELETE"
      });
      if (res.ok) {
        loadAssets();
      } else {
        alert("Failed to delete asset");
      }
    } catch (err) {
      console.error("deleteAsset error", err);
      alert("Error deleting asset");
    }
  }

  async function saveAsset() {
    if (!currentEditingHostname) return;

    const friendlyName = document.getElementById("edit-friendly-name").value.trim();
    const ip = document.getElementById("edit-ip").value.trim();
    const rdpUrl = document.getElementById("edit-rdp-url").value.trim();

    if (!ip) {
      alert("IP address is required");
      return;
    }

    try {
      const params = new URLSearchParams(window.location.search);
      const key = params.get("key");
      const res = await fetch(`/api/assets/${currentEditingHostname}?key=${encodeURIComponent(key)}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          friendly_name: friendlyName || null,
          ip: ip,
          rdp_url: rdpUrl || null,
        })
      });

      if (res.ok) {
        closeEditModal();
        loadAssets();
      } else {
        alert("Failed to update asset");
      }
    } catch (err) {
      console.error("saveAsset error", err);
      alert("Error updating asset");
    }
  }

  window.closeEditModal = closeEditModal;
  window.saveAsset = saveAsset;

  window.onclick = function(event) {
    const modal = document.getElementById("edit-modal");
    if (event.target === modal) {
      closeEditModal();
    }
  };

  checkAccessKey();
  syncLinks();
  initDark();
  loadAssets();
  setInterval(loadAssets, 30000);
})();
