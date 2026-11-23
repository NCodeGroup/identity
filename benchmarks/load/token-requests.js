// k6 load test of one tenant's token endpoint, run against a dedicated cell to measure the highest
// machine-to-machine token rate its node pool sustains. README.md beside this file describes the
// procedure; ADR-1524 records the rating it produces.
//
// Three constant-arrival-rate scenarios run together: a generated client secret with
// client_secret_basic, a private_key_jwt client with an ES256 key (each half of RATE), and a supplied
// client secret at SUPPLIED_RATE.

import http from 'k6/http';
import encoding from 'k6/encoding';
import { check } from 'k6';

function required(name) {
  const value = __ENV[name];
  if (value === undefined || value === '') {
    throw new Error(`The environment variable ${name} is required.`);
  }
  return value;
}

const ISSUER = required('ISSUER');
const GENERATED_CLIENT_ID = required('GENERATED_CLIENT_ID');
const GENERATED_CLIENT_SECRET = required('GENERATED_CLIENT_SECRET');
const JWT_CLIENT_ID = required('JWT_CLIENT_ID');
const JWT_CLIENT_KEY = required('JWT_CLIENT_KEY');
const SUPPLIED_CLIENT_ID = required('SUPPLIED_CLIENT_ID');
const SUPPLIED_CLIENT_SECRET = required('SUPPLIED_CLIENT_SECRET');
const SCOPE = __ENV.SCOPE || '';
const RATE = Number(__ENV.RATE || '50');
const SUPPLIED_RATE = Number(__ENV.SUPPLIED_RATE || '2');
const DURATION = __ENV.DURATION || '10m';

const jwk = JSON.parse(JWT_CLIENT_KEY);
const halfRate = Math.max(1, Math.round(RATE / 2));

export const options = {
  scenarios: {
    generated_secret: {
      executor: 'constant-arrival-rate',
      rate: halfRate,
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 50,
      maxVUs: 500,
      exec: 'generatedSecret',
    },
    private_key_jwt: {
      executor: 'constant-arrival-rate',
      rate: halfRate,
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 50,
      maxVUs: 500,
      exec: 'privateKeyJwt',
    },
    supplied_secret: {
      executor: 'constant-arrival-rate',
      rate: SUPPLIED_RATE,
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 10,
      maxVUs: 100,
      exec: 'suppliedSecret',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.001'],
    'http_req_duration{scenario:generated_secret}': ['p(95)<200'],
    'http_req_duration{scenario:private_key_jwt}': ['p(95)<200'],
    'http_req_duration{scenario:supplied_secret}': ['p(95)<1000'],
    dropped_iterations: ['count==0'],
    checks: ['rate>0.999'],
  },
};

export function setup() {
  const res = http.get(`${ISSUER}/.well-known/openid-configuration`);
  if (res.status !== 200) {
    throw new Error(`Discovery returned status ${res.status}.`);
  }
  return { tokenEndpoint: res.json('token_endpoint') };
}

function basic(id, secret) {
  return 'Basic ' + encoding.b64encode(`${encodeURIComponent(id)}:${encodeURIComponent(secret)}`);
}

function base64Url(value) {
  return encoding.b64encode(value, 'rawurl');
}

function ascii(text) {
  return Uint8Array.from(text, (c) => c.charCodeAt(0)).buffer;
}

function tokenRequest(tokenEndpoint, body, headers) {
  const res = http.post(tokenEndpoint, body, {
    headers: headers,
    tags: { name: 'token' },
  });
  check(res, {
    'status is 200': (r) => r.status === 200,
    'has access_token': (r) => r.json('access_token') !== undefined,
  });
}

function clientCredentialsBody() {
  const body = { grant_type: 'client_credentials' };
  if (SCOPE !== '') {
    body.scope = SCOPE;
  }
  return body;
}

export function generatedSecret(data) {
  tokenRequest(data.tokenEndpoint, clientCredentialsBody(), {
    Authorization: basic(GENERATED_CLIENT_ID, GENERATED_CLIENT_SECRET),
  });
}

export function suppliedSecret(data) {
  tokenRequest(data.tokenEndpoint, clientCredentialsBody(), {
    Authorization: basic(SUPPLIED_CLIENT_ID, SUPPLIED_CLIENT_SECRET),
  });
}

let signingKey;

export async function privateKeyJwt(data) {
  if (signingKey === undefined) {
    signingKey = await crypto.subtle.importKey('jwk', jwk, { name: 'ECDSA', namedCurve: 'P-256' }, false, ['sign']);
  }

  const header = { alg: 'ES256', typ: 'JWT' };
  if (jwk.kid) {
    header.kid = jwk.kid;
  }

  const now = Math.floor(Date.now() / 1000);
  const claims = {
    iss: JWT_CLIENT_ID,
    sub: JWT_CLIENT_ID,
    aud: ISSUER,
    jti: crypto.randomUUID(),
    iat: now,
    exp: now + 60,
  };

  const input = `${base64Url(JSON.stringify(header))}.${base64Url(JSON.stringify(claims))}`;
  const signature = await crypto.subtle.sign({ name: 'ECDSA', hash: 'SHA-256' }, signingKey, ascii(input));

  const body = clientCredentialsBody();
  body.client_id = JWT_CLIENT_ID;
  body.client_assertion_type = 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer';
  body.client_assertion = `${input}.${base64Url(signature)}`;

  tokenRequest(data.tokenEndpoint, body, {});
}
