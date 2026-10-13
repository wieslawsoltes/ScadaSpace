# Property search and flat categories

The native Property Editor keeps its compact search field, clear command, and
configured-bound-only toggle. The adjacent options menu adds case sensitivity,
Literal Text / Wildcards / Regular Expression syntax, and Match Anywhere / Match
from Start / Match Exactly. It also provides Expand All Categories, Collapse All
Categories and Reset Search Options. Options are local inspector preferences, not
project properties.

Search tests both the real property name and its precomputed qualified path.
For example, exact `backgroundColor` finds matching props and custom fields;
exact `props.backgroundColor` selects only the props field. Dots in a custom name
are not interpreted as a hierarchy. Bound-only mode combines with every syntax.
Literal/wildcard input trims outer whitespace; regex input preserves whitespace
and escape letter case. In particular, `\d` and `\D` are different patterns even
when Case Sensitive is disabled. Empty text matches every eligible property.

## Search syntax and limits

Literal mode uses ordinal string comparisons. Wildcards recognize `*` (zero or
more UTF-16 code units) and `?` (one unit); other punctuation is literal, including
brackets, backslashes and regex operators. Wildcards do not use recursive
backtracking or construct a regex. For illustration, exact `diode?*Color` finds
`diodeOnColor` and `diodeOffColor` but not `diodeColor`.

Regular Expression uses .NET's culture-invariant nonbacktracking engine. Ordinary
and noncapturing groups, alternation, anchors, character classes, shorthand and
Unicode categories, and `*`, `+`, `?` quantifiers are supported. This deliberately
bounded subset rejects counted repetitions, inline options/comments, lookaround,
backreferences, and other unsupported constructs. Queries are limited to 256
characters, regex groups to 32 levels, each regex subject to 1024 characters, and
regex matching to a 25 ms timeout. Prefix/exact modes anchor the whole expression,
including all alternation branches. Invalid patterns and exceeded budgets show
an explicit local error; no stale or partially filtered result is presented.
There is no silent switch to a different syntax. These are not claims of complete
Java/JavaScript/.NET regex compatibility or identical original search semantics.

## Categories and editing

Each top-level category has a compact neutral header with a disclosure arrow.
Click, Enter or Space toggles it; Left collapses it and Right expands it. Folding
changes row visibility, not controls, document history, or the cached match set.
The count distinguishes visible fields from hidden matches. Collapsing every
category preserves the headers and is not mistaken for an empty search result.

Ordinary category preferences survive selection changes and document refreshes.
Starting or changing a search temporarily reveals matching categories. Categories
can still be folded within that search; clearing the search restores ordinary
preferences. Clear/Escape reset text and bound-only filtering, while retaining
syntax, matching and case options. Reset Search Options restores their defaults.
No preferences are serialized into the portable project. Nested objects/arrays
are not expanded into a property tree by this feature.

The existing color, Boolean, choice and scalar editors remain alive while typing
or folding. They retain guarded edits, Undo/Redo and read-only evaluated bindings.
The reusable `QualifiedEditorFields` map distinguishes duplicate names in props,
style, custom and meta categories; the legacy unqualified map stays available.
Search and category keyboard focus cannot route Delete or arrow keys into canvas
editing. Long scalar-name labels now expose their full names in native tooltips.

## Reuse, performance and validation

`PropertyFilter` remains independent in the Designer package. Its original
`Set(string, bool)` and `Matches(string, bool)` signatures retain literal defaults;
the new overloads select syntax/boundaries/case and accept explicit real names.
Wildcard matching uses a rolling state machine with O(query × input) work, bounded
stack storage and no per-row heap allocation. Regex plans are reused when only
bound-only mode changes. Equivalent updates do not allocate replacement strings.

The Controls package caches row matches separately from expansion state. Folding
and idle value refreshes do not rerun matching or recreate native editors; match
and category snapshots are read-only. This is not full inspector virtualization,
a whole-application allocation/FPS claim, or physical-GPU qualification.

The existing ten filter regressions remain. Twenty-one additional engine tests
cover all modes, explicit names, safety/error recovery, pattern reuse and wildcard
allocation; a separate dynamic-programming oracle checks 31,713 small wildcard
cases. Three new standalone package checks cover wildcard/regex APIs and invalid
patterns. The sixteenth real Uno browser suite defines 30 native assertions for
search options, colliding names, actual edits/Undo, bound values, category and
keyboard behavior, retained controls, desktop/tablet use and full export equality.
Executed results belong to the exact revision in Actions artifacts; definitions
or compilation alone are not evidence that native interaction checks passed.

References: [Perspective Component Properties](https://www.docs.inductiveautomation.com/docs/8.3/ignition-modules/perspective/working-with-perspective-components/perspective-component-properties)
(Search Filter and Property Categories), and [.NET regex options](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-options#nonbacktracking-mode).
Independent implementation and artwork only. Full visual/Gateway parity and the
excluded chart-renderer/path-cache, configurable-popup and nested-property-tree
proposals remain outside this change.
