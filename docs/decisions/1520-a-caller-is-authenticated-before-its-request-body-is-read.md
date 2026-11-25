# 1520. A caller is authenticated before its request body is read

- **Status:** Accepted
- **Date:** 2026-11-21
- **Deciders:** NCode Group

## Context

Minimal APIs bind a route's parameters, its body included, inside the endpoint's request delegate, so the binding
finishes before any endpoint filter runs. An API that authenticates its caller in an endpoint filter therefore reads the
request's body for an anonymous request too. The forces:

- **A body can be large.** Kestrel's default request body limit is 30 MB, and `POST /api/tenant-imports` raises its own
  to 64 MiB plus 64 KiB for an encrypted tenant export
  ([ADR-1514](1514-a-tenant-moves-between-cells-of-one-region-through-an-encrypted-export-and-import.md)). An anonymous
  request that sends such a body costs the server the body, however little the request is worth.
- **The management API authenticates inside the request environment.** The management access token is validated against
  the keys and the issuer of the control-plane tenant, which are known only once the request's OpenID environment is
  materialized for that tenant
  ([ADR-1044](1044-the-management-api-authenticates-its-own-access-tokens-and-a-configured-client-bootstraps-the-first-globaladmin.md),
  [ADR-1191](1191-a-management-request-is-authenticated-by-the-control-plane-tenant-and-scoped-by-its-routes-tenant.md)).
- **The directory and control-plane authority APIs take bearer tokens.** The directory API serves the cells' machine
  clients, and the control-plane authority API serves the cells that ask the control-plane cell for a caller's authority
  ([ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md)).
  Both read a JSON body, so both pay for one before they know who asks.
- **Authorization depends on the route.** A management route authorizes at the node its route names
  ([ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md)),
  which a handler reads from the route's parameters, so most routes cannot authorize before those parameters bind.

## Decision

**An API that authenticates its caller does so in an `IEndpointGate`, which runs before minimal APIs bind the endpoint's
parameters. An endpoint filter does only the work that follows binding.**

1. **`IEndpointGate` and `AddEndpointGate<TGate>()` are the mechanism.** `IEndpointGate`
   (`NCode.Identity.AspNetCore.Abstractions`) decides before an endpoint reads its request whether the endpoint runs:
   `InvokeAsync(httpContext, cancellationToken)` returns the `IResult` that answers the request, or `null` to let the
   endpoint run. `AddEndpointGate<TGate>()` (`NCode.Identity.AspNetCore`) adds a gate to an endpoint or a route group.
   Each call adds endpoint metadata and one `Finally` convention that wraps the final request delegate once with every
   gate in metadata order, so a group's gates run before a route's and every gate runs before minimal APIs bind. A gate
   is created for each request from `HttpContext.RequestServices` with `ActivatorUtilities`, as `AddEndpointFilter<T>`
   creates a filter. A caller gives the builder type and the gate type, as `AddEndpointFilter<TBuilder, TFilterType>()`
   is called: `group.AddEndpointGate<RouteGroupBuilder, ManagementCallerGate>()`.
2. **`ManagementCallerGate` authenticates every management request.** It runs for the `/api` group before a route binds.
   It materializes the control-plane environment, validates the management access token as ADR-1044 decides, answers
   `ManagementProblems.AuthenticationRequired(HttpContext)` (`401`, with a `WWW-Authenticate` challenge for each of the
   schemes `Bearer` and `DPoP`) to a request without a valid token and `503` while the validator is unavailable, and
   publishes `HttpContext.User`, `IManagementCallerFeature` and `ManagementActorFeature`. Every management route needs
   an authenticated caller, so a request without one is answered `401` whatever its body.
3. **`ManagementFailureEndpointFilter` keeps the work that follows binding.** It is the outermost endpoint filter. It
   stops a request whose binding failed, answers an `HttpResultException` with its result and answers a
   `DirectoryUnavailableException` with the `503` of `ControlPlaneUnavailableProblem`. A binding failure of an
   authenticated caller stays `400`, `413` or `415`.
4. **`TenantImportAuthorizationGate` authorizes the tenant import before its body binds.** `POST /api/tenant-imports`
   authorizes `Operations.Update` at the platform node, which depends on nothing in the body, and its body may reach 64
   MiB plus 64 KiB, so its route adds the gate after the caller gate and a caller without authority is answered `403`,
   or `503` while the control plane that holds the platform roles is unavailable, before the body is read. No other
   route authorizes before binding: each reads the node its route names from its bound parameters and authorizes in its
   handler.
5. **Every `/api` route reads a body of at most 1 MiB.** `ManagementRootGroup.MaxRequestBodyBytes` bounds the request
   body of the `/api` group's routes at 1 MiB, far above the largest ordinary management document. A route that needs
   more raises its limit with a `RequestSizeLimitAttribute` of its own, as the tenant import does, because an endpoint
   applies the last request size limit in its metadata and a group's conventions add theirs before a route's. So an
   authenticated caller without authority makes a cell read at most that much.
6. **`DirectoryAuthenticationGate` authenticates the directory API.** The directory group validates the bearer access
   token against the directory's own issuer, audience and scope, answers `401`, and publishes the calling client for its
   routes' handlers.
7. **An endpoint filter never authenticates.** An endpoint filter runs after binding, so a filter that authenticates
   costs a request its body. An authenticated API adds a gate.

## Options considered

- **Endpoint filters.** Rejected: they run after minimal APIs bind the route's parameters, so an anonymous request costs
  its body, which is the flaw to remove.
- **An ASP.NET Core authentication scheme.** Rejected: ADR-1044 has the management API validate its own tokens, because
  the keys and issuer are known only inside the request environment of the control-plane tenant, which a scheme that
  runs before routing cannot reach.
- **A host middleware.** Rejected: a package cannot place a middleware in the host's pipeline, and an API that relies on
  the host to add one is unauthenticated when the host does not.
- **A custom body binder.** Rejected: every route that reads a body would have to opt into it, so a route that forgets
  keeps the flaw.
- **A gate that runs before binding.** Chosen: it runs on the endpoint's own request delegate, so no host step and no
  route's opt-in is needed, it can use the request's services, and it answers with the same `IResult` an endpoint
  returns.

## Consequences

- An anonymous request to the management, control-plane authority or directory API costs no more than its headers.
- An authenticated caller without authority for a route still sends that route its body, up to the route's limit (1 MiB
  unless the route sets its own), because the route authorizes in its handler. The tenant import is the exception.
- An anonymous request whose parameters do not bind gets `401`, not `400`.
- A gate is created for each request from the request's services, so it depends on scoped services as an endpoint filter
  does.
- A gate is added with the builder type and the gate type: `AddEndpointGate<RouteGroupBuilder, ManagementCallerGate>()`.

## References

- [#863](https://github.com/NCodeGroup/identity/issues/863)
- [ADR-1044](1044-the-management-api-authenticates-its-own-access-tokens-and-a-configured-client-bootstraps-the-first-globaladmin.md):
  the management API's access tokens
- [ADR-1191](1191-a-management-request-is-authenticated-by-the-control-plane-tenant-and-scoped-by-its-routes-tenant.md):
  the control-plane tenant authenticates a management request
- [ADR-1411](1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md):
  the global directory and the control-plane authority
- [ADR-1424](1424-a-management-route-authorizes-at-the-node-it-names-before-it-reads-through-replaceable-validators.md):
  a route authorizes at the node it names
- [ADR-1466](1466-a-client-is-its-own-principal-and-a-machine-client-acts-only-within-its-owning-node.md): a client is
  its own principal
- [ADR-1514](1514-a-tenant-moves-between-cells-of-one-region-through-an-encrypted-export-and-import.md): the tenant
  import
- `src/NCode.Identity.AspNetCore.Abstractions/IEndpointGate.cs`
- `src/NCode.Identity.AspNetCore/EndpointGateConventionBuilderExtensions.cs`
- `src/NCode.Identity.OpenId.Management/Endpoints/ManagementCallerGate.cs`
- `src/NCode.Identity.OpenId.Management/Endpoints/ManagementFailureEndpointFilter.cs`
- `src/NCode.Identity.OpenId.Management/Endpoints/ManagementRootGroup.cs`
- `src/NCode.Identity.OpenId.Management/Endpoints/Tenants/TenantImportAuthorizationGate.cs`
- `src/NCode.Identity.OpenId.GlobalDirectory.Server/DirectoryAuthenticationGate.cs`
