# What it does not claim

Being precise about the limits is not a disclaimer exercise. Overclaiming is the failure mode that
would actually hurt someone relying on this: an archive that promises more than it delivers gets
taken apart in exactly the situation it was built for.

## What an evidence package proves

1. The archive file is byte-for-byte the file that was recorded.
2. The recorded metadata — URL, capture time, profile, browser conditions — hashes to the entry
   hash that was committed to the ledger.
3. That entry belongs to the Merkle root for its day.
4. An independent authority signed that root at the attested time.

Together: **this snapshot existed, in exactly this form, before the attested moment, and has not
changed since.**

## What it does not prove

**That the capture was complete.** A crawler with a page limit, a site that renders differently
for a datacentre IP, content behind an interaction the crawler did not perform — all produce a
genuine, unaltered record of an incomplete visit. Judge completeness from the WACZ itself and the
recorded capture conditions.

**That the capture was representative.** Personalisation, A/B tests and geographic variation mean
one visit is one visit. Capturing frequently, from a documented configuration, is the mitigation;
it is not a proof.

**That the site was reachable to everyone.** The archive records what this browser, from this
network, at this moment, received.

**That storage was immutable.** Reported per snapshot, honestly, and frequently "no". See
[Storage and WORM](/operations/storage). It does not weaken points 1–4 above.

**That a court will accept it.** That depends on your process as much as on the software — which
is what the [Verfahrensdokumentation](/legal/verfahrensdokumentation) is for.

## Where responsibility sits

PriorState is AGPL-3.0 software you run yourself. The licence disclaims warranty, and that is not
a formality here: **responsibility for the evidentiary value of what this produces lies with the
operator.** Whether an archive holds up depends on the timestamp authority you chose, the
retention you configured, the access controls you put in place and the process you documented.

This documentation is not legal advice. Questions about evidentiary weight, licence choice and
employment contracts belong with a lawyer.
