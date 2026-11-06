# Xrpl.BinaryCodec

Convert XRP Ledger transactions and ledger objects between JSON and the canonical binary format in C#, and build the bytes that a signature covers.

This is a base package of the [`Xrpl`](https://www.nuget.org/packages/Xrpl) SDK, which already depends on it and signs transactions for you. Install it on its own when you only need serialization. It targets .NET Standard 2.0.

```bash
dotnet add package Xrpl.BinaryCodec
```

## Encode and decode a transaction

`XrplBinaryCodec.Encode` returns the serialized object as a hex string, and `Decode` turns that string back into JSON:

```csharp
using System.Text.Json.Nodes;
using Xrpl.BinaryCodec;

JsonObject tx = new JsonObject
{
    ["TransactionType"] = "Payment",
    ["Account"] = "rBtXmAdEYcno9LWRnAGfT9qBxCeDvuVRZo",
    ["Destination"] = "rEqtEHKbinqm18wQSQGstmqg9SFpUELasT",
    ["Amount"] = "1000000",
    ["Fee"] = "12",
    ["Sequence"] = 1,
    ["SigningPubKey"] = ""
};

string blob = XrplBinaryCodec.Encode(tx);
// 12000024000000016140000000000F424068400000000000000C73008114777029549E95629525730029E0D0D9E4FFE7924C8314A2D0815DD52160FF1979A60C50B00C09ECD669D4

JsonNode decoded = XrplBinaryCodec.Decode(blob);
```

## Prepare a transaction for signing

Signing covers a different byte string than the one you submit. It starts with a hash prefix and leaves out the fields that aren't signed, such as `TxnSignature`. Continuing from the example above:

```csharp
string singleSign = XrplBinaryCodec.EncodeForSigning(tx);
// 53545800 followed by the serialized fields

string multiSign = XrplBinaryCodec.EncodeForMultiSigning(tx, "rEqtEHKbinqm18wQSQGstmqg9SFpUELasT");
// 534D5400 followed by the serialized fields and the signer's account ID
```

To sign the result, pass it to `XrplKeypairs.Sign` from [`Xrpl.Keypairs`](https://www.nuget.org/packages/Xrpl.Keypairs).

## More information

- [API reference](https://staticbit-io.github.io/XrplCSharp/)
- [Source code](https://github.com/StaticBit-io/XrplCSharp)
- [Binary format on xrpl.org](https://xrpl.org/docs/references/protocol/binary-format)
