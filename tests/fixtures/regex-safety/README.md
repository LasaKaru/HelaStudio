# Regex-safety fixtures

The contract between the four implementations of the catastrophic-backtracking
heuristic.

A user's link-rule pattern is checked in the studio (TypeScript), in the API
(C#), and again by each shell before it is run on a phone (Kotlin, Swift). Four
implementations of one judgement, sharing no code. `patterns.json` is what stops
them disagreeing.

Disagreement is not academic. If the studio accepts a pattern the shell then
refuses, a customer's rule silently stops working. If the studio accepts one the
shell _runs_, the app freezes on every navigation.

## Why the shells check at all

The validator rejects these patterns at config time, so in principle none
reaches a device. The shells check anyway, because:

- a config may have been built before the rule existed, and old builds keep running;
- on iOS the check is the **only** defence. Android can interrupt a runaway match
  by counting the reads `java.util.regex` makes against the `CharSequence` it is
  given. `NSRegularExpression` is ICU-backed and never yields during
  backtracking, so nothing can stop it once it starts — not a deadline, not a
  timer, not the block passed to `enumerateMatches`. The pattern has to be
  refused before it is ever run.

That asymmetry is recorded in [`docs/qa/shell-parity.md`](../../../docs/qa/shell-parity.md).

## Shape

`cases` — `{ pattern, verdict, why }`, where `verdict` is one of:

| verdict        | meaning                                                    |
| -------------- | ---------------------------------------------------------- |
| `ok`           | compiles, and the heuristic finds no dangerous nesting     |
| `catastrophic` | compiles, but has a shape that can backtrack exponentially |
| `invalid`      | does not compile                                           |

Every pattern here behaves the same way in all four engines. Where they differ,
the case belongs in that engine's own test file, not here. Two such differences
are known, and both are about _compilability_ rather than about the heuristic:

| pattern | difference                                                                               | why it is safe to leave out                                                  |
| ------- | ---------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| `\p`    | JavaScript outside Unicode mode reads it as a literal `p`; the other three reject it     | the validator tries Unicode mode first, so a user sees the rejection         |
| `""`    | ICU rejects the empty pattern; JavaScript, Java and .NET accept it as a match-everything | the schema's `UrlPattern` sets `minLength: 1`, so it never reaches an engine |

## Adding a case

Add it here **first**, then make all four implementations agree. A `why` is
required: a table of patterns without reasons decays into one nobody dares
change, and the false positive this corpus most exists to prevent
(`^[a-z]+(-[a-z]+)*$`, the ordinary separated-list idiom) is only obviously
wrong once someone has written down why.
