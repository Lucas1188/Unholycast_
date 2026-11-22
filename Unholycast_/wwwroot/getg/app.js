const statusEl = document.getElementById("status");

const setStatus = (msg) => {
  statusEl.textContent = msg || "";
};

const requestJson = async (url, opts) => {
  const response = await fetch(url, opts);
  const body = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(body.msg || "Request failed");
  }
  return body;
};

let state = { devices: [], groups: [], presets: [] };

async function loadState() {
  state = await requestJson("/dashboard/api/state");
  renderDevices();
  renderGroupEditor();
  renderPresets();
  await loadPlayback();
}

async function loadPlayback() {
  const info = await requestJson("/dashboard/api/playback");
  const root = document.getElementById("playbackInfo");

  if (!info.available) {
    root.textContent = info.message || "Playback unavailable";
    return;
  }

  const leader = info.leader
    ? `${info.leader.name || "unknown"} (${info.leader.sonosIp || info.leader.ip})`
    : "none";
  const current = info.current
    ? `${info.current.title || info.current.videoId} [${info.current.fileStatus}, ${info.current.bytes} bytes]`
    : "none";
  const track = info.track || {};
  const transport = info.transport || {};
  const volume = info.volume || {};

  root.innerHTML = `
    <div><strong>Status:</strong> ${transport.state || "unknown"} / ${transport.status || "unknown"}</div>
    <div><strong>Track:</strong> ${current}</div>
    <div><strong>Sonos metadata:</strong> ${track.title || "none"} (${track.position || 0}s / ${track.duration || 0}s)</div>
    <div><strong>Paired leader:</strong> ${leader}</div>
    <div><strong>Volume:</strong> ${volume.level ?? 0}${volume.muted ? " (muted)" : ""}</div>`;
}

function renderDevices() {
  const root = document.getElementById("devices");
  root.innerHTML = "";

  for (const d of state.devices) {
    const el = document.createElement("div");
    el.className = "device";
    el.innerHTML = `
      <div class="row"><strong>${d.name}</strong> <span class="muted">${d.ip}</span></div>
      <div class="muted">Coordinator: ${d.coordinatorIp}${d.isCoordinator ? " (leader)" : ""}</div>
      <div class="row" style="margin-top:.4rem">
        <input type="range" min="0" max="100" value="${d.volume}" data-ip="${d.ip}" style="flex:1" />
        <span id="v-${d.ip.replaceAll(".", "_")}">${d.volume}</span>
        <label><input type="checkbox" data-mute="${d.ip}" ${d.muted ? "checked" : ""} /> mute</label>
        <button data-set="${d.ip}" type="button">Set</button>
      </div>`;
    root.appendChild(el);
  }

  root.querySelectorAll('input[type="range"]').forEach((range) => {
    range.addEventListener("input", () => {
      const id = `v-${range.dataset.ip.replaceAll(".", "_")}`;
      const valueEl = document.getElementById(id);
      if (valueEl) {
        valueEl.textContent = range.value;
      }
    });
  });

  root.querySelectorAll("button[data-set]").forEach((button) => {
    button.addEventListener("click", async () => {
      const ip = button.dataset.set;
      const slider = root.querySelector(`input[data-ip="${ip}"]`);
      const mute = root.querySelector(`input[data-mute="${ip}"]`);
      await requestJson("/dashboard/api/volume", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ items: [{ ip, level: Number(slider.value), muted: !!mute.checked }] })
      });
      setStatus("Volume updated");
      await loadState();
    });
  });
}

function renderGroupEditor() {
  const leader = document.getElementById("leaderSelect");
  const members = document.getElementById("memberList");

  leader.innerHTML = state.devices
    .map((d) => `<option value="${d.ip}">${d.name} (${d.ip})</option>`)
    .join("");

  members.innerHTML = state.devices
    .map(
      (d) => `
      <label class="row" style="margin:.2rem 0">
        <input type="checkbox" value="${d.ip}" ${d.isCoordinator ? "checked" : ""} />
        <span>${d.name}</span>
        <span class="muted">${d.ip}</span>
      </label>`
    )
    .join("");
}

function renderPresets() {
  const root = document.getElementById("presetList");
  root.innerHTML = "";

  for (const p of state.presets) {
    const el = document.createElement("div");
    el.className = "device";
    el.innerHTML = `
      <div class="row"><strong>${p.name}</strong><span class="muted">${p.devices.length} speakers</span></div>
      <div class="row" style="margin-top:.4rem">
        <button data-apply="${p.name}" class="primary" type="button">Apply</button>
        <button data-reset="${p.name}" type="button">Reset</button>
        <button data-del="${p.name}" type="button">Delete</button>
      </div>`;
    root.appendChild(el);
  }

  root.querySelectorAll("[data-apply]").forEach((button) => {
    button.addEventListener("click", async () => {
      await requestJson("/dashboard/api/presets/apply", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: button.dataset.apply })
      });
      setStatus("Preset applied");
      await loadState();
    });
  });

  root.querySelectorAll("[data-reset]").forEach((button) => {
    button.addEventListener("click", async () => {
      await requestJson("/dashboard/api/presets/reset", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: button.dataset.reset })
      });
      setStatus("Preset reset complete");
      await loadState();
    });
  });

  root.querySelectorAll("[data-del]").forEach((button) => {
    button.addEventListener("click", async () => {
      await requestJson(`/dashboard/api/presets/${encodeURIComponent(button.dataset.del)}`, {
        method: "DELETE"
      });
      setStatus("Preset deleted");
      await loadState();
    });
  });
}

document.getElementById("refreshBtn").addEventListener("click", async () => {
  await loadState();
  setStatus("Discovery refreshed");
});

document.getElementById("applyGroupBtn").addEventListener("click", async () => {
  const coordinatorIp = document.getElementById("leaderSelect").value;
  const checks = [...document.querySelectorAll('#memberList input[type="checkbox"]:checked')];
  const memberIps = checks.map((x) => x.value);

  await requestJson("/dashboard/api/group", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ coordinatorIp, memberIps })
  });
  setStatus("Group assignment requested");
  await loadState();
});

document.getElementById("savePresetBtn").addEventListener("click", async () => {
  const name = document.getElementById("presetName").value.trim();
  if (!name) {
    setStatus("Preset name required");
    return;
  }

  await requestJson("/dashboard/api/presets/save", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ name })
  });
  setStatus("Preset saved");
  await loadState();
});

document.getElementById("pauseBtn").addEventListener("click", async () => {
  await requestJson("/pause");
  setStatus("Paused");
  await loadPlayback();
});

document.getElementById("resumeBtn").addEventListener("click", async () => {
  await requestJson("/resume");
  setStatus("Resumed");
  await loadPlayback();
});

document.getElementById("playbackRefreshBtn").addEventListener("click", async () => {
  await loadPlayback();
  setStatus("Playback refreshed");
});

document.getElementById("seekBtn").addEventListener("click", async () => {
  const pos = document.getElementById("seekPos").value;
  if (pos === "") {
    setStatus("Seek position required");
    return;
  }
  await requestJson(`/seek?pos=${encodeURIComponent(pos)}`);
  setStatus("Seek requested");
  await loadPlayback();
});

document.getElementById("volumeBtn").addEventListener("click", async () => {
  const level = Number(document.getElementById("volumeLevel").value || 0);
  const muted = !!document.getElementById("volumeMute").checked;
  await requestJson("/volume", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ level, muted })
  });
  setStatus("Volume updated");
  await loadPlayback();
});

loadState().catch((err) => setStatus(err.message));
