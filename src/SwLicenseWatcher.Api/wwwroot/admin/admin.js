"use strict";

const TOKEN_KEY = "swlw.adminToken";
const PAGE_SIZE = 50;
const SOFTWARE_CLASSES = ["", "white", "managed", "black", "unclassified"];
const POLICY_CLASSES = ["", "white", "managed", "black"];

const state = {
  tab: "devices",
  skip: 0,
  take: PAGE_SIZE,
  totalCount: 0,
  csvName: "export.csv",
  csvPath: "/api/inventory/devices",
  drawerKind: null,
  drawerKey: ""
};

function $(id) {
  return document.getElementById(id);
}

function field(obj, name) {
  if (obj == null || typeof obj !== "object") {
    return undefined;
  }
  if (name in obj) {
    return obj[name];
  }
  const pascal = name.charAt(0).toUpperCase() + name.slice(1);
  return pascal in obj ? obj[pascal] : undefined;
}

function dash(value) {
  return value == null || value === "" ? "-" : String(value);
}

function formatTime(value) {
  if (value == null || value === "") {
    return "-";
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "-" : date.toLocaleString();
}

function emptyToNull(value) {
  const text = value.trim();
  return text === "" ? null : text;
}

function empty(node) {
  while (node.firstChild) {
    node.removeChild(node.firstChild);
  }
}

function el(tag, props, children) {
  const node = document.createElement(tag);
  if (props) {
    for (const [key, value] of Object.entries(props)) {
      if (value == null) {
        continue;
      }
      if (key === "className") {
        node.className = value;
      } else if (key === "text") {
        node.textContent = value;
      } else if (key === "checked" || key === "disabled" || key === "hidden") {
        node[key] = Boolean(value);
      } else if (key.startsWith("on") && typeof value === "function") {
        node.addEventListener(key.slice(2).toLowerCase(), value);
      } else {
        node.setAttribute(key, String(value));
      }
    }
  }
  if (children) {
    for (const child of children) {
      if (child == null) {
        continue;
      }
      node.appendChild(typeof child === "string" ? document.createTextNode(child) : child);
    }
  }
  return node;
}

function setHidden(node, hidden) {
  node.hidden = hidden;
}

function showError(node, message) {
  if (!message) {
    node.textContent = "";
    setHidden(node, true);
    return;
  }
  node.textContent = message;
  setHidden(node, false);
}

function getToken() {
  return sessionStorage.getItem(TOKEN_KEY) || "";
}

function setToken(token) {
  sessionStorage.setItem(TOKEN_KEY, token);
}

function clearToken() {
  sessionStorage.removeItem(TOKEN_KEY);
}

function fillSelect(select, values, includeAll) {
  empty(select);
  for (const value of values) {
    const option = el("option", { value: value, text: value === "" ? (includeAll ? "전체" : "") : value });
    select.appendChild(option);
  }
}

async function readError(response) {
  if (response.status === 500 || response.status === 503) {
    return "데이터베이스에 연결할 수 없습니다";
  }
  const text = await response.text();
  const trimmed = text.trim();
  if (!trimmed) {
    return "요청에 실패했습니다 (" + response.status + ")";
  }
  try {
    const parsed = JSON.parse(trimmed);
    if (typeof parsed === "string") {
      return parsed;
    }
    const message = field(parsed, "error") || field(parsed, "detail") || field(parsed, "title") || field(parsed, "reason");
    if (message) {
      return String(message);
    }
  } catch {
    /* plain text body */
  }
  return trimmed;
}

async function api(path, opts) {
  const options = opts ? Object.assign({}, opts) : {};
  const headers = Object.assign({}, options.headers);
  const token = getToken();
  if (token) {
    headers.Authorization = "Bearer " + token;
  }
  if (options.body && !headers["Content-Type"]) {
    headers["Content-Type"] = "application/json";
  }
  options.headers = headers;
  let response;
  try {
    response = await fetch(path, options);
  } catch {
    const err = new Error("서버에 연결할 수 없습니다.");
    err.status = 0;
    throw err;
  }
  if (response.status === 401) {
    clearToken();
    showTokenForm("인증에 실패했습니다. 관리자 토큰을 다시 입력하세요.");
    const err = new Error("unauthorized");
    err.status = 401;
    throw err;
  }
  return response;
}

function renderTable(columns, rows) {
  const table = el("table", { className: "data" });
  const thead = el("thead");
  const headRow = el("tr");
  for (const column of columns) {
    headRow.appendChild(el("th", { text: column.header }));
  }
  thead.appendChild(headRow);
  table.appendChild(thead);
  const tbody = el("tbody");
  if (rows.length === 0) {
    const tr = el("tr");
    const td = el("td", { className: "empty", text: "항목이 없습니다." });
    td.colSpan = columns.length;
    tr.appendChild(td);
    tbody.appendChild(tr);
  } else {
    for (const row of rows) {
      const tr = el("tr");
      for (const column of columns) {
        const td = el("td");
        const value = column.value(row);
        if (value instanceof Node) {
          td.appendChild(value);
        } else {
          td.textContent = value == null ? "-" : String(value);
        }
        tr.appendChild(td);
      }
      tbody.appendChild(tr);
    }
  }
  table.appendChild(tbody);
  return table;
}

function linkButton(text, onClick) {
  return el("button", { type: "button", className: "linkish", text: text, onClick: onClick });
}

function updatePager() {
  const from = state.totalCount === 0 ? 0 : state.skip + 1;
  const to = Math.min(state.skip + state.take, state.totalCount);
  $("page-label").textContent = from + "-" + to + " / " + state.totalCount;
  $("prev-btn").disabled = state.skip <= 0;
  $("next-btn").disabled = state.skip + state.take >= state.totalCount;
}

function listQuery(extra) {
  const query = new URLSearchParams();
  query.set("skip", String(state.skip));
  query.set("take", String(state.take));
  const search = $("search-input").value.trim();
  if (search) {
    query.set("search", search);
  }
  if (extra) {
    extra(query);
  }
  return query;
}

function showTokenForm(message) {
  showError($("token-error"), message);
  $("token-input").value = "";
  setHidden($("token-panel"), false);
  setHidden($("app-panel"), true);
  setHidden($("logout-btn"), true);
  closeDrawer();
}

function showApp() {
  showError($("token-error"), "");
  setHidden($("token-panel"), true);
  setHidden($("app-panel"), false);
  setHidden($("logout-btn"), false);
}

function closeDrawer() {
  state.drawerKind = null;
  state.drawerKey = "";
  setHidden($("drawer"), true);
  setHidden($("backdrop"), true);
  empty($("drawer-body"));
  showError($("drawer-error"), "");
}

function openDrawer(title) {
  $("drawer-title").textContent = title;
  setHidden($("drawer"), false);
  setHidden($("backdrop"), false);
}

function applyTabFilters() {
  const tab = state.tab;
  setHidden($("stale-filter-wrap"), tab !== "devices");
  setHidden($("class-filter-wrap"), tab !== "software" && tab !== "policies");
  setHidden($("since-filter-wrap"), tab !== "violations");
  setHidden($("policy-form"), tab !== "policies");
  fillSelect($("class-filter"), tab === "policies" ? POLICY_CLASSES : SOFTWARE_CLASSES, true);
}

async function refreshHealth() {
  const line = $("status-line");
  const origin = window.location.origin;
  try {
    const response = await fetch("/health");
    const data = await response.json();
    const status = field(data, "status") || (response.ok ? "Healthy" : "Unhealthy");
    line.textContent = "API: " + origin + "  ·  /health: " + status;
    line.className = "status " + (status === "Healthy" ? "ok" : "warn");
  } catch {
    line.textContent = "API: " + origin + "  ·  /health: Unhealthy";
    line.className = "status warn";
  }
}

async function downloadCsv() {
  showError($("list-error"), "");
  const query = listQuery(addTabFilters);
  query.set("format", "csv");
  query.delete("skip");
  query.delete("take");
  try {
    const response = await api(state.csvPath + "?" + query.toString());
    if (!response.ok) {
      showError($("list-error"), await readError(response));
      return;
    }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const anchor = el("a", { href: url, download: state.csvName });
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  } catch (err) {
    if (err.status !== 401) {
      showError($("list-error"), err.message || "CSV를 받지 못했습니다.");
    }
  }
}

function addTabFilters(query) {
  if (state.tab === "devices") {
    const stale = $("stale-input").value.trim();
    if (stale) {
      query.set("staleAfterHours", stale);
    }
  }
  if (state.tab === "software" || state.tab === "policies") {
    const classification = $("class-filter").value;
    if (classification) {
      query.set("classification", classification);
    }
  }
  if (state.tab === "violations") {
    const since = $("since-input").value;
    if (since) {
      query.set("since", since);
    }
  }
}

function paintList(host, columns, items, totalCount) {
  empty(host);
  host.appendChild(renderTable(columns, items));
  state.totalCount = totalCount;
  $("list-meta").textContent = "총 " + totalCount + "건";
  updatePager();
}

async function loadList() {
  showError($("list-error"), "");
  $("list-meta").textContent = "불러오는 중...";
  empty($("table-host"));
  try {
    if (state.tab === "devices") {
      await loadDevices();
    } else if (state.tab === "software") {
      await loadSoftware();
    } else if (state.tab === "violations") {
      await loadViolations();
    } else {
      await loadPolicies();
    }
  } catch (err) {
    if (err.status !== 401) {
      showError($("list-error"), err.message || "목록을 불러오지 못했습니다.");
      $("list-meta").textContent = "";
    }
  }
}

async function loadJson(path) {
  const response = await api(path);
  if (!response.ok) {
    throw new Error(await readError(response));
  }
  return response.json();
}

async function loadDevices() {
  state.csvPath = "/api/inventory/devices";
  state.csvName = "devices.csv";
  const data = await loadJson("/api/inventory/devices?" + listQuery(addTabFilters).toString());
  const items = field(data, "items") || [];
  paintList($("table-host"), [
    { header: "자산코드", value: (row) => linkButton(dash(field(row, "deviceCode")), () => openDevice(field(row, "deviceCode"))) },
    { header: "호스트", value: (row) => dash(field(row, "hostName")) },
    { header: "도메인", value: (row) => dash(field(row, "domainName")) },
    { header: "OS", value: (row) => dash(field(row, "operatingSystem")) },
    { header: "에이전트", value: (row) => dash(field(row, "agentVersion")) },
    { header: "heartbeat", value: (row) => formatTime(field(row, "lastHeartbeatUtc")) },
    { header: "inventory", value: (row) => formatTime(field(row, "lastInventoryUtc")) }
  ], items, field(data, "totalCount") || 0);
}

async function loadSoftware() {
  state.csvPath = "/api/inventory/software";
  state.csvName = "software.csv";
  const data = await loadJson("/api/inventory/software?" + listQuery(addTabFilters).toString());
  const items = field(data, "items") || [];
  paintList($("table-host"), [
    { header: "이름", value: (row) => linkButton(dash(field(row, "name")), () => openSoftware(field(row, "name"))) },
    { header: "버전", value: (row) => dash(field(row, "version")) },
    { header: "분류", value: (row) => dash(field(row, "classification")) },
    { header: "PC 수", value: (row) => dash(field(row, "deviceCount")) }
  ], items, field(data, "totalCount") || 0);
}

async function loadViolations() {
  state.csvPath = "/api/violations";
  state.csvName = "violations.csv";
  const data = await loadJson("/api/violations?" + listQuery(addTabFilters).toString());
  const items = field(data, "items") || [];
  paintList($("table-host"), [
    { header: "PC", value: (row) => dash(field(row, "deviceCode")) },
    { header: "호스트", value: (row) => dash(field(row, "hostName")) },
    { header: "소프트웨어", value: (row) => dash(field(row, "softwareName")) },
    { header: "버전", value: (row) => dash(field(row, "softwareVersion")) },
    { header: "정책", value: (row) => dash(field(row, "policyProductName")) },
    { header: "분류", value: (row) => dash(field(row, "classification")) },
    { header: "최초 적발", value: (row) => formatTime(field(row, "detectedAtUtc")) },
    { header: "마지막 발견", value: (row) => formatTime(field(row, "lastSeenAtUtc")) }
  ], items, field(data, "totalCount") || 0);
}

async function loadPolicies() {
  state.csvPath = "/api/policies";
  state.csvName = "policies.csv";
  const data = await loadJson("/api/policies?" + listQuery(addTabFilters).toString());
  const items = field(data, "items") || [];
  paintList($("table-host"), [
    { header: "ID", value: (row) => dash(field(row, "id")) },
    { header: "제품", value: (row) => linkButton(dash(field(row, "productName")), () => fillPolicyForm(row)) },
    { header: "게시자", value: (row) => dash(field(row, "publisher")) },
    { header: "버전 패턴", value: (row) => dash(field(row, "versionPattern")) },
    { header: "분류", value: (row) => dash(field(row, "classification")) },
    { header: "사용", value: (row) => field(row, "enabled") ? "사용" : "중지" },
    { header: "수정 시각", value: (row) => formatTime(field(row, "updatedAtUtc")) }
  ], items, field(data, "totalCount") || 0);
}

function deviceMeta(detail) {
  const wrap = el("div");
  wrap.appendChild(el("p", { text: dash(field(detail, "hostName")) + " / " + dash(field(detail, "domainName")) }));
  wrap.appendChild(el("p", { className: "meta", text: dash(field(detail, "operatingSystem")) + " · 에이전트 " + dash(field(detail, "agentVersion")) }));
  wrap.appendChild(el("p", { className: "meta", text: "heartbeat " + formatTime(field(detail, "lastHeartbeatUtc")) + " · inventory " + formatTime(field(detail, "lastInventoryUtc")) }));
  return wrap;
}

async function openDevice(deviceCode) {
  if (!deviceCode) {
    return;
  }
  state.drawerKind = "device";
  state.drawerKey = String(deviceCode);
  fillSelect($("drawer-class"), SOFTWARE_CLASSES, true);
  setHidden($("drawer-class-wrap"), false);
  openDrawer(String(deviceCode));
  await loadDrawer();
}

async function openSoftware(name) {
  if (!name) {
    return;
  }
  state.drawerKind = "software";
  state.drawerKey = String(name);
  fillSelect($("drawer-class"), SOFTWARE_CLASSES, true);
  setHidden($("drawer-class-wrap"), false);
  openDrawer(String(name));
  await loadDrawer();
}

async function loadDrawer() {
  const body = $("drawer-body");
  empty(body);
  showError($("drawer-error"), "");
  const classification = $("drawer-class").value;
  const classQuery = classification ? "?classification=" + encodeURIComponent(classification) : "";
  try {
    if (state.drawerKind === "device") {
      const detail = await loadJson("/api/inventory/devices/" + encodeURIComponent(state.drawerKey) + classQuery);
      body.appendChild(deviceMeta(detail));
      const software = field(detail, "installedSoftware") || [];
      body.appendChild(renderTable([
        { header: "이름", value: (row) => dash(field(row, "name")) },
        { header: "버전", value: (row) => dash(field(row, "version")) },
        { header: "게시자", value: (row) => dash(field(row, "publisher")) },
        { header: "분류", value: (row) => dash(field(row, "classification")) },
        { header: "범위", value: (row) => dash(field(row, "discoveryScope")) }
      ], software));
    } else if (state.drawerKind === "software") {
      const query = new URLSearchParams();
      query.set("take", "100");
      if (classification) {
        query.set("classification", classification);
      }
      const data = await loadJson("/api/inventory/software/" + encodeURIComponent(state.drawerKey) + "/devices?" + query.toString());
      const items = field(data, "items") || [];
      body.appendChild(el("p", { className: "meta", text: "설치 PC " + (field(data, "totalCount") || items.length) + "대" }));
      body.appendChild(renderTable([
        { header: "자산코드", value: (row) => dash(field(row, "deviceCode")) },
        { header: "호스트", value: (row) => dash(field(row, "hostName")) },
        { header: "OS", value: (row) => dash(field(row, "operatingSystem")) },
        { header: "버전", value: (row) => dash(field(row, "version")) },
        { header: "게시자", value: (row) => dash(field(row, "publisher")) },
        { header: "분류", value: (row) => dash(field(row, "classification")) }
      ], items));
    }
  } catch (err) {
    if (err.status !== 401) {
      showError($("drawer-error"), err.message || "상세를 불러오지 못했습니다.");
    }
  }
}

function fillPolicyForm(row) {
  showError($("policy-form-error"), "");
  if (!row) {
    $("policy-id").value = "";
    $("policy-product").value = "";
    $("policy-publisher").value = "";
    $("policy-version").value = "";
    $("policy-classification").value = "black";
    $("policy-notes").value = "";
    $("policy-enabled").checked = true;
    $("policy-form-title").textContent = "정책 생성";
    $("policy-delete").disabled = true;
    return;
  }
  $("policy-id").value = String(field(row, "id") || "");
  $("policy-product").value = field(row, "productName") || "";
  $("policy-publisher").value = field(row, "publisher") || "";
  $("policy-version").value = field(row, "versionPattern") || "";
  $("policy-classification").value = field(row, "classification") || "managed";
  $("policy-notes").value = field(row, "notes") || "";
  $("policy-enabled").checked = Boolean(field(row, "enabled"));
  $("policy-form-title").textContent = "정책 수정";
  $("policy-delete").disabled = false;
}

async function savePolicy() {
  showError($("policy-form-error"), "");
  const id = $("policy-id").value.trim();
  const body = JSON.stringify({
    productName: $("policy-product").value.trim(),
    publisher: emptyToNull($("policy-publisher").value),
    versionPattern: emptyToNull($("policy-version").value),
    classification: $("policy-classification").value,
    notes: emptyToNull($("policy-notes").value),
    enabled: $("policy-enabled").checked
  });
  try {
    const response = await api(id ? "/api/policies/" + encodeURIComponent(id) : "/api/policies", {
      method: id ? "PUT" : "POST",
      body: body
    });
    if (!response.ok) {
      showError($("policy-form-error"), await readError(response));
      return;
    }
    fillPolicyForm(null);
    await loadList();
  } catch (err) {
    if (err.status !== 401) {
      showError($("policy-form-error"), err.message || "저장하지 못했습니다.");
    }
  }
}

async function deletePolicy() {
  const id = $("policy-id").value.trim();
  if (!id || !window.confirm("이 정책을 삭제할까요?")) {
    return;
  }
  showError($("policy-form-error"), "");
  try {
    const response = await api("/api/policies/" + encodeURIComponent(id), { method: "DELETE" });
    if (!response.ok && response.status !== 204) {
      showError($("policy-form-error"), await readError(response));
      return;
    }
    fillPolicyForm(null);
    await loadList();
  } catch (err) {
    if (err.status !== 401) {
      showError($("policy-form-error"), err.message || "삭제하지 못했습니다.");
    }
  }
}

function selectTab(tab) {
  state.tab = tab;
  state.skip = 0;
  for (const button of document.querySelectorAll("#tabs .tab")) {
    button.classList.toggle("is-active", button.getAttribute("data-tab") === tab);
  }
  applyTabFilters();
  closeDrawer();
  if (tab === "policies") {
    fillPolicyForm(null);
  }
  if (getToken()) {
    loadList();
  }
}

function init() {
  fillSelect($("class-filter"), SOFTWARE_CLASSES, true);
  fillSelect($("drawer-class"), SOFTWARE_CLASSES, true);
  $("status-line").textContent = "API: " + window.location.origin + "  ·  /health: ...";
  refreshHealth();

  $("token-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const token = $("token-input").value.trim();
    if (!token) {
      showError($("token-error"), "토큰을 입력하세요.");
      return;
    }
    setToken(token);
    showApp();
    loadList();
  });
  $("logout-btn").addEventListener("click", () => {
    clearToken();
    showTokenForm("");
  });
  $("tabs").addEventListener("click", (event) => {
    const button = event.target.closest("[data-tab]");
    if (button) {
      selectTab(button.getAttribute("data-tab"));
    }
  });
  $("query-btn").addEventListener("click", () => {
    state.skip = 0;
    loadList();
  });
  $("search-input").addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      state.skip = 0;
      loadList();
    }
  });
  $("csv-btn").addEventListener("click", downloadCsv);
  $("prev-btn").addEventListener("click", () => {
    state.skip = Math.max(0, state.skip - state.take);
    loadList();
  });
  $("next-btn").addEventListener("click", () => {
    state.skip += state.take;
    loadList();
  });
  $("drawer-close").addEventListener("click", closeDrawer);
  $("backdrop").addEventListener("click", closeDrawer);
  $("drawer-class").addEventListener("change", () => {
    if (state.drawerKind) {
      loadDrawer();
    }
  });
  $("policy-save").addEventListener("click", savePolicy);
  $("policy-new").addEventListener("click", () => fillPolicyForm(null));
  $("policy-delete").addEventListener("click", deletePolicy);

  applyTabFilters();
  if (getToken()) {
    showApp();
    loadList();
  } else {
    showTokenForm("");
  }
}

init();
