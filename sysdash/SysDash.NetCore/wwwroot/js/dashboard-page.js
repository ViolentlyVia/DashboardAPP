(function initDashboardShell() {
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
    const assets = document.getElementById("assets-link");
    const idrac = document.getElementById("idrac-link");
    if (assets && key) {
      assets.href = "/assets?key=" + encodeURIComponent(key);
    }
    if (idrac && key) {
      idrac.href = "/idrac?key=" + encodeURIComponent(key);
    }
  }

  checkAccessKey();
  syncLinks();
})();
