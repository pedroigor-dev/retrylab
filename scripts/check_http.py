"""Integration smoke checks against a running local instance; creates synthetic rows."""
import concurrent.futures
import json
import time
import urllib.error
import urllib.request

BASE = "http://127.0.0.1:5284"


def post(path, body, extra=None):
    req = urllib.request.Request(
        BASE + path,
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json", **(extra or {})},
    )
    try:
        response = urllib.request.urlopen(req, timeout=20)
    except urllib.error.HTTPError as exc:
        response = exc
    with response:
        raw = response.read()
        return response.status, json.loads(raw) if raw else None


if __name__ == "__main__":
    body = {"reference": "HTTP verification", "amountCents": 8700}
    key = "http-check-" + str(time.time_ns())
    with concurrent.futures.ThreadPoolExecutor(max_workers=12) as pool:
        results = list(pool.map(lambda _: post("/api/operations", body, {"Idempotency-Key": key}), range(12)))
    assert [r[0] for r in results].count(201) == 1
    assert len({r[1]["operation"]["id"] for r in results}) == 1
    assert post("/api/operations", {**body, "amountCents": 8701}, {"Idempotency-Key": key})[0] == 409
    assert post("/api/operations", body)[0] == 400
    assert post("/api/dispatch", {"mode": "Unavailable"}, {"Origin": "https://untrusted.example"})[0] == 403
    assert post("/api/dispatch", {"mode": 999})[0] == 400
    print("PASS: concurrency (1 create, 11 replays), payload conflict, missing key, cross-origin rejection, invalid mode")
