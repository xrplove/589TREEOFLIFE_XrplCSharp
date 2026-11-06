# Xrpl.AddressCodec

Encode, decode, and validate XRP Ledger base58 values in C#: classic addresses, X-addresses, seeds, and public keys.

This is a base package of the [`Xrpl`](https://www.nuget.org/packages/Xrpl) SDK, which already depends on it. Install it on its own when you only need address handling. It targets .NET Standard 2.0.

```bash
dotnet add package Xrpl.AddressCodec
```

## Work with classic addresses

`XrplCodec` validates a classic address and converts it to and from its 20-byte account ID:

```csharp
using Xrpl.AddressCodec;

string address = "rEqtEHKbinqm18wQSQGstmqg9SFpUELasT";

bool isValid = XrplCodec.IsValidClassicAddress(address);
// true

byte[] accountId = XrplCodec.DecodeAccountID(address);
string sameAddress = XrplCodec.EncodeAccountID(accountId);
// rEqtEHKbinqm18wQSQGstmqg9SFpUELasT
```

`XrplCodec` also encodes and decodes seeds (`EncodeSeed`, `DecodeSeed`), account public keys (`EncodeAccountPublic`, `DecodeAccountPublic`), and node public keys (`EncodeNodePublic`, `DecodeNodePublic`).

## Work with X-addresses

An X-address packs a classic address, an optional destination tag, and a network flag into one string. `XrplAddressCodec` converts in both directions. Pass `false` for `isTest` on Mainnet:

```csharp
using Xrpl.AddressCodec;

string xAddress = XrplAddressCodec.ClassicAddressToXAddress("rEqtEHKbinqm18wQSQGstmqg9SFpUELasT", 12345, false);
// XVHvsC9LTkrK3bqJTsVFvjSAM7GGjBNBYNan5dcMZHubXQn

XrplAddressCodec.CodecAddress decoded = XrplAddressCodec.XAddressToClassicAddress(xAddress);
// decoded.ClassicAddress: rEqtEHKbinqm18wQSQGstmqg9SFpUELasT
// decoded.Tag: 12345
// decoded.Test: False
```

## More information

- [API reference](https://staticbit-io.github.io/XrplCSharp/)
- [Source code](https://github.com/StaticBit-io/XrplCSharp)
- [Addresses on xrpl.org](https://xrpl.org/docs/concepts/accounts/addresses)
