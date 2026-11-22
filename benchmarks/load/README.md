# Load tests

[`token-requests.js`](token-requests.js) is a [k6](https://k6.io) test of one tenant's token endpoint. It runs three
constant-arrival-rate scenarios together against the endpoint that the tenant's discovery document names:

| Scenario           | Client authentication                                   | Rate            | p95 threshold |
| ------------------ | ------------------------------------------------------- | --------------- | ------------- |
| `generated_secret` | `client_secret_basic` with a generated secret           | half of `RATE`  | 200 ms        |
| `private_key_jwt`  | `private_key_jwt` with an ES256 key                     | half of `RATE`  | 200 ms        |
| `supplied_secret`  | `client_secret_basic` with a secret the tenant imported | `SUPPLIED_RATE` | 1 s           |

Every request is a `client_credentials` grant. Beyond the p95 thresholds, the test fails when more than 0.1% of requests
fail or any check fails, and when k6 drops an iteration because the cell could not keep up with the arrival rate.

No build gate runs the test: it measures a deployed cell, as the micro-benchmarks beside it measure code. It rates a
dedicated cell, whose capacity is 70% of the highest rate that passes every threshold
([ADR-1524](../../docs/decisions/1524-a-dedicated-cells-capacity-rating-is-measured-by-the-repositorys-k6-test-on-dedicated-cpu-nodes.md)).

## Prerequisites

- k6 v2.4.0 ([release](https://github.com/grafana/k6/releases/tag/v2.4.0)).
- A deployed cell and a tenant on it that sets no rate limit, no daily quota and no `admission_capacity_percent`.

## Prepare the tenant

Create three confidential clients on the tenant. Each is allowed the `client_credentials` grant and one scope:

1. A client with `client_secret_basic` and a generated secret. The secret's value is shown once, when you create the
   client.
2. A client with `client_secret_basic` and a secret you import, which the server verifies with PBKDF2.
3. A client with `private_key_jwt` whose public key you import.

This PowerShell makes the key pair for the third client and writes both keys as JWKs to files outside the repository:

```powershell
$ec = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$p = $ec.ExportParameters($true)
function ConvertTo-Base64Url([byte[]] $bytes) { [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') }
$kid = [guid]::NewGuid().ToString('N')
$x = ConvertTo-Base64Url $p.Q.X; $y = ConvertTo-Base64Url $p.Q.Y; $d = ConvertTo-Base64Url $p.D
[ordered]@{ kty = 'EC'; crv = 'P-256'; x = $x; y = $y; kid = $kid; alg = 'ES256'; use = 'sig' } | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $HOME 'load-client.public.jwk.json')
[ordered]@{ kty = 'EC'; crv = 'P-256'; x = $x; y = $y; d = $d; kid = $kid; alg = 'ES256'; use = 'sig' } | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $HOME 'load-client.private.jwk.json')
```

Import `load-client.public.jwk.json` into the third client. The test signs with `load-client.private.jwk.json`, which
holds the private key: delete it after the run.

## Run

The test reads its configuration from environment variables, which `--env` sets:

| Variable                  | Required | Default | Meaning                                                               |
| ------------------------- | -------- | ------- | --------------------------------------------------------------------- |
| `ISSUER`                  | yes      |         | The tenant's issuer, such as `https://<tenant host>`                  |
| `GENERATED_CLIENT_ID`     | yes      |         | The client with the generated secret                                  |
| `GENERATED_CLIENT_SECRET` | yes      |         | That client's secret                                                  |
| `JWT_CLIENT_ID`           | yes      |         | The `private_key_jwt` client                                          |
| `JWT_CLIENT_KEY`          | yes      |         | That client's private EC P-256 JWK, as JSON                           |
| `SUPPLIED_CLIENT_ID`      | yes      |         | The client with the imported secret                                   |
| `SUPPLIED_CLIENT_SECRET`  | yes      |         | That client's secret                                                  |
| `SCOPE`                   | no       | empty   | The scope to request; the request omits `scope` when it is empty      |
| `RATE`                    | no       | `50`    | Requests per second for the two machine-to-machine scenarios together |
| `SUPPLIED_RATE`           | no       | `2`     | Requests per second for the supplied-secret scenario                  |
| `DURATION`                | no       | `10m`   | How long each scenario runs                                           |

```powershell
k6 run --env ISSUER=https://<tenant host> --env GENERATED_CLIENT_ID=<id> --env GENERATED_CLIENT_SECRET=<secret> `
  --env JWT_CLIENT_ID=<id> --env JWT_CLIENT_KEY="$(Get-Content -Raw <private jwk file>)" `
  --env SUPPLIED_CLIENT_ID=<id> --env SUPPLIED_CLIENT_SECRET=<secret> --env RATE=50 benchmarks/load/token-requests.js
```

k6 exits non-zero when a threshold fails.

## Rate a dedicated cell

The rating follows
[ADR-1524](../../docs/decisions/1524-a-dedicated-cells-capacity-rating-is-measured-by-the-repositorys-k6-test-on-dedicated-cpu-nodes.md),
points 2 to 5.

1. Create the cell under test in `us-iad` with the dedicated cell's shape: a tainted node pool of 3 × Akamai Dedicated
   4 GB (2 dedicated vCPUs, 4 GB), three identity replicas whose requests equal their limits, the cell's own Valkey and
   its 3-node managed PostgreSQL. Create the load generator as a separate Dedicated 4 GB Linode in the same region.
2. Deploy the release under test, leave `OpenId:Admission:ConcurrencyLimit` unset, and prepare the tenant as above.
3. Run the test from the load generator with `DURATION=10m` and `RATE=25`, then raise `RATE` in steps of 25 (50, 75, …)
   and run again until a run fails. The highest `RATE` that passes every threshold is the cell's capacity. Note that
   run's p95 for each scenario. The rating is 70% of the capacity, rounded down to a multiple of 5.
4. Raise `SUPPLIED_RATE` from 1 in steps of 1 the same way. The supplied secret's rating is 70% of its highest passing
   rate, rounded down.
5. At the highest passing rate, read the highest `http.server.active_requests` that each replica showed. The cell's
   `OpenId:Admission:ConcurrencyLimit` is the highest of those values, rounded up to a multiple of 10.
6. Record the date, the release, the node and database plans, the k6 version, the highest passing rates with their p95s,
   the ratings and the concurrency limit in §3 of the
   [hosting analysis](../../docs/hosting-analysis.md#3-compute-sizing).
7. Delete the nodes, the load generator and the private key.
