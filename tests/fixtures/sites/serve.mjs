// Minimal server for the fixture sites. Deliberately dependency-free: these
// sites exist to isolate shell bugs, so they must not introduce any of their
// own. The production copies are served by Cloudflare Pages, with the same
// routes implemented as Pages Functions.
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';

const [, , dir = 'public', port = '4310'] = process.argv;
const root = resolve(process.cwd(), dir);

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
};

const SESSION_COOKIE = 'fixture_session';

/**
 * The auth fixture's endpoints.
 *
 * The session cookie is deliberately HttpOnly: a shell that fails to persist
 * cookies across launches will sign the user out, which is exactly the
 * regression this fixture exists to catch. Credentials are not checked — the
 * cookie is the subject under test, not the login.
 */
function handleApi(url, request, response) {
  const cookies = Object.fromEntries(
    (request.headers.cookie ?? '')
      .split(';')
      .map((part) => part.trim().split('='))
      .filter((pair) => pair.length === 2),
  );

  switch (url.pathname) {
    case '/api/login':
      response.writeHead(303, {
        location: '/protected.html',
        'set-cookie': `${SESSION_COOKIE}=it%40example.com; Path=/; HttpOnly; SameSite=Lax; Max-Age=86400`,
      });
      response.end();
      return true;

    case '/api/logout':
      response.writeHead(303, {
        location: '/',
        'set-cookie': `${SESSION_COOKIE}=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0`,
      });
      response.end();
      return true;

    case '/api/session': {
      const email = cookies[SESSION_COOKIE];
      response.writeHead(200, { 'content-type': TYPES['.json'] });
      response.end(
        JSON.stringify(
          email === undefined
            ? { signedIn: false }
            : { signedIn: true, email: decodeURIComponent(email) },
        ),
      );
      return true;
    }

    // A two-hop redirect chain, standing in for a real provider. Shells that
    // mishandle redirects during navigation fail here rather than in production.
    case '/oauth/start':
      response.writeHead(302, { location: '/oauth/callback?code=fixture-code' }).end();
      return true;

    case '/oauth/callback':
      response.writeHead(303, {
        location: '/protected.html',
        'set-cookie': `${SESSION_COOKIE}=it%40example.com; Path=/; HttpOnly; SameSite=Lax; Max-Age=86400`,
      });
      response.end();
      return true;

    default:
      return false;
  }
}

createServer(async (request, response) => {
  const url = new URL(request.url ?? '/', 'http://localhost');

  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/oauth/')) {
    if (handleApi(url, request, response)) return;
  }

  // Reject traversal before touching the filesystem.
  const relative = normalize(decodeURIComponent(url.pathname)).replace(/^(\.\.[/\\])+/, '');
  const target = join(root, relative.endsWith('/') ? `${relative}index.html` : relative);

  if (!target.startsWith(root)) {
    response.writeHead(403).end('Forbidden');
    return;
  }

  try {
    const body = await readFile(target);
    const type = extname(target) === '' ? TYPES['.json'] : TYPES[extname(target)];
    response.writeHead(200, { 'content-type': type ?? 'application/octet-stream' });
    response.end(body);
  } catch {
    // Unknown paths fall through to index.html so client-side routing works.
    try {
      const fallback = await readFile(join(root, 'index.html'));
      response.writeHead(200, { 'content-type': TYPES['.html'] }).end(fallback);
    } catch {
      response.writeHead(404).end('Not found');
    }
  }
}).listen(Number(port), () => {
  process.stdout.write(`fixture site ${dir} on http://localhost:${port}\n`);
});
