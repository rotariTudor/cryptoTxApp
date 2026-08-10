public class TransferValidator
{
    public static void Validate(TransferRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.nume) || string.IsNullOrWhiteSpace(request.prenume))
            throw new ArgumentException("name and surname req.");

        if (string.IsNullOrWhiteSpace(request.adrDest))
            throw new ArgumentException("destination address req.");

        var addressUtil = new Nethereum.Util.AddressUtil();
        if (!addressUtil.IsValidEthereumAddressHexFormat(request.adrDest))
            throw new ArgumentException("invalid ETH address.");

        if (request.sumaETH <= 0)
            throw new ArgumentException("sum has to be more than 0.");
    }
}