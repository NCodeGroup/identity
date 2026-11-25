# Platform bootstrap runbook

- **Status:** Living runbook — update it in the same change as anything it describes.
- **Audience:** anyone reproducing the hosted platform from scratch — the maintainers, or a self-hoster running the
  published stacks ([ADR-1133](decisions/1133-the-hosted-platform-runs-only-the-published-artifacts-it-offers-self-hosters.md)).
- **Scope:** the **manual** operations only — the steps no API can do (creating accounts, proving identity, payment,
  registrar delegation, clicking verification links). Everything after them is the OpenTofu bootstrap stack and then
  GitOps ([ADR-1132](decisions/1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md),
  [ADR-1134](decisions/1134-the-platform-and-the-host-follow-written-cloud-native-operating-principles.md)).

Each step says **what** to do, **why** it is manual, and how to **verify** it. Placeholders are in angle brackets; the
values used by the NCode hosted platform are listed once here.

| Placeholder            | Meaning                                         | NCode value                              |
| ---------------------- | ----------------------------------------------- | ---------------------------------------- |
| `<platform-domain>`    | Domain of every issuer-bearing host             | `ncodeid.net`                            |
| `<brand-domain>`       | Domain of the site, docs, status page and mail  | `ncodeid.com`                            |
| `<redirect-domains>`   | Domains that only redirect to `<brand-domain>`  | `ncodeidentity.com`, `ncodeidentity.net` |
| `<github-org>`         | GitHub organization                             | `NCodeGroup`                             |
| `<infra-repo>`         | Public infrastructure repository                | `identity-platform`                      |
| `<aws-region>`         | Home region of the platform's AWS account       | `us-west-2` (Oregon)                     |
| `<oci-region>`         | Home region of the lab's OCI tenancy            | `us-phoenix-1` (Phoenix)                 |
| `<recovery-email>`     | An **off-domain** mailbox you control           | the owner's personal mailbox             |

"The platform's domains" means all four
([ADR-1495](decisions/1495-the-hosted-platform-runs-customer-environments-as-tenants-on-production-under-the-ncodeid-net-platform-domain.md)).

**Region choice.** The AWS account keeps the platform's state, backups and vendor tokens in `us-west-2`, a different
geography from the production cluster in Washington, DC — a regional outage can't take both. Production's region is
chosen for its customers ([hosting-analysis.md](hosting-analysis.md#the-production-region)); only the lab is placed near
the operator. OCI has no Oregon region; Phoenix is the closest OCI region to Denver and has three availability domains
to spread the lab's nodes across.

## 0. Before you start

- **Accounts security baseline** (applies to every step): a password manager, a passkey or hardware security key for
  every account that supports one, and recovery codes stored offline.
- **Recovery email rule:** the registrar, DNS, cloud, and GitHub owner accounts use `<recovery-email>` — **never** an
  address at any of the platform's domains. If a domain or its DNS breaks, those are the accounts you need to fix it.
  Other vendor accounts may use per-purpose `@<brand-domain>` routing addresses.
- **Local tools:** `git`; OpenTofu through [tenv](https://github.com/tofuutils/tenv) (`scoop install tenv` /
  `brew install tenv`), which picks the version in the infrastructure repository's `.opentofu-version` (`tenv tofu
  install` in the repository) and verifies its signature; AWS CLI v2;
  OCI CLI (lab); `kubectl`, Flux CLI, Helm; `gitleaks` (pre-commit). Optional: GitHub CLI `gh`.
- **Expected cost:** see the [hosting analysis](hosting-analysis.md#stages) — the lab is within OCI's free allowance
  ($0/month on two 1 OCPU / 6 GB nodes); the AWS account costs about $3–4 a month, mostly KMS keys at $1 each.

## 1. Domain registrar (Porkbun)

Manual because a registrar credential can hand over the whole domain; it is never automated.

Every item applies to each of the platform's domains: `<platform-domain>`, `<brand-domain>` and each of
`<redirect-domains>`.

1. Turn on account 2FA (passkey / hardware key), **auto-renew**, and **registrar lock** for each domain, and renew
   `<platform-domain>` and `<brand-domain>` to the registry's 10-year maximum.
2. Leave **API access off** for each domain, keep the **Cloudflare integration disconnected** (it would store a standing
   Cloudflare credential at the registrar), and turn **off** "Allow Afternic fast transfer opt-ins" so third-party
   attempts to list a domain for sale are rejected automatically.
3. Do **not** change nameservers yet — that is step 7, after the bootstrap stack has created the zones.

**Verify:** RDAP for each domain shows `client transfer prohibited`
(`foreach ($d in 'ncodeid.net', 'ncodeid.com', 'ncodeidentity.com', 'ncodeidentity.net') { Invoke-RestMethod
https://rdap.org/domain/$d | Select-Object -ExpandProperty status }`).

## 2. GitHub organization and repository

Manual because the repository must exist before any code can be pushed to it. The bootstrap stack then manages its
settings declaratively.

1. **Organization** (`<github-org>`):
   - Settings → Authentication security → **Require two-factor authentication** for everyone, and **Only allow secure
     two-factor methods**.
   - Member privileges → base permission **Read**; repository creation unchecked (owners only); uncheck **Allow members
     to change repository visibilities** and **Allow members to delete or transfer repositories**; App access requests
     → **Members only**.
   - Personal access tokens → fine-grained tokens **allowed** with **administrator approval** and a **maximum lifetime
     of 30 days**; tokens (classic) **restricted**.
2. **Create the repository:** New repository → owner `<github-org>`, name `<infra-repo>`, **Public**, description
   "Public infrastructure as code for the NCode.Identity hosted platform: reusable OpenTofu and Kubernetes components,
   opinionated stacks, and the live configuration we run.", and **no** README, `.gitignore`, license, or Copilot starter
   prompt — the first commit adds the files. The website (`https://<brand-domain>`) and topics are managed by the bootstrap
   stack (an empty repository has no About panel to set them in).
3. **Harden it before the first push** (the bootstrap stack takes these over afterwards):
   - Settings → Actions → General:
     - **Actions permissions** → *Allow `<github-org>`, and select non-`<github-org>`, actions and reusable workflows*,
       with *Allow actions created by GitHub* and *Allow actions by Marketplace verified creators* checked, and
       `opentofu/setup-opentofu@*` in *Allow or block specified actions* (the CI workflow needs it and its publisher
       isn't a verified creator; the bootstrap stack manages this list afterwards). Turn on **Require actions to be
       pinned to a full-length commit SHA** — a moved tag can't swap in different code.
     - **Approval for running fork pull request workflows from contributors** → *Require approval for all external
       contributors*.
     - **Workflow permissions** → *Read repository contents and packages permissions*; leave *Allow GitHub Actions to
       create and approve pull requests* **unchecked**.
   - Settings → Advanced Security: **Enable** private vulnerability reporting; turn on **Dependency graph**,
     **Dependabot alerts**, **Malware alerts**, **Dependabot security updates**, and **Grouped security updates**; under
     **Secret Protection** click **Enable**, then turn on **Push protection**. After the first push, set up **CodeQL**
     (default setup) — it analyzes the GitHub Actions workflows.
   - Settings → General: turn on **Enable release immutability** (releases and their tags can't be changed after
     publishing — [ADR-1133](decisions/1133-the-hosted-platform-runs-only-the-published-artifacts-it-offers-self-hosters.md)); disable **Wikis**; under Pull
     Requests, allow **squash merging** only and **automatically delete head branches**.
4. **Clone it:** `git clone https://github.com/<github-org>/<infra-repo>.git`.

Only one new repository is needed: components, stacks, and live configuration share it, and the host's Helm chart lives
in the library repository ([ADR-1133](decisions/1133-the-hosted-platform-runs-only-the-published-artifacts-it-offers-self-hosters.md)).

**Verify:** the repository is public and empty; a test push of a file containing a fake AWS key is **blocked** by push
protection.

## 3. Cloudflare (DNS and inbound email)

Manual because account creation and the first API token can't be created by an API.

1. Create the account with `<recovery-email>` and 2FA (security key). No payment method is needed.
2. Note the **account ID** (the long hex value in the dashboard URL, or Account home → ⋯ → *Copy account ID*). It is an
   identifier, not a secret.
3. Create a short-lived **bootstrap token** on the day you run the bootstrap stack (it expires tomorrow): My Profile →
   API Tokens → *Create Custom Token*, named `<infra-repo>-bootstrap-<yyyy-mm-dd>`:
   - Permissions: Account → *Account API Tokens* → Edit (to mint the scoped CI tokens); Account → *Email Routing
     Addresses* → Edit; Zone → *Zone* → Edit; Zone → *DNS* → Edit; Zone → *Zone Settings* → Edit; Zone → *SSL and
     Certificates* → Edit (to turn Universal SSL off in every zone except the two `<redirect-domains>` zones, so those
     zones' CAA records are the only CAs that can issue for them); Zone → *Email Routing Rules* → Edit. _(Cloudflare
     renames permission groups occasionally — match by meaning.)_
   - Account resources: only your account. Zone resources: *All zones from an account* → your account (not *All
     zones*, which spans every account you can access).
   - Client IP filtering: your current public IPv4 **and** IPv6 address, if you have one
     (`Invoke-RestMethod https://api64.ipify.org`) — requests leave over IPv6 when it's available. TTL: a range from
     **today to tomorrow** — the picker sets a start date as well as an end date, and picking only tomorrow makes the
     token unusable until midnight (`Invalid access token`, code 9109).
4. Keep the token only in an environment variable for the bootstrap run (`$env:CLOUDFLARE_API_TOKEN`); never write it to
   a file in the repository.

**Verify:**
`(Invoke-RestMethod https://api.cloudflare.com/client/v4/user/tokens/verify -Headers @{ Authorization = "Bearer $env:CLOUDFLARE_API_TOKEN" }).result`
shows `status: active` **and** a `not_before` that is already in the past (times are UTC) — `active` alone doesn't mean
the token is usable yet.

## 4. AWS (state, backups, vendor tokens)

Manual because AWS accounts and the identity used to administer them must exist before any automation can authenticate.

1. **Organization:** in your existing AWS account (it becomes the *management* account), open AWS Organizations →
   *Create an organization*. Make sure its root user has MFA. Signing in as root is acceptable for this one-time setup
   if no IAM administrator exists yet; once IAM Identity Center is set up (item 4), stop using the root user.
2. **Member account:** Organizations → *Add an AWS account* → *Create an AWS account*: name `nidentity-platform`, email
   a unique alias of `<recovery-email>` (for example a `+aws-platform` sub-address), IAM role name left as
   `OrganizationAccountAccessRole`, tags `application` = `ncode-identity` and `owner_email` = `platform@<brand-domain>`
   ([ADR-1135](decisions/1135-platform-resources-carry-one-stable-tag-set-and-provenance-comes-from-attestations-and-audit-logs.md)). Leave it directly under *Root* — one
   workload account needs no organizational units. It is billed to the management account — no new payment method.
   Record its account ID: it goes into `live/bootstrap/terraform.tfvars` as `aws_account_id`.
3. **Root access management:** in IAM (management account) → *Root access management* → **Enable** both capabilities,
   with **no** delegated administrator (that would hand organization-wide root actions to the workload account). The
   root user can enable it but can't use it — privileged actions run through `sts:AssumeRoot`, which refuses root — so
   the new account's root credentials are checked at the end of item 4.
4. **IAM Identity Center** — switch the console to `<aws-region>` **first**; the instance stays in the region it is
   enabled in.
   - *Enable* → organization instance, **Single-Region** (AWS-owned key). A multi-Region instance adds a
     customer-managed KMS key whose loss locks everyone out, to keep human sign-in alive through a regional outage —
     not worth it when CI uses GitHub OIDC and the management root user remains the break-glass.
   - *Settings → Authentication → Multi-factor authentication*: **every time they sign in**; security keys and built-in
     authenticators plus authenticator apps; users without a device must **register one at sign-in**.
   - *Groups* → `platform-admins`; *Users* → your user (password by email), member of `platform-admins`.
   - *Permission sets* → predefined `AdministratorAccess` with a **1-hour** session.
   - *AWS accounts* → select `nidentity-platform` **and** the management account → assign the **group**
     `platform-admins` with `AdministratorAccess`. Permissions go to the group, never the user, so adding or removing an
     administrator is one membership change. The management-account assignment covers organization and billing work,
     so the management account's root user (which centralized root access can't remove) goes back in the drawer with
     its MFA device.
   - Accept the invitation, set the password and MFA device, and note the **AWS access portal URL** from the
     dashboard.

   Create **no IAM users or access keys** — CI uses GitHub OIDC
   ([ADR-1132](decisions/1132-platform-infrastructure-code-is-public-on-github-and-its-state-and-backups-live-in-aws.md)). Then sign out of root, sign in
   through the access portal to the management account, and in IAM → *Root access management* confirm that
   `nidentity-platform` shows root user credentials **Not present** — an account created through Organizations never
   had any, so *Delete root user credentials* is greyed out — and that password recovery is **not** allowed for it.
   (Delete them with *Take privileged action* only if any are present.)
5. **Local profile:** `aws configure sso` — session name `nidentity`, start URL = the access portal URL, SSO region
   `<aws-region>`, default scopes; approve in the browser; account `nidentity-platform`, role `AdministratorAccess`,
   client region `<aws-region>`, output `json`, profile name `nidentity-platform-admin`. Later sessions start with
   `aws sso login --profile nidentity-platform-admin`. Ignore the portal's *Access keys* links — the profile fetches
   short-lived credentials itself.

**Verify:** `aws sts get-caller-identity --profile nidentity-platform-admin` returns the member account ID and an
`AWSReservedSSO_AdministratorAccess_…` role.

## 5. OCI (the lab)

Manual because tenancy sign-up requires identity and card verification, and the home region is chosen once. Only the
lab needs it — the bootstrap stack (section 6) doesn't touch OCI — so it can wait until the lab is built; starting early
only gets the sign-up verification out of the way.

1. **Sign up** with `<recovery-email>`; choose home region **`<oci-region>`** — it **cannot** be changed later.
2. **Upgrade to Pay As You Go** (Billing → Upgrade). It keeps the Always Free resources, the monthly Ampere A1 allowance
   among them (1,500 OCPU-hours and 9,000 GB-hours, ≈ 2 OCPU / 12 GB always-on, which the lab's two nodes use whole),
   and exempts the tenancy from idle-resource reclamation.
3. Enable **MFA** for your console user (Identity → Domains → Default → Security → MFA).
4. Create an **API signing key** for the OCI CLI: Profile → *API keys* → *Add API key* → generate, download the
   private key to `~/.oci/`, and paste the shown configuration into `~/.oci/config` (or run `oci setup config`).

**Verify:** `oci iam region-subscription list` lists `<oci-region>` as the home region.

## 6. Run the bootstrap stack

The bootstrap stack lives in the infrastructure repository (`live/bootstrap`); its
[README](https://github.com/NCodeGroup/identity-platform/tree/main/live/bootstrap) has the exact commands. It is the
one root run by hand, from your machine, with the short-lived credentials above: it creates the identity CI uses, so CI
can't create it. Everything after it runs from GitHub Actions.

1. **Prepare.** Commit the member account ID as `aws_account_id` in `live/bootstrap/terraform.tfvars` (not a secret;
   the AWS provider refuses any other account). Generate a **state passphrase** of at least 32 random characters and
   keep it in your password manager — it encrypts this root's state and plans, and the same passphrase is needed on
   every later run.
2. **GitHub bootstrap token** (manual because the token that lets automation manage the repository can't come from
   that automation): avatar → *Settings* → *Developer settings* → *Personal access tokens* → *Fine-grained tokens* →
   *Generate new token*:
   - Name `<infra-repo>-bootstrap-<yyyy-mm-dd>`; resource owner **`<github-org>`**; expiration *Custom* → tomorrow.
   - Repository access: *Only select repositories* → `<infra-repo>`.
   - Repository permissions: **Administration**, **Environments**, and **Variables** → *Read and write* (*Metadata:
     Read* is added automatically); everything else *No access*.
   - Copy the token into your password manager — it is shown once. The organization requires approval for
     fine-grained tokens (section 2), so approve your own request: `<github-org>` → *Settings* → *Personal access
     tokens* → *Pending requests*. Until then the token gets `403` responses.
3. **Credentials for this shell only:** `$env:AWS_PROFILE = 'nidentity-platform-admin'` (then `aws sso login`), the
   Cloudflare bootstrap token in `$env:CLOUDFLARE_API_TOKEN`, the GitHub token in `$env:GITHUB_TOKEN`, and the
   passphrase in `$env:TF_VAR_state_passphrase` — enter secrets with `Read-Host -MaskInput` so they never reach shell
   history.
4. **First apply on local state.** The state bucket can't hold this root's state before this root creates it, so the
   first run uses a local backend: create the git-ignored `backend_override.tf` (`terraform { backend "local" {} }`),
   then `tofu init`, `tofu plan -out bootstrap.tfplan`, review the plan (everything is an add, plus one import, and
   nothing changes or is destroyed), and `tofu apply bootstrap.tfplan`. The local state file and the plan are already
   encrypted with the passphrase. The apply creates the S3 state and backup buckets, the KMS keys, the GitHub OIDC
   provider and roles, one Cloudflare zone per platform domain with DNSSEC, the redirect rules of `<redirect-domains>`
   (Cloudflare Single Redirect rules on proxied apex and `www` records, with Universal SSL on in those two zones), and
   a scoped token per zone (stored as SSM SecureStrings), the budget alert, and the repository's rulesets and `lab` /
   `production` environments — importing the repository from section 2 rather than creating it. The apex and `www.` of
   `<platform-domain>` are DNS-only records to the platform's ingress, which serves their redirect. If DNSSEC fails
   because a zone is still pending, continue and re-run `tofu apply` after section 7.
5. **Move the state into the bucket it created:** delete `backend_override.tf`, run `tofu init -migrate-state`
   (answer `yes`), then delete the local `terraform.tfstate`, `terraform.tfstate.backup`, and `bootstrap.tfplan`.
6. **Revoke** the Cloudflare bootstrap token and the GitHub token; let the AWS session expire.

**Verify:** `tofu plan` (with fresh credentials) shows no changes — except, until section 7 is done, an in-place
update of `cloudflare_zone_dnssec` from `pending` to `active`; **don't apply it**, it clears on its own once the DS
record is published — and the state object exists in the bucket with versioning enabled.

**Re-run it at least every 90 days** with fresh credentials (no override file) to rotate the CI Cloudflare tokens; they
expire 30 days after their scheduled rotation. Re-run it too whenever `live/bootstrap` changes. Until section 7 is done,
limit such a re-run to what changed so the DNSSEC update stays out of it — for example
`tofu plan '-target=module.github_oidc' '-out=bootstrap.tfplan'`; quote the argument, or PowerShell splits it at the dot.

## 7. Delegate the domains

Manual because it is a registrar change (step 1). Order matters for DNSSEC: publish a DS record only once the zone it
describes is the one answering, or validating resolvers reject the domain.

Do items 1–4 for each of the platform's domains in turn, using that domain's zone `name_servers` and `dnssec_ds`
bootstrap outputs.

1. In Porkbun → *Domain Management* → the domain → *Details*: if a **DNSSEC** record is listed, delete it first (it
   describes the registrar's old zone).
2. *Authoritative Nameservers* → *Edit* → replace them with the two Cloudflare name servers from the bootstrap outputs
   (`name_servers`) of that domain's zone.
3. Wait until Cloudflare reports the zone **Active** (email, or the zone's *Overview*) **and** public resolvers have
   dropped the registrar's name servers from their caches — up to the old NS records' TTL, which is **24 hours** for
   Porkbun (`(Resolve-DnsName <platform-domain> -Type NS -Server 1.1.1.1).TTL` shows the time left). Cloudflare turns
   *Active* as soon as the registry delegates to it, long before every cache agrees.
4. *DNSSEC* → *Add* the DS record from that zone's bootstrap output `dnssec_ds`: key tag, algorithm (`13`, ECDSA P-256
   SHA-256), digest type (`2`, SHA-256), and digest.

**Verify** for each domain: `Resolve-DnsName <platform-domain> -Type NS -Server 1.1.1.1` returns the Cloudflare
nameservers; `Resolve-DnsName <platform-domain> -Type DS -Server 1.1.1.1` returns the DS record;
[DNSViz](https://dnsviz.net/) shows a secure chain for `<platform-domain>`. Run all three with each of the platform's
domains in place of `<platform-domain>`, for example
`foreach ($d in 'ncodeid.net', 'ncodeid.com', 'ncodeidentity.com', 'ncodeidentity.net') { Resolve-DnsName $d -Type DS
-Server 1.1.1.1 }`.

## 8. Confirm inbound email

Inbound email routing (`security@`, `platform@`, `postmaster@`, `abuse@`, vendor aliases) is created by the CI-managed
`live/global` root, on `<brand-domain>`; the other zones accept no mail. Until its first apply, mail to
`@<brand-domain>` — including the AWS budget alerts sent to `platform@<brand-domain>` — bounces. Manual because the forwarding mailbox is personal data that never enters the repository, and because only its
owner can confirm it.

1. **Store the forwarding mailbox** for the two scopes that read it (the plan role reads `plan`, the production apply
   role reads `global`), encrypted with the bootstrap's secrets key:

   ```powershell
   $email = Read-Host -MaskInput 'Forwarding mailbox'
   foreach ($scope in 'plan', 'global') {
     aws ssm put-parameter --profile nidentity-platform-admin --region <aws-region> `
       --name "/<infra-repo>/$scope/email-destination" --type SecureString `
       --key-id alias/nidentity-platform-secrets --value $email
   }
   ```

2. **Apply `live/global` through CI:** merge the pull request that changes it (its plan appears in the `deploy` run's
   summary), then approve the `production` deployment of the run on `main`. Cloudflare forwards only to a verified
   mailbox, so this first apply creates the destination and then fails on the forwarding rules (`Destination address
   is not verified`) — expected.
3. Click the Cloudflare **destination verification** link sent to the forwarding mailbox, then on the failed run choose
   **Re-run all jobs** (so it plans again; the old plan is stale) and approve the new `production` deployment.
4. In the mailbox, add a filter for mail to `*@<brand-domain>`: never send to spam, apply a label.

**Verify:** a test message to `security@<brand-domain>` from a different account (Gmail hides a forwarded copy of your own
message) arrives in the mailbox; one to an unlisted address is dropped. Senders whose resolver still caches the
registrar's name servers (section 7) can't find the MX records until that cache expires.

## 9. Run the cells of a platform

A platform of more than one cell has a global plane of two isolated parts: the global directory, which runs as a release
of its own (the chart `ncode-identity-directory`) with its own database and issues the cells' tokens itself, and the
control-plane cell, which is dedicated to the control-plane tenant, the platform's trust anchor. Every other cell, a
workload cell, authenticates and authorizes management requests from authority and keys replicated into its own
database, and calls no other cell
([ADR-1536](decisions/1536-a-platforms-global-plane-is-a-standalone-directory-and-a-dedicated-control-plane-cell-and-every-other-cell-runs-and-is-managed-on-its-own.md),
[ADR-1411](decisions/1411-a-platform-is-a-global-directory-over-independent-cells-and-a-cells-tenants-share-one-database.md),
[Cells and the global directory](hosting-analysis.md#cells-and-the-global-directory)). A cell is a host, its database,
its Valkey and its KMS keys, in one region. The control-plane cell holds the platform's global responsibilities only:
the control-plane tenant, the platform's administrators, the teams and the cell registry, which its platform console
manages; it never holds a customer's tenant. Each customer cell is managed from its own cell console, which keeps
working while the global plane is down. The steps below follow the order of ADR-1536 decision 12, and no step waits on a
later one. They are manual because only an operator holds the private keys and the platform authority that register a
cell. A platform of one cell skips this section: with no `directory.url` the cell uses local adapters and needs no other
service.

1. **Provision the keys.** Before either release, provision the directory's signing key and the control-plane cell's
   directory-client key, each a PEM key in a Secret or a key that a key management service holds. The directory's key
   ring is the one `keyRing` names in the `ncode-identity-directory` chart (`DirectoryServer:KeyRing`); the control-plane
   cell's is the one `directory.keyRing` names in the host chart (`Directory:KeyRing`). Each has at least one key. A
   workload cell has a client key of its own, provisioned the same way before its own step 4.
2. **Print the control-plane cell's public keys.** Run the host image's `directory-client-jwks` command with the
   control-plane cell's `Directory:KeyRing` configured, for example
   `docker run <host image> directory-client-jwks`, with the key ring's Secret mounted or its key management service
   reachable. It prints the public JWKS of the key ring's keys. Put it into the directory chart's
   `controlPlane.clientJwks`, beside `controlPlane.cell`, `controlPlane.region`, `controlPlane.kind`,
   `controlPlane.tenantId` and `controlPlane.issuer`, which pin the anchor.
3. **Install the directory.** Install the chart `ncode-identity-directory` with the anchor values of step 2, its
   `keyRing` and `keys`, its own database (the runtime role's connection and the schema owner's connection, each from a
   Secret), its serving certificate and its route, as the chart's README lists. It initializes the anchor's record and
   placement before it serves, and answers `503` until then. It runs as one replica, and its database is its own, never
   a cell's.
4. **Install the control-plane cell.** Install the host chart with `cell.isControlPlane=true`, its directory client
   (`directory.url`, `directory.clientId`, `directory.keyRing` and `directory.keys`, which the host reads as
   `Directory:Url`, `Directory:ClientId`, `Directory:KeyRing` and `KeyRings:{name}`), `cell.managementUrl` and the setup
   Job, as [first use of a deployed instance](first-deployed-instance.md) describes. The cell creates its control-plane
   tenant locally without asking the directory, and the setup Job, which runs with the cell's directory client on an
   empty database, creates the root `GlobalAdmin` and registers the platform console's client, and an automation client
   when `setup.automation` names one. What setup saves goes through the outbox, so it reaches the directory and every
   workload cell. Its first call to the directory is its token request, signed with the key the anchor pins. The
   control-plane cell serves only the control-plane tenant.
5. **Register each workload cell.** Provision the cell's directory-client key and print its public JWKS with
   `directory-client-jwks`, as in steps 1 and 2. As a `GlobalAdmin`, through the platform console's Cells page or the
   control-plane cell's management API, send the cell's registration:

   ```http
   PUT /api/cells/{name}
   ```

   The body names the cell's region, its kind and its JWKS. The control-plane cell writes the registration to the
   directory. `DELETE /api/cells/{name}` retires a cell: the directory answers `409` while the cell holds tenants and
   never reissues a retired name.
   `GET /api/cells` lists the cells and whether each answers.
6. **Install each workload cell.** Install the host chart with the cell's own `cell.name`, its directory client
   (`directory.url`, `directory.clientId` set to the registered `{name}`, `directory.keyRing` and `directory.keys`),
   `cell.controlPlaneIssuer` set to the control-plane cell's issuer and `cell.managementUrl` set to the origin of the
   cell's own root tenant's issuer. The host refuses to start when `Cell:ManagementUrl` is empty or its origin is not
   the root tenant's issuer origin. A workload cell serves a dynamic tenant-resolution strategy (`DynamicByHost` or
   `DynamicByPath`), so its root tenant is its management tenant; the chart and the host refuse `StaticSingle` in a
   workload cell. Give every workload cell its own root tenant identifier (`OpenId:TenantResolution:Root:TenantId`): a
   tenant identifier is unique across the platform's cells. The cell setup Job (`setup.job`) runs with the cell's
   directory client on an empty database, reserves the management tenant in the directory and places it in the cell, and
   creates the first `CellAdmin` and the cell console's client (`setup.managementConsole`), in any order with the
   Deployment. Run the cell console (`managementConsole.cellConsole`) from the cell's own release.
7. **Let a team's tenants be created in a cell.** A team's administrators, or a `GlobalAdmin`, name the cells whose
   `CellAdmin`s may create the team's tenants in the team's `tenant_creation_cells` setting, through the platform
   console or `PATCH /api/teams/{teamId}/settings`. The setting reaches every cell with the team, and the cell console
   then creates the team's tenants from its home page, through the cell's own management API. The platform's own team,
   which holds the control-plane tenant and every cell's management tenant, grants no cell.

**Verify:** the directory reports ready and the control-plane cell starts and reports ready. A cell's token request
signed with its key ring, at the directory's token endpoint, returns a token for the directory. Creating a tenant in a
cell reserves its identifier in the directory and places it in the cell, and fails with `503` while the directory is
unreachable; a tenant that is already created keeps serving whether or not the directory is reachable. A team created in
the control-plane cell reaches the directory, and every other cell mirrors it. A `GlobalAdmin`'s token, bound to a DPoP
proof, calls the management API of a workload cell, which validates it against the keys it replicated, with no reset or
restart after setup. With the control-plane cell and the platform console stopped, a `CellAdmin` signs in to the cell
console, lists the cell's tenants and creates a tenant of a team that grants the cell; with the directory stopped too,
only the creation answers `503`.

### Break-glass

When the platform's administrators are locked out, or a setting refuses every sign-in, recover offline with the host
image's commands, each run as a Job with the cell's configuration, its directory client included, as `setup` and
`secret-protection` are
([ADR-1512](decisions/1512-an-instance-is-set-up-once-through-a-one-time-setup-token-that-creates-a-root-global-administrator.md)):

- `recover-admin` creates or re-enables a local account in the anchor tenant from `Recovery:Admin:*` (`UserName`,
  `Password`, `Email`, `DisplayName`, from a Secret) and grants `GlobalAdmin` in the control-plane cell or `CellAdmin`
  in a workload cell.
- `recover-sign-in` removes the anchor tenant's own values of the settings that can block every sign-in, and in the
  control-plane cell the platform's values too, and re-enables the tenant's default local connection, the management
  console client and the management resource server. It logs and audits each change. A workload cell's platform values
  arrive from the directory's feed, so repair them in the control-plane cell.
- `revoke-management-key <kid>` removes a compromised management signing key from a cell's replica and records its
  identifier as revoked, which closes the exposure of a cell that cannot reach the directory. The control-plane cell's
  management API revokes the key for every cell through `POST /api/platform/management-keys/{kid}/revoke`.

The host chart's `recovery.*` values render each command as a Job.

## 10. Close out

- [ ] `<platform-domain>` and `<brand-domain>` renewed to the registry's 10-year maximum; auto-renew on for every
      domain.
- [ ] Bootstrap tokens revoked; no credentials in shell history or files.
- [ ] 2FA confirmed on Porkbun, GitHub, Cloudflare, and AWS (Identity Center) — and on OCI once section 5 is done.
- [ ] Budget alert present on AWS (created by the bootstrap stack) — and on OCI once the lab's stack creates it.
- [ ] Record what changed in this runbook if any step differed from it.
