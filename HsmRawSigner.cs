
using System.Numerics;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Model;
using Nethereum.RLP;
using Nethereum.Signer;
using Nethereum.Util;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

public class HsmRawSigner
{

    private readonly string _pkcs11LibraryPath;
    private readonly byte[] _keyId;

    public byte[] PublicKeyBytes { get; }
    public string PublicAddress { get; }

    public HsmRawSigner(string pkcs11LibraryPath, byte keyId, string publicKeyDerFilePath)
    {
        _pkcs11LibraryPath = pkcs11LibraryPath;
        _keyId = new byte[] { keyId };

        byte[] derBytes = File.ReadAllBytes(publicKeyDerFilePath);
        Console.WriteLine($"[DEBUG] .der file read, {derBytes.Length} bytes total.");

        var spki = SubjectPublicKeyInfo.GetInstance(derBytes);
        byte[] ecPoint = spki.PublicKeyData.GetBytes();

        Console.WriteLine($"[DEBUG] EC point extracted from DER: {ecPoint.Length} bytes, prefix: 0x{ecPoint[0]:X2}");

        if (ecPoint.Length != 65 || ecPoint[0] != 0x04)
            throw new Exception($"Unexpected EC public key format. Length: {ecPoint.Length}, prefix: 0x{ecPoint[0]:X2}. Expected 65 bytes, prefix 0x04 (uncompressed).");

        PublicKeyBytes = ecPoint.Skip(1).ToArray();

        Console.WriteLine($"[DEBUG] PublicKeyBytes.Length = {PublicKeyBytes.Length} (must be 64)");
        Console.WriteLine($"[DEBUG] PublicKeyBytes (hex) = {PublicKeyBytes.ToHex()}");

        var sha3 = new Sha3Keccack();
        byte[] hash = sha3.CalculateHash(PublicKeyBytes);
        byte[] addressBytes = hash.Skip(12).Take(20).ToArray();
        PublicAddress = "0x" + BitConverter.ToString(addressBytes).Replace("-", "").ToLower();

        Console.WriteLine($"[DEBUG] PublicAddress = {PublicAddress}");
        Console.WriteLine($"[DEBUG] PublicAddress.Length = {PublicAddress.Length} (must be 42: '0x' + 40 hex chars)\n");
    }

    private bool VerifySignatureDirectly(byte[] hash, byte[] r, byte[] s)
    {
        var curve = SecNamedCurves.GetByName("secp256k1");
        var domainParams = new Org.BouncyCastle.Crypto.Parameters.ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);

        byte[] x = PublicKeyBytes.Take(32).ToArray();
        byte[] y = PublicKeyBytes.Skip(32).Take(32).ToArray();

        var xBig = new Org.BouncyCastle.Math.BigInteger(1, x);
        var yBig = new Org.BouncyCastle.Math.BigInteger(1, y);
        var point = curve.Curve.CreatePoint(xBig, yBig);

        var pubKeyParams = new ECPublicKeyParameters(point, domainParams);

        var signer = new ECDsaSigner();
        signer.Init(false, pubKeyParams);

        var rBig = new Org.BouncyCastle.Math.BigInteger(1, r);
        var sBig = new Org.BouncyCastle.Math.BigInteger(1, s);

        bool isValid = signer.VerifySignature(hash, rBig, sBig);
        Console.WriteLine($"[DEBUG] Is the signature mathematically valid for PublicKeyBytes? {isValid}");
        return isValid;
    }

    public string SignLegacyTransaction(
        BigInteger nonce, BigInteger gasPrice, BigInteger gasLimit,
        string to, BigInteger valueWei, BigInteger chainId, string _pin)
    {
        var tx = new LegacyTransactionChainId(
            nonce.ToBytesForRLPEncoding(),
            gasPrice.ToBytesForRLPEncoding(),
            gasLimit.ToBytesForRLPEncoding(),
            to.HexToByteArray(),
            valueWei.ToBytesForRLPEncoding(),
            "".HexToByteArray(),
            chainId.ToBytesForRLPEncoding()
        );

        byte[] hashToSign = tx.RawHash;

        byte[] rawHsmSignature = SignHashOnHsm(hashToSign, _pin);

        Console.WriteLine($"[DEBUG] rawHsmSignature.Length = {rawHsmSignature.Length}");
        Console.WriteLine($"[DEBUG] rawHsmSignature (hex) = {rawHsmSignature.ToHex()}");

        byte[] raw64 = DecodeDerSignature(rawHsmSignature);

        Console.WriteLine($"[DEBUG] raw64.Length = {raw64.Length}");
        Console.WriteLine($"[DEBUG] raw64 (hex) = {raw64.ToHex()}");

        byte[] r = raw64.Take(32).ToArray();
        byte[] s = raw64.Skip(32).Take(32).ToArray();

        var curveOrder = new Org.BouncyCastle.Math.BigInteger("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141", 16);
        var halfOrder = curveOrder.ShiftRight(1);
        var sBig = new Org.BouncyCastle.Math.BigInteger(1, s);

        Console.WriteLine($"[DEBUG] Original S: {sBig}");
        Console.WriteLine($"[DEBUG] Half order: {halfOrder}");
        Console.WriteLine($"[DEBUG] S > halfOrder? {sBig.CompareTo(halfOrder) > 0}");

        if (sBig.CompareTo(halfOrder) > 0)
        {
            sBig = curveOrder.Subtract(sBig);
            s = ForceTo32Bytes(sBig.ToByteArrayUnsigned());
            Console.WriteLine($"[DEBUG] Normalized S (hex): {s.ToHex()}");
        }

        VerifySignatureDirectly(hashToSign, r, s);

        byte recoveryId = FindRecoveryId(hashToSign, r, s);

        BigInteger v = chainId * 2 + 35 + recoveryId;

        byte[] vBytes = v.ToByteArray();
        Array.Reverse(vBytes);
        vBytes = vBytes.SkipWhile(b => b == 0).ToArray();
        if (vBytes.Length == 0) vBytes = new byte[] { 0 };

        Console.WriteLine($"[DEBUG] v (decimal) = {v}");
        Console.WriteLine($"[DEBUG] v bytes (big-endian, hex) = {vBytes.ToHex()}");

        var signature = EthECDSASignatureFactory.FromComponents(r, s, vBytes);
        tx.SetSignature(signature);

        return "0x" + tx.GetRLPEncoded().ToHex();
    }

    private byte[] SignHashOnHsm(byte[] hash, string _pin)
    {
        var factories = new Pkcs11InteropFactories();
        using (IPkcs11Library pkcs11Library = factories.Pkcs11LibraryFactory.LoadPkcs11Library(factories, _pkcs11LibraryPath, AppType.SingleThreaded))
        {
            List<ISlot> slots = pkcs11Library.GetSlotList(SlotsType.WithTokenPresent);
            if (slots == null || slots.Count == 0)
                throw new Exception("No active HSM slot found.");

            ISlot slot = slots[0];

            using (ISession session = slot.OpenSession(SessionType.ReadWrite))
            {
                session.Login(CKU.CKU_USER, _pin);

                var searchTemplate = new List<IObjectAttribute>
                {
                    session.Factories.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
                    session.Factories.ObjectAttributeFactory.Create(CKA.CKA_ID, _keyId)
                };

                List<IObjectHandle> found = session.FindAllObjects(searchTemplate);
                if (found.Count == 0)
                    throw new Exception("Private key not found on the HSM.");

                IMechanism mechanism = session.Factories.MechanismFactory.Create(CKM.CKM_ECDSA);
                byte[] signature = session.Sign(mechanism, found[0], hash);

                session.Logout();
                return signature;
            }
        }
    }


    private byte FindRecoveryId(byte[] hash, byte[] r, byte[] s)
    {
        Console.WriteLine($"\n[DEBUG] Hash being signed : {hash.ToHex()}");
        Console.WriteLine($"[DEBUG] R value from HSM: {r.ToHex()}");
        Console.WriteLine($"[DEBUG] S value from HSM: {s.ToHex()}");

        for (byte recId = 0; recId <= 1; recId++)
        {
            var candidateSignature = EthECDSASignatureFactory.FromComponents(r, s, (byte)(recId + 27));
            var recoveredKey = EthECKey.RecoverFromSignature(candidateSignature, hash);

            if (recoveredKey != null)
            {
                string recoveredAddr = recoveredKey.GetPublicAddress();
                Console.WriteLine($"[DEBUG] RecId {recId} produced address: {recoveredAddr}");

                if (recoveredAddr.Equals(PublicAddress, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[SUCCESS] Match found at RecId: {recId}!");
                    return recId;
                }
            }
        }

        Console.WriteLine($"[ERROR] Expected address from HSM/DER is: {PublicAddress}");
        throw new Exception("Could not determine the correct recovery id (v).");
    }

    private static byte[] DecodeDerSignature(byte[] derSignature)
    {
        if (derSignature.Length == 64) return derSignature;

        using var stream = new MemoryStream(derSignature);
        if (stream.ReadByte() != 0x30) throw new FormatException("Invalid DER format.");
        stream.ReadByte(); // skip sequence length

        if (stream.ReadByte() != 0x02) throw new FormatException("Invalid DER format for R.");
        int rLen = stream.ReadByte();
        byte[] rBytes = new byte[rLen];
        stream.Read(rBytes, 0, rLen);

        if (stream.ReadByte() != 0x02) throw new FormatException("Invalid DER format for S.");
        int sLen = stream.ReadByte();
        byte[] sBytes = new byte[sLen];
        stream.Read(sBytes, 0, sLen);

        byte[] rClean = ForceTo32Bytes(rBytes);
        byte[] sClean = ForceTo32Bytes(sBytes);

        byte[] raw = new byte[64];
        Array.Copy(rClean, 0, raw, 0, 32);
        Array.Copy(sClean, 0, raw, 32, 32);
        return raw;
    }

    private static byte[] ForceTo32Bytes(byte[] input)
    {
        if (input == null || input.Length == 0)
            return new byte[32];

        int skip = 0;
        while (skip < input.Length - 1 && input[skip] == 0x00)
        {
            skip++;
        }
        if (skip > 0)
        {
            input = input.Skip(skip).ToArray();
        }

        if (input.Length == 32)
            return input;

        if (input.Length > 32)
        {
            return input.Skip(input.Length - 32).Take(32).ToArray();
        }

        byte[] padded = new byte[32];
        Array.Copy(input, 0, padded, 32 - input.Length, input.Length);
        return padded;
    }
}