// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

// Sends one management API request as a machine client of a management tenant, the way an automation client of a
// platform with a directory does (ADR-1536, decision 8): it authenticates to the tenant's token endpoint with
// private_key_jwt, binds the management token to a DPoP key it generates, and sends the request with a DPoP proof for
// the request's own URL. The kind workflows use it to register a cell and to read a cell's management API from the
// runner, through port-forwards, so every URL keeps the public origin that the issuer and Cell:ManagementUrl name.
//
// node build/kind/management-request.mjs --issuer <url> --client-id <id> --key <pem> --method <verb> --url <url>
//   [--body <json>] [--scope <scope>] [--connect <origin>=<host>:<port>[,<tls-name>]]... [--ca <pem>]...
//   [--expect <status>] [--attempts <n>] [--delay-seconds <n>]
//
// --connect sends every request for an origin to another address, such as a port-forward, while the Host header stays
// the origin's; the server's certificate is checked against <tls-name>, such as the Service name that a pod's
// certificate names, or else against the origin's host. --ca adds a trusted CA certificate. --expect retries the whole exchange, a fresh token included, until the API
// answers that status, for at most --attempts tries --delay-seconds apart. The response body goes to standard output.

import { createHash, createPrivateKey, createPublicKey, generateKeyPairSync, randomUUID, sign } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { request } from 'node:https';
import { checkServerIdentity } from 'node:tls';
import { basename, extname } from 'node:path';
import { parseArgs } from 'node:util';

const { values } = parseArgs({
  options: {
    issuer: { type: 'string' },
    'client-id': { type: 'string' },
    key: { type: 'string' },
    method: { type: 'string', default: 'GET' },
    url: { type: 'string' },
    body: { type: 'string' },
    scope: { type: 'string', default: 'read:clients' },
    connect: { type: 'string', multiple: true, default: [] },
    ca: { type: 'string', multiple: true, default: [] },
    expect: { type: 'string' },
    attempts: { type: 'string', default: '1' },
    'delay-seconds': { type: 'string', default: '5' },
  },
  strict: true,
});

for (const required of ['issuer', 'client-id', 'key', 'url']) {
  if (!values[required]) {
    console.error(`--${required} is required.`);
    process.exit(2);
  }
}

const issuer = values.issuer.replace(/\/$/, '');
const clientId = values['client-id'];
const clientKey = createPrivateKey(readFileSync(values.key));
const ca = values.ca.length > 0 ? values.ca.map((file) => readFileSync(file)) : undefined;
const connections = new Map(
  values.connect.map((mapping) => {
    const separator = mapping.lastIndexOf('=');
    const [address, tlsName] = mapping.slice(separator + 1).split(',');
    const [host, port] = address.split(':');
    return [new URL(mapping.slice(0, separator)).origin, { host, port: Number(port), tlsName }];
  }),
);

const base64url = (input) => Buffer.from(input).toString('base64url');

/** Signs a compact ES256 JWT. */
function signJwt(header, payload, key) {
  const input = `${base64url(JSON.stringify(header))}.${base64url(JSON.stringify(payload))}`;
  const signature = sign('sha256', Buffer.from(input), { key, dsaEncoding: 'ieee-p1363' });
  return `${input}.${base64url(signature)}`;
}

// The key's identifier is the PEM file's name without its extension, as the host's key rings name a PEM key, so it
// matches the JWK Set that the host image's directory-client-jwks command prints for the client's registration.
const clientKeyId = basename(values.key, extname(values.key));

/** Sends one HTTPS request to the URL's origin, or to the address that --connect names for it. */
function send(method, url, headers, body) {
  const target = new URL(url);
  const connection = connections.get(target.origin);
  return new Promise((resolve, reject) => {
    const outgoing = request(
      {
        method,
        host: connection?.host ?? target.hostname,
        port: connection?.port ?? (target.port || 443),
        path: `${target.pathname}${target.search}`,
        servername: target.hostname,
        ca,
        checkServerIdentity: (_host, certificate) =>
          checkServerIdentity(connection?.tlsName ?? target.hostname, certificate),
        headers: { host: target.host, ...headers },
      },
      (response) => {
        const chunks = [];
        response.on('data', (chunk) => chunks.push(chunk));
        response.on('end', () =>
          resolve({
            status: response.statusCode ?? 0,
            headers: response.headers,
            body: Buffer.concat(chunks).toString('utf8'),
          }),
        );
      },
    );
    outgoing.on('error', reject);
    if (body !== undefined) {
      outgoing.write(body);
    }
    outgoing.end();
  });
}

/** Builds a DPoP proof (RFC 9449) for a request, with the server's nonce when it named one. */
function proof(dpopKey, method, url, nonce, accessToken) {
  const target = new URL(url);
  const payload = {
    jti: randomUUID(),
    htm: method,
    htu: `${target.origin}${target.pathname}`,
    iat: Math.floor(Date.now() / 1000),
    ...(nonce ? { nonce } : {}),
    ...(accessToken
      ? { ath: base64url(createHash('sha256').update(accessToken).digest()) }
      : {}),
  };
  const jwk = createPublicKey(dpopKey).export({ format: 'jwk' });
  return signJwt({ typ: 'dpop+jwt', alg: 'ES256', jwk }, payload, dpopKey);
}

/** Sends a request with a DPoP proof, and once more with the nonce that a use_dpop_nonce answer names. */
async function sendWithProof(dpopKey, method, url, headers, body, accessToken) {
  let nonce;
  for (let attempt = 0; attempt < 2; attempt++) {
    const response = await send(
      method,
      url,
      { ...headers, DPoP: proof(dpopKey, method, url, nonce, accessToken) },
      body,
    );
    const answered = response.headers['dpop-nonce'];
    const asksForNonce =
      typeof answered === 'string' &&
      (response.status === 400 || response.status === 401) &&
      `${response.body}${response.headers['www-authenticate'] ?? ''}`.includes('use_dpop_nonce');
    if (!asksForNonce || nonce === answered) {
      return response;
    }
    nonce = answered;
  }
  throw new Error(`${method} ${url} kept asking for a new DPoP nonce.`);
}

/** Gets a DPoP-bound access token from the issuer's token endpoint, then sends the management request with it. */
async function exchange() {
  const discovery = await send('GET', `${issuer}/.well-known/openid-configuration`, {});
  if (discovery.status !== 200) {
    throw new Error(`The discovery document answered ${discovery.status}: ${discovery.body}`);
  }
  const tokenEndpoint = JSON.parse(discovery.body).token_endpoint;
  const now = Math.floor(Date.now() / 1000);
  const assertion = signJwt(
    { typ: 'JWT', alg: 'ES256', kid: clientKeyId },
    { iss: clientId, sub: clientId, aud: tokenEndpoint, jti: randomUUID(), iat: now, exp: now + 120 },
    clientKey,
  );
  const { privateKey: dpopKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });
  const form = new URLSearchParams({
    grant_type: 'client_credentials',
    scope: values.scope,
    client_id: clientId,
    client_assertion_type: 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer',
    client_assertion: assertion,
  }).toString();
  const token = await sendWithProof(
    dpopKey,
    'POST',
    tokenEndpoint,
    { 'content-type': 'application/x-www-form-urlencoded', 'content-length': Buffer.byteLength(form) },
    form,
  );
  if (token.status !== 200) {
    throw new Error(`The token request answered ${token.status}: ${token.body}`);
  }
  const accessToken = JSON.parse(token.body).access_token;
  const headers = { authorization: `DPoP ${accessToken}` };
  if (values.body !== undefined) {
    headers['content-type'] = 'application/json';
    headers['content-length'] = Buffer.byteLength(values.body);
  }
  return sendWithProof(dpopKey, values.method, values.url, headers, values.body, accessToken);
}

const attempts = Number(values.attempts);
const delayMs = Number(values['delay-seconds']) * 1000;
let last;
for (let attempt = 1; attempt <= attempts; attempt++) {
  try {
    last = await exchange();
    if (!values.expect || String(last.status) === values.expect) {
      console.log(last.body);
      process.exit(values.expect || (last.status >= 200 && last.status < 300) ? 0 : 1);
    }
    console.error(`Attempt ${attempt}: ${values.method} ${values.url} answered ${last.status}: ${last.body}`);
  } catch (error) {
    console.error(`Attempt ${attempt}: ${error.message}`);
  }
  if (attempt < attempts) {
    await new Promise((resolve) => setTimeout(resolve, delayMs));
  }
}
console.error(`${values.method} ${values.url} never answered ${values.expect ?? 'a success'}.`);
process.exit(1);
