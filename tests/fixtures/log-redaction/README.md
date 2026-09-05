# Log redaction corpus

Shapes that real build tools print, and what must not survive into the archive.

⚠️ **Every value here is invented.** The point of the corpus is the surrounding
text, which is copied from actual Gradle, apksigner, keytool, and plugin output
— because that surrounding text is what a filter has to recognise, and guessing
at it is how a filter ends up matching nothing it will ever meet.

⚠️ **This file grows.** The filter is a net under a rule, not a proof: the rule
is that secrets are not printed, and this catches the cases where a tool prints
one anyway. Every time something new leaks, the line that leaked it goes in here
first and the pattern goes in second.

The last three cases are the other half of the job. A filter that redacts
everything is useless: a keystore _path_, a compilation error, and a version
string containing the word "key" all have to survive intact, or the log stops
being something a person can debug from.

## Using it

Load cases through `RedactionCorpus` rather than pasting a credential shape into
a test. `RedactionCorpus.Cases` is the whole corpus (the redactor's own theory
runs over it); `RedactionCorpus.Case(name)` picks one out by name, which is how
the pipeline tests get a realistic secret to push through the archive and the
live stream.

⚠️ Keeping the shapes here rather than in source is also what keeps the secret
scanner useful: this one path is allowlisted in `.gitleaks.toml`, and every
`.cs` and `.ts` file in the repository is still scanned. Inline a plausible key
in a source file and the choice becomes tripping the scanner on every commit or
allowlisting a source file — and a source file is where a real credential
actually ends up.
