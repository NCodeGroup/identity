# 1518. A long-lived OpenID object owns a concurrent property bag, and an operation layers a child over its owner's

- **Status:** Accepted
- **Date:** 2026-11-21
- **Deciders:** NCode Group

## Context

Every OpenID object carries an `IPropertyBag` from NCode.PropertyBag, which a consumer or a sibling package fills with
information of its own: the environment, the platform, each team, each tenant, each `OpenIdContext`, each
`OpenIdClient` and each JWT validation. The environment, the platform, the teams and the tenants are long-lived: one
instance serves every request a replica handles, from many threads at once, and the team and tenant caches share them.
A context, a client and a validation serve one operation.

NCode.PropertyBag offers `PropertyBagDictionary`, which no thread may write while another reads it;
`ConcurrentPropertyBagDictionary`, whose every member is atomic under one lock and whose `Clone()` returns another
concurrent bag; and `CreateChild()`, a `LayeredPropertyBag` that reads through to its parent, keeps its own writes and
removals in a plain dictionary, never writes the parent and copies nothing. `Clone()` copies every value and
deep-clones each `ICloneable` one.

A factory also takes a caller's bag for the operation it runs, such as a cache lookup or a seed. A long-lived object
built from that bag keeps a value one operation wrote for every later one.

## Decision

**A long-lived OpenID object owns a `ConcurrentPropertyBagDictionary` cloned from its parent object's bag when it is
created, and every operation, and every object that serves one operation, uses a child bag that `CreateChild()` layers
over its owner's bag.**

1. **The environment's bag is concurrent.** `DefaultOpenIdEnvironment.PropertyBag` is a
   `ConcurrentPropertyBagDictionary`, so every clone down the chain is one too.
2. **Each long-lived object clones its parent's bag.** The platform clones the environment's bag, a top-level team
   clones the platform's, a nested team clones its parent team's, and a tenant clones its team's, the chain their
   settings merge through. The clone is taken once, when the object is created, so a later write to a parent reaches
   the objects created after it.
3. **An operation layers a child bag.** The platform factory and the tenant factory run their own lookups, seeds and
   cache calls on a child of the bag they are given, and never store that bag in the object they create. An
   `OpenIdContext` layers a child over its tenant's bag, an `OpenIdClient` over its context's, and `DefaultJwtValidator`
   over the `JwtValidationParameters` bag, which the validation's context and result carry.
4. **An operation with nothing to inherit starts from `PropertyBagFactory.Create()`**, such as a cache eviction or a
   team-chain walk.

## Options considered

- **Clone at every level.** Rejected: each request copies its tenant's whole bag for its context and again for each
  client, and each copy of a `ConcurrentPropertyBagDictionary` takes its lock.
- **Layer at every level.** Rejected: a `LayeredPropertyBag` keeps its own writes in a plain dictionary, so a
  long-lived object that many threads share could not take a write.
- **Build each long-lived object from the caller's operation bag.** Rejected: a value one operation wrote would stay
  with a cached team or tenant and reach every later request.
- **Long-lived objects own concurrent clones, and operations layer children (chosen).**

## Consequences

- A context, a client or a validation reads every value its owners hold, and its writes stay with it.
- A child bag reads its parent's values by reference, so a mutable value a long-lived object holds is shared with
  every operation that reads it; an operation that needs its own copy sets one in its child bag.
- `IOpenIdTeamFactory.CreateTeamAsync` and `DefaultOpenIdTenantFactory.CreateOpenIdTenantAsync` take the operation's
  bag, as every seam of the factories does, so an override can read it; the default implementations store none of
  it, and give the team or tenant a clone of its parent's bag.

## References

- `DefaultOpenIdEnvironment`, `DefaultOpenIdPlatformFactory`, `DefaultOpenIdTeamFactory`, `DefaultOpenIdTenantFactory`,
  `DefaultOpenIdContextFactory`, `DefaultOpenIdClientFactory` and `DefaultJwtValidator`
- [NCode.PropertyBag](https://github.com/NCodeGroup/PropertyBag) 2.1: `ConcurrentPropertyBagDictionary`,
  `LayeredPropertyBag` and `CreateChild()`
- [ADR-1036](1036-one-factory-materializes-every-openid-context-for-a-request-an-addressed-tenant-or-a-job.md): one
  factory materializes every OpenID context
