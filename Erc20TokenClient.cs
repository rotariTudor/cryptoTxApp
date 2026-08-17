using System.Numerics;
using Nethereum.Web3;
using Nethereum.Hex.HexConvertors.Extensions;

public class Erc20TokenClient
{
    private const string Erc20Abi = @"[
        {
            ""constant"": true,
            ""inputs"": [{""name"": ""owner"", ""type"": ""address""}],
            ""name"": ""balanceOf"",
            ""outputs"": [{""name"": """", ""type"": ""uint256""}],
            ""type"": ""function""
        },
        {
            ""constant"": false,
            ""inputs"": [{""name"": ""to"", ""type"": ""address""}, {""name"": ""value"", ""type"": ""uint256""}],
            ""name"": ""transfer"",
            ""outputs"": [{""name"": """", ""type"": ""bool""}],
            ""type"": ""function""
        },
        {
            ""constant"": true,
            ""inputs"": [],
            ""name"": ""decimals"",
            ""outputs"": [{""name"": """", ""type"": ""uint8""}],
            ""type"": ""function""
        }
    ]";

    private readonly Web3 _web3;
    private readonly string _contractAddress;

    public Erc20TokenClient(Web3 web3, string contractAddress)
    {
        _web3 = web3;
        _contractAddress = contractAddress;
    }

    public async Task<decimal> GetBalanceAsync(string ownerAddress, int decimals)
    {
        var contract = _web3.Eth.GetContract(Erc20Abi, _contractAddress);
        var balanceOfFunction = contract.GetFunction("balanceOf");

        BigInteger rawBalance = await balanceOfFunction.CallAsync<BigInteger>(ownerAddress);

        decimal divisor = (decimal)BigInteger.Pow(10, decimals);
        return (decimal)rawBalance / divisor;
    }

    public async Task<int> GetDecimalsAsync()
    {
        var contract = _web3.Eth.GetContract(Erc20Abi, _contractAddress);
        var decimalsFunction = contract.GetFunction("decimals");
        return await decimalsFunction.CallAsync<int>();
    }

    public byte[] BuildTransferData(string toAddress, decimal amount, int decimals)
    {
        var contract = _web3.Eth.GetContract(Erc20Abi, _contractAddress);
        var transferFunction = contract.GetFunction("transfer");

        BigInteger rawAmount = new BigInteger(amount * (decimal)BigInteger.Pow(10, decimals));
        string dataHex = transferFunction.GetData(toAddress, rawAmount);

        return dataHex.HexToByteArray();
    }

    public string ContractAddress => _contractAddress;
}