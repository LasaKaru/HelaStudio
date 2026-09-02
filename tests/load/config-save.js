/**
 * TC-S06-PRF-002 — validating and saving a configuration.
 *
 * The budget is p95 < 400 ms, four times the read budget, because this path
 * runs the whole validator, canonicalises, computes three BLAKE3 digests, and
 * writes inside a transaction.
 *
 * ⚠️ Every virtual user saves the *same* document on purpose. That is the
 * common case in practice — a studio autosaving while somebody thinks — and it
 * is the one the content-addressed unique index turns into a read. Sending a
 * distinct document per iteration would measure a workload no deployment has
 * and would grow the version table without bound during the run.
 */
import http from 'k6/http';
import { provision, baseUrl, ok } from './lib/session.js';

export const options = {
  scenarios: {
    save: {
      // Fixed arrival rate, for the reason set out in config-read.js. Lower
      // than the read rate because saving is a write path and this is the
      // shape of real use, not a saturation probe.
      executor: 'constant-arrival-rate',
      rate: 50,
      timeUnit: '1s',
      duration: '60s',
      preAllocatedVUs: 20,
      maxVUs: 60,
    },
  },
  thresholds: {
    http_req_failed: ['rate==0'],
    http_req_duration: ['p(95)<400'],
    dropped_iterations: ['count==0'],
  },
};

export function setup() {
  return provision();
}

export default function (data) {
  const response = http.post(
    `${baseUrl()}/v1/apps/${data.appId}/config`,
    JSON.stringify({ config: data.config }),
    {
      headers: {
        Authorization: `Bearer ${data.token}`,
        'Content-Type': 'application/json',
      },
    },
  );

  ok(response, 'config save succeeded');
}
