/**
 * Validation on the studio's keystroke path.
 *
 * Not in the sprint's test list, and included because it is the endpoint most
 * likely to be called in anger: the studio issues one per debounced keystroke,
 * so its latency is felt directly and its throughput bounds how many people can
 * type at once. It writes nothing, so the budget is the read budget.
 */
import http from 'k6/http';
import { provision, baseUrl, ok } from './lib/session.js';

export const options = {
  scenarios: {
    validate: {
      executor: 'constant-arrival-rate',
      rate: 100,
      timeUnit: '1s',
      duration: '30s',
      preAllocatedVUs: 30,
      maxVUs: 80,
    },
  },
  thresholds: {
    http_req_failed: ['rate==0'],
    http_req_duration: ['p(95)<100'],
    dropped_iterations: ['count==0'],
  },
};

export function setup() {
  return provision();
}

export default function (data) {
  const response = http.post(
    `${baseUrl()}/v1/apps/${data.appId}/config/validate`,
    JSON.stringify({ config: data.config }),
    {
      headers: {
        Authorization: `Bearer ${data.token}`,
        'Content-Type': 'application/json',
      },
    },
  );

  ok(response, 'validate succeeded');
}
