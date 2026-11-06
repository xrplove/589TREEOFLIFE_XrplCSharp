# Xrpl.Keypairs

Generate XRP Ledger seeds, derive Ed25519 and secp256k1 keypairs and addresses, and sign and verify messages in C#.

This is a base package of the [`Xrpl`](https://www.nuget.org/packages/Xrpl) SDK, which already depends on it and offers `XrplWallet` for everyday key handling. Install it on its own when you only need key operations. It targets .NET Standard 2.1.

```bash
dotnet add package Xrpl.Keypairs
```

## Generate a seed and derive an address

`GenerateSeed` uses secp256k1 unless you pass `"ed25519"`. `DeriveKeypair` reads the algorithm from the seed. On the keypair, `Id()` returns the public key and `Pk()` the private key, both as hex strings:

```csharp
using Xrpl.Keypairs;

string seed = XrplKeypairs.GenerateSeed(algorithm: "ed25519");

IXrplKeyPair keypair = XrplKeypairs.DeriveKeypair(seed);
string publicKey = keypair.Id();
string privateKey = keypair.Pk();

string address = XrplKeypairs.DeriveAddress(publicKey);
```

Anyone who has the seed or the private key controls the account. Keep both out of logs and source control.

## Sign and verify a message

`Sign` and `Verify` take the message as a hex string or as a byte array, and pick the algorithm from the key. Continuing from the example above:

```csharp
string signature = XrplKeypairs.Sign("DEADBEEF", privateKey);

bool isValid = XrplKeypairs.Verify("DEADBEEF", signature, publicKey);
// true
```

To sign a transaction, build the message with `XrplBinaryCodec.EncodeForSigning` from [`Xrpl.BinaryCodec`](https://www.nuget.org/packages/Xrpl.BinaryCodec).

## More information

- [API reference](https://staticbit-io.github.io/XrplCSharp/)
- [Source code](https://github.com/StaticBit-io/XrplCSharp)
- [Cryptographic keys on xrpl.org](https://xrpl.org/docs/concepts/accounts/cryptographic-keys)
