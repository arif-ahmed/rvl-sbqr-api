#!/usr/bin/env python
"""Safe dynamic probes for the SBQR API VAPT audit (vapt-audit skill).

Non-destructive by design:
- no valid credentials are used or needed
- no writes, no deletes, no exploit payloads, no fuzzing
- bounded request counts (rate-limit probe uses a probe-only client_id)

Output: single JSON document (stdout or --output) with one record per probe:
{id, name, endpoint, request, status, observed, expected, verdict, notes}

Verdicts: PASS | FAIL | INCONCLUSIVE | SKIP
"""

import argparse
import json
import ssl
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from http.client import HTTPResponse

# ---------------------------------------------------------------- constants

PROBE_CLIENT_ID = "vapt-probe"  # never a real client; burns only its own window
MAX_BODY_SNIPPET = 400

SECURITY_HEADERS = [
    "X-Content-Type-Options",
    "X-Frame-Options",
    "Strict-Transport-Security",
    "Content-Security-Policy",
    "Referrer-Policy",
]
FINGERPRINT_HEADERS = ["Server", "X-Powered-By", "X-AspNet-Version", "X-AspNetMvc-Version"]

STACK_MARKERS = ["   at ", "StackTrace", "System.Exception", "<br>", "Exception:"]

# Protected public endpoints (expect 401 without credentials)
PROTECTED_ENDPOINTS = [
    ("POST", "/v1/qr/generate/static"),
    ("POST", "/v1/qr/generate/dynamic"),
    ("POST", "/v1/qr/validate"),
]

ANON_INFRA_ENDPOINTS = [
    ("GET", "/health/live"),
    ("GET", "/health/ready"),
    ("GET", "/openapi/v1.public.json"),
    ("GET", "/openapi/v1.internal-admin.json"),
    ("GET", "/docs/public"),
    ("GET", "/docs/internal-admin"),
    ("GET", "/"),
]


# ---------------------------------------------------------------- http helper

def send(method: str, base_url: str, path: str, *, headers=None, body: bytes | None = None,
         timeout: float = 10.0) -> dict:
    """Perform one request; never raises for HTTP errors. Returns observation."""
    url = base_url.rstrip("/") + path
    req = urllib.request.Request(url, data=body, method=method)
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    req.add_header("User-Agent", "sbqr-vapt-probe/1.0 (safe audit)")
    started = time.monotonic()
    try:
        with urllib.request.urlopen(req, timeout=timeout, context=ssl._create_unverified_context()) as resp:
            return _observed(resp, url, started)
    except urllib.error.HTTPError as e:
        return _observed(e, url, started, http_error=e)
    except Exception as e:  # connection refused, timeout, DNS
        return {
            "url": url,
            "status": None,
            "headers": {},
            "body": "",
            "elapsed_ms": round((time.monotonic() - started) * 1000),
            "transport_error": f"{type(e).__name__}: {e}",
        }


def _observed(resp: HTTPResponse, url: str, started: float, http_error=None) -> dict:
    raw = resp.read(MAX_BODY_SNIPPET + 1) if resp is not None else b""
    body = raw[:MAX_BODY_SNIPPET].decode("utf-8", errors="replace")
    return {
        "url": url,
        "status": resp.status if resp is not None else None,
        "headers": {k: v for k, v in resp.headers.items()} if resp is not None else {},
        "body": body,
        "elapsed_ms": round((time.monotonic() - started) * 1000),
        "transport_error": None,
    }


# ---------------------------------------------------------------- finding helpers

def finding(probe_id: str, name: str, endpoint: str, req_desc: str, obs: dict,
            expected: str, verdict: str, notes: str = "") -> dict:
    rec = {
        "id": probe_id,
        "name": name,
        "endpoint": endpoint,
        "request": req_desc,
        "status": obs.get("status"),
        "observed": {
            "status": obs.get("status"),
            "transport_error": obs.get("transport_error"),
            "elapsed_ms": obs.get("elapsed_ms"),
            "body_snippet": obs.get("body", "")[:200],
        },
        "expected": expected,
        "verdict": verdict,
    }
    if notes:
        rec["notes"] = notes
    return rec


def unreachable(recs: list) -> bool:
    return any(r["observed"].get("transport_error") for r in recs)


# ---------------------------------------------------------------- probes

def probe_anonymous_access(base: str, timeout: float) -> list:
    """P-01: protected endpoints must reject anonymous callers with 401."""
    out = []
    for method, path in PROTECTED_ENDPOINTS:
        obs = send(method, base, path, body=b"{}", timeout=timeout,
                   headers={"Content-Type": "application/json"})
        if obs["transport_error"]:
            out.append(finding("P-01", "anonymous access rejected", path,
                               f"{method} {path} (no auth)", obs, "401", "SKIP", "instance unreachable"))
            continue
        ok = obs["status"] == 401
        out.append(finding("P-01", "anonymous access rejected", path,
                           f"{method} {path} (no auth)", obs, "401",
                           "PASS" if ok else "FAIL",
                           "" if ok else f"got HTTP {obs['status']} — verify no anonymous path exists"))
    return out


def probe_malformed_bearer(base: str, timeout: float) -> list:
    """P-02: malformed/expired/garbage bearer tokens must yield 401, not 500."""
    out = []
    for token in ["garbage", "Bearer ", "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.e30.", "AAAA"]:
        obs = send("POST", base, "/v1/qr/validate", timeout=timeout,
                   headers={"Content-Type": "application/json",
                            "Authorization": f"Bearer {token}"},
                   body=b"{}")
        if obs["transport_error"]:
            out.append(finding("P-02", "malformed bearer handling", "/v1/qr/validate",
                               f"Authorization: Bearer {token!r}", obs, "401", "SKIP", "instance unreachable"))
            continue
        verdict = "PASS" if obs["status"] == 401 else ("FAIL" if obs["status"] >= 500 else "WARN")
        notes = "" if obs["status"] == 401 else f"got HTTP {obs['status']}"
        out.append(finding("P-02", "malformed bearer handling", "/v1/qr/validate",
                           f"Authorization: Bearer {token!r}", obs, "401", verdict, notes))
    return out


def probe_security_headers(base: str, timeout: float) -> list:
    """P-03: security headers present on responses."""
    obs = send("GET", base, "/health/live", timeout=timeout)
    if obs["transport_error"]:
        return [finding("P-03", "security headers", "/health/live", "GET /health/live",
                        obs, ">=1 of the assessed headers", "SKIP", "instance unreachable")]
    present = [h for h in SECURITY_HEADERS if h.lower() in {k.lower() for k in obs["headers"]}]
    missing = [h for h in SECURITY_HEADERS if h not in present]
    verdict = "PASS" if len(present) >= 3 else ("WARN" if present else "FAIL")
    return [finding(
        "P-03", "security headers", "/health/live", "GET /health/live", obs,
        "X-Content-Type-Options, X-Frame-Options, HSTS, CSP, Referrer-Policy",
        verdict, f"present={present or 'none'}; missing={missing}")]


def probe_fingerprint(base: str, timeout: float) -> list:
    """P-04: no technology fingerprint headers."""
    obs = send("GET", base, "/health/live", timeout=timeout)
    if obs["transport_error"]:
        return [finding("P-04", "fingerprint headers", "/health/live", "GET /health/live",
                        obs, "no Server/X-Powered-By leakage", "SKIP", "instance unreachable")]
    lower = {k.lower(): v for k, v in obs["headers"].items()}
    leaked = {h: lower[h.lower()] for h in FINGERPRINT_HEADERS if h.lower() in lower}
    return [finding("P-04", "fingerprint headers", "/health/live", "GET /health/live", obs,
                    "no Server/X-Powered-By leakage",
                    "WARN" if leaked else "PASS",
                    f"leaked={leaked or 'none'}")]


def probe_verb_tampering(base: str, timeout: float) -> list:
    """P-05: unsupported verbs on defined routes -> 405 (or 401), never 200."""
    out = []
    for verb in ["GET", "PUT", "DELETE"]:
        obs = send(verb, base, "/v1/qr/validate", timeout=timeout,
                   headers={"Content-Type": "application/json", "Authorization": "Bearer garbage"})
        if obs["transport_error"]:
            out.append(finding("P-05", "verb tampering", "/v1/qr/validate",
                               f"{verb} /v1/qr/validate", obs, "405 or 401", "SKIP", "instance unreachable"))
            continue
        verdict = "PASS" if obs["status"] in (401, 405) else ("FAIL" if obs["status"] < 400 else "WARN")
        out.append(finding("P-05", "verb tampering", "/v1/qr/validate",
                           f"{verb} /v1/qr/validate", obs, "405 or 401", verdict,
                           f"got HTTP {obs['status']}"))
    return out


def probe_rate_limit(base: str, timeout: float) -> list:
    """P-06: 12 rapid bogus token requests -> expect >=1 HTTP 429.

    Uses PROBE_CLIENT_ID only; partition key is client_id|ip so a real
    client's window is never touched. Historically limit = 10 req/60s.
    """
    statuses = []
    for _ in range(12):
        body = f"grant_type=client_credentials&client_id={PROBE_CLIENT_ID}&client_secret=probe".encode()
        obs = send("POST", base, "/v1/oauth/token", timeout=timeout,
                   headers={"Content-Type": "application/x-www-form-urlencoded"}, body=body)
        if obs["transport_error"]:
            return [finding("P-06", "token endpoint rate limit", "/v1/oauth/token",
                            "12 x POST bogus credentials", obs, ">=1 x HTTP 429", "SKIP",
                            "instance unreachable")]
        statuses.append(obs["status"])
    got_429 = 429 in statuses
    last = obs  # noqa: F821 — defined unless transport error returned early
    return [finding(
        "P-06", "token endpoint rate limit", "/v1/oauth/token",
        f"12 x POST bogus credentials (client_id={PROBE_CLIENT_ID})",
        last, ">=1 x HTTP 429",
        "PASS" if got_429 else "FAIL",
        f"statuses={statuses}")]


def probe_oversized_body(base: str, timeout: float) -> list:
    """P-07: 1 MiB JSON body to token endpoint — record handling (evidence for API4)."""
    pad = {"pad": "A" * (1024 * 1024)}
    obs = send("POST", base, "/v1/oauth/token", timeout=timeout,
               headers={"Content-Type": "application/json"},
               body=json.dumps(pad).encode())
    if obs["transport_error"]:
        return [finding("P-07", "oversized body handling", "/v1/oauth/token",
                        "POST 1 MiB JSON", obs, "413/400 preferred (bounded read)", "SKIP",
                        "instance unreachable")]
    st = obs["status"]
    verdict = "PASS" if st in (400, 413) else ("WARN" if st in (401, 429) else "INCONCLUSIVE")
    return [finding("P-07", "oversized body handling", "/v1/oauth/token", "POST 1 MiB JSON",
                    obs, "413/400 preferred (bounded read)", verdict,
                    f"got HTTP {st} — auth-side short-circuit may mask body handling; "
                    f"file under API4 with Kestrel default-limit evidence")]


def probe_docs_exposure(base: str, timeout: float) -> list:
    """P-08: OpenAPI/Scalar endpoints reachable anonymously; internal doc = finding."""
    out = []
    for method, path in ANON_INFRA_ENDPOINTS:
        obs = send(method, base, path, timeout=timeout)
        if obs["transport_error"]:
            out.append(finding("P-08", "inventory exposure", path, f"{method} {path}",
                               obs, "public doc 200 / internal doc not 200-anonymous", "SKIP",
                               "instance unreachable"))
            continue
        if path.endswith("internal-admin.json") or path == "/docs/internal-admin":
            expected = "401/404 (not anonymous)"
            verdict = "FAIL" if obs["status"] == 200 else "PASS"
        else:
            expected = "200 (known infra endpoint)"
            verdict = "PASS" if obs["status"] in (200, 301, 302) else "WARN"
        out.append(finding("P-08", "inventory exposure", path, f"{method} {path}",
                           obs, expected, verdict, f"got HTTP {obs['status']}"))
    return out


def probe_cors(base: str, timeout: float) -> list:
    """P-09: CORS must not reflect arbitrary origins (browser clients optional).

    Two distinct hostile origins: both must be refused (no wildcard, no
    reflection). Absent ACAO = deny-by-default (fine while browser support
    is optional). An explicit fixed allow-list value is acceptable.
    """
    out = []
    for origin in ["https://vapt-probe.example", "https://evil-vapt-probe.test"]:
        obs = send("OPTIONS", base, "/v1/qr/validate", timeout=timeout,
                   headers={"Origin": origin,
                            "Access-Control-Request-Method": "POST"})
        if obs["transport_error"]:
            out.append(finding("P-09", "CORS policy", "/v1/qr/validate",
                               f"OPTIONS w/ hostile Origin {origin}", obs,
                               "no wildcard/reflected ACAO", "SKIP", "instance unreachable"))
            continue
        acao = next((v for k, v in obs["headers"].items()
                     if k.lower() == "access-control-allow-origin"), None)
        reflected = acao == "*" or acao == origin
        if reflected:
            verdict, notes = "FAIL", f"ACAO={acao!r} — wildcard/reflected origin"
        elif acao is None:
            verdict, notes = "PASS", ("no ACAO — deny-by-default "
                                      "(acceptable while browser support is optional)")
        else:
            verdict, notes = "PASS", f"ACAO={acao!r} — explicit fixed allow-list"
        out.append(finding("P-09", "CORS policy", "/v1/qr/validate",
                           f"OPTIONS w/ hostile Origin {origin}", obs,
                           "no wildcard/reflected ACAO", verdict, notes))
    return out


def probe_correlation_id(base: str, timeout: float) -> list:
    """P-10: client-supplied X-Correlation-Id must not be reflected verbatim."""
    marker = "vapt-probe-should-not-echo-9f8e7d"
    obs = send("GET", base, "/health/live", timeout=timeout,
               headers={"X-Correlation-Id": marker})
    if obs["transport_error"]:
        return [finding("P-10", "correlation id trust", "/health/live",
                        "GET w/ X-Correlation-Id marker", obs, "server mints own id",
                        "SKIP", "instance unreachable")]
    echoed = marker in json.dumps(obs["headers"]) or marker in obs["body"]
    return [finding("P-10", "correlation id trust", "/health/live",
                    "GET w/ X-Correlation-Id marker", obs, "server mints own id",
                    "WARN" if echoed else "PASS",
                    "client value echoed verbatim" if echoed else "not reflected")]


def probe_error_leakage(base: str, timeout: float) -> list:
    """P-11: malformed JSON -> 4xx with clean body (no stack trace)."""
    obs = send("POST", base, "/v1/oauth/token", timeout=timeout,
               headers={"Content-Type": "application/json"}, body=b"{not json")
    if obs["transport_error"]:
        return [finding("P-11", "error detail leakage", "/v1/oauth/token",
                        "POST malformed JSON", obs, "4xx, no stack trace", "SKIP",
                        "instance unreachable")]
    leaked = [m for m in STACK_MARKERS if m in obs["body"]]
    clean = obs["status"] is not None and 400 <= obs["status"] < 500 and not leaked
    return [finding("P-11", "error detail leakage", "/v1/oauth/token", "POST malformed JSON",
                    obs, "4xx, no stack trace", "PASS" if clean else "FAIL",
                    f"leak_markers={leaked or 'none'}")]


def probe_transport_scheme(base_url: str) -> list:
    """P-12: record the scheme actually used (TLS evidence for API8/V14.4)."""
    plain = base_url.lower().startswith("http://")
    return [{
        "id": "P-12", "name": "transport scheme", "endpoint": "(all)",
        "request": f"base-url {base_url}",
        "status": None,
        "observed": {"scheme": base_url.split("://")[0]},
        "expected": "https at the effective edge",
        "verdict": "WARN" if plain else "PASS",
        "notes": ("plain HTTP accepted in-app — TLS may terminate at reverse proxy; "
                  "grade under API8/ASVS V14.4 with charter-deferred annotation"
                  if plain else "https base-url"),
    }]


# ---------------------------------------------------------------- main

def main() -> int:
    ap = argparse.ArgumentParser(description="Safe VAPT probes for SBQR API (localhost only)")
    ap.add_argument("--base-url", default="http://localhost:5001",
                    help="API base URL (default http://localhost:5001)")
    ap.add_argument("--timeout", type=float, default=10.0)
    ap.add_argument("--output", help="write JSON here instead of stdout")
    ap.add_argument("--skip-rate-limit", action="store_true",
                    help="skip P-06 (avoids burning the probe client's window twice)")
    args = ap.parse_args()

    if not any(h in args.base_url for h in ("localhost", "127.0.0.1", "[::1]")):
        print("REFUSING: probes must target localhost only (safety rule).", file=sys.stderr)
        return 2

    results: list[dict] = []
    results += probe_anonymous_access(args.base_url, args.timeout)
    results += probe_malformed_bearer(args.base_url, args.timeout)
    results += probe_security_headers(args.base_url, args.timeout)
    results += probe_fingerprint(args.base_url, args.timeout)
    results += probe_verb_tampering(args.base_url, args.timeout)
    if not args.skip_rate_limit:
        results += probe_rate_limit(args.base_url, args.timeout)
    else:
        results.append({"id": "P-06", "name": "token endpoint rate limit",
                        "endpoint": "/v1/oauth/token", "request": "-", "status": None,
                        "observed": {}, "expected": ">=1 x HTTP 429", "verdict": "SKIP",
                        "notes": "skipped by flag"})
    results += probe_oversized_body(args.base_url, args.timeout)
    results += probe_docs_exposure(args.base_url, args.timeout)
    results += probe_cors(args.base_url, args.timeout)
    results += probe_correlation_id(args.base_url, args.timeout)
    results += probe_error_leakage(args.base_url, args.timeout)
    results += probe_transport_scheme(args.base_url)

    summary = {}
    for r in results:
        summary[r["verdict"]] = summary.get(r["verdict"], 0) + 1

    doc = {
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "base_url": args.base_url,
        "tool": "vapt_probe.py/1.0 (safe, non-destructive)",
        "summary": summary,
        "probes": results,
    }

    text = json.dumps(doc, indent=2)
    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            f.write(text)
        print(f"wrote {len(results)} probe results -> {args.output}")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
