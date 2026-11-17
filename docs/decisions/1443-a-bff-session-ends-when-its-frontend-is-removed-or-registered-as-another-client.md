# 1443. A BFF session ends when its frontend is removed or registered as another client

- **Status:** Accepted
- **Date:** 2026-11-06
- **Deciders:** NCode Group

## Context

A frontend changes at run time
([ADR-1442](1442-a-bff-serves-the-frontends-its-configuration-lists-each-selected-per-request-by-an-open-rule.md)),
so a session can outlive what it was issued for: its frontend can be removed, or registered as another OAuth client
with another `Authority` or `ClientId`, whose ID token, claims and refresh token the session does not hold.

Four forces shape the answer:

- A removed frontend's cookie is never read, because no request selects the frontend, but its stored record keeps a
  refresh token until it expires, and a frontend added later under the same name would read it again.
- Replicas apply a change at different moments, and a change made by restarting the replicas is seen by none of them
  as a change.
- Correctness never depends on process-local state, and work proportional to the sessions never runs inside a request.
- The store is a replaceable seam that holds the session records, which it treats as opaque, their indexes, and a
  value per member of the client list that names the client in plaintext, as a record's readable metadata does
  ([ADR-1290](1290-the-bff-keeps-each-session-as-an-encrypted-server-side-record-under-a-cookie-handle.md)).

## Decision

**A session records the client it was issued to; a request refuses and ends a session whose frontend is another
client; and a background pass on one replica ends every stored session whose frontend is gone or is another client.**

1. **The session records its client.** `BffSessionMetadata` holds `Frontend`, `Authority` and `ClientId` as the
   frontend configured them when the session was created. The metadata is the record's AEAD associated data, so they
   cannot change without the handle.
2. **A request checks it.** The resolver refuses a session whose `Authority` or `ClientId` differs from its frontend's,
   ends it with the reason `client_changed` (`BffSessionEndReasons.ClientChanged`) and deletes the cookie. A connection
   makes the same check when it revalidates, and a connection whose frontend is gone ends.
3. **The store indexes sessions by client.** `IBffSessionStore.FindClientsAsync` lists the distinct clients
   (`BffSessionClient`: frontend, authority and client id) that have stored sessions, `FindByClientAsync` lists the
   store keys of one client's sessions, and `ForgetEndedSessionAsync` removes the key of a session whose record is gone
   from its client's index; a store adapter implements all three. The default store keeps one `bff:client:` index per
   client, keyed by digests of the length-prefixed values, and one `bff:clients` index whose members are the UTC day of
   its sessions' absolute expiry and the client's digest. Every replica reads the same members, and each member
   expires at the end of its day, so adding a session never shortens how long its client stays listed. Each member has
   a value of its own, `bff:client-member:<member>`, that holds the plaintext client and expires with the member, so
   the members and the keys name a client only by its digest, and `FindClientsAsync` reads one value per member and no
   session. It leaves out the clients that its frontends are configured as, by digest, without reading their values,
   reads the value of each other member until its client is listed, and skips a member whose value is missing or holds
   another client, which expires on its own. A session stays in its client's index until its absolute expiry, after its
   record has ended at its idle expiry, so the key of an ended session stays listed until a pass forgets it.
4. **A pass reconciles the store with the set.** `BffSessionRetirementService` runs `BffSessionRetirement` when the
   host starts and after every change of the set. For each stored client whose frontend is gone, or is configured as
   another client, the pass takes the store lease `retire:<frontend>` for `Bff:SessionRetirement:LeaseDuration` (five
   minutes by default) and ends each matching session as `frontend_removed` (`BffSessionEndReasons.FrontendRemoved`)
   or `client_changed`, recording the session-ended audit event and logging event 19013. A key of the client's index
   whose record is already gone is forgotten through `ForgetEndedSessionAsync`, so no later pass reads it again. The
   pass names the clients that its frontends are configured as to the store, which skips their digests without reading
   their values, so a pass over a set that matches the store reads only `bff:clients`.
5. **One caller ends a session.** A session ends through an atomic take of its record, so when a request and the pass
   end one session at once, only the caller whose take removed it audits and counts it.
6. **Failures retry.** A replica that cannot take the lease leaves the frontend to the one that holds it and runs the
   pass again after `Bff:SessionRetirement:RetryDelay` (a minute by default). A store failure is logged as event
   19014, any other failure of a pass is logged the same way so the service keeps running, and a session that cannot
   be ended is logged as event 19015 while the pass ends the client's other sessions. In each case the pass runs
   again after the delay or at the next change.
7. **Restarts and interruptions converge.** The pass reads the store, not a remembered set, so a frontend removed by
   restarting the replicas is reconciled at their next start, and a pass that a crash interrupted runs again at the
   next start or change.
8. **No revocation.** A session ended this way is not revoked at the OP: the BFF cannot authenticate as the
   client that holds its refresh token, so the OP's refresh-token lifetime bounds it.

## Options considered

- **Ending the sessions on the replica that saw the change, from the set it held before.** Rejected: a change made by
  restarting the replicas is seen by no replica, and a replica that crashes during the pass leaves sessions that a
  frontend added later under the same name would read again.
- **Scanning every session on every change.** Rejected: the pass would read every record of every frontend; the
  client index reaches only the stale ones.
- **Leaving the records to expire.** Rejected: a removed frontend's record keeps a refresh token until it expires, and
  adding the frontend again under its name would resume its sessions.
- **Binding a session to its frontend's name only.** Rejected: a frontend registered as another client would serve
  claims and tokens that its client never issued.
- **Naming each client in the members of `bff:clients`.** Rejected: the key space of a shared Valkey would name every
  frontend, authority and client id. A member's value holds them instead, as a session record's metadata does.
- **Resolving a member's client through a readable session in the client's index.** Rejected: a session stays in its
  client's index after its record has ended at its idle expiry, so listing the clients reads the ended sessions of
  every index again on each pass, and an index's members have no order, so stopping early would skip a client whose
  readable session comes late and leave its sessions, refresh tokens included, unretired. One multi-key command cannot
  read them either, because session keys share no hash tag and a clustered Valkey spreads one client's sessions over
  its nodes.
- **Having `FindByClientAsync` drop the keys whose records are gone.** Rejected: a query stays free of writes, and the
  pass, which holds the lease and reads each record, removes a key through `ForgetEndedSessionAsync` when it finds the
  record gone.
- **Recording the client in the session metadata, refusing a mismatch on each request and retiring stored sessions in
  a leased background pass over a client index whose members each have a value that holds the client** (chosen).

## Consequences

- Removing a frontend ends its users' sessions: each replica serves none of them once it applies the change, and the
  pass removes them from the store.
- Registering a frontend as another client signs its users out.
- A store adapter implements `FindClientsAsync`, `FindByClientAsync` and `ForgetEndedSessionAsync`.
- Listing the stored clients reads one value per member of `bff:clients` and no session, however many sessions a
  client has.
- A pass reads the record of an ended session once, because it forgets the session's key from its client's index.

## References

- [`docs/backend-for-frontend.md`](../backend-for-frontend.md) §5.4, §5.10 and §5.13
- [ADR-1290](1290-the-bff-keeps-each-session-as-an-encrypted-server-side-record-under-a-cookie-handle.md),
  [ADR-1293](1293-the-bff-ends-sessions-by-a-guarded-logout-form-post-and-by-back-channel-logout-through-indexes.md),
  [ADR-1442](1442-a-bff-serves-the-frontends-its-configuration-lists-each-selected-per-request-by-an-open-rule.md)
- `src/NCode.Identity.Bff/Sessions/BffSessionRetirement.cs`,
  `src/NCode.Identity.Bff/Sessions/DefaultBffSessionStore.cs`, `src/NCode.Identity.Bff.Abstractions/IBffSessionStore.cs`
