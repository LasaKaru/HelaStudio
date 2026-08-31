# Native shells

`shells/android` and `shells/ios` are **separate public repositories**, added
here as git submodules. See ADR 0002 for why: unmetered macOS CI minutes, and
customers being able to read the code that runs on their users' devices.

| Repository       | Sprint | Language                                                        |
| ---------------- | ------ | --------------------------------------------------------------- |
| `shells/android` | 02     | Kotlin, Compose for native surfaces, Views for the WebView host |
| `shells/ios`     | 03     | Swift                                                           |

**This is the product.** Everything else exists to generate and deliver it. The
shells are hand-written first, in Sprints 02 and 03; Sprint 04 teaches the
generator to produce what was built by hand, which is a much safer order than
generating something nobody has ever run.

Budgets, asserted rather than aspirational (`03_TEST_STRATEGY.md` §12):

| Metric                    | Budget   |
| ------------------------- | -------- |
| Cold start to first frame | < 300 ms |
| Interactive               | < 500 ms |
| Base APK, arm64 split     | < 12 MB  |
| Base IPA                  | < 25 MB  |

⚠️ Nothing secret may ever enter these repositories. They are public.
