#!/usr/bin/env node
// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

// Copies the files brand.lock.json names from one release of NCodeGroup/brand, and checks them. Copy this file into
// the repository that vendors the brand, next to its brand.lock.json. Node.js alone: no packages. Apache-2.0.
//
//   node vendor-brand.mjs --check
//       Offline: every vendored file has the SHA-256 the lock pins. Exits 1, naming each file, when one is missing or
//       differs. Run it in the gate and in CI. It needs nothing but Node.js.
//   node vendor-brand.mjs --update <version> [--from <archive file>]
//       Downloads the release's archive, brand-<version>.tar.gz, from the GitHub release v<version> with the GitHub
//       CLI, and has gh attestation verify prove its build provenance: signed by NCodeGroup/brand's
//       .github/workflows/publish.yml for the tag v<version> on a GitHub-hosted runner, for the archive's SHA-256.
//       It refuses the archive unless its manifest.json is that version and it holds every file the manifest lists,
//       at the manifest's size and SHA-256, and nothing else; only then does it write the files the lock names, and
//       the lock. The archive is at most 64 MiB and unpacks to at most 256 MiB. A release never changes, so when the
//       lock already pins that version, a file whose SHA-256 is not the pinned one is refused too. gh must be
//       signed in: gh auth login, or GH_TOKEN in CI. --from reads the archive from a file instead of downloading
//       it, and verifies it the same way.
//   --lock <file>  the lock to use, brand.lock.json by default. Vendored paths are relative to its folder, and nothing
//       is written outside it, by a path or through a symbolic link.
//
// brand.lock.json: { "version": "0.1.0", "files": { "<vendored path>": { "source": "<path in the release>", "sha256": "" } } }
// Add a file by adding its entry with an empty sha256, then run --update.
import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { closeSync, existsSync, fstatSync, lstatSync, mkdirSync, mkdtempSync, openSync, readFileSync, realpathSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, isAbsolute, join, normalize, resolve, sep } from 'node:path';
import process from 'node:process';
import { parseArgs } from 'node:util';
import { gunzipSync } from 'node:zlib';

const USAGE = 'Usage: node vendor-brand.mjs --check | --update <version> [--from <archive file>] [--lock <file>]';
const sha256 = (bytes) => createHash('sha256').update(bytes).digest('hex');
/** The repository that publishes the brand, and the workflow that must have signed a release's archive. */
const REPO = 'NCodeGroup/brand';
const SIGNER = `${REPO}/.github/workflows/publish.yml`;
/** The most a release's archive may hold, packed and unpacked, and the longest one gh command may take. */
const ARCHIVE_LIMIT = 64 * 1024 * 1024;
const UNPACKED_LIMIT = 256 * 1024 * 1024;
const GH_TIMEOUT = 300_000;

/** A relative path that stays inside its folder, or an error naming what it is. */
function inside(path, what) {
  const clean = normalize(path);
  if (isAbsolute(path) || clean === '..' || clean.startsWith(`..${sep}`) || clean.split(sep).includes('..')) {
    throw new Error(`${what} "${path}" leaves its folder`);
  }
  return clean;
}

/**
 * Where a vendored file is written: inside the lock's folder as the file system resolves it, so a symbolic link on
 * the way cannot take the write elsewhere, and a file or nothing yet, never a folder.
 */
function destination(root, path) {
  const file = join(root, inside(path, 'The vendored path'));
  const entry = (at) => {
    try {
      return lstatSync(at, { throwIfNoEntry: false });
    } catch (error) {
      if (error.code === 'ENOTDIR') throw new Error(`The vendored path "${path}" runs through a file`);
      throw error;
    }
  };
  let existing = file;
  while (!entry(existing)) existing = dirname(existing);
  let real;
  try {
    real = realpathSync(existing);
  } catch {
    throw new Error(`The vendored path "${path}" leads through a symbolic link to nothing`);
  }
  const top = realpathSync(root);
  if (real !== top && !real.startsWith(`${top}${sep}`)) throw new Error(`The vendored path "${path}" leaves its folder through a symbolic link`);
  if (existing === file && !statSync(file).isFile()) throw new Error(`The vendored path "${path}" is not a file`);
  return file;
}

/** Runs the GitHub CLI and returns what it prints, or an error saying why it could not. */
function gh(args) {
  const result = spawnSync('gh', args, { encoding: 'utf8', timeout: GH_TIMEOUT, maxBuffer: 16 * 1024 * 1024 });
  if (result.error?.code === 'ENOENT') {
    throw new Error('--update needs the GitHub CLI, gh, to download a release and verify its attestation: install it from https://cli.github.com, then run gh auth login, or set GH_TOKEN in CI');
  }
  if (result.error) throw result.error;
  if (result.status !== 0) {
    const text = (result.stderr || result.stdout || '').trim() || `exit code ${result.status}`;
    throw new Error(`gh ${args[0]} ${args[1]} failed: ${text}`);
  }
  return result.stdout;
}

/** The regular files of a gzipped ustar archive, as a Map of path to bytes. */
function unpack(packed, name) {
  let tar;
  try {
    tar = gunzipSync(packed, { maxOutputLength: UNPACKED_LIMIT });
  } catch (error) {
    if (error.code === 'ERR_BUFFER_TOO_LARGE') throw new Error(`${name}: the archive unpacks to more than ${UNPACKED_LIMIT} bytes`);
    throw new Error(`${name}: ${error.message}`);
  }
  const files = new Map();
  let at = 0;
  while (at + 512 <= tar.length) {
    if (tar.subarray(at, at + 512).every((byte) => byte === 0)) break;
    // A field is its bytes up to the first NUL, as UTF-8.
    const field = (offset, length) => {
      const raw = tar.subarray(at + offset, at + offset + length);
      const end = raw.indexOf(0);
      return raw.subarray(0, end < 0 ? raw.length : end).toString('utf8');
    };
    if (field(257, 6) !== 'ustar') throw new Error(`${name}: the entry at byte ${at} is not a ustar header`);
    const prefix = field(345, 155);
    const path = prefix ? `${prefix}/${field(0, 100)}` : field(0, 100);
    const type = field(156, 1);
    if (type !== '0' && type !== '') throw new Error(`${name} holds ${path}, which is not a file`);
    const size = Number.parseInt(field(124, 12).trim(), 8);
    if (!Number.isSafeInteger(size) || size < 0 || at + 512 + size > tar.length) throw new Error(`${name}: ${path} is cut short`);
    if (files.has(path)) throw new Error(`${name} holds ${path} twice`);
    files.set(path, tar.subarray(at + 512, at + 512 + size));
    at += 512 + Math.ceil(size / 512) * 512;
  }
  return files;
}

async function main() {
  const { values } = parseArgs({
    options: { check: { type: 'boolean' }, update: { type: 'string' }, from: { type: 'string' }, lock: { type: 'string', default: 'brand.lock.json' } },
  });
  const lockPath = resolve(values.lock);
  const root = dirname(lockPath);
  const lock = JSON.parse(readFileSync(lockPath, 'utf8'));
  if (!lock.files || typeof lock.files !== 'object') throw new Error(`${values.lock} has no "files"`);

  if (values.check) {
    const problems = [];
    for (const [path, entry] of Object.entries(lock.files)) {
      const file = join(root, inside(path, 'The vendored path'));
      if (!entry.sha256) problems.push(`${path}: the lock has no SHA-256 for it; run --update`);
      else if (!existsSync(file)) problems.push(`${path}: missing`);
      else if (sha256(readFileSync(file)) !== entry.sha256) problems.push(`${path}: differs from ${entry.source} of brand ${lock.version}`);
    }
    for (const problem of problems) console.error(problem);
    if (problems.length) {
      console.error(`Run "node vendor-brand.mjs --update ${lock.version}" to restore the files.`);
      return 1;
    }
    console.log(`brand ${lock.version}: ${Object.keys(lock.files).length} vendored files match the lock.`);
    return 0;
  }

  if (values.update) {
    if (!/^\d+\.\d+\.\d+$/.test(values.update)) throw new Error(`${values.update} is not a version such as 1.2.3`);
    if (values.from && /^[a-z][a-z0-9+.-]*:\/\//i.test(values.from)) {
      throw new Error(`--from takes the path of an archive file, not ${values.from}`);
    }
    const tag = `v${values.update}`;
    const name = `brand-${values.update}.tar.gz`;
    const scratch = mkdtempSync(join(tmpdir(), 'vendor-brand-'));
    let files;
    try {
      const archive = values.from ? resolve(values.from) : join(scratch, name);
      if (!values.from) gh(['release', 'download', tag, '--repo', REPO, '--pattern', name, '--output', archive]);
      // One descriptor for the size check and the read, so both see the same file.
      const descriptor = openSync(archive, 'r');
      let packed;
      try {
        if (fstatSync(descriptor).size > ARCHIVE_LIMIT) throw new Error(`${name}: the archive is larger than ${ARCHIVE_LIMIT} bytes`);
        packed = readFileSync(descriptor);
      } finally {
        closeSync(descriptor);
      }
      if (packed.length > ARCHIVE_LIMIT) throw new Error(`${name}: the archive is larger than ${ARCHIVE_LIMIT} bytes`);
      const digest = sha256(packed);
      const verified = JSON.parse(gh([
        'attestation', 'verify', archive, '--repo', REPO, '--signer-workflow', SIGNER, '--source-ref', `refs/tags/${tag}`,
        '--deny-self-hosted-runners', '--format', 'json',
      ]));
      if (!verified.some((r) => r.verificationResult?.statement?.subject?.some((s) => s.digest?.sha256 === digest))) {
        throw new Error(`${name}: gh verified an attestation, but none names the archive's SHA-256 ${digest}`);
      }
      files = unpack(packed, name);
    } finally {
      rmSync(scratch, { recursive: true, force: true });
    }
    const manifestBytes = files.get('manifest.json');
    if (!manifestBytes) throw new Error(`${name} holds no manifest.json`);
    const manifest = JSON.parse(manifestBytes.toString('utf8'));
    if (manifest.version !== values.update) throw new Error(`The release is ${manifest.version}, not ${values.update}.`);
    const released = manifest.files ?? {};
    for (const [path, bytes] of files) {
      if (path === 'manifest.json') continue;
      if (!Object.hasOwn(released, path)) throw new Error(`${name} holds ${path}, which the release's manifest does not list`);
      if (bytes.length !== released[path].bytes || sha256(bytes) !== released[path].sha256) {
        throw new Error(`${path}: the archive does not match the release's manifest`);
      }
    }
    for (const path of Object.keys(released)) {
      if (!files.has(path)) throw new Error(`${path}: the release's manifest lists it, but ${name} does not hold it`);
    }
    const fetched = [];
    for (const [path, entry] of Object.entries(lock.files)) {
      const file = destination(root, path);
      if (!Object.hasOwn(released, entry.source)) throw new Error(`brand ${manifest.version} has no ${entry.source}`);
      const listed = released[entry.source];
      if (lock.version === manifest.version && entry.sha256 && entry.sha256 !== listed.sha256) {
        throw new Error(`${path}: brand ${manifest.version} has another SHA-256 for ${entry.source} than the lock pins. A release never changes, so this is not that release; if you changed the entry's source, empty its sha256 and run --update again.`);
      }
      fetched.push({ path, entry, file, bytes: files.get(entry.source), hash: listed.sha256 });
    }
    // Every file is verified and every destination checked before any is written. Each is written beside its
    // destination and renamed into place once all are written, so a refused release or a failed write leaves the
    // vendored files and the lock as they were; only a rename that fails after them can leave some replaced, which
    // --check then names.
    const written = [];
    try {
      for (const f of fetched) {
        mkdirSync(dirname(f.file), { recursive: true });
        const temp = `${f.file}.${process.pid}.vendor-brand`;
        writeFileSync(temp, f.bytes, { flag: 'wx' });
        written.push([temp, f]);
      }
    } catch (error) {
      for (const [temp] of written) rmSync(temp, { force: true });
      throw error;
    }
    for (const [temp, f] of written) {
      renameSync(temp, f.file);
      f.entry.sha256 = f.hash;
      console.log(`${f.path}  <-  ${f.entry.source}  ${f.bytes.length} bytes`);
    }
    lock.version = manifest.version;
    writeFileSync(lockPath, `${JSON.stringify(lock, null, 2)}\n`);
    console.log(`brand ${lock.version}: ${fetched.length} files vendored.`);
    return 0;
  }

  console.error(USAGE);
  return 2;
}

main().then(
  (code) => { process.exitCode = code; },
  (error) => { console.error(`vendor-brand.mjs: ${error.message}`); process.exitCode = 1; },
);
