const $ = (id) => document.getElementById(id);
let state = { results: [] },
  polling = false,
  lastSnapshot = "";
function element(tag, text, cls) {
  const e = document.createElement(tag);
  e.textContent = text;
  if (cls) e.className = cls;
  return e;
}
async function api(path, body) {
  const response = await fetch("/api/" + path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : undefined,
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || "Request failed.");
  return data;
}
function notice(text) {
  $("notice").textContent = text;
  $("notice").hidden = !text;
}
function render(s) {
  $("elapsed").textContent = s.started
    ? `${Math.max(0, Math.floor(((s.ended ? Date.parse(s.ended) : Date.now()) - Date.parse(s.started)) / 1000))}s elapsed`
    : "No batch started";
  const snapshot = JSON.stringify(s);
  if (snapshot === lastSnapshot) return;
  lastSnapshot = snapshot;
  state = s;
  $("status").textContent = s.status;
  const busy = ["Running", "Paused", "Stopping"].includes(s.status);
  $("settings").disabled = busy;
  $("start").disabled = busy;
  $("pause").disabled = s.status !== "Running";
  $("resume").disabled = s.status !== "Paused";
  $("stop").disabled = !["Running", "Paused"].includes(s.status);
  for (const key of ["discovered", "pendingCount", "completed", "failed"])
    $(key).textContent = s[key];
  $("statusDetail").textContent =
    s.error ||
    (s.status === "Idle"
      ? "Ready for your next collection"
      : `${s.scanned} folders scanned · ${s.skipped} images skipped`);
  $("elapsed").textContent = s.started
    ? `${Math.max(0, Math.floor(((s.ended ? Date.parse(s.ended) : Date.now()) - Date.parse(s.started)) / 1000))}s elapsed`
    : "No batch started";
  $("workerCount").textContent = s.workers.length
    ? `${s.workers.length} WORKERS`
    : "YOUR PARALLEL TEAM";
  const cards = s.workers.map((w) => {
    const card = element("div", "", "worker"),
      header = element("header", `Worker ${String(w.id).padStart(2, "0")}`);
    header.append(element("span", w.state));
    card.append(
      header,
      element("p", w.file || "Waiting for work"),
      element("small", `${w.completed} images completed`),
    );
    return card;
  });
  $("workerGrid").replaceChildren(
    ...(cards.length
      ? cards
      : [
          element(
            "div",
            "Your workers will appear here when you start a batch.",
            "empty",
          ),
        ]),
  );
  $("queue").replaceChildren(
    ...(s.pending.length
      ? s.pending.map((p) => element("li", p))
      : [element("li", "No images waiting", "empty")]),
  );
  renderResults();
}
function renderResults() {
  const query = $("search").value.toLowerCase();
  const rows = state.results
    .filter((r) => r.file.toLowerCase().includes(query))
    .map((r) => {
      const tr = element("tr", ""),
        name = element("td", "");
      if (r.status === "Done") {
        const button = element("button", r.file);
        button.onclick = () => {
          $("previewTitle").textContent = r.file;
          $("previewImage").src =
            `/api/results/${r.id}/image?batch=${encodeURIComponent(state.started)}`;
          $("preview").showModal();
        };
        name.append(button);
      } else {
        name.textContent = r.file;
        name.title = r.error;
        name.append(element("small", ` — ${r.error}`));
      }
      const status = element("td", "");
      status.append(
        element(
          "span",
          r.status,
          "badge " + (r.status === "Failed" ? "failed" : ""),
        ),
      );
      tr.append(
        name,
        element("td", r.width ? `${r.width} × ${r.height}` : "—"),
        status,
        element("td", `${r.milliseconds}ms`),
      );
      return tr;
    });
  if (!rows.length) {
    const tr = element("tr", ""),
      td = element(
        "td",
        query
          ? "No matching files"
          : "Processed images and errors will appear here.",
        "empty",
      );
    td.colSpan = 4;
    tr.append(td);
    rows.push(tr);
  }
  $("results").replaceChildren(...rows);
}
$("batchForm").onsubmit = async (e) => {
  e.preventDefault();
  notice("");
  $("preview").close();
  try {
    render(
      await api("start", {
        source: $("source").value,
        output: $("output").value,
        width: Number($("width").value),
        height: Number($("height").value),
        workers: Number($("workers").value),
        maintainAspectRatio: $("aspect").checked,
      }),
    );
  } catch (error) {
    notice(error.message);
  }
};
for (const action of ["pause", "resume", "stop"])
  $(action).onclick = async () => {
    try {
      render(await api(action));
      notice("");
    } catch (error) {
      notice(error.message);
    }
  };
$("preset").onchange = () => {
  if ($("preset").value !== "custom") {
    const [w, h] = $("preset").value.split(",");
    $("width").value = w;
    $("height").value = h;
  }
};
for (const id of ["width", "height"])
  $(id).oninput = () => {
    $("preset").value = "custom";
  };
$("search").oninput = renderResults;
$("closePreview").onclick = () => $("preview").close();
async function poll() {
  if (polling) return;
  polling = true;
  try {
    const response = await fetch("/api/state");
    if (!response.ok) throw new Error();
    render(await response.json());
    if ($("notice").dataset.connection) {
      notice("");
      delete $("notice").dataset.connection;
    }
  } catch {
    notice("Cannot reach the local app. Check that the server is running.");
    $("notice").dataset.connection = "true";
  } finally {
    polling = false;
  }
}
poll();
setInterval(poll, 500);
