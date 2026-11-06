[![NuGet Badge](https://buildstats.info/nuget/Xrpl)](https://www.nuget.org/packages/Xrpl/)

# XrplCSharp

A pure C# implementation for interacting with the XRP Ledger, the `XrplCSharp` library simplifies the hardest parts of XRP Ledger interaction, like serialization and transaction signing, by providing native C# methods and models for [XRP Ledger transactions](https://xrpl.org/transaction-formats.html) and core server [API](https://xrpl.org/api-conventions.html) ([`rippled`](https://github.com/ripple/rippled)) objects.


```csharp
// create a network client
using System.Diagnostics;
using Xrpl.Client;
using Xrpl.Models.Methods;
using Xrpl.Wallet;

var client = new XrplClient("wss://s.altnet.rippletest.net:51233");
await client.Connect();
Debug.WriteLine("CONNECTED");

// create a wallet on the testnet
XrplWallet testWallet = XrplWallet.Generate();
await WalletSugar.FundWallet(client, testWallet);
Debug.WriteLine(testWallet);
// public_key: ED3CC1BBD0952A60088E89FA502921895FC81FBD79CAE9109A8FE2D23659AD5D56
// private_key: -HIDDEN -
// classic_address: rBtXmAdEYcno9LWRnAGfT9qBxCeDvuVRZo

// look up account info
string account = "rBtXmAdEYcno9LWRnAGfT9qBxCeDvuVRZo";
AccountInfoRequest request = new AccountInfoRequest(account);
AccountInfo accountInfo = await client.AccountInfo(request).Typed();
Debug.WriteLine(accountInfo);
```

Every request also hands back the bytes the node actually sent, beside the typed
projection. The typed model is lossy by nature — it drops members it has no property
for — so anything that has to *show* or *verify* what a node said reads the raw text:

```csharp
// continuing from the example above
XrplResponse<AccountInfo> response = await client.AccountInfo(request);

AccountInfo typed = response.Result;                // the projection
string asTheNodeSentIt = response.Raw.ToString();   // byte for byte, as the node sent it
string status = response.Status;                    // the envelope reaches you too
```

Three ways to read the same call, depending on what you need:

```csharp
AccountInfo info = await client.AccountInfo(request).Typed();   // the projection alone
var (info, raw) = await client.AccountInfo(request);            // both
XrplResponse<AccountInfo> r = await client.AccountInfo(request); // the whole envelope
```

## Installation and supported versions

The `Xrpl` library is available on [NuGet](https://www.nuget.org/packages/Xrpl/). Install with `dotnet`:

```
dotnet add package Xrpl
```

The library supports .NET 8, .NET 9, and .NET 10.

## Features

Use `XrplCSharp` to build C# applications that leverage the [XRP Ledger](https://xrpl.org/). The library helps with all aspects of interacting with the XRP Ledger, including:

* Key and wallet management
* Serialization
* Transaction Signing

`XrplCSharp` also provides:

* A network client — See [xrpl.clients](https://staticbit-io.github.io/XrplCSharp/xrpl.clients.html) for more information.
* Methods for inspecting accounts — See [XRPL Account Methods](https://staticbit-io.github.io/XrplCSharp/xrpl.account.html) for more information.
* Codecs for encoding and decoding addresses and other objects — See [Core Codecs](https://staticbit-io.github.io/XrplCSharp/xrpl.core.html) for more information.

## [Reference Documentation](https://staticbit-io.github.io/XrplCSharp/)

See the complete [`XrplCSharp` reference documentation](https://staticbit-io.github.io/XrplCSharp/).


## Usage

The following sections describe some of the most commonly used modules in the `XrplCSharp` library and provide sample code.

### Network client

Use the `Xrpl.Client` library to create a network client for connecting to the XRP Ledger.

```csharp
using System.Diagnostics;
using Xrpl.Client;
using Xrpl.Models.Methods;
using Xrpl.Wallet;

var client = new XrplClient("wss://s.altnet.rippletest.net:51233");
await client.Connect();
Debug.WriteLine("CONNECTED");
```

### Manage keys and wallets

#### `Xrpl.Wallet`

Use the [`Xrpl.Wallet`](https://staticbit-io.github.io/XrplCSharp/xrpl.wallet.html) module to create a wallet from a given seed or via a [Testnet faucet](https://xrpl.org/xrp-testnet-faucet.html).

To create a wallet from a seed (in this case, the value generated using [`Xrpl.Keypairs`](#xrpl-keypairs)):

```csharp
using System.Diagnostics;
using Xrpl.Wallet;
// ...
string seed = "s";
XrplWallet wallet = XrplWallet.FromSeed(seed);
Debug.WriteLine(wallet);
// pub_key: ED46949E414A3D6D758D347BAEC9340DC78F7397FEE893132AAF5D56E4D7DE77B0
// priv_key: -HIDDEN-
// classic_address: rG5ZvYsK5BPi9f1Nb8mhFGDTNMJhEhufn6
```

To create a wallet from a Testnet faucet:

```csharp
using System.Diagnostics;
using Xrpl.Wallet;
XrplWallet testWallet = XrplWallet.Generate();
await WalletSugar.FundWallet(client, testWallet);
Debug.WriteLine(testWallet.ClassicAddress);
// Classic address: rEQB2hhp3rg7sHj6L8YyR4GG47Cb7pfcuw
```

#### `Xrpl.Keypairs`

Use the [`Xrpl.Keypairs`](https://staticbit-io.github.io/XrplCSharp/xrpl.core.keypairs.html) module to generate seeds and derive keypairs and addresses from those seed values.

Here's an example of how to generate a `seed` value and derive an [XRP Ledger "classic" address](https://xrpl.org/cryptographic-keys.html#account-id-and-address) from that seed.


```csharp
using System.Diagnostics;
using Xrpl.Wallet;
// ...
XrplWallet wallet = XrplWallet.Generate();
string publicKey = wallet.PublicKey;
string privateKey = wallet.PrivateKey;
Debug.WriteLine("Here's the public key:");
Debug.WriteLine(publicKey);
Debug.WriteLine("Here's the private key:");
Debug.WriteLine(privateKey);
Debug.WriteLine("Store this in a secure place!");
```

### Serialize and sign transactions

To securely submit transactions to the XRP Ledger, you need to first serialize data from JSON and other formats into the [XRP Ledger's canonical format](https://xrpl.org/serialization.html), then to [authorize the transaction](https://xrpl.org/transaction-basics.html#authorizing-transactions) by digitally signing it with the account's private key. The `XrplCSharp` library provides several methods to simplify this process.

```csharp
using System.Diagnostics;
using Xrpl.Models.Transactions;
using Xrpl.Models.Methods;
using Xrpl.Sugar;

string classicAddress = "rBtXmAdEYcno9LWRnAGfT9qBxCeDvuVRZo";
AccountInfoRequest request = new AccountInfoRequest(wallet.ClassicAddress);
AccountInfo accountInfo = await client.AccountInfo(request).Typed();

Payment tx = new Payment()
{
    Account = wallet.ClassicAddress,
    Destination = "rEqtEHKbinqm18wQSQGstmqg9SFpUELasT",
    Amount = new Xrpl.Models.Common.Currency { ValueAsXrp = 1 },
};

TransactionSummary response = await client.SubmitAndWait(tx, wallet);
Debug.WriteLine(response);
```

#### Get fee from the XRP Ledger

In most cases, you can specify the minimum [transaction cost](https://xrpl.org/transaction-cost.html#current-transaction-cost) of `"10"` for the `fee` field unless you have a strong reason not to. But if you want to get the [current load-balanced transaction cost](https://xrpl.org/transaction-cost.html#current-transaction-cost) from the network, you can use the `Fees` function:

```csharp
using System.Diagnostics;
using Xrpl.Models.Methods;
FeeRequest feeRequest = new FeeRequest();
Fee fee = await client.Fee(feeRequest).Typed();
Debug.WriteLine(fee);
// 10
```

#### Read transaction history through the `I` interfaces

A transaction that comes back from the ledger — `account_tx`, `tx`, `SubmitAndWait` — is a *response*
type: `NFTokenCreateOfferResponse`, not `NFTokenCreateOffer`. Matching on the request type compiles,
warns about nothing and finds nothing:

```csharp
foreach (TransactionSummary summary in history.Transactions)
{
    if (summary.Transaction is NFTokenCreateOffer request) { }  // never matches
    if (summary.Transaction is INFTokenCreateOffer offer)       // this is the one
    {
        Debug.WriteLine(offer.NFTokenID);
    }
}
```

Request and response types come in pairs that share an `I` interface — use those to read what the
ledger sent, and the request types only to send. Every pair has one, with no exceptions, and a
unit test holds the SDK to that.


## Contributing

If you want to contribute to this project, see [CONTRIBUTING.md].

### Mailing Lists

We have a low-traffic mailing list for announcements of new `XrplCSharp` releases. (About 1 email per week)

+ [Subscribe to xrpl-announce](https://groups.google.com/g/xrpl-announce)

If you're using the XRP Ledger in production, you should run a [rippled server](https://github.com/ripple/rippled) and subscribe to the ripple-server mailing list as well.

+ [Subscribe to ripple-server](https://groups.google.com/g/ripple-server)


## License

The `XrplCSharp` library is licensed under the Apache License 2.0. See [LICENSE] for more information.


[CONTRIBUTING.md]: https://github.com/StaticBit-io/XrplCSharp/blob/dev/CONTRIBUTING.md
[LICENSE]: https://github.com/StaticBit-io/XrplCSharp/blob/dev/LICENSE

## Repository Credit

This project is originally based on work by [Chris Williams](https://github.com/chriswill), and descends from the [`XrplCSharp`](https://github.com/Transia-RnD/XrplCSharp) C# port by [Denis Angell](https://github.com/dangell7) ([Transia-RnD](https://github.com/Transia-RnD)).

Currently maintained and developed by [StaticBit-io](https://github.com/StaticBit-io).
