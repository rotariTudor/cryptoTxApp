using Microsoft.Data.Sqlite;
using System.Numerics;

public class TransactionDatabase
{
    private readonly string _connectionString;

    public TransactionDatabase(string dbPath = "wallet.db")
    {
        _connectionString = $"Data Source={dbPath}";
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = @"
        CREATE TABLE IF NOT EXISTS Accounts (
            Address TEXT PRIMARY KEY,
            KeyId TEXT NOT NULL,
            Label TEXT,
            CachedNonce TEXT,
            LastSyncedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Transactions (
            RequestId TEXT PRIMARY KEY,
            TransactionHash TEXT,
            FromAddress TEXT NOT NULL,
            ToAddress TEXT NOT NULL,
            ValueWei TEXT NOT NULL,
            Nonce TEXT,
            Status TEXT NOT NULL,
            BlockNumber INTEGER,
            GasUsed INTEGER,
            Nume TEXT,
            Prenume TEXT,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );";
        command.ExecuteNonQuery();
    }

    // ---------- Nonce ----------

    public BigInteger? GetCachedNonce(string address)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT CachedNonce FROM Accounts WHERE Address = $addr";
        command.Parameters.AddWithValue("$addr", address);

        var result = command.ExecuteScalar();
        if (result == null || result is DBNull)
            return null;

        return BigInteger.Parse(result.ToString());
    }

    public void SetCachedNonce(string address, BigInteger nonce)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE Accounts SET CachedNonce = $nonce, LastSyncedAt = $time WHERE Address = $addr";
        command.Parameters.AddWithValue("$nonce", nonce.ToString());
        command.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));
        command.Parameters.AddWithValue("$addr", address);
        command.ExecuteNonQuery();
    }

    // ---------- Tranzacții ----------

    public bool RequestExists(string requestId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Transactions WHERE RequestId = $id";
        command.Parameters.AddWithValue("$id", requestId);
        long count = (long)command.ExecuteScalar();
        return count > 0;
    }

    public void InsertPendingTransaction(string requestId, string fromAddress, string toAddress, decimal amountEth, string nume, string prenume, string status)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Transactions
            (RequestId, FromAddress, ToAddress, ValueWei, Status, Nume, Prenume, CreatedAt, UpdatedAt)
            VALUES ($id, $from, $to, $value, $status, $nume, $prenume, $now, $now)";
        command.Parameters.AddWithValue("$id", requestId);
        command.Parameters.AddWithValue("$from", fromAddress);
        command.Parameters.AddWithValue("$to", toAddress);
        command.Parameters.AddWithValue("$value", Nethereum.Web3.Web3.Convert.ToWei(amountEth).ToString());
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$nume", nume);
        command.Parameters.AddWithValue("$prenume", prenume);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void UpdateStatus(string requestId, string status)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE Transactions SET Status = $status, UpdatedAt = $now WHERE RequestId = $id";
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        command.Parameters.AddWithValue("$id", requestId);
        command.ExecuteNonQuery();
    }

    public void UpdateNonce(string requestId, BigInteger nonce)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE Transactions SET Nonce = $nonce, UpdatedAt = $now WHERE RequestId = $id";
        command.Parameters.AddWithValue("$nonce", nonce.ToString());
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        command.Parameters.AddWithValue("$id", requestId);
        command.ExecuteNonQuery();
    }

    public void UpdateTransactionHash(string requestId, string txHash)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE Transactions SET TransactionHash = $hash, UpdatedAt = $now WHERE RequestId = $id";
        command.Parameters.AddWithValue("$hash", txHash);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        command.Parameters.AddWithValue("$id", requestId);
        command.ExecuteNonQuery();
    }

    public void UpdateConfirmation(string requestId, long blockNumber, long gasUsed)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE Transactions
            SET Status = 'confirmed', BlockNumber = $block, GasUsed = $gas, UpdatedAt = $now
            WHERE RequestId = $id";
        command.Parameters.AddWithValue("$block", blockNumber);
        command.Parameters.AddWithValue("$gas", gasUsed);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        command.Parameters.AddWithValue("$id", requestId);
        command.ExecuteNonQuery();
    }

    public List<(string RequestId, string Hash, string To, string ValueWei, string Status, string Nume, string Prenume, string CreatedAt)> GetHistory(string fromAddress)
    {
        var results = new List<(string, string, string, string, string, string, string, string)>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT RequestId, TransactionHash, ToAddress, ValueWei, Status, Nume, Prenume, CreatedAt
            FROM Transactions WHERE FromAddress = $addr ORDER BY CreatedAt DESC";
        command.Parameters.AddWithValue("$addr", fromAddress);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? "(pending)" : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? "" : reader.GetString(5),
                reader.IsDBNull(6) ? "" : reader.GetString(6),
                reader.GetString(7)
            ));
        }
        return results;
    }

    public void RegisterAccount(string address, string keyId, string label)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Accounts (Address, KeyId, Label, CachedNonce, LastSyncedAt)
            VALUES ($addr, $keyId, $label, NULL, $now)
            ON CONFLICT(Address) DO UPDATE SET KeyId = $keyId, Label = $label";
        command.Parameters.AddWithValue("$addr", address);
        command.Parameters.AddWithValue("$keyId", keyId);
        command.Parameters.AddWithValue("$label", label ?? "");
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        command.ExecuteNonQuery();
    }

    public List<(string Address, string KeyId, string Label)> ListAccounts()
    {
        var results = new List<(string, string, string)>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Address, KeyId, Label FROM Accounts";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2)));
        }
        return results;
    }
}