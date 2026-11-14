using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Sugar;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

using Common = Xrpl.Models.Common.Common;

namespace MyApp;

/// <summary>
/// Specifies the type of XRPL node to connect to.
/// </summary>
public enum TestNodeType
{
    TestNet,
    DevNet,
    Standalone,
    MainNet
}

/// <summary>
/// Fluent API builder for creating test accounts with various XRPL objects.
/// All objects are owned by primary account (set via AddPrimaryAccount), IssuerAccount serves as counterparty/issuer.
/// Usage: await new TestAccountBuilder(client, nodeType)
///     .AddPrimaryAccount(myWallet)
///     .AddTrustlines()
///     .AddAmmPools()
///     .AddNFTs()
///     .AddOffers()
///     .BuildAsync();
/// </summary>
public class TestAccountBuilder
{
    #region Static Helper Wallets

    /// <summary>Issuer account - issues tokens/MPT, acts as counterparty for checks/escrows.</summary>
    public static readonly XrplWallet IssuerAccount = XrplWallet.FromNormalizedText("test builder issuer account");

    /// <summary>Second issuer account - used for deep freeze tests.</summary>
    public static readonly XrplWallet Issuer2Account = XrplWallet.FromNormalizedText("test builder issuer deep and deep-freezed account");

    /// <summary>Third issuer account - used for require auth tests.</summary>
    public static readonly XrplWallet Issuer3Account = XrplWallet.FromNormalizedText("test builder issuer require auth account");

    /// <summary>Signer 1 for multi-sign.</summary>
    public static readonly XrplWallet Signer1Account = XrplWallet.FromNormalizedText("test builder signer 1 account");

    /// <summary>Signer 2 for multi-sign.</summary>
    public static readonly XrplWallet Signer2Account = XrplWallet.FromNormalizedText("test builder signer 2 account");

    /// <summary>Signer 3 for multi-sign.</summary>
    public static readonly XrplWallet Signer3Account = XrplWallet.FromNormalizedText("test builder signer 3 account");

    /// <summary>Helper wallets for batch funding (excludes primary - funded separately).</summary>
    public static readonly XrplWallet[] HelperWallets = new[]
    {
        IssuerAccount, Issuer2Account, Issuer3Account, Signer1Account, Signer2Account, Signer3Account
    };

    #endregion

    #region Token Currency Codes

    public static readonly string[] TokenCodes = new[]
    {
        "USD", "EUR", "GBP", "JPY", "CNY", "TSTS".CurrencyToHex()
    };

    #endregion

    #region Private Fields

    private readonly IXrplClient _client;
    private readonly TestNodeType _nodeType;
    private readonly List<Func<Task>> _buildActions = new();
    private XrplWallet _primaryAccount;

    private const decimal MinBalanceThreshold = 50m;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new TestAccountBuilder instance.
    /// </summary>
    /// <param name="client">Connected XRPL client.</param>
    /// <param name="nodeType">Type of node for funding logic.</param>
    public TestAccountBuilder(IXrplClient client, TestNodeType nodeType = TestNodeType.TestNet)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _nodeType = nodeType;
    }

    #endregion

    #region Fluent API Methods

    /// <summary>
    /// Sets the primary account that will own all created objects.
    /// Must be called before BuildAsync().
    /// </summary>
    /// <param name="wallet">The wallet that will own all objects.</param>
    public TestAccountBuilder AddPrimaryAccount(XrplWallet wallet)
    {
        _primaryAccount = wallet ?? throw new ArgumentNullException(nameof(wallet));
        return this;
    }

    /// <summary>
    /// Adds trustlines from primary account to IssuerAccount for specified tokens.
    /// </summary>
    /// <param name="tokenCodes">Token codes to create trustlines for. Uses defaults if null.</param>
    public TestAccountBuilder AddTrustlines(params string[] tokenCodes)
    {
        var codes = tokenCodes.Length > 0 ? tokenCodes : TokenCodes;
        _buildActions.Add(() => CreateTrustlinesAsync(codes));
        return this;
    }
    public TestAccountBuilder AddTokensAsync(params string[] tokenCodes)
    {
        var codes = tokenCodes.Length > 0 ? tokenCodes : TokenCodes;
        _buildActions.Add(() => SendTokensAsync(codes));
        return this;
    }

    /// <summary>
    /// Adds AMM pools for specified number of token pairs.
    /// Primary account creates the pools with tokens from IssuerAccount.
    /// </summary>
    /// <param name="count">Number of AMM pools to create (default 5).</param>
    public TestAccountBuilder AddAmmPools(int count = 5)
    {
        _buildActions.Add(() => CreateAmmPoolsAsync(count));
        return this;
    }
    public TestAccountBuilder AddIssuerAmmPools(int count = 5)
    {
        _buildActions.Add(() => CreateIssuerAmmPoolsAsync(count));
        return this;
    }

    /// <summary>
    /// Mints NFTs owned by primary account.
    /// </summary>
    /// <param name="count">Number of NFTs to mint (default 3).</param>
    public TestAccountBuilder AddNFTs(int count = 3)
    {
        _buildActions.Add(() => CreateNFTsAsync(count));
        return this;
    }

    /// <summary>
    /// Creates NFT sell offers from primary account.
    /// </summary>
    /// <param name="i"></param>
    public TestAccountBuilder AddNFTOffers(int i = 5)
    {
        _buildActions.Add(() => CreateNFTOffersAsync(i));
        return this;
    }

    /// <summary>
    /// Creates DEX offers for token trading from primary account.
    /// </summary>
    /// <param name="count">Number of offers to create (default 5).</param>
    public TestAccountBuilder AddOffers(int count = 5)
    {
        _buildActions.Add(() => CreateDEXOffersAsync(count));
        return this;
    }

    public TestAccountBuilder AddIssuerOffers(int count = 5)
    {
        _buildActions.Add(() => CreateDEXOffersForIssuerAsync(count));
        return this;
    }

    /// <summary>
    /// Creates MPT issuance (IssuerAccount) and authorizes primary account as holder.
    /// </summary>
    public TestAccountBuilder AddMPTokens()
    {
        _buildActions.Add(() => CreateMPTokensAsync());
        return this;
    }

    /// <summary>
    /// Creates ticket objects on primary account.
    /// </summary>
    /// <param name="count">Number of tickets to create (default 10).</param>
    public TestAccountBuilder AddTickets(int count = 10)
    {
        _buildActions.Add(() => CreateTicketsAsync(count));
        return this;
    }

    /// <summary>
    /// Creates check objects from primary account to IssuerAccount.
    /// </summary>
    /// <param name="count">Number of checks to create (default 3).</param>
    public TestAccountBuilder AddChecks(int count = 3)
    {
        _buildActions.Add(() => CreateChecksAsync(count));
        return this;
    }
    public TestAccountBuilder AddIncomeChecks(int count = 3)
    {
        _buildActions.Add(() => CreateIncomeChecksAsync(count));
        return this;
    }

    /// <summary>
    /// Sets up SignerList on primary account with Signer1-3.
    /// </summary>
    public TestAccountBuilder AddSignerList()
    {
        _buildActions.Add(() => CreateSignerListAsync());
        return this;
    }

    /// <summary>
    /// Creates escrow from primary account to IssuerAccount.
    /// </summary>
    public TestAccountBuilder AddEscrows()
    {
        _buildActions.Add(() => CreateEscrowsAsync());
        return this;
    }
    public TestAccountBuilder AddIncomeEscrows()
    {
        _buildActions.Add(() => CreateIncomeEscrowsAsync());
        return this;
    }

    /// <summary>
    /// Creates a deep freeze test scenario with Issuer2Account:
    /// trustline from primary, token payment, then deep freeze.
    /// </summary>
    /// <param name="currencyCode">Currency code for the deep-frozen token.</param>
    public TestAccountBuilder AddDeepFreezeToken(string currencyCode = "DFZ")
    {
        _buildActions.Add(() => CreateDeepFreezeTokenAsync(currencyCode));
        return this;
    }

    /// <summary>
    /// Creates a require-auth test scenario with Issuer3Account:
    /// trustline from primary to Issuer3 (which has asfRequireAuth).
    /// If <paramref name="auth"/> is true, Issuer3 authorizes the trustline and sends tokens.
    /// If false, the trustline remains unauthorized (payments will fail with tecNO_AUTH).
    /// </summary>
    /// <param name="currencyCode">Currency code for the authorized token.</param>
    /// <param name="auth">Whether to authorize the trustline and send tokens.</param>
    public TestAccountBuilder AddRequireAuthTest(string currencyCode = "ATH", bool auth = false)
    {
        _buildActions.Add(() => CreateRequireAuthTestAsync(currencyCode, auth));
        return this;
    }

    #endregion

    #region Build Method

    /// <summary>
    /// Executes all configured build actions.
    /// First funds all required accounts, then creates objects.
    /// </summary>
    public async Task BuildAsync()
    {
        if (_primaryAccount == null)
            throw new InvalidOperationException("Primary account not set. Call AddPrimaryAccount() before BuildAsync().");

        Console.WriteLine($"[TestAccountBuilder] Starting build on {_nodeType}...");
        Console.WriteLine($"[TestAccountBuilder] Primary account: {_primaryAccount.ClassicAddress}");

        await FundAllAccountsAsync();
        await EnableDefaultRippleAsync();
        await EnableIssuer2FlagsAsync();
        await EnableIssuer3FlagsAsync();

        Console.WriteLine($"[TestAccountBuilder] Executing {_buildActions.Count} build actions...");

        foreach (var action in _buildActions)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] Action failed: {ex.Message}");
            }
        }

        Console.WriteLine("[TestAccountBuilder] Build completed!");
    }

    #endregion

    #region Funding Methods

    private async Task FundAllAccountsAsync()
    {
        await TryFundWalletAsync(_primaryAccount);

        foreach (var wallet in HelperWallets)
        {
            await TryFundWalletAsync(wallet);
        }
    }

    private async Task TryFundWalletAsync(XrplWallet wallet)
    {
        try
        {
            var balance = await _client.GetXrpFreeBalance(wallet.ClassicAddress);
            Console.WriteLine($"[TestAccountBuilder] Balance {wallet.ClassicAddress}: {balance} XRP");

            if (balance <= MinBalanceThreshold)
            {
                await FundWalletAsync(wallet);
            }
        }
        catch (Exception)
        {
            await FundWalletAsync(wallet);
            Console.WriteLine($"[TestAccountBuilder] Funded new account {wallet.ClassicAddress}");
        }
    }

    private async Task FundWalletAsync(XrplWallet wallet)
    {
        if (_nodeType == TestNodeType.Standalone)
        {
            await StandAloneUtils.FundAccount(_client, wallet);
        }
        else if (_nodeType == TestNodeType.TestNet || _nodeType == TestNodeType.DevNet)
        {
            await FundFromFaucetAsync(wallet);
        }
        else
        {
            throw new InvalidOperationException($"Cannot fund wallet on {_nodeType}");
        }
    }

    private async Task FundFromFaucetAsync(XrplWallet wallet)
    {
        var result = await _client.FundWallet(wallet);
        Console.WriteLine($"[TestAccountBuilder] Faucet funded {wallet.ClassicAddress}: {result.Balance} XRP");
    }

    private async Task LedgerAcceptAsync()
    {
        if (_nodeType != TestNodeType.Standalone) return;

        try
        {
            await _client.Request(new Dictionary<string, object> { { "command", "ledger_accept" } });
        }
        catch { }
    }

    private async Task EnableDefaultRippleAsync()
    {
        try
        {
            AccountInfoRequest infoRequest = new AccountInfoRequest(IssuerAccount.ClassicAddress);
            AccountInfo info = await _client.AccountInfo(infoRequest).Typed();
            if (info.AccountFlags.DefaultRipple)
            {
                Console.WriteLine("[TestAccountBuilder] DefaultRipple: already enabled, skipping");
                return;
            }
        }
        catch { }

        try
        {
            AccountSet accountSet = new AccountSet
            {
                Account = IssuerAccount.ClassicAddress,
                SetFlag = AccountSetAsfFlags.asfDefaultRipple
            };

            var autofilled = await _client.Autofill(accountSet);
            var response = await _client.Submit(autofilled, IssuerAccount, true);
            Console.WriteLine($"[TestAccountBuilder] DefaultRipple");

            if (_nodeType == TestNodeType.Standalone)
                await LedgerAcceptAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TestAccountBuilder] DefaultRipple failed: {ex.Message}");
        }
    }

    private async Task EnableIssuer2FlagsAsync()
    {
        bool needDefaultRipple = true;
        bool needTrustLineLocking = true;

        try
        {
            AccountInfoRequest infoRequest = new AccountInfoRequest(Issuer2Account.ClassicAddress);
            AccountInfo info = await _client.AccountInfo(infoRequest).Typed();
            needDefaultRipple = !info.AccountFlags.DefaultRipple;
            needTrustLineLocking = !info.AccountFlags.AllowTrustLineLocking;
        }
        catch { }

        if (needDefaultRipple)
        {
            try
            {
                AccountSet accountSet = new AccountSet
                {
                    Account = Issuer2Account.ClassicAddress,
                    SetFlag = AccountSetAsfFlags.asfDefaultRipple
                };
                var autofilled = await _client.Autofill(accountSet);
                await _client.SubmitAndWait(autofilled, Issuer2Account);
                Console.WriteLine("[TestAccountBuilder] Issuer2: DefaultRipple enabled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] Issuer2 DefaultRipple failed: {ex.Message}");
            }
        }

        if (needTrustLineLocking)
        {
            try
            {
                AccountSet accountSet = new AccountSet
                {
                    Account = Issuer2Account.ClassicAddress,
                    SetFlag = AccountSetAsfFlags.asfAllowTrustLineLocking
                };
                var autofilled = await _client.Autofill(accountSet);
                await _client.SubmitAndWait(autofilled, Issuer2Account);
                Console.WriteLine("[TestAccountBuilder] Issuer2: AllowTrustLineLocking enabled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] Issuer2 AllowTrustLineLocking failed: {ex.Message}");
            }
        }
    }

    private async Task EnableIssuer3FlagsAsync()
    {
        bool needDefaultRipple = true;
        bool needRequireAuth = true;
        bool needTrustLineLocking = true;

        try
        {
            AccountInfoRequest infoRequest = new AccountInfoRequest(Issuer3Account.ClassicAddress);
            AccountInfo info = await _client.AccountInfo(infoRequest).Typed();
            needDefaultRipple = !info.AccountFlags.DefaultRipple;
            needRequireAuth = !info.AccountFlags.RequireAuthorization;
            needTrustLineLocking = !info.AccountFlags.AllowTrustLineLocking;
        }
        catch { }

        if (needDefaultRipple)
        {
            try
            {
                AccountSet accountSet = new AccountSet
                {
                    Account = Issuer3Account.ClassicAddress,
                    SetFlag = AccountSetAsfFlags.asfDefaultRipple
                };
                var autofilled = await _client.Autofill(accountSet);
                await _client.SubmitAndWait(autofilled, Issuer3Account);
                Console.WriteLine("[TestAccountBuilder] Issuer3: DefaultRipple enabled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] Issuer3 DefaultRipple failed: {ex.Message}");
            }
        }

        if (needRequireAuth)
        {
            try
            {
                AccountSet accountSet = new AccountSet
                {
                    Account = Issuer3Account.ClassicAddress,
                    SetFlag = AccountSetAsfFlags.asfRequireAuth
                };
                var autofilled = await _client.Autofill(accountSet);
                await _client.SubmitAndWait(autofilled, Issuer3Account);
                Console.WriteLine("[TestAccountBuilder] Issuer3: RequireAuth enabled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] Issuer3 RequireAuth failed: {ex.Message}");
            }
        }

        if (needTrustLineLocking)
        {
            try
            {
                AccountSet accountSet = new AccountSet
                {
                    Account = Issuer3Account.ClassicAddress,
                    SetFlag = AccountSetAsfFlags.asfAllowTrustLineLocking
                };
                var autofilled = await _client.Autofill(accountSet);
                await _client.SubmitAndWait(autofilled, Issuer3Account);
                Console.WriteLine("[TestAccountBuilder] Issuer3: AllowTrustLineLocking enabled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] Issuer3 AllowTrustLineLocking failed: {ex.Message}");
            }
        }
    }

    #endregion

    #region Existence Checks

    private async Task<bool> TrustlineExistsAsync(string currencyCode, string issuer, string account)
    {
        try
        {
            var line = await _client.LedgerEntry(
                new LedgerEntryRequest()
                {
                    RippleState = new RippleStateQuery()
                    {
                        Addresses = [issuer,account],
                        Currency = currencyCode
                    }
                });
            return true;
        }
        catch(Exception e)
        {
            var info = e.Classify();
            Console.WriteLine($"[TrustlineExistsAsync] failed: {info.UserMessage}");
            return false;
        }
    }

    private async Task<bool> AmmExistsAsync(string currencyCode, string issuer)
    {
        try
        {
            var request = new AMMInfoRequest
            {
                Asset = new Xrpl.Models.Common.Common.IssuedCurrency { Currency = "XRP" },
                Asset2 = new Xrpl.Models.Common.Common.IssuedCurrency { Currency = currencyCode, Issuer = issuer }
            };
            var response = await _client.AmmInfo(request).Typed();
            return response?.Amm != null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> SignerListExistsAsync()
    {
        try
        {
            var request = new AccountInfoRequest(_primaryAccount.ClassicAddress) { SignerLists = true };
            var response = await _client.AccountInfo(request).Typed();
            return response.SignerLists != null && response.SignerLists.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<int> GetTicketCountAsync()
    {
        try
        {
            var request = new AccountObjectsRequest(_primaryAccount.ClassicAddress) { Type = LedgerEntryType.Ticket };
            var response = await _client.AccountObjects(request).Typed();
            return response.AccountObjectList?.Count ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<bool> MptIssuanceExistsAsync()
    {
        try
        {
            var request = new AccountObjectsRequest(IssuerAccount.ClassicAddress) { Type = LedgerEntryType.MPTokenIssuance };
            var response = await _client.AccountObjects(request).Typed();
            return response.AccountObjectList?.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Object Creation Methods

    private async Task CreateTrustlinesAsync(string[] tokenCodes)
    {
        Console.WriteLine($"[TestAccountBuilder] Creating trustlines on primary account...");
        uint? seq = null;
        var counter = 0;
        foreach (var code in tokenCodes)
        {
            counter++;
            try
            {
                if (await TrustlineExistsAsync(code, IssuerAccount.ClassicAddress, _primaryAccount.ClassicAddress))
                {
                    Console.WriteLine($"[TestAccountBuilder] TrustSet {code.CurrencyReadableName()}: already exists, skipping");
                    continue;
                }

                var trustSet = new TrustSet
                {
                    Account = _primaryAccount.ClassicAddress,
                    LimitAmount = new Currency
                    {
                        CurrencyCode = code,
                        Issuer = IssuerAccount.ClassicAddress,
                        Value = "10000000"
                    },
                    Sequence = seq
                };

                var autofilled = await _client.Autofill(trustSet);
                seq = await SendAsync(tokenCodes.Length, counter, tokenCodes.Length, autofilled, _primaryAccount);
                Console.WriteLine($"[TestAccountBuilder] TrustSet {code.CurrencyReadableName()}");

                if (_nodeType == TestNodeType.Standalone)
                    await LedgerAcceptAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] TrustSet {code.CurrencyReadableName()} failed: {ex.Message}");
            }
        }
    }

    private async Task SendTokensAsync(string[] tokenCodes)
    {
        Console.WriteLine($"[TestAccountBuilder] Send tokens to primary account...");
        uint? seq = null;
        var counter = 0;
        foreach (var code in tokenCodes)
        {
            counter++;
            try
            {
                if (await TrustlineExistsAsync(code, IssuerAccount.ClassicAddress, _primaryAccount.ClassicAddress) == false)
                {
                    Console.WriteLine($"[TestAccountBuilder] TrustSet {code.CurrencyReadableName()}: not found, skipping");
                    continue;
                }

                var payment = new Payment()
                {
                    Account = IssuerAccount.ClassicAddress,
                    Amount = new Currency
                    {
                        CurrencyCode = code,
                        Issuer = IssuerAccount.ClassicAddress,
                        Value = "100"
                    },
                    Destination = _primaryAccount.ClassicAddress,
                    Sequence = seq
                };

                var autofilled = await _client.Autofill(payment);
                seq = await SendAsync(tokenCodes.Length, counter, tokenCodes.Length, autofilled, IssuerAccount);
                Console.WriteLine($"[TestAccountBuilder] Payment {code.CurrencyReadableName()}");

                if (_nodeType == TestNodeType.Standalone)
                    await LedgerAcceptAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] Payment {code.CurrencyReadableName()} failed: {ex.Message}");
            }
        }
    }

    private async Task CreateAmmPoolsAsync(int count)
    {
        Console.WriteLine($"[TestAccountBuilder] Creating AMM pools from primary account...");
        for (int i = 0; i < count && i < TokenCodes.Length; i++)
        {
            var code = TokenCodes[i];

            try
            {

                if (await AmmExistsAsync(code, IssuerAccount.ClassicAddress))
                {
                    try
                    {
                        var ammDeposit = new AMMDeposit()
                        {
                            Account = _primaryAccount.ClassicAddress,
                            Asset = new Common.IssuedCurrency()
                            {
                                Currency = "XRP"
                            },
                            Asset2 = new Common.IssuedCurrency()
                            {
                                Currency = code,
                                Issuer = IssuerAccount.ClassicAddress
                            },
                            Amount = new Currency
                            {
                                ValueAsXrp = 10
                            },
                            Amount2 = new Currency
                            {
                                CurrencyCode = code,
                                Issuer = IssuerAccount.ClassicAddress,
                                Value = "10"
                            },
                            Flags = AMMDepositFlags.tfTwoAsset
                        };

                        await _client.SubmitAndWait(ammDeposit, _primaryAccount, true);

                        Console.WriteLine($"[TestAccountBuilder] AMMDeposit XRP/{code.CurrencyReadableName()}");
                    }
                    catch (Exception e)
                    {
                        var info = e.Classify();
                        Console.WriteLine($"[TestAccountBuilder] AMMDeposit XRP/{code.CurrencyReadableName()} failed: {info.UserMessage}");
                    }
                    finally
                    {
                    }
                    continue;

                }

                if (!await TrustlineExistsAsync(code, IssuerAccount.ClassicAddress, _primaryAccount.ClassicAddress))
                {
                    var trustSet = new TrustSet
                    {
                        Account = _primaryAccount.ClassicAddress,
                        LimitAmount = new Currency
                        {
                            CurrencyCode = code,
                            Issuer = IssuerAccount.ClassicAddress,
                            Value = "10000000"
                        },
                    };
                    await _client.SubmitAndWait(trustSet, _primaryAccount, true);
                }

                var payment = new Payment
                {
                    Account = IssuerAccount.ClassicAddress,
                    Destination = _primaryAccount.ClassicAddress,
                    Amount = new Currency
                    {
                        CurrencyCode = code,
                        Issuer = IssuerAccount.ClassicAddress,
                        Value = "10000"
                    },
                };
                await _client.SubmitAndWait(payment, IssuerAccount, true);
                var ammCreate = new AMMCreate
                {
                    Account = _primaryAccount.ClassicAddress,
                    Amount = new Currency { ValueAsXrp = 10 },
                    Amount2 = new Currency
                    {
                        CurrencyCode = code,
                        Issuer = IssuerAccount.ClassicAddress,
                        Value = "10"
                    },
                    TradingFee = 500,
                };

                await _client.SubmitAndWait(ammCreate, _primaryAccount, true);
                Console.WriteLine($"[TestAccountBuilder] AMMCreate XRP/{code.CurrencyReadableName()}");

                if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
            }
            catch (Exception ex)
            {
                var info = ex.Classify();
                Console.WriteLine($"[TestAccountBuilder] AMMDeposit XRP/{code.CurrencyReadableName()} failed: {info.UserMessage}");
            }
        }
    }
    private async Task CreateIssuerAmmPoolsAsync(int count)
    {
        Console.WriteLine($"[TestAccountBuilder] Creating AMM pools from issuer account...");
        for (int i = 0; i < count && i < TokenCodes.Length; i++)
        {
            try
            {
                var code = TokenCodes[i];

                if (await AmmExistsAsync(code, IssuerAccount.ClassicAddress))
                {
                    var ammDeposit = new AMMDeposit()
                    {
                        Account = IssuerAccount.ClassicAddress,
                        Asset = new Common.IssuedCurrency(){Currency = "XRP"},
                        Asset2 = new Common.IssuedCurrency() { Currency = code, Issuer = IssuerAccount.ClassicAddress },
                        Amount = new Currency { ValueAsXrp = 10 },
                        Amount2 = new Currency
                        {
                            CurrencyCode = code,
                            Issuer = IssuerAccount.ClassicAddress,
                            Value = "10"
                        },
                        Flags = AMMDepositFlags.tfTwoAsset
                    };

                    await _client.SubmitAndWait(ammDeposit, IssuerAccount, true);
                    Console.WriteLine($"[TestAccountBuilder] AMMDeposit XRP/{code.CurrencyReadableName()}");
                    continue;
                }

                var ammCreate = new AMMCreate
                {
                    Account = IssuerAccount.ClassicAddress,
                    Amount = new Currency { ValueAsXrp = 10 },
                    Amount2 = new Currency
                    {
                        CurrencyCode = code,
                        Issuer = IssuerAccount.ClassicAddress,
                        Value = "10"
                    },
                    TradingFee = 500,
                };

                await _client.SubmitAndWait(ammCreate, IssuerAccount, true);
                Console.WriteLine($"[TestAccountBuilder] AMMCreate XRP/{code.CurrencyReadableName()}");

                if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] AMMCreate failed: {ex.Message}");
            }
        }
    }

    private async Task CreateNFTsAsync(int count)
    {
        Console.WriteLine($"[TestAccountBuilder] Minting {count} NFTs on primary account...");
        uint? seq = null;
        for (int i = 0; i < count; i++)
        {
            try
            {
                var nftMint = new NFTokenMint
                {
                    Account = _primaryAccount.ClassicAddress,
                    NFTokenTaxon = (uint)i,
                    Flags = NFTokenMintFlags.tfTransferable | NFTokenMintFlags.tfBurnable | NFTokenMintFlags.tfMutable,
                    URI = ConvertStringToHex("ipfs://bafkreigbnsf4lgajtfe76tziimcux22oknqjjpkvqqfb2msznpdctwi2wy"),
                    Sequence = seq,
                };

                var autofilled = await _client.Autofill(nftMint);
                seq = await SendAsync(count, i, count, autofilled, _primaryAccount);
                Console.WriteLine($"[TestAccountBuilder] NFTokenMint #{i}");

                if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] NFTokenMint failed: {ex.Message}");
            }
        }
    }
    internal static string ConvertStringToHex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Xrpl.AddressCodec.Utils.ToHex(bytes);
    }

    private async Task CreateNFTOffersAsync(int i)
    {
        Console.WriteLine("[TestAccountBuilder] Creating NFT offers from primary account...");

        try
        {
            var nftsResponse = await _client.AccountNFTs(new AccountNFTsRequest(_primaryAccount.ClassicAddress)).Typed();
            if (nftsResponse.NFTs == null || nftsResponse.NFTs.Count == 0)
            {
                Console.WriteLine("[TestAccountBuilder] No NFTs found to create offers for");
                return;
            }

            uint? seq = null;
            var counter = 0;
            foreach (var nft in nftsResponse.NFTs)
            {
                if (counter == i)
                {
                    break;
                }

                counter++;

                var sellOffer = new NFTokenCreateOffer
                {
                    Account = _primaryAccount.ClassicAddress,
                    NFTokenID = nft.NFTokenID,
                    Amount = new Currency { ValueAsXrp = 10 },
                    Flags = NFTokenCreateOfferFlags.tfSellNFToken,
                    Sequence = seq
                };

                var autofilled = await _client.Autofill(sellOffer);
                seq = await SendAsync(nftsResponse.NFTs.Count, counter, nftsResponse.NFTs.Count, autofilled, _primaryAccount);
                Console.WriteLine($"[TestAccountBuilder] NFT SellOffer #{counter}");

                if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TestAccountBuilder] NFT offers failed: {ex.Message}");
        }
    }

    private async Task CreateDEXOffersAsync(int count)
    {
        Console.WriteLine($"[TestAccountBuilder] Creating {count} DEX offers from primary account...");
        uint? seq = null;
        for (int i = 0; i < count && i < TokenCodes.Length; i++)
        {
            try
            {
                var code = TokenCodes[i];

                var offerCreate = new OfferCreate
                {
                    Account = _primaryAccount.ClassicAddress,
                    TakerGets = new Currency { ValueAsXrp = 10 },
                    TakerPays = new Currency
                    {
                        CurrencyCode = code,
                        Issuer = IssuerAccount.ClassicAddress,
                        Value = "100"
                    },
                    Sequence = seq
                };

                var autofilled = await _client.Autofill(offerCreate);
                seq = await SendAsync(count, i, TokenCodes.Length, autofilled, _primaryAccount);
                Console.WriteLine($"[TestAccountBuilder] OfferCreate buy {code.CurrencyReadableName()}");

                if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] OfferCreate failed: {ex.Message}");
            }
        }
    }

    private async Task<uint?> SendAsync(int count, int i, int maxCount, ITransactionRequest autofilled, XrplWallet wallet)
    {
        if (i >= count - 1 || i >= maxCount - 1)
        {
            _ = await _client.SubmitAndWait(autofilled, wallet, true);
            return null;
        }
        else
        {
            var response = await _client.Submit(autofilled, wallet, true);
            Console.WriteLine(response.EngineResult);
            return response.AccountSequenceNext;
        }
    }

    private async Task CreateDEXOffersForIssuerAsync(int count)
    {
        Console.WriteLine($"[TestAccountBuilder] Creating {count} DEX offers from issuer account...");
        uint? seq = null;
        for (int i = 0; i < count && i < TokenCodes.Length; i++)
        {
            try
            {
                var code = TokenCodes[i];

                var offerCreate = new OfferCreate
                {
                    Account = IssuerAccount.ClassicAddress,
                    TakerGets = new Currency { ValueAsXrp = 10 },
                    TakerPays = new Currency
                    {
                        CurrencyCode = code,
                        Issuer = IssuerAccount.ClassicAddress,
                        Value = "10"
                    },
                    Sequence = seq
                };

                var autofilled = await _client.Autofill(offerCreate);
                seq = await SendAsync(count, i, TokenCodes.Length, autofilled, IssuerAccount);
                Console.WriteLine($"[TestAccountBuilder] OfferCreate buy {code.CurrencyReadableName()}");

                if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] OfferCreate failed: {ex.Message}");
            }
        }
    }

    private async Task CreateMPTokensAsync()
    {
        Console.WriteLine("[TestAccountBuilder] Creating MPT tokens (IssuerAccount issues, primary account holds)...");

        try
        {
            if (await MptIssuanceExistsAsync())
            {
                Console.WriteLine("[TestAccountBuilder] MPTokenIssuanceCreate: already exists, skipping");
                return;
            }

            var mptCreate = new MPTokenIssuanceCreate
            {
                Account = IssuerAccount.ClassicAddress,
                MaximumAmount = "1000000000",
                Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer
            };

            var autofilled = await _client.Autofill(mptCreate);
            var response = await _client.Submit(autofilled, IssuerAccount, true);
            Console.WriteLine($"[TestAccountBuilder] MPTokenIssuanceCreate:");

            if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TestAccountBuilder] MPT creation failed: {ex.Message}");
        }
    }

    private async Task CreateTicketsAsync(int count)
    {
        Console.WriteLine($"[TestAccountBuilder] Creating tickets on primary account...");

        try
        {
            var existingCount = await GetTicketCountAsync();
            if (existingCount >= count)
            {
                Console.WriteLine($"[TestAccountBuilder] TicketCreate: {existingCount} tickets already exist, skipping");
                return;
            }

            var toCreate = count - existingCount;
            var ticketCreate = new TicketCreate
            {
                Account = _primaryAccount.ClassicAddress,
                TicketCount = (uint)toCreate
            };

            var autofilled = await _client.Autofill(ticketCreate);
            var response = await _client.Submit(autofilled, _primaryAccount, true);
            Console.WriteLine($"[TestAccountBuilder] TicketCreate ({toCreate})");

            if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TestAccountBuilder] Tickets creation failed: {ex.Message}");
        }
    }

    private async Task CreateChecksAsync(int count)
    {
        Console.WriteLine($"[TestAccountBuilder] Creating {count} checks from primary account to IssuerAccount...");
        uint? seq = null;
        for (int i = 0; i < count; i++)
        {
            try
            {
                var checkCreate = new CheckCreate
                {
                    Account = _primaryAccount.ClassicAddress,
                    Destination = IssuerAccount.ClassicAddress,
                    SendMax = new Currency { ValueAsXrp = 100 },
                    Sequence = seq
                };

                var autofilled = await _client.Autofill(checkCreate);
                seq = await SendAsync(count, i, count, autofilled, _primaryAccount);
                Console.WriteLine($"[TestAccountBuilder] CheckCreate #{i}");

                if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] Check creation failed: {ex.Message}");
            }
        }
    }

    private async Task CreateIncomeChecksAsync(int count)
    {
        Console.WriteLine($"[TestAccountBuilder] Creating {count} checks to primary account from IssuerAccount...");
        uint? seq = null;

        for (int i = 0; i < count; i++)
        {
            try
            {
                var checkCreate = new CheckCreate
                {
                    Account = IssuerAccount.ClassicAddress,
                    Destination = _primaryAccount.ClassicAddress,
                    SendMax = new Currency { ValueAsXrp = 100 },
                    Sequence = seq
                };

                var autofilled = await _client.Autofill(checkCreate);
                seq = await SendAsync(count, i, count, autofilled, IssuerAccount);
                Console.WriteLine($"[TestAccountBuilder] CheckCreate #{i}");

                if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TestAccountBuilder] Check creation failed: {ex.Message}");
            }
        }
    }

    private async Task CreateSignerListAsync()
    {
        Console.WriteLine("[TestAccountBuilder] Creating SignerList on primary account...");

        try
        {
            if (await SignerListExistsAsync())
            {
                Console.WriteLine("[TestAccountBuilder] SignerListSet: already exists, skipping");
                return;
            }

            var signerListSet = new SignerListSet
            {
                Account = _primaryAccount.ClassicAddress,
                SignerQuorum = 2,
                SignerEntries = new List<SignerEntryWrapper>
                {
                    new SignerEntryWrapper { SignerEntry = new SignerEntry { Account = Signer1Account.ClassicAddress, SignerWeight = 1 } },
                    new SignerEntryWrapper { SignerEntry = new SignerEntry { Account = Signer2Account.ClassicAddress, SignerWeight = 1 } },
                    new SignerEntryWrapper { SignerEntry = new SignerEntry { Account = Signer3Account.ClassicAddress, SignerWeight = 1 } }
                }
            };

            var autofilled = await _client.Autofill(signerListSet);
            var response = await _client.Submit(autofilled, _primaryAccount, true);
            Console.WriteLine($"[TestAccountBuilder] SignerListSet");

            if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TestAccountBuilder] SignerList creation failed: {ex.Message}");
        }
    }

    private async Task CreateEscrowsAsync()
    {
        Console.WriteLine("[TestAccountBuilder] Creating escrow from primary account to IssuerAccount...");

        try
        {
            var finishAfter = DateTime.UtcNow.AddMinutes(5);

            var escrowCreate = new EscrowCreate
            {
                Account = _primaryAccount.ClassicAddress,
                Destination = IssuerAccount.ClassicAddress,
                Amount = new Currency { ValueAsXrp = 50 },
                FinishAfter = finishAfter
            };

            var autofilled = await _client.Autofill(escrowCreate);
            var response = await _client.Submit(autofilled, _primaryAccount, true);
            Console.WriteLine($"[TestAccountBuilder] EscrowCreate");

            if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TestAccountBuilder] Escrow creation failed: {ex.Message}");
        }
    }

    private async Task CreateIncomeEscrowsAsync()
    {
        Console.WriteLine("[TestAccountBuilder] Creating escrow to primary account from IssuerAccount...");

        try
        {
            var finishAfter = DateTime.UtcNow.AddMinutes(5);

            var escrowCreate = new EscrowCreate
            {
                Account = IssuerAccount.ClassicAddress,
                Destination = _primaryAccount.ClassicAddress,
                Amount = new Currency { ValueAsNumber = 150, CurrencyCode = "ESC", Issuer = IssuerAccount.ClassicAddress },
                FinishAfter = finishAfter
            };

            var autofilled = await _client.Autofill(escrowCreate);
            var response = await _client.Submit(autofilled, IssuerAccount, true);
            Console.WriteLine($"[TestAccountBuilder] EscrowCreate");

            if (_nodeType == TestNodeType.Standalone) await LedgerAcceptAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TestAccountBuilder] Escrow creation failed: {ex.Message}");
        }
    }

    private async Task CreateDeepFreezeTokenAsync(string currencyCode)
    {
        Console.WriteLine("[TestAccountBuilder] Deep Freeze test scenario with Issuer2...");

        if (!await TrustlineExistsAsync(currencyCode, Issuer2Account.ClassicAddress, _primaryAccount.ClassicAddress))
        {
            TrustSet trustSet = new TrustSet
            {
                Account = _primaryAccount.ClassicAddress,
                LimitAmount = new Currency
                {
                    CurrencyCode = currencyCode,
                    Issuer = Issuer2Account.ClassicAddress,
                    Value = "10000000"
                }
            };
            var autofilled = await _client.Autofill(trustSet);
            await _client.SubmitAndWait(autofilled, _primaryAccount);
            Console.WriteLine($"[TestAccountBuilder] TrustSet {currencyCode} -> Issuer2");
        }
        else
        {
            Console.WriteLine($"[TestAccountBuilder] TrustSet {currencyCode} -> Issuer2: already exists, skipping");
        }



        Payment payment = new Payment
        {
            Account = Issuer2Account.ClassicAddress,
            Destination = _primaryAccount.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = currencyCode,
                Issuer = Issuer2Account.ClassicAddress,
                Value = "500"
            }
        };
        var autofilledPayment = await _client.Autofill(payment);
        await _client.SubmitAndWait(autofilledPayment, Issuer2Account);
        Console.WriteLine($"[TestAccountBuilder] Payment {currencyCode} Issuer2 -> Primary");

        TrustSet freeze = new TrustSet
        {
            Account = Issuer2Account.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = currencyCode,
                Issuer = _primaryAccount.ClassicAddress,
                Value = "0"
            },
            Flags = TrustSetFlags.tfSetFreeze | TrustSetFlags.tfSetDeepFreeze
        };
        var autofilledFreeze = await _client.Autofill(freeze);
        await _client.SubmitAndWait(autofilledFreeze, Issuer2Account);
        Console.WriteLine($"[TestAccountBuilder] Freeze and DeepFreeze {currencyCode} for Primary");

        //TrustSet deepFreeze = new TrustSet
        //{
        //    Account = Issuer2Account.ClassicAddress,
        //    LimitAmount = new Currency
        //    {
        //        CurrencyCode = currencyCode,
        //        Issuer = _primaryAccount.ClassicAddress,
        //        Value = "0"
        //    },
        //    Flags = 
        //};
        //var autofilledDeepFreeze = await _client.Autofill(deepFreeze);
        //await _client.SubmitAndWait(autofilledDeepFreeze, Issuer2Account);
        //Console.WriteLine($"[TestAccountBuilder] DeepFreeze {currencyCode} for Primary");
    }

    private async Task CreateRequireAuthTestAsync(string currencyCode, bool auth)
    {
        Console.WriteLine($"[TestAccountBuilder] RequireAuth test (auth={auth}) with Issuer3...");

        if (!await TrustlineExistsAsync(currencyCode, Issuer3Account.ClassicAddress, _primaryAccount.ClassicAddress))
        {
            TrustSet trustSet = new TrustSet
            {
                Account = _primaryAccount.ClassicAddress,
                LimitAmount = new Currency
                {
                    CurrencyCode = currencyCode,
                    Issuer = Issuer3Account.ClassicAddress,
                    Value = "10000000"
                }
            };
            var autofilled = await _client.Autofill(trustSet);
            await _client.SubmitAndWait(autofilled, _primaryAccount);
            Console.WriteLine($"[TestAccountBuilder] TrustSet {currencyCode} -> Issuer3");
        }
        else
        {
            Console.WriteLine($"[TestAccountBuilder] TrustSet {currencyCode} -> Issuer3: already exists, skipping");
        }

        if (!auth)
        {
            Console.WriteLine($"[TestAccountBuilder] auth=false, skipping authorization and token payment");
            return;
        }

        TrustSet authorize = new TrustSet
        {
            Account = Issuer3Account.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = currencyCode,
                Issuer = _primaryAccount.ClassicAddress,
                Value = "0"
            },
            Flags = TrustSetFlags.tfSetfAuth
        };
        var autofilledAuth = await _client.Autofill(authorize);
        await _client.SubmitAndWait(autofilledAuth, Issuer3Account);
        Console.WriteLine($"[TestAccountBuilder] Authorize {currencyCode} trustline for Primary");

        Payment payment = new Payment
        {
            Account = Issuer3Account.ClassicAddress,
            Destination = _primaryAccount.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = currencyCode,
                Issuer = Issuer3Account.ClassicAddress,
                Value = "500"
            }
        };
        var autofilledPayment = await _client.Autofill(payment);
        await _client.SubmitAndWait(autofilledPayment, Issuer3Account);
        Console.WriteLine($"[TestAccountBuilder] Payment {currencyCode} Issuer3 -> Primary");
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Prints all wallet addresses for import into other applications.
    /// </summary>
    public void PrintAllWallets()
    {
        Console.WriteLine("=== Test Wallets ===");
        Console.WriteLine($"PrimaryAccount: {_primaryAccount?.ClassicAddress ?? "(not set)"}");
        Console.WriteLine($"IssuerAccount:  {IssuerAccount.ClassicAddress}");
        Console.WriteLine($"Issuer2Account: {Issuer2Account.ClassicAddress}");
        Console.WriteLine($"Issuer3Account: {Issuer3Account.ClassicAddress}");
        Console.WriteLine($"Signer1Account: {Signer1Account.ClassicAddress}");
        Console.WriteLine($"Signer2Account: {Signer2Account.ClassicAddress}");
        Console.WriteLine($"Signer3Account: {Signer3Account.ClassicAddress}");
        Console.WriteLine("====================");
    }

    /// <summary>
    /// Prints static helper wallet addresses.
    /// </summary>
    public static void PrintHelperWallets()
    {
        Console.WriteLine("=== Helper Wallets ===");
        Console.WriteLine($"IssuerAccount:  {IssuerAccount.ClassicAddress}");
        Console.WriteLine($"Issuer2Account: {Issuer2Account.ClassicAddress}");
        Console.WriteLine($"Issuer3Account: {Issuer3Account.ClassicAddress}");
        Console.WriteLine($"Signer1Account: {Signer1Account.ClassicAddress}");
        Console.WriteLine($"Signer2Account: {Signer2Account.ClassicAddress}");
        Console.WriteLine($"Signer3Account: {Signer3Account.ClassicAddress}");
        Console.WriteLine("======================");
    }

    #endregion
}
