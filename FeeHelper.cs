using System.Numerics;
using Nethereum.Web3;
using Nethereum.Util;

public static class FeeHelper
{
    public static decimal ToGwei(BigInteger wei) => Web3.Convert.FromWei(wei, UnitConversion.EthUnit.Gwei);
    public static decimal ToEth(BigInteger wei) => Web3.Convert.FromWei(wei);
    public static BigInteger EstimateFeeWei(BigInteger gasPrice, BigInteger gasLimit) => gasPrice * gasLimit;
}