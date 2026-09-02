/**
 * Shared setup for the load scripts: one account, one organisation, one app.
 *
 * ⚠️ Built once in `setup()` and shared by every virtual user, rather than
 * signed up per user. Registration runs Argon2id at 64 MiB by design, so a
 * hundred virtual users each creating an account would measure the password
 * hasher rather than the endpoint under test — and would measure it under a
 * load pattern no real deployment ever sees.
 */
import http from 'k6/http';
import { check, fail } from 'k6';

const BASE = __ENV.SHELLWRIGHT_BASE_URL || 'http://127.0.0.1:5199';

export function baseUrl() {
  return BASE;
}

/** Registers, signs in, and creates an organisation, workspace, and app. */
export function provision() {
  const email = `load-${Date.now()}-${Math.random().toString(36).slice(2)}@example.test`;
  const password = 'correct horse battery staple';

  post('/v1/auth/register', { email, password });

  const login = post('/v1/auth/login', { email, password });
  if (login.status !== 200) {
    fail(`login failed: ${login.status} ${login.body}`);
  }

  const token = login.json('accessToken');
  const auth = { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' };

  const org = post('/v1/orgs', { name: `Load ${Date.now()}` }, auth);
  const orgId = org.json('id');

  const workspace = post(`/v1/orgs/${orgId}/workspaces`, { name: 'Default' }, auth);
  const workspaceId = workspace.json('id');

  const app = post(
    `/v1/workspaces/${workspaceId}/apps`,
    {
      name: 'Load',
      bundleId: `test.load.a${Date.now()}`,
      initialUrl: 'https://93.184.216.34/',
    },
    auth,
  );

  if (app.status !== 201) {
    fail(`app creation failed: ${app.status} ${app.body}`);
  }

  const appId = app.json('id');
  const config = http.get(`${BASE}/v1/apps/${appId}/config`, { headers: auth });

  return { token, orgId, workspaceId, appId, config: config.json('config') };
}

function post(path, body, headers) {
  return http.post(`${BASE}${path}`, JSON.stringify(body), {
    headers: headers || { 'Content-Type': 'application/json' },
  });
}

export function ok(response, name) {
  return check(response, { [name]: (r) => r.status >= 200 && r.status < 300 });
}
