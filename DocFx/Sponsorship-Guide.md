# Sponsored Fees & Reserves Guide (XLS-68)

This guide explains how to use XRPL Sponsored Fees & Reserves with the XrplCSharp SDK. Sponsorship lets one account (the **sponsor**) pay transaction fees and/or object reserves on behalf of another account (the **sponsee**) — the building block for gasless onboarding and custodial UX.

> **Note:** Requires the `Sponsor` amendment (XLS-68). As of mid-2026 it exists only on the rippled `develop` branch — it is not part of any release and is not active on mainnet or testnet. Use the nightly Docker stand from this repository to try it (see [Testing](#testing)). The feature is in draft and subject to change.

## Table of Contents

- [Overview](#overview)
- [Key Concepts](#key-concepts)
- [Transaction Types](#transaction-types)
- [Step-by-Step: Establishing a Sponsorship](#step-by-step-establishing-a-sponsorship)
- [Sending a Sponsored Transaction](#sending-a-sponsored-transaction)
- [Signing Flows (V1/V2/V3)](#signing-flows-v1v2v3)
- [Sponsorship inside a Batch](#sponsorship-inside-a-batch)
- [Fees](#fees)
- [Ledger Objects](#ledger-objects)
- [Testing](#testing)
- [Common Errors](#common-errors)

---

## Overview

```text
Sponsor (rSponsor...)                    Sponsee (rUser...)
┌───────────────────────┐               ┌───────────────────────┐
│ SponsorshipSet        │──────────────►│ may now send txs with │
│   Sponsee: rUser...   │               │   Sponsor: rSponsor...│
│   FeeAmount: 5 XRP    │               │   SponsorFlags: fee   │
│   RemainingOwnerCount │               │ fee is charged to the │
└───────────────────────┘               │ sponsor, not the user │
        creates                         └───────────────────────┘
┌───────────────────────┐
│ Sponsorship (ledger)  │
│   Owner / Sponsee     │
│   FeeAmount budget    │
│   RemainingOwnerCount │
└───────────────────────┘
```

Two independent dimensions can be sponsored:

| Dimension | `SponsorFlags` value | What the sponsor pays |
|---|---|---|
| Fees | `SponsorCoverage.spfSponsorFee` (= 1) | The `Fee` of the sponsee's transactions |
| Reserves | `SponsorCoverage.spfSponsorReserve` (= 2) | Reserves on behalf of the sponsee: owner reserves of objects it creates **and** account reserves (including sponsored account creation) |

Every transaction type gains three common fields: `Sponsor`, `SponsorFlags`, and (when the sponsorship demands a co-signature) `SponsorSignature` — an inner not-signing STObject over the **same transaction as the main signature, under the sponsor's own hash prefix** (rippled `fixCleanup3_4_0`; before it every role signed identical bytes). It comes in two alternative forms: single-signature (`SigningPubKey` + `TxnSignature`) or sponsor multisig (a nested `Signers` array).

---

## Key Concepts

### The Sponsorship ledger object

Created by `SponsorshipSet`, one per sponsor/sponsee pair:

| Field | Meaning |
|---|---|
| `Owner` | The sponsoring account |
| `Sponsee` | The sponsored account |
| `FeeAmount` | Remaining XRP budget for sponsored fees |
| `RemainingOwnerCount` | How many more objects the sponsor will cover reserves for |

Both are ledger-object fields only. The transaction adjusts them with the signed
`FeeAmountDelta` / `RemainingOwnerCountDelta` fields — a positive delta tops the budget
up, a negative one returns it to the sponsor. Sending the absolute names in a
transaction is rejected outright (`Field 'FeeAmount' found in disallowed location`).

### Require-signature mode

By default a sponsee can spend the sponsorship budget without the sponsor's participation. `SponsorshipSet` flags flip that per dimension:

- `tfSponsorshipSetRequireSignForFee` / `tfSponsorshipClearRequireSignForFee`
- `tfSponsorshipSetRequireSignForReserve` / `tfSponsorshipClearRequireSignForReserve`

When required, the sponsee's transaction must carry a valid `SponsorSignature` — see [Signing Flows](#signing-flows-v1v2v3).

### Accounting fields

- `AccountRoot`: `SponsoredOwnerCount`, `SponsoringOwnerCount`, `SponsoringAccountCount`
- `RippleState`: `HighSponsor` / `LowSponsor` — who covers the reserve of each trust-line side

---

## Transaction Types

### SponsorshipSet

Creates/updates the relationship or deletes it with `tfDeleteObject`. Per rippled `SponsorshipSet::preflight`, exactly one of `Sponsee` / `CounterpartySponsor` is present (the submitter is the other side); **only the sponsor can create or update**, but **either side can delete**:

```csharp
// The sponsor creates/updates (names the sponsee):
var setup = new SponsorshipSet
{
    Account = sponsor.ClassicAddress,
    Sponsee = sponsee.ClassicAddress,
    FeeAmountDelta = new Currency { ValueAsXrp = 5m },
    RemainingOwnerCountDelta = 3,
};
setup = await client.Autofill(setup);
await client.SubmitAndWait(setup, sponsor, true);

// The sponsee deletes its own sponsorship (names the sponsor);
// deletion forbids the modification flags and FeeAmountDelta/MaxFee/RemainingOwnerCountDelta:
var deletion = new SponsorshipSet
{
    Account = sponsee.ClassicAddress,
    CounterpartySponsor = sponsor.ClassicAddress,
    Flags = SponsorshipSetFlags.tfDeleteObject,
};
deletion = await client.Autofill(deletion);
await client.SubmitAndWait(deletion, sponsee, true);
```

### SponsorshipTransfer

Creates, moves or terminates reserve sponsorship of existing ledger objects. Exactly one mode flag is required (per rippled `SponsorshipTransfer::preflight`; the target owner is `Account` for Create/Reassign — `Sponsee` is forbidden there):

| Flag | Who sends | What it does | Extra fields |
|---|---|---|---|
| `tfSponsorshipCreate` | the sponsee (owner of the unsponsored objects) | establishes reserve sponsorship of existing objects | `Sponsor` (the new sponsor) + `spfSponsorReserve` required; the sponsor co-signs |
| `tfSponsorshipReassign` | the sponsee (owner of the sponsored objects) | moves the reserve from the current sponsor to a new one | `Sponsor` (the new sponsor) + `spfSponsorReserve` required; the new sponsor co-signs |
| `tfSponsorshipEnd` | sponsee or sponsor | removes reserve sponsorship (target = `Sponsee` if present, else `Account`) | `Sponsor` and `SponsorFlags` forbidden |

---

## Step-by-Step: Establishing a Sponsorship

```csharp
XrplWallet sponsor = XrplWallet.Generate();
XrplWallet sponsee = XrplWallet.Generate();
// fund both wallets first

var tx = new SponsorshipSet
{
    Account = sponsor.ClassicAddress,
    Sponsee = sponsee.ClassicAddress,
    FeeAmountDelta = new Currency { ValueAsXrp = 5m },
    RemainingOwnerCountDelta = 3,
};
tx = await client.Autofill(tx);
TransactionSummary result = await client.SubmitAndWait(tx, sponsor, true);
// result.Meta.TransactionResult == "tesSUCCESS"
```

Verify via `account_objects`:

```csharp
var request = new AccountObjectsRequest(sponsor.ClassicAddress) { Type = LedgerEntryType.Sponsorship };
var objects = await client.AccountObjects(request);
LOSponsorship sponsorship = objects.AccountObjectList.OfType<LOSponsorship>().First();
```

---

## Sending a Sponsored Transaction

The sponsee sends any ordinary transaction with the sponsorship fields:

```csharp
var payment = new Payment
{
    Account = sponsee.ClassicAddress,
    Destination = destination.ClassicAddress,
    Amount = new Currency { ValueAsXrp = 1m },
    Sponsor = sponsor.ClassicAddress,
    SponsorFlags = SponsorCoverage.spfSponsorFee,
};
payment = await client.Autofill(payment);
```

If the sponsorship does **not** require a co-signature, sign and submit as usual. If it does, use one of the flows below.

---

## Signing Flows (V1/V2/V3)

`SponsorSignature` covers the same transaction as the main signature but under the sponsor's own hash prefix, which `fixCleanup3_4_0` introduced (analogous to the LoanSet counterparty pattern). The SDK picks the prefix from the role; nothing to pass.

### The simple path — standard Sign/Submit (10.8.0+)

No helper choice is needed: the standard API routes by role. A wallet matching `tx.Sponsor` produces the sponsor co-signature; the submitter's wallet signs the main signature preserving an existing `SponsorSignature`; the smart `SubmitAndWait` composes, pre-checks the sponsorship's require-sign flags against the ledger and submits.

```csharp
// Decodes a handed-over blob back into a signable transaction
static Dictionary<string, object> Reparse(string blob) =>
    JsonSerializer.Deserialize<Dictionary<string, object>>(
        XrplBinaryCodec.Decode(blob).ToJsonString(), XrplJsonOptions.Default);

// Both keys local — one call:
await client.SubmitAndWaitSponsored(payment, sponseeWallet, sponsorWallet);

// Keys on different devices — each side just calls Sign:
var sponsorPart = sponsorWallet.Sign(preparedTx);               // adds SponsorSignature
var final = sponseeWallet.Sign(Reparse(sponsorPart.TxBlob));    // adds the main signature
await client.SubmitRequest(final.TxBlob, true);

// Or let the submitting side finish everything:
await client.SubmitAndWait(partiallySignedTx, sponsorWallet); // sponsor finalizes a sponsee-signed tx
```

If a required signature is missing, `SubmitAndWait` fails fast with "transaction is not signed by all participants" instead of a node-side error. Multisig on either side stays portable: devices sign with `multisign: true` and `client.ComposeSignatures(parts)` routes the Signer entries into the right section by the ledger SignerLists (with a quorum-by-weight pre-check).

### Advanced — explicit helper flows

**V1 — Automatic (both keys available in one process):**

```csharp
JsonObject prepared = SponsorSigningHelper.PrepareForSigning(payment, sponseeWallet);
SignatureResult signed = SponsorSigningHelper.SignSponsored(prepared, sponseeWallet, sponsorWallet);
await client.SubmitRequest(signed.TxBlob, true);
```

**V2 — Parallel (keys on separate devices, combine later):**

```csharp
var sponsorSig  = sponsorWallet.SignAsSponsor(prepared);
var submitterSig = sponseeWallet.Sign(prepared);
var combined = SponsorSigningHelper.CombineSponsorSignatures(submitterSig.TxBlob, sponsorSig.TxBlob);
await client.SubmitRequest(combined.TxBlob, true);
```

**V3 — Sequential (sponsor first, then submitter finalizes):**

```csharp
var withSponsor = sponsorWallet.SignAsSponsor(prepared);
var final = SponsorSigningHelper.SubmitterSign(withSponsor.TxBlob, sponseeWallet);
await client.SubmitRequest(final.TxBlob, true);
```

---

## Sponsorship inside a Batch

Sponsorship composes with Batch (XLS-56), with rules mirrored from rippled `Batch::preflight`:

- **Reserve-sponsored inner transaction**: set `Sponsor` + `spfSponsorReserve` on the inner and add an **empty** `SponsorSignature` object as a marker. The marker makes the sponsor a *required batch signer* — the sponsor then authorizes the whole batch through the standard `Sign`, either single-signed or **through its SignerList** (a nested-multisig `BatchSigner.Signers` entry). No signature material ever goes inside the inner marker.
- **Fee-sponsored outer batch**: `Sponsor` + `spfSponsorFee` on the outer Batch; the sponsor co-signs the batch itself with a regular `SponsorSignature` via the standard `Sign`.
- **Forbidden by protocol** (`ValidateBatch` rejects these client-side): `spfSponsorReserve` on the outer Batch, fee sponsorship on inner transactions, and signature material inside inner co-signature markers.
- **Vault, LoanBroker and Loan inners** are refused by the node (rippled `kDisabledTxTypes` → `temINVALID_INNER_BATCH`) until the `LendingProtocolV1_2` amendment is enabled, and accepted after it. `ValidateBatch` does not check this: it has no ledger rules, so the node decides. Once the amendment is enabled, an inner `LoanSet` makes its `Counterparty` a required batch signer, like any other inner.

```csharp
// Inner reserve-sponsored TrustSet inside a Batch; the sponsor signs via its
// SignerList (Reparse — see "The simple path" above)
SignatureResult holderPart  = holder.Sign(batchDict);
SignatureResult signer1Part = signer1.Sign(Reparse(holderPart.TxBlob),  multisign: true, signingFor: sponsor.ClassicAddress);
SignatureResult signer2Part = signer2.Sign(Reparse(signer1Part.TxBlob), multisign: true, signingFor: sponsor.ClassicAddress);
SignatureResult final       = root.Sign(Reparse(signer2Part.TxBlob));
```

---

## Fees

Per rippled `Transactor::calculateBaseFee`, a **single-signed** `SponsorSignature` adds nothing to the base fee. Only when the sponsor co-signs with **multisig** does each signer nested in `SponsorSignature.Signers` add one base fee unit. The SDK's `Autofill` accounts for this automatically.

`GetFeePayer` says who was charged. `tx.GetFeePayer()` reads the transaction alone: the sponsor when `SponsorFlags` has `spfSponsorFee`, otherwise the `Delegate`, otherwise the `Account`. `tx.GetFeePayer(meta)` also reads the metadata and returns `null` when the fee came out of the prepaid `FeeAmount` of a `Sponsorship` object, because then no account's balance carries it. An existing `Sponsorship` object is preferred over the sponsor's account even when the sponsor co-signed.

---

## Ledger Objects

- `LOSponsorship` — the relationship object (see [Key Concepts](#key-concepts))
- `LOAccountRoot.SponsoredOwnerCount` / `SponsoringOwnerCount` / `SponsoringAccountCount`
- `LORippleState.HighSponsor` / `LowSponsor`
- `BaseLedgerEntry.Sponsor` — the sponsor of the reserve of any other entry, including an `AccountRoot` created with `tfSponsorCreatedAccount`, where it is the payment's `Account`

---

## Testing

The amendment is develop-only. The repository ships a nightly stand with `Sponsor` enabled at genesis:

```bash
docker compose -f .ci-config/docker-compose.batchv11.yml up -d --build
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestISponsorship"
```

Integration tests are gated by `AmendmentGuard`: on a node without the amendment they exit as *inconclusive* instead of failing. See `Tests/Xrpl.Tests/Integration/transactions/TestISponsorship.cs` for complete working examples, and the [Standalone Node Guide](StandaloneNode-Guide.md) for stand details.

---

## Common Errors

| Error | Meaning |
|---|---|
| `temDISABLED` | The `Sponsor` amendment is not active on the node |
| `tecNO_SPONSOR_PERMISSION` | The sponsorship does not cover this dimension, is out of budget, or the required `SponsorSignature` is missing/invalid |
| `terNO_PERMISSION` | Relationship state does not allow the operation (e.g. transferring someone else's sponsorship) |
| Validation: "invalid Sponsor" | The `Sponsor` field is not a string address (client-side check) |

*Русская версия: [Sponsorship-Guide.ru](Sponsorship-Guide.ru.md)*
