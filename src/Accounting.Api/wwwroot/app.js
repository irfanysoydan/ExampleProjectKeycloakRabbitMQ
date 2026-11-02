"use strict";

const apiUrl = "/api/v1/accounting/invoices";
const redirectUri = `${location.origin}/`;

const $ = (id) => document.getElementById(id);
const form = $("invoice");
const flowSteps = [...$("flow").children];

let config;
let accessToken = null;
let idToken = null;

const scenarios = [
  {
    title: "Send invoice to the queue",
    hint: "Expect 202, then the worker result.",
    expectedStatus: 202,
    needsToken: true,
    invoice: (invoice) => invoice,
  },
  {
    title: "ERP is briefly down",
    hint: "Two retries through the retry queue, then success.",
    expectedStatus: 202,
    needsToken: true,
    invoice: (invoice) => ({ ...invoice, erpSystem: "Demo-Flaky" }),
  },
  {
    title: "ERP stays down",
    hint: "Three attempts, then the dead-letter queue.",
    expectedStatus: 202,
    needsToken: true,
    invoice: (invoice) => ({ ...invoice, erpSystem: "Demo-Down" }),
  },
  {
    title: "Another hotel's invoice",
    hint: "The token's tenant doesn't match. Expect 403.",
    expectedStatus: 403,
    needsToken: true,
    invoice: (invoice) => ({ ...invoice, tenantId: invoice.tenantId === "Hotel_01" ? "Hotel_02" : "Hotel_01" }),
  },
  {
    title: "No token",
    hint: "Expect 401.",
    expectedStatus: 401,
    needsToken: false,
    invoice: (invoice) => invoice,
  },
  {
    title: "Negative amount",
    hint: "Validation fails. Expect 400.",
    expectedStatus: 400,
    needsToken: true,
    invoice: (invoice) => ({ ...invoice, amount: -1 }),
  },
];

for (const scenario of scenarios) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "scenario";
  const title = document.createElement("strong");
  title.textContent = scenario.title;
  const hint = document.createElement("span");
  hint.textContent = scenario.hint;
  button.append(title, hint);
  button.addEventListener("click", () => runScenario(scenario));
  scenario.button = button;
  $("scenarios").append(button);
}

function base64Url(bytes) {
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function randomString() {
  return base64Url(crypto.getRandomValues(new Uint8Array(32)));
}

function decodeClaims(token) {
  const payload = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
  const bytes = Uint8Array.from(atob(payload), (c) => c.charCodeAt(0));
  return JSON.parse(new TextDecoder().decode(bytes));
}

function log(kind, title, detail) {
  $("activity-empty").hidden = true;
  const item = document.createElement("li");
  item.className = kind;
  item.textContent = title;
  if (detail) {
    const small = document.createElement("small");
    small.textContent = detail;
    item.append(small);
  }
  $("activity").prepend(item);
}

function setFlow(completedSteps, nextState) {
  flowSteps.forEach((step, index) => {
    step.className = index < completedSteps ? "ok" : index === completedSteps ? nextState ?? "" : "";
  });
}

async function signIn() {
  const verifier = randomString();
  const state = randomString();
  sessionStorage.setItem("pkce", JSON.stringify({ verifier, state }));

  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier));
  const query = new URLSearchParams({
    client_id: config.clientId,
    redirect_uri: redirectUri,
    response_type: "code",
    scope: "openid",
    state,
    code_challenge: base64Url(new Uint8Array(digest)),
    code_challenge_method: "S256",
  });
  location.assign(`${config.issuer}/protocol/openid-connect/auth?${query}`);
}

function signOut() {
  const query = new URLSearchParams({
    client_id: config.clientId,
    post_logout_redirect_uri: redirectUri,
    id_token_hint: idToken,
  });
  location.assign(`${config.issuer}/protocol/openid-connect/logout?${query}`);
}

async function completeSignIn() {
  const params = new URLSearchParams(location.search);
  const code = params.get("code");
  const error = params.get("error");
  if (!code && !error) {
    return;
  }

  history.replaceState(null, "", location.pathname);
  const saved = JSON.parse(sessionStorage.getItem("pkce") ?? "null");
  sessionStorage.removeItem("pkce");

  if (error) {
    log("bad", "Keycloak sign-in failed", error);
    return;
  }
  if (saved?.state !== params.get("state")) {
    log("bad", "Sign-in rejected", "The state value did not match this browser session.");
    return;
  }

  const response = await fetch(`${config.issuer}/protocol/openid-connect/token`, {
    method: "POST",
    body: new URLSearchParams({
      grant_type: "authorization_code",
      client_id: config.clientId,
      redirect_uri: redirectUri,
      code,
      code_verifier: saved.verifier,
    }),
  });
  if (!response.ok) {
    log("bad", "Could not get a token from Keycloak", `HTTP ${response.status}`);
    return;
  }

  const tokens = await response.json();
  accessToken = tokens.access_token;
  idToken = tokens.id_token;
  log("ok", "Signed in with Keycloak", "Authorization code flow with PKCE.");
  setFlow(1);
}

function renderSession() {
  const signedIn = accessToken !== null;
  $("login").hidden = signedIn;
  $("logout").hidden = !signedIn;
  $("claims").hidden = !signedIn;
  $("demo-users").hidden = signedIn;
  $("tenant-badge").hidden = !signedIn;
  $("claims").replaceChildren();

  if (!signedIn) {
    return;
  }

  const claims = decodeClaims(accessToken);
  const appRoles = [claims.roles ?? []].flat().filter((role) => role === "invoice-submitter");
  $("session-status").textContent = `Signed in as ${claims.preferred_username}. From the access token:`;
  $("tenant-badge").textContent = claims.tenant_id ?? "No hotel";
  form.elements.tenantId.value = claims.tenant_id ?? "";

  const rows = [
    ["Tenant", claims.tenant_id ?? "none"],
    ["Role", appRoles.join(", ") || "none"],
    ["Audience", [claims.aud].flat().join(", ")],
    ["Issuer", claims.iss],
    ["Expires", new Date(claims.exp * 1000).toLocaleTimeString()],
  ];
  for (const [name, value] of rows) {
    const term = document.createElement("dt");
    term.textContent = name;
    const definition = document.createElement("dd");
    definition.textContent = value;
    $("claims").append(term, definition);
  }
}

function setBusy(busy) {
  for (const scenario of scenarios) {
    scenario.button.disabled = busy || (scenario.needsToken && accessToken === null);
  }
}

function readInvoice() {
  const values = Object.fromEntries(new FormData(form));
  return { ...values, amount: Number(values.amount) };
}

function describeProblem(status, body) {
  if (body?.errors) {
    return Object.values(body.errors).flat().join(" ");
  }
  if (body?.title) {
    return body.title;
  }
  return status === 401 ? "The API requires a valid Keycloak token." : "This account doesn't have the invoice-submitter role.";
}

async function postInvoice(invoice, useToken) {
  const headers = { "Content-Type": "application/json" };
  if (useToken) {
    headers.Authorization = `Bearer ${accessToken}`;
  }
  const response = await fetch(`${apiUrl}/sync`, { method: "POST", headers, body: JSON.stringify(invoice) });
  const text = await response.text();
  return { status: response.status, body: text ? JSON.parse(text) : null };
}

async function followWorker(eventId, invoiceId) {
  log("wait", "Waiting for the worker", `Event ${eventId}`);
  setFlow(3, "wait");

  let lastSeen = null;
  for (let poll = 0; poll < 60; poll++) {
    await new Promise((resolve) => setTimeout(resolve, 500));
    const response = await fetch(`${apiUrl}/events/${eventId}/status`, {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    if (!response.ok) {
      continue;
    }

    const status = await response.json();
    const seen = `${status.status}:${status.attempt}`;
    if (status.status === "Pending" || seen === lastSeen) {
      continue;
    }
    lastSeen = seen;

    if (status.status === "Retrying") {
      log("wait", `Attempt ${status.attempt} failed, retrying`, "Waiting 5 seconds in accounting.invoice-sync.retry.");
      setFlow(4, "wait");
    } else if (status.status === "Synchronized") {
      log("ok", `Invoice ${invoiceId} synchronized`, `Succeeded on attempt ${status.attempt}.`);
      setFlow(5);
      return;
    } else {
      log("bad", `Invoice ${invoiceId} dead-lettered`, `Failed ${status.attempt} times. Now in accounting.invoice-sync.dead.`);
      setFlow(4, "bad");
      return;
    }
  }
  log("bad", "No worker result", "Check the worker: docker compose logs worker");
}

async function runScenario(scenario) {
  if (scenario.needsToken && decodeClaims(accessToken).exp * 1000 <= Date.now()) {
    accessToken = null;
    renderSession();
    setBusy(false);
    log("bad", "Session expired", "Sign in again to continue.");
    return;
  }
  if (!form.reportValidity()) {
    return;
  }

  setBusy(true);
  try {
    const { status, body } = await postInvoice(scenario.invoice(readInvoice()), scenario.needsToken);
    const detail = status === 202 ? "Published to RabbitMQ and confirmed." : describeProblem(status, body);
    log(status === scenario.expectedStatus ? "ok" : "bad", `${scenario.title}: HTTP ${status}`, detail);

    if (status === 202) {
      await followWorker(body.eventId, body.invoiceId);
    } else if (status === 503) {
      setFlow(2, "bad");
    } else {
      setFlow(scenario.needsToken ? 1 : 0, "bad");
    }
  } catch (error) {
    log("bad", `${scenario.title}: request failed`, error.message);
  } finally {
    setBusy(false);
  }
}

$("login").addEventListener("click", signIn);
$("logout").addEventListener("click", signOut);

async function start() {
  setBusy(true);
  try {
    config = await (await fetch("/demo/config")).json();
    await completeSignIn();
  } catch (error) {
    log("bad", "Could not reach Keycloak", error.message);
  }
  renderSession();
  setBusy(false);
}

start();
