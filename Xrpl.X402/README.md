# Xrpl.X402

> **Preview.** x402 (HTTP-402) agentic payments for the XRP Ledger, implementing the t54 **"XRPL exact scheme"** wire format. Lets an AI agent / client autonomously pay for HTTP resources (APIs, model inference, services) in **XRP** or **RLUSD/IOU**, and lets a server require payment for an endpoint.

📖 **Full documentation** — end-to-end flow (price → pay → receipt) with XRP and RLUSD/IOU examples: **[x402 Agentic Payments Guide](https://staticbit-io.github.io/XrplCSharp/X402-Guide.html)** · [API reference](https://staticbit-io.github.io/XrplCSharp/).

Two packages:

| Package | Role |
|---|---|
| `Xrpl.X402` | **Client** — a `DelegatingHandler` that detects HTTP 402, builds + locally signs an XRPL `Payment`, and retries with a `PAYMENT-SIGNATURE` header. |
| `Xrpl.X402.AspNetCore` | **Server** — a `RequirePayment` endpoint filter + a ledger-settling facilitator that returns 402, verifies the signed payment, and settles it on-ledger. |

Both build on the [`Xrpl`](https://www.nuget.org/packages/Xrpl) SDK. The client **signs but does not submit** — the merchant's facilitator settles the transaction (per the t54 scheme).

---

## Wire format (t54 XRPL exact scheme)

| Header | Direction | Body (base64-encoded JSON) |
|---|---|---|
| `PAYMENT-REQUIRED` | server → client (on 402) | `{ x402Version, accepts: [ {scheme:"exact", network, asset, payTo, amount, maxTimeoutSeconds, extra} ] }` |
| `PAYMENT-SIGNATURE` | client → server | `{ x402Version:2, accepted, payload:{ signedTxBlob, invoiceId } }` |
| `PAYMENT-RESPONSE` | server → client | `{ success, transaction, network, payer }` |

- `asset` is exactly `"XRP"` (amount in **drops**) or a 40-hex currency code (e.g. RLUSD) with `extra.issuer` and a decimal `amount`.
- The payment intent is bound to the XRPL transaction via the native `InvoiceID` field (`Payment.InvoiceID = SHA-256(invoiceId)`) and/or a `Memo` (`MemoData` = hex of the raw `invoiceId`), selectable via `X402IntentBinding` (default `Both`).
- `Payment.SourceTag` is taken from `extra.sourceTag` (the x402 protocol value `804681468`); `DestinationTag` from `extra.destinationTag` when present.

---

## Client usage

```csharp
using Xrpl.Client;
using Xrpl.Wallet;
using Xrpl.X402;

// A connected XRPL client + a wallet you control.
IXrplClient xrpl = /* your IXrplClient, connected */;
XrplWallet wallet = XrplWallet.FromSeed(seed);

// The signer autofills + signs locally (it never submits).
var signer = new XrplWalletX402Signer(xrpl, wallet);

var options = new X402ClientOptions
{
    Network = "xrpl:1",            // CAIP-2 network you will pay on
    MaxAmountDrops = 10_000_000,   // hard cap for XRP payments (10 XRP)
    // IouValueCaps["rIssuer..."] = 5m,   // REQUIRED to pay an IOU/RLUSD from that issuer
    // PayToAllowlist = { "rMerchant...", "rIssuer..." },  // optional allowlist
};

// Wrap any HttpClient with the payment handler.
var http = new HttpClient(new X402PaymentHandler(signer, options)
{
    InnerHandler = new HttpClientHandler()
});

// Transparent: a 402 is paid automatically and the resource is returned.
HttpResponseMessage resource = await http.GetAsync("https://api.example.com/paid");
```

### Security model (the client treats the merchant's 402 as untrusted)

- **Spending caps enforced *before* signing.** XRP is capped by `MaxAmountDrops` (always checked). **IOU/RLUSD fails closed**: a payment is refused unless an explicit per-issuer cap exists in `IouValueCaps`.
- **Optional `PayToAllowlist`** — when non-empty, both `payTo` and the IOU `issuer` must be allow-listed.
- **Anti-double-pay** — the client pays at most once per request; a repeated 402 throws.
- **Validity window** — `LastLedgerSequence` is capped by the requirement's `maxTimeoutSeconds`.
- All refusals throw `X402PaymentException` (`Reason` carries a machine code, e.g. `amount_over_cap`, `no_acceptable_requirement`, `payto_not_allowed`, `issuer_not_allowed`, `payment_rejected`).

---

## Server usage (`Xrpl.X402.AspNetCore`)

```csharp
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

IXrplClient xrpl = /* your connected IXrplClient */;
// One facilitator for every endpoint: its record of settled payments is what stops a replay.
IX402Facilitator facilitator = new LedgerSettlingFacilitator(xrpl);

app.MapGet("/paid", () => "premium content")
   .RequirePayment(facilitator, ctx => new PaymentRequirement
   {
       Scheme = "exact",
       Network = "xrpl:1",
       Asset = "XRP",
       PayTo = "rYourMerchantAddress...",
       Amount = "1000000",            // 1 XRP in drops
       MaxTimeoutSeconds = 60,
       Extra = { ["invoiceId"] = JsonSerializer.SerializeToElement("inv-123") }
   });
```

Without a `PAYMENT-SIGNATURE` the endpoint returns **402** + `PAYMENT-REQUIRED`. With a valid signature, `LedgerSettlingFacilitator` checks the blob against the endpoint's own requirement - a non-partial `Payment` of exactly the required amount and asset to `payTo`, bound to `extra.invoiceId` - refuses a blob it already settled, settles the transaction (`SubmitRequestAndWait`, waits for `tesSUCCESS`), checks `delivered_amount`, sets `PAYMENT-RESPONSE`, and runs your handler.

Two `IX402Facilitator` implementations ship:

- `LedgerSettlingFacilitator(IXrplClient)` — settles **locally** against your own connected node.
- `T54Facilitator(HttpClient, baseUrl)` — delegates verify + settle to an **external t54 facilitator** over HTTP (`POST /settle` with `{ paymentPayload, paymentRequirements }`). Default base URL is the t54 testnet facilitator.

Implement `IX402Facilitator` yourself for any other facilitator.

---

## Interop notes / configurability

**Intent binding** — how the payment id is bound to the XRPL transaction, via `X402ClientOptions.IntentBinding`. The id is read as any string from `extra.invoiceId` (key configurable via `InvoiceIdExtraKey`, default `"invoiceId"`). This matches the t54 reference payer exactly.

| Mode | Binding |
|---|---|
| `Both` (**default**) | sets both of the below — matches the t54 reference payer |
| `InvoiceIdField` | `Payment.InvoiceID = SHA-256(invoiceId)` (uppercase hex) |
| `Memo` | a single `MemoData` = uppercase hex of the UTF-8 `invoiceId` string |

The PAYMENT-SIGNATURE `payload` carries both `signedTxBlob` **and** `invoiceId`. For t54, the requirement's `extra.sourceTag` is stamped onto `Payment.SourceTag` (t54 enforces it — the x402 protocol sourceTag is `804681468`); IOU payments include a matching `SendMax`.

## Status & limitations (preview)

- **Assets:** XRP and RLUSD/IOU, exercised end-to-end against a standalone `rippled`.
- **Schemes:** only `exact` — the only scheme t54 advertises for XRPL (`GET /supported` → `{scheme:"exact", network:"xrpl:1"}`). Extensible, but no other XRPL scheme exists today.
- **Live t54 interop — CONFIRMED (XRP + RLUSD/IOU):** the client's PAYMENT-SIGNATURE is accepted by the **live t54 testnet facilitator** — `/verify` returns `isValid:true` and `/settle` settles on-chain. Both an XRP payment and an issued-currency (RLUSD-coded, own testnet issuer) payment with `SendMax` are verified live. Matching t54 requires: `payload` carrying `invoiceId`; `Payment.InvoiceID = SHA-256(invoiceId)`; a `MemoData` = hex(invoiceId); and the requirement's `extra.sourceTag` (`804681468`) stamped on the tx (t54 enforces SourceTag). `T54Facilitator` calls the real `/verify` + `/settle`. The live tests in `X402T54LiveInteropTests` (`TestT54LiveSettlesXrpOnTestnet`, `TestT54LiveSettlesRlusdOnTestnet`) run in `dotnet test`; they depend on the public testnet faucet + the t54 hosted facilitator, so they are kept out of the repo's `TestI`/`TestU` CI filters by name.
- **Verifiable Intent:** passthrough only — set `X402ClientOptions.VerifiableIntentProvider` and its `extensions` object is attached to each PAYMENT-SIGNATURE under `x402Secure.verifiableIntentChain`. The SD-JWT L1→L3 chain (Mastercard Agentic / Trustline) is **not** generated by this package — supply it from your own provider.

## References

- t54 XRPL x402 scheme: <https://xrpl-x402.t54.ai/docs/xrpl-scheme>
- x402 protocol: <https://github.com/x402-foundation/x402>
- XRPL agentic transactions: <https://xrpl.org/docs/agents/agentic-transactions>
