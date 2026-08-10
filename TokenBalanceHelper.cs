using System.Numerics;
using Nethereum.Web3;
using Nethereum.Contracts;

public static class TokenBalanceHelper
{
    private const string Erc20BalanceAbi = @"[
        {
            ""constant"": true,
            ""inputs"": [{""name"": ""owner"", ""type"": ""address""}],
            ""name"": ""balanceOf"",
            ""outputs"": [{""name"": """", ""type"": ""uint256""}],
            ""type"": ""function""
        }
    ]";

    public static async Task<decimal> GetTokenBalanceAsync(Web3 web3, string tokenContractAddress, string ownerAddress, int decimals)
    {
        var contract = web3.Eth.GetContract(Erc20BalanceAbi, tokenContractAddress);
        var balanceOfFunction = contract.GetFunction("balanceOf");

        BigInteger rawBalance = await balanceOfFunction.CallAsync<BigInteger>(ownerAddress);

        decimal divisor = (decimal)BigInteger.Pow(10, decimals);
        return (decimal)rawBalance / divisor;
    }
}