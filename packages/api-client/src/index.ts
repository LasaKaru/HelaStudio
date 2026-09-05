/**
 * Types for the Shellwright control plane.
 *
 * Everything here is re-exported from a generated file, so this module is the
 * only stable import path — `src/generated/v1.ts` is replaced wholesale on
 * every regeneration and its internal shape is openapi-typescript's business,
 * not ours.
 */
export type { components, operations, paths } from './generated/v1.js';

import type { components, paths } from './generated/v1.js';

/** A JSON response body for a given path and method, when there is one. */
export type Ok<
  Path extends keyof paths,
  Method extends keyof paths[Path],
> = paths[Path][Method] extends {
  responses: { 200: { content: { 'application/json': infer Body } } };
}
  ? Body
  : never;

/**
 * An RFC 9457 problem document, as this API returns them.
 *
 * ⚠️ Branch on `code`, never on `title`. The code and the `type` URI are a
 * contract with a documentation page behind it; the title and detail are
 * written for people and get reworded.
 */
export interface ApiProblem {
  /** Stable identifier, dereferenceable to documentation. */
  readonly type?: string;
  /** Short human-readable summary. Not stable. */
  readonly title?: string;
  /** HTTP status code. */
  readonly status?: number;
  /** Human-readable specifics. Not stable. */
  readonly detail?: string;
  /** The request path. */
  readonly instance?: string;
  /** The catalogue code, such as `API_NOT_FOUND`. Stable. */
  readonly code?: string;
  /** Identifier to quote when reporting the problem. */
  readonly correlationId?: string;
  /** Per-field messages, on a validation failure. */
  readonly errors?: Readonly<Record<string, readonly string[]>>;
}

/** Narrows an unknown response body to a problem document. */
export function isApiProblem(value: unknown): value is ApiProblem {
  return typeof value === 'object' && value !== null && 'title' in value && 'status' in value;
}

/** Configuration diagnostics travel inside the problem, not as its `errors`. */
export type ConfigDiagnostic = components['schemas']['DiagnosticResponse'];
