public class TransferResult
{
    public string Nume { get; set; }
    public string Prenume { get; set; }
    public decimal SumaEth { get; set; }
    public string AdresaDestinatie { get; set; }
    public string TransactionHash { get; set; }
    public long BlockNumber { get; set; }
    public long GasUsed { get; set; }
    public DateTime DataOra { get; set; }
    public long nonce { get; set; }
}