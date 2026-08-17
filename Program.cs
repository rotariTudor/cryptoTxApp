using System.Numerics;
using System.Text.Json;
using Nethereum.BlockchainProcessing.BlockStorage.Entities.Mapping;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Web3;
using Org.BouncyCastle.Asn1.X509;
//cum mai bine sa pastrez nonce-ul? local sau sa-l interoghez din blockchain
//cache local pentru informatie ca nonce... etc
//sincronizez pana la un anumit moment, cu istoria de tranzactii din blockchain
//lista de tranzactii se ia din blockchain
//fisier de config pentru toate adresele, log, dir
//public keys saved from hsm nitrokey
//sa am posibilitatea sa transform Fee
// .exe
//erc 20 pentru tranzactii usdt, tranzactii cu smart contracts
//bilant fie in ether sau in mai multi tokeni erc20

var config = AppConfig.Load();

var hsm = new HsmRawSigner(
        config.Pkcs11LibraryPath,
        (byte)config.KeyId,
        config.PublicKeyDerPath
    );

var db = new TransactionDatabase(config.DatabasePath);

var web3 = new Web3(config.infuraUrl);

db.RegisterAccount(hsm.PublicAddress, config.KeyId.ToString(), "eth-signing-key");
Console.WriteLine($"[DEBUG] hsm.PublicAddress = '{hsm.PublicAddress}'");
Console.WriteLine($"[DEBUG] config.KeyId = {config.KeyId}\n");

Console.WriteLine("\n=== ETH Transfer Program (Sepolia Testnet) ===\n");

bool serviceON = true;

while (serviceON)
{
    Console.WriteLine("\n--- --- [MENU] --- ---");
    Console.WriteLine("0. Exit.");
    Console.WriteLine("1. Information on current address.");
    Console.WriteLine("2. Make a transfer.");
    Console.WriteLine("3. Generate a random recipient address.");
    Console.WriteLine("4. View transaction history.");
    Console.WriteLine("5. View registered accounts.");
    Console.WriteLine("6. Resync nonce from blockchain.");
    Console.WriteLine("7. View full balance (ETH + tokens).");
    Console.WriteLine("8. Transfer ERC-20 token.");
    Console.Write("Choose an option: ");
    string chosenOption = Console.ReadLine();
    switch (chosenOption)
    {
        case "0":
            serviceON = false;
            break;
        case "1":
            var sold = await web3.Eth.GetBalance.SendRequestAsync(hsm.PublicAddress);
            decimal ethBalance = Web3.Convert.FromWei(sold.Value);
            var nonce = await web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(hsm.PublicAddress);
            Console.WriteLine($"\n[INFO] Current account balance: {ethBalance} ETH");
            Console.WriteLine($"[INFO] Nonce: {nonce.Value}");
            Console.WriteLine($"[INFO] Account public address: {hsm.PublicAddress}");
            break;

        case "2":
            await MakeTransfer();
            break;

        case "3":
            generateRandomAddr();
            break;

        case "4":
            showHistoryFromDb();
            break;

        case "5":
            ShowAccounts();
            break;

        case "6":
            await ResyncNonce();
            break;

        case "7":
            await showFullBalance();
            break;
        case "8":
            await MakeTokenTransfer();
            break;

        default:
            Console.WriteLine("\n[ERR ] Invalid option, try again.");
            break;
    }
}

async Task showFullBalance()
{
    var ethBalanceWei = await web3.Eth.GetBalance.SendRequestAsync(hsm.PublicAddress);
    decimal ethBalance = Web3.Convert.FromWei(ethBalanceWei.Value);

    Console.WriteLine($"\n[INFO] Balance for {hsm.PublicAddress}:");
    Console.WriteLine($"  ETH: {ethBalance}");

    foreach (var token in config.Tokens)
    {
        try
        {
            var client = new Erc20TokenClient(web3, token.Address);
            int decimals = await client.GetDecimalsAsync();
            decimal balance = await client.GetBalanceAsync(hsm.PublicAddress, decimals);
            Console.WriteLine($"  {token.Symbol}: {balance}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {token.Symbol}: (error reading balance — {ex.Message})");
        }
    }
}

void ShowAccounts()
{
    var accounts = db.ListAccounts();

    if (accounts.Count == 0)
    {
        Console.WriteLine("\nNo accounts registered.");
        return;
    }

    Console.WriteLine($"\n[INFO] Registered accounts ({accounts.Count} total):");
    foreach (var acc in accounts)
    {
        Console.WriteLine($"[INFO] {acc.Address} | KeyId: {acc.KeyId} | Label: {acc.Label}");
    }
}

void showHistoryFromDb()
{
    var history = db.GetHistory(hsm.PublicAddress);

    if (history.Count == 0)
    {
        Console.WriteLine("\nNo saved history.");
        return;
    }

    TimeZoneInfo chisinauTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Chisinau");

    Console.WriteLine($"\n[INFO] Transaction History -> Num of transfers: ({history.Count} total)");
    foreach (var t in history)
    {
        DateTime utcTime = DateTime.Parse(t.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
        DateTime localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, chisinauTz);

        Console.WriteLine($"[INFO] [{localTime:yyyy-MM-dd HH:mm}] {t.Prenume} {t.Nume} -> {t.To} : {t.ValueWei} {t.TokenSymbol} | status: {t.Status} | hash: {t.Hash} | reqId: {t.RequestId}");
    }
}

string ReadPinMasked()
{
    var pin = new System.Text.StringBuilder();
    ConsoleKeyInfo key;

    do
    {
        key = Console.ReadKey(intercept: true); // intercept=true -> nu afișează automat caracterul

        if (key.Key == ConsoleKey.Backspace && pin.Length > 0)
        {
            pin.Remove(pin.Length - 1, 1);
            Console.Write("\b \b"); // șterge vizual ultimul *
        }
        else if (!char.IsControl(key.KeyChar))
        {
            pin.Append(key.KeyChar);
            Console.Write("*");
        }
    }
    while (key.Key != ConsoleKey.Enter);

    Console.WriteLine();
    return pin.ToString();
}

async Task MakeTransfer()
{
    var request = new TransferRequest();

    Console.Write("\n[INFO] Name: ");
    request.nume = Console.ReadLine();

    Console.Write("[INFO] Surname: ");
    request.prenume = Console.ReadLine();

    Console.Write("[INFO] Destination address (0x...): ");
    request.adrDest = Console.ReadLine();

    Console.Write("[INFO] Amount in ETH (ex: 0.001): ");
    string sumaInput = Console.ReadLine();
    if (!decimal.TryParse(sumaInput, out decimal suma))
    {
        Console.WriteLine("[INFO] Invalid amount.");
        return;
    }
    request.sumaETH = suma;

    Console.Write("[INFO] Description of transaction (optional): ");
    request.descrTranzactie = Console.ReadLine();

    var gasPriceForEstimate = await web3.Eth.GasPrice.SendRequestAsync();
    BigInteger estimatedFeeWei = FeeHelper.EstimateFeeWei(gasPriceForEstimate.Value, 21000);
    decimal estimatedFeeEth = FeeHelper.ToEth(estimatedFeeWei);

    Console.WriteLine("\n--- --- TRANSFER DETAILS --- ---\n");
    Console.WriteLine("Confirm transfer:");
    Console.WriteLine($"To: {request.prenume} {request.nume}");
    Console.WriteLine($"Address: {request.adrDest}");
    Console.WriteLine($"Amount: {request.sumaETH} ETH");
    Console.WriteLine($"Estimated fee: {estimatedFeeEth} ETH (~{FeeHelper.ToGwei(gasPriceForEstimate.Value)} Gwei/gas)");
    Console.WriteLine($"Total: {request.sumaETH + estimatedFeeEth} ETH");
    Console.Write("Continue? (y/n): ");
    string confirmare = Console.ReadLine();

    bool confirmed = string.IsNullOrWhiteSpace(confirmare) || confirmare.Trim().ToLower() == "y";
    if (!confirmed)
    {
        Console.WriteLine("[INFO] Cancelled transfer.");
        return;
    }

    string requestId = Guid.NewGuid().ToString();
    Console.WriteLine($"[INFO] Request ID: {requestId}");

    db.InsertPendingTokenTransaction(requestId, hsm.PublicAddress, request.adrDest, request.sumaETH, request.nume, request.prenume, "received");

    try
    {
        Console.WriteLine("[INFO] HSM address: " + hsm.PublicAddress);

        BigInteger? cachedNonce = db.GetCachedNonce(hsm.PublicAddress);
        BigInteger nonce;

        if (cachedNonce == null)
        {
            var onChainNonce = await web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(hsm.PublicAddress);
            nonce = onChainNonce.Value;
            Console.WriteLine($"[INFO] Nonce cache gol — sincronizat din rețea: {nonce}");
        }
        else
        {
            nonce = cachedNonce.Value;
            Console.WriteLine($"[INFO] Nonce din cache local: {nonce}");
        }

        db.UpdateNonce(requestId, nonce);

        var gasPrice = await web3.Eth.GasPrice.SendRequestAsync();
        var chainId = await web3.Eth.ChainId.SendRequestAsync();

        db.UpdateStatus(requestId, "signing");

        Console.WriteLine("Enter HSM PIN to sign this transaction: ");
        string hsm_pin = ReadPinMasked();

        string signedTxHex = hsm.SignLegacyTransaction(
            nonce, gasPrice.Value, 21000,
            request.adrDest, Web3.Convert.ToWei(request.sumaETH),
            chainId.Value, Array.Empty<byte>(), hsm_pin
        );
        db.UpdateStatus(requestId, "signed");
        Console.WriteLine("[INFO] Signed transaction: " + signedTxHex);

        var txHash = await web3.Eth.Transactions.SendRawTransaction.SendRequestAsync(signedTxHex);
        db.UpdateTransactionHash(requestId, txHash);
        db.UpdateStatus(requestId, "sent");
        Console.WriteLine("[INFO] Sent with hash: " + txHash);

        db.SetCachedNonce(hsm.PublicAddress, nonce + 1);

        Console.WriteLine($"[INFO] Comanda {requestId} a fost trimisă cu succes.");
    }
    catch (Exception ex)
    {
        db.UpdateStatus(requestId, "failed");
        Console.WriteLine("[ERR ] transfer error: " + ex.Message);
    }
}

void generateRandomAddr()
{
    var randKey = EthECKey.GenerateKey();
    Console.WriteLine("\n[INFO] Random Generated key: " + randKey.GetPublicAddress());
}

async Task ResyncNonce()
{
    var onChainNonce = await web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(hsm.PublicAddress);
    db.SetCachedNonce(hsm.PublicAddress, onChainNonce.Value);
    Console.WriteLine($"[INFO] Taken nonce from web3: {onChainNonce.Value}");
}

async Task MakeTokenTransfer()
{
    if (config.Tokens.Count == 0)
    {
        Console.WriteLine("\n[INFO] No tokens configured in config.json.");
        return;
    }

    Console.WriteLine("\nAvailable tokens:");
    for (int i = 0; i < config.Tokens.Count; i++)
        Console.WriteLine($"{i + 1}. {config.Tokens[i].Symbol} ({config.Tokens[i].Address})");

    Console.Write("Choose token number: ");
    if (!int.TryParse(Console.ReadLine(), out int tokenIndex) || tokenIndex < 1 || tokenIndex > config.Tokens.Count)
    {
        Console.WriteLine("[INFO] Invalid selection.");
        return;
    }
    var token = config.Tokens[tokenIndex - 1];

    Console.Write("Destination address (0x...): ");
    string toAddress = Console.ReadLine();

    Console.Write($"Amount in {token.Symbol}: ");
    if (!decimal.TryParse(Console.ReadLine(), out decimal amount))
    {
        Console.WriteLine("[INFO] Invalid amount.");
        return;
    }


    var tokenClient = new Erc20TokenClient(web3, token.Address);
    int decimals = await tokenClient.GetDecimalsAsync();
    byte[] data = tokenClient.BuildTransferData(toAddress, amount, decimals);

    Console.WriteLine($"\nConfirm token transfer:");
    Console.WriteLine($"  Token: {token.Symbol} ({token.Address})");
    Console.WriteLine($"  To: {toAddress}");
    Console.WriteLine($"  Amount: {amount} {token.Symbol}");
    Console.Write("Continue? (y/n): ");
    string confirmare = Console.ReadLine();
    if (!(string.IsNullOrWhiteSpace(confirmare) || confirmare.Trim().ToLower() == "y"))
    {
        Console.WriteLine("[INFO] Cancelled.");
        return;
    }

    // PASUL 1 — ID unic pentru comandă
    string requestId = Guid.NewGuid().ToString();
    Console.WriteLine($"[INFO] Request ID: {requestId}");

    // PASUL 2 — salvăm ÎNAINTE de semnare
    db.InsertPendingTokenTransaction(requestId, hsm.PublicAddress, toAddress, amount, token.Symbol, token.Address, "received");

    try
    {
        BigInteger? cachedNonce = db.GetCachedNonce(hsm.PublicAddress);
        var onChainNonce = await web3.Eth.Transactions.GetTransactionCount.SendRequestAsync(hsm.PublicAddress);

        BigInteger nonce;
        if (cachedNonce == null || onChainNonce.Value > cachedNonce.Value)
            nonce = onChainNonce.Value;
        else
            nonce = cachedNonce.Value;

        db.UpdateNonce(requestId, nonce);

        var gasPrice = await web3.Eth.GasPrice.SendRequestAsync();
        var chainId = await web3.Eth.ChainId.SendRequestAsync();

        db.UpdateStatus(requestId, "signing");

        Console.WriteLine("Enter HSM PIN to sign this transaction: ");
        string hsm_pin = ReadPinMasked();

        string signedTxHex = hsm.SignLegacyTransaction(
            nonce, gasPrice.Value, 100000,
            token.Address, BigInteger.Zero, chainId.Value, data, hsm_pin
        );
        db.UpdateStatus(requestId, "signed");

        var txHash = await web3.Eth.Transactions.SendRawTransaction.SendRequestAsync(signedTxHex);
        db.UpdateTransactionHash(requestId, txHash);
        db.UpdateStatus(requestId, "sent");
        Console.WriteLine("[INFO] Token transfer sent, hash: " + txHash);

        db.SetCachedNonce(hsm.PublicAddress, nonce + 1);
    }
    catch (Exception ex)
    {
        db.UpdateStatus(requestId, "failed");
        Console.WriteLine("[ERR ] Token transfer error: " + ex.Message);
    }
}