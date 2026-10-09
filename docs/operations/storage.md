# Storage and WORM

## The short version

PriorState uses configurable S3-compatible storage. Garage is bundled with Docker Compose for
local development, testing and evaluation because it is lightweight and easy to run. Production
can use AWS S3 or another compatible provider without application code changes. **Garage does not implement S3 Object Lock**,
so the bundled configuration has no storage-level WORM. That is a supported configuration, not a
broken one — but you should understand exactly what it does and does not change before you rely
on the system.

## Why the original design changed

The natural design for an evidence archive puts immutability in the storage layer: write the WACZ
to an S3 bucket with Object Lock in COMPLIANCE mode, set a retention date, and nobody — including
the operator — can delete it until the date passes.

That design assumes a self-hostable S3 implementation that enforces Object Lock. As of September
2026, there is not a good one:

| Backend | Object Lock | Status |
|---|---|---|
| MinIO | Yes | Community edition unmaintained; repository archived February 2026. Not a foundation to build on. |
| Garage | **No** | [Issue #1127](https://git.deuxfleurs.fr/Deuxfleurs/garage/issues/1127) is blocked on versioning support. A `feat/s3-versioning-object-lock` branch exists, unmerged, with no target release. |
| SeaweedFS | API present | 3.94 added versioning and Object Lock with GOVERNANCE/COMPLIANCE and legal hold. There are open reports ([#8350](https://github.com/seaweedfs/seaweedfs/issues/8350), [#7194](https://github.com/seaweedfs/seaweedfs/issues/7194)) that COMPLIANCE mode does not actually prevent deletion. |
| RustFS | — | Alpha; distributed mode unreleased. |
| Ceph RGW | Yes | Real and enforced, but a serious operational commitment for a single-site archive. |
| AWS S3, Backblaze B2, Wasabi, Scaleway | Yes | Enforced, hosted. |

A WORM claim that is advertised but not enforced is worse than no claim. It is precisely the kind
of thing opposing counsel finds, and it damages the credibility of everything else in the archive.

## What PriorState does instead

**The immutability guarantee rests on the hash chain and the external RFC-3161 timestamps.**

Those are independent of storage entirely. The timestamp token proves that a specific entry hash
existed before an attested moment; the canonical form and chain prove that the entry has not been
altered since. If someone deletes the whole bucket, the archives are gone — but nothing that was
recorded can be *changed* without detection, and no snapshot can be quietly rewritten.

Storage-level WORM is treated as defence in depth, and as something to be **measured rather than
assumed**. At startup PriorState:

1. Asks the bucket for its Object Lock configuration. If there is none, the result is `Unsupported`.
2. Writes a small scratch object with a COMPLIANCE-mode retention one minute in the future.
3. Captures its version ID and reads that version's COMPLIANCE retention back.
4. Uploads an unprotected control object and deletes its exact version to check deletion permissions.
5. Tries to delete the protected version by ID.
   - `AccessDenied` (HTTP 403) after those checks pass: `Enforced`.
   - Successful deletion, missing version IDs, mismatched retention or other probe errors:
     `ApiPresentUnverified`.

A general permission error is not proof of retention enforcement. The control checks ordinary
version deletion, but cannot rule out every object-specific IAM policy. Use equivalent permissions
for both probe objects. Bucket default retention can also protect the control version; in that
case the result remains `ApiPresentUnverified`. Protected probe objects require separate cleanup
after retention expires; expiration does not automatically delete them.

The result is stored **on every snapshot row**, shown in the interface, reported on `/health`, and
printed on every evidence protocol. Nothing claims protection that was not applied.

## Choosing a backend

**Evaluating, or a small internal archive.** The bundled Garage is fine. Understand that the
single-node configuration also has no redundancy: one disk failure loses the WACZ files. The
ledger and timestamps would still prove what existed, but you would not be able to produce it.

### Production with AWS S3

API and worker use the same configurable S3 client behind `IObjectStore`. For an existing AWS S3
bucket in Frankfurt, configure both services through `deploy/.env`:

```bash
STORAGE_SERVICE_URL=https://s3.eu-central-1.amazonaws.com
STORAGE_REGION=eu-central-1
STORAGE_BUCKET=your-archive-bucket
STORAGE_ACCESS_KEY=...
STORAGE_SECRET_KEY=...
```

Choose the [regional S3 endpoint](https://docs.aws.amazon.com/general/latest/gr/s3.html) matching
your bucket. Outside Compose, set the corresponding `Storage` options for both services; see
[Configuration](/reference/configuration). The default `Storage:ForcePathStyle=true` works with
AWS S3 general purpose buckets. The current client requires access and secret keys; it does not
use the AWS default credential chain, IAM roles or session tokens.

**Compose limitation:** the bundled file still starts `garage` and `garage-init`, and the API
still depends on `garage-init`. For production without Garage, use a separate Compose file that
omits those services and that dependency. Changing the endpoint redirects application storage;
it does not migrate existing objects. Keep the original storage accessible or migrate separately.

The WORM probe deletes the specific uploaded version, so AWS delete markers no longer cause a
false negative. See [AWS Object Lock behaviour](https://docs.aws.amazon.com/AmazonS3/latest/userguide/object-lock.html).
Its credentials need `s3:GetBucketObjectLockConfiguration`, `s3:GetObjectRetention`, `s3:PutObject`,
`s3:PutObjectRetention` and `s3:DeleteObjectVersion`; startup also reads the bucket location.
If the probe remains `ApiPresentUnverified`, PriorState still requests COMPLIANCE retention
on archive uploads when Object Lock is advertised. Bucket default retention or insufficient
probe permissions can prevent verification as described above.

**Anything you may need to rely on.** Use a backend that enforces Object Lock, and create the
bucket with Object Lock enabled — on most implementations that can only be done at creation time.

```bash
# In deploy/.env
STORAGE_SERVICE_URL=https://s3.eu-central-003.backblazeb2.com
STORAGE_REGION=eu-central-003
STORAGE_BUCKET=your-archive-bucket
STORAGE_ACCESS_KEY=...
STORAGE_SECRET_KEY=...
```

Then confirm the probe agrees with you:

```bash
curl -s localhost:8080/health | grep -i worm
```

If it reports anything other than enforced, do not assume it is a configuration mistake on your
side — check whether the backend actually enforces retention at all.

## Retention arithmetic

Object Lock cuts both ways: with retention set, **you** cannot delete either. Before enabling it
for a six- or ten-year retention, work out the storage growth and write the number down. A WACZ
for a medium site is typically 5–50 MB; a daily capture of ten pages over six years is a few
hundred gigabytes, and it cannot be pruned.

PriorState will not shorten a retention period after the fact — the API refuses it. An archive
whose operator can make inconvenient snapshots expire early is not an archive.

## When Garage adds Object Lock

Nothing in PriorState needs to change. The probe will start reporting `Enforced` and new snapshots
will record it. Snapshots written before then keep the honest label they were given, which is
correct: they genuinely were not protected at storage level, and the protocol should keep saying so.
