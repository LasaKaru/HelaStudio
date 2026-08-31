/**
 * The studio's first real screen: paste a configuration, see what is wrong with it.
 *
 * Small on purpose. Sprint 11 replaces it with the visual editors; what it proves
 * today is the thing worth proving early — that the validation engine runs
 * unchanged in the browser, on every keystroke, fast enough to feel instant.
 */
import { useDeferredValue, useMemo, useState } from 'react';
import { computeHashes, validate, type Diagnostic } from '@shellwright/config-schema';

const STARTER = `{
  "$schema": "https://schema.shellwright.dev/appconfig/v1.json",
  "schemaVersion": 1,
  "app": {
    "name": "Acme",
    "bundleId": "com.acme.app",
    "initialUrl": "https://app.acme.com/",
    "allowedOrigins": ["https://app.acme.com"]
  },
  "navigation": {
    "tabBar": {
      "enabled": true,
      "items": [
        { "id": "home", "label": "Home", "icon": "home", "url": "/" },
        { "id": "orders", "label": "Orders", "icon": "package", "url": "/orders" }
      ]
    }
  },
  "linkRules": [
    { "id": "app", "pattern": "^https://app\\\\.acme\\\\.com", "action": "internal" },
    { "id": "fallback", "pattern": ".*", "action": "externalBrowser" }
  ]
}`;

interface Report {
  readonly parseError: string | null;
  readonly diagnostics: readonly Diagnostic[];
  readonly hashes: {
    readonly codeKey: string;
    readonly assetKey: string;
    readonly contentKey: string;
  } | null;
  readonly elapsedMs: number;
}

function check(source: string): Report {
  let parsed: unknown;
  try {
    parsed = JSON.parse(source);
  } catch (error) {
    return {
      parseError: (error as Error).message,
      diagnostics: [],
      hashes: null,
      elapsedMs: 0,
    };
  }

  const started = performance.now();
  const { result, resolved } = validate(parsed as never);
  const elapsedMs = performance.now() - started;

  return {
    parseError: null,
    diagnostics: [...result.errors, ...result.warnings, ...result.info],
    hashes: result.valid ? computeHashes(resolved, { shellVersion: '1.0.0' }) : null,
    elapsedMs,
  };
}

export function ConfigChecker(): React.JSX.Element {
  const [source, setSource] = useState(STARTER);
  // Keeps typing responsive: the editor updates immediately, the report catches up.
  const deferred = useDeferredValue(source);
  const report = useMemo(() => check(deferred), [deferred]);

  return (
    <main className="app">
      <header>
        <h1>Shellwright Studio</h1>
        <p className="lede">
          Paste an <code>appconfig.json</code> to see what a store reviewer would object to.
        </p>
      </header>

      <div className="split">
        <label className="editor">
          <span className="visually-hidden">Configuration JSON</span>
          <textarea
            spellCheck={false}
            value={source}
            onChange={(event) => {
              setSource(event.target.value);
            }}
          />
        </label>

        <section className="report" aria-live="polite">
          {report.parseError !== null ? (
            <p className="diagnostic error">
              <strong>That is not valid JSON.</strong> {report.parseError}
            </p>
          ) : (
            <Findings report={report} />
          )}
        </section>
      </div>
    </main>
  );
}

function Findings({ report }: { readonly report: Report }): React.JSX.Element {
  const { diagnostics, hashes, elapsedMs } = report;

  return (
    <>
      <p className="timing">
        Checked in {elapsedMs.toFixed(1)} ms — {diagnostics.length}{' '}
        {diagnostics.length === 1 ? 'finding' : 'findings'}
      </p>

      {diagnostics.length === 0 && (
        <p className="diagnostic ok">Nothing to fix. This configuration is ready to build.</p>
      )}

      <ul className="diagnostics">
        {diagnostics.map((d) => (
          <li key={`${d.code}${d.path}`} className={`diagnostic ${d.severity}`}>
            <code className="path">{d.path === '' ? '(whole config)' : d.path}</code>
            <p>{d.message}</p>
            <a href={d.docsUrl} rel="noreferrer" target="_blank">
              {d.code}
            </a>
          </li>
        ))}
      </ul>

      {hashes !== null && (
        <dl className="hashes">
          <dt>Code key</dt>
          <dd title="Changing this forces a full native rebuild">{hashes.codeKey.slice(0, 16)}</dd>
          <dt>Asset key</dt>
          <dd title="Changing this only repackages resources">{hashes.assetKey.slice(0, 16)}</dd>
          <dt>Content key</dt>
          <dd title="Changing this only patches the embedded config">
            {hashes.contentKey.slice(0, 16)}
          </dd>
        </dl>
      )}
    </>
  );
}
