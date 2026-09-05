/**
 * TC-S06-PRF-001 — reading the current configuration.
 *
 * The studio polls this, so it is the highest-frequency authenticated endpoint
 * in the system and the one whose latency a user feels as "the editor is
 * laggy". The budget is p95 < 100 ms.
 */
import http from 'k6/http';
import { provision, baseUrl, ok } from './lib/session.js';

export const options = {
  scenarios: {
    read: {
      /*
       * ⚠️ A fixed arrival rate, not a fixed number of virtual users.
       *
       * `constant-vus` with no think time is a saturation test wearing a
       * latency test's clothes: each user issues the next request the instant
       * the last one returns, so the offered load is whatever the server
       * happens to allow and the "p95" is the p95 of a queue. The first run of
       * this script did 13,418 requests a second from fifty users, which is not
       * a load any studio generates.
       *
       * Pinning the rate makes the number mean something: this is the latency
       * at 200 requests a second, and `dropped_iterations` says outright if the
       * server could not keep up rather than quietly inflating the percentile.
       */
      executor: 'constant-arrival-rate',
      rate: 200,
      timeUnit: '1s',
      duration: '60s',
      preAllocatedVUs: 50,
      maxVUs: 100,
    },
  },
  thresholds: {
    // ⚠️ A failure rate above zero fails the run outright. A latency number
    // measured while a tenth of the requests were erroring is not a latency
    // number, and reporting one is how a regression gets recorded as a win.
    http_req_failed: ['rate==0'],
    http_req_duration: ['p(95)<100'],
    dropped_iterations: ['count==0'],
  },
};

export function setup() {
  return provision();
}

export default function (data) {
  const response = http.get(`${baseUrl()}/v1/apps/${data.appId}/config`, {
    headers: { Authorization: `Bearer ${data.token}` },
  });

  ok(response, 'config read succeeded');
}
