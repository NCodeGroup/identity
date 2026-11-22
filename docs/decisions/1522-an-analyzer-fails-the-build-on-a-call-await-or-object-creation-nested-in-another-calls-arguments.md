# 1522. An analyzer fails the build on a call, await or object creation nested in another call's arguments

- **Status:** Accepted
- **Date:** 2026-11-21
- **Deciders:** NCode Group

## Context

The project template's Clean Code rule in `.github/instructions/csharp.instructions.md` says "Don't nest a call inside
another call's arguments; hoist it into a named local first". The repository's own analyzer project,
`NCode.Identity.Analyzers`
([ADR-1486](1486-the-repositorys-own-analyzer-project-fails-the-build-on-a-field-an-auto-property-could-replace.md),
[ADR-1496](1496-an-analyzer-reports-an-own-async-method-that-returns-task-or-lacks-a-trailing-cancellation-token.md)),
holds the family's other code conventions. The forces:

- **Review alone does not hold the rule.** A nested call reads as one line and passes review, and no other gate reports
  it, so the code drifts from the convention one call at a time.
- **A named local shows and names each intermediate value.** A debugger shows the value of a local before the call
  that receives it, and the local's name says in the code what the value is.
- **Some places cannot declare a local.** A field or property initializer, a constructor initializer, an attribute
  argument, a parameter default, a catch filter, a pattern `when` clause, a query expression and the body of a lambda
  converted to an `Expression<TDelegate>` have no statement in front of them, so no hoist exists.
- **A hoist must not change what runs.** The arguments of a `[Conditional]` call such as `Debug.Assert` run only where
  the call is kept, and the right operand of `&&`, `||` or `??` may not run at all, so a local declared before the
  statement would run work the program skipped.
- **The rule is for the code that ships.** The tests state an act and its expectation in one statement, and that reads
  as the expectation it asserts.

## Decision

**`NCode.Identity.Analyzers` fails the build with `NCID0006` on a call, an `await` or an object creation nested in
another call's arguments in production code, and nothing suppresses it.**

1. **`NCID0006` reports an expression of three kinds in an argument.** `NestedCallArgumentAnalyzer` (Design category)
   reports an invocation, an `await` or an object creation (`new T(...)`, a target-typed `new(...)` or an anonymous
   object) that is an argument of a method call, a delegate call or an object creation, directly or anywhere inside the
   argument's expression, such as `Use(1 + Make())` or `Use($"{Make()}")`. It reports the outermost such expression of
   the argument, so one hoist fixes the argument: `Use(Wrap(Make()))` reports `Wrap(Make())` and not `Make()` twice
   over. The receiver of a call is never its argument, so `Make().Use(value)` is not reported. Each diagnostic is at the
   expression, and its message names the kind: call, await or object creation. The rule binds the arguments of a
   call, so a call in an initializer's member value, a `with` expression or a cast that is not inside a call's
   arguments is outside it, as the template's rule is. A collection expression passed as an argument is neither a
   call, an `await` nor an object creation, so it is not reported itself, while a call inside it is.
2. **The analyzer does not report what a local cannot hold or what a hoist would change:**
   - a lambda or anonymous-method argument itself, because the rule applies inside its body, as `Run(() => Use(Make()))`
     reports `Make()`;
   - `nameof`, `typeof`, `default`, `sizeof`, literals, locals, parameters and member accesses, which are not calls;
   - a place C# cannot declare a local in: a field or property initializer, a constructor initializer, a
     primary-constructor base argument, an attribute argument, a parameter default, a catch filter, a pattern `when`
     clause and a query expression;
   - a lambda converted to an `Expression<TDelegate>`, whose body is an expression tree that cannot hold a local;
   - the arguments of a call to a `[Conditional]` method, such as `Debug.Assert`, because hoisting them would run them
     where the call is removed.

   An expression-bodied member, a lambda that is not an expression tree and a local function are not exempt: each one
   becomes a block body that declares the local. Generated code is not analyzed.
3. **Production code is bound, tests and benchmarks are not.** `.editorconfig` turns `NCID0006` off under `tests/` and
   `benchmarks/`. `src/` and `hosts/` load the analyzer. `analyzers/` is held to the rule by a test that parses the
   analyzer project's sources, copied to the test output, compiles them and runs `NestedCallArgumentAnalyzer` over the
   compilation, because no build loads an analyzer into its own project.
4. **The diagnostic is an error in the Design category and nothing suppresses it.** No `#pragma`, no
   `[SuppressMessage]` and no `.editorconfig` severity below `tests/` and `benchmarks/` turns it off, because each
   exemption of item 2 is a shape the analyzer already skips and the rest is code that can be hoisted.
5. **A hoist keeps what runs and its order.** The reported expression becomes a `var` local declared directly before
   the statement that holds it, named in camelCase for what it holds, and the call receives the local. An earlier
   argument, or the receiver of the call, that is itself a call or reads state the hoisted expression can change is
   hoisted first, in source order. An expression that may not run, such as the right operand of `&&`, `||` or `??`, a
   branch of `?:`, a `switch` arm or a value a pattern binds, stays behind an `if` whose body declares the local. A
   `while`, `do` or `for` condition is computed in the loop body each time it is tested. A hoist never adds, removes or
   reorders a call.
6. **The tests compile snippets with Roslyn** against `TRUSTED_PLATFORM_ASSEMBLIES`, as the tests of `NCID0001` do, so
   no test downloads a reference assembly.

## Options considered

- **Review only.** Rejected: nothing holds the rule, so it erodes.
- **A warning.** Rejected: the build must keep the rule, as it keeps the others of the analyzer project.
- **Holding tests and benchmarks too.** Rejected: an assertion that names its act in one statement reads as the
  expectation, and tests are out of the rule's scope.
- **Reporting every nested expression rather than the outermost of an argument.** Rejected: one hoist fixes an
  argument, and a diagnostic for each level asks for hoists that the first one makes.
- **An analyzer that reports the outermost nested expression of an argument as an error in production code.** Chosen:
  it holds the rule where the family ships code, skips the places that cannot hold a local and never asks for a hoist
  that changes what runs.

## Consequences

- Every call in production code passes named values, and a debugger shows each intermediate value before the call that
  receives it.
- A hoist out of a short-circuit operand or a loop condition restructures the code into an `if` or a loop body that
  declares the local.
- An exemption added later is a change to the analyzer, its tests and this record, because nothing else suppresses the
  rule.
- A change to the analyzer reaches `src/` and `hosts/`, and its own project through the test that runs it.

## References

- [#867](https://github.com/NCodeGroup/identity/issues/867)
- [ADR-1486](1486-the-repositorys-own-analyzer-project-fails-the-build-on-a-field-an-auto-property-could-replace.md):
  the analyzer project
- [ADR-1496](1496-an-analyzer-reports-an-own-async-method-that-returns-task-or-lacks-a-trailing-cancellation-token.md):
  the analyzer that this one follows in shape
- `analyzers/NCode.Identity.Analyzers/NestedCallArgumentAnalyzer.cs`
