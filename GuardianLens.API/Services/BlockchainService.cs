using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuardianLens.API.Models;

namespace GuardianLens.API.Services;

/// <summary>
/// Blockchain Provenance Service — writes asset registrations and DMCA evidence
/// to the Polygon Mumbai testnet (or mainnet) using Nethereum.
///
/// SETUP (5 minutes — fully free):
///   1. Install MetaMask → create a wallet → copy the private key
///   2. Add Polygon Mumbai to MetaMask (chainId 80001)
///   3. Get free MATIC from faucet.polygon.technology
///   4. Deploy GuardianLens.sol in Remix IDE → copy contract address
///   5. Fill in appsettings.json Blockchain section
///
/// SIMULATION MODE (default when no keys configured):
///   Works without any blockchain setup. Generates a realistic-looking tx hash
///   and stores "Simulated" as the network. All dashboard features work.
///   Perfect for the hackathon demo — upgrade to real chain any time.
/// </summary>
public interface IBlockchainService
{
    Task<BlockchainReceipt> RegisterAssetAsync(string pHash, string watermarkToken, string organization);
    Task<BlockchainReceipt> RecordViolationAsync(DigitalAsset asset, Violation violation);
    Task<OwnershipProof>    VerifyOwnershipAsync(string pHash, string watermarkToken, string organization);
    bool IsConfigured { get; }
    string NetworkName { get; }
}

public class BlockchainService : IBlockchainService
{
    private readonly IConfiguration _config;
    private readonly ILogger<BlockchainService> _logger;
    private readonly HttpClient _http;

    // Lazy Nethereum Web3 instance — only created if keys are configured
    private Nethereum.Web3.Web3? _web3;
    private bool _initialized;

    // Polygon Mumbai testnet details
    private const string MumbaiRpc      = "https://rpc-mumbai.maticvigil.com";
    private const string MumbaiChainId  = "80001";
    private const string MumbaiExplorer = "https://mumbai.polygonscan.com";
    private const string MainnetExplorer = "https://polygonscan.com";

    // Solidity contract ABI — only the functions we call
    // Full contract source: see blockchain/GuardianLens.sol
    private const string ContractAbi = """
    [
      {
        "inputs": [
          {"name": "commitmentHash", "type": "bytes32"},
          {"name": "organisation",   "type": "string"}
        ],
        "name": "registerAsset",
        "outputs": [],
        "stateMutability": "nonpayable",
        "type": "function"
      },
      {
        "inputs": [
          {"name": "assetHash",      "type": "bytes32"},
          {"name": "platform",       "type": "string"},
          {"name": "infringingUrl",  "type": "string"},
          {"name": "confidenceBps",  "type": "uint256"}
        ],
        "name": "recordViolation",
        "outputs": [],
        "stateMutability": "nonpayable",
        "type": "function"
      },
      {
        "inputs": [{"name": "commitmentHash", "type": "bytes32"}],
        "name": "verifyAsset",
        "outputs": [
          {"name": "exists",       "type": "bool"},
          {"name": "organisation", "type": "string"},
          {"name": "registeredAt", "type": "uint256"}
        ],
        "stateMutability": "view",
        "type": "function"
      }
    ]
    """;

    public BlockchainService(IConfiguration config, ILogger<BlockchainService> logger,
                              HttpClient http)
    {
        _config = config; _logger = logger; _http = http;
    }

    public bool IsConfigured
    {
        get
        {
            var key  = _config["Blockchain:PrivateKey"];
            var addr = _config["Blockchain:ContractAddress"];
            return !string.IsNullOrWhiteSpace(key)
                && key != "YOUR_WALLET_PRIVATE_KEY"
                && !string.IsNullOrWhiteSpace(addr)
                && addr != "0xYOUR_DEPLOYED_CONTRACT_ADDRESS";
        }
    }

    public string NetworkName => IsConfigured
        ? (_config["Blockchain:Network"] ?? "Polygon Mumbai")
        : "Simulated";

    // ─── Register Asset on Chain ──────────────────────────────────────────────

    public async Task<BlockchainReceipt> RegisterAssetAsync(
        string pHash, string watermarkToken, string organization)
    {
        var commitment = ComputeCommitmentHash(pHash, watermarkToken, organization);

        if (!IsConfigured)
            return await SimulateTransactionAsync("RegisterAsset", commitment, organization);

        try
        {
            EnsureWeb3();
            var contractAddress = _config["Blockchain:ContractAddress"]!;
            var contract = _web3!.Eth.GetContract(ContractAbi, contractAddress);
            var func     = contract.GetFunction("registerAsset");

            var bytes32 = HexToBytes32(commitment);

            var txHash = await func.SendTransactionAsync(
                from:     _web3.TransactionManager.Account.Address,
                gas:      new Nethereum.Hex.HexTypes.HexBigInteger(200_000),
                value:    null,
                functionInput: new object[] { bytes32, organization }
            );

            _logger.LogInformation("Asset registered on {Net}: {Tx}", NetworkName, txHash);

            // Poll for receipt to get block number
            var receipt = await WaitForReceiptAsync(txHash);
            return new BlockchainReceipt
            {
                TxHash         = txHash,
                CommitmentHash = commitment,
                BlockNumber    = (long)(receipt?.BlockNumber.Value ?? 0),
                Network        = NetworkName,
                ExplorerUrl    = BuildExplorerUrl(txHash),
                Timestamp      = DateTime.UtcNow,
                Success        = receipt?.Status.Value == 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Blockchain registration failed — falling back to simulation");
            return await SimulateTransactionAsync("RegisterAsset", commitment, organization);
        }
    }

    // ─── Record Violation Evidence on Chain ──────────────────────────────────

    public async Task<BlockchainReceipt> RecordViolationAsync(
        DigitalAsset asset, Violation violation)
    {
        var commitment = ComputeCommitmentHash(
            asset.PHash, asset.WatermarkToken ?? "", asset.Organization ?? "");

        if (!IsConfigured)
            return await SimulateTransactionAsync("RecordViolation", commitment,
                violation.Platform ?? "Unknown");

        try
        {
            EnsureWeb3();
            var contractAddress = _config["Blockchain:ContractAddress"]!;
            var contract = _web3!.Eth.GetContract(ContractAbi, contractAddress);
            var func     = contract.GetFunction("recordViolation");

            var bytes32    = HexToBytes32(commitment);
            var confBps    = (ulong)(violation.MatchConfidence * 10_000);

            var txHash = await func.SendTransactionAsync(
                from:  _web3.TransactionManager.Account.Address,
                gas:   new Nethereum.Hex.HexTypes.HexBigInteger(200_000),
                value: null,
                functionInput: new object[]
                {
                    bytes32,
                    violation.Platform ?? "Unknown",
                    violation.InfringingUrl,
                    confBps
                }
            );

            var receipt = await WaitForReceiptAsync(txHash);
            return new BlockchainReceipt
            {
                TxHash         = txHash,
                CommitmentHash = commitment,
                BlockNumber    = (long)(receipt?.BlockNumber.Value ?? 0),
                Network        = NetworkName,
                ExplorerUrl    = BuildExplorerUrl(txHash),
                Timestamp      = DateTime.UtcNow,
                Success        = receipt?.Status.Value == 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Blockchain evidence recording failed — falling back");
            return await SimulateTransactionAsync("RecordViolation", commitment,
                violation.Platform ?? "Unknown");
        }
    }

    // ─── Verify Ownership (read-only call, free) ──────────────────────────────

    public async Task<OwnershipProof> VerifyOwnershipAsync(
        string pHash, string watermarkToken, string organization)
    {
        var commitment = ComputeCommitmentHash(pHash, watermarkToken, organization);

        if (!IsConfigured)
        {
            return new OwnershipProof
            {
                CommitmentHash = commitment,
                Exists         = true,
                Organization   = organization,
                RegisteredAt   = DateTime.UtcNow.AddDays(-1),
                Network        = "Simulated",
                IsSimulated    = true
            };
        }

        try
        {
            EnsureWeb3();
            var contract = _web3!.Eth.GetContract(ContractAbi,
                _config["Blockchain:ContractAddress"]!);
            var func = contract.GetFunction("verifyAsset");
            var bytes32 = HexToBytes32(commitment);

            var result = await func.CallDeserializingToObjectAsync<(bool, string, ulong)>(bytes32);
            var (exists, org, timestamp) = result;

            return new OwnershipProof
            {
                CommitmentHash = commitment,
                Exists         = exists,
                Organization   = org,
                RegisteredAt   = DateTimeOffset.FromUnixTimeSeconds((long)timestamp).UtcDateTime,
                Network        = NetworkName,
                ExplorerUrl    = $"{GetExplorerBase()}/address/{_config["Blockchain:ContractAddress"]}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ownership verification failed");
            return new OwnershipProof
            {
                CommitmentHash = commitment,
                Exists         = false,
                Error          = ex.Message
            };
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the 32-byte commitment: SHA-256(pHash:watermarkToken:organisation)
    /// This same formula is used in the Solidity contract for verification.
    /// </summary>
    public static string ComputeCommitmentHash(
        string pHash, string watermarkToken, string organization)
    {
        var raw   = $"{pHash}:{watermarkToken}:{organization}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "0x" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void EnsureWeb3()
    {
        if (_initialized) return;
        var privateKey = _config["Blockchain:PrivateKey"]!;
        var rpcUrl     = _config["Blockchain:RpcUrl"] ?? MumbaiRpc;
        var chainId    = int.Parse(_config["Blockchain:ChainId"] ?? MumbaiChainId);
        var account    = new Nethereum.Web3.Accounts.Account(privateKey, chainId);
        _web3          = new Nethereum.Web3.Web3(account, rpcUrl);
        _web3.TransactionManager.UseLegacyAsDefault = true;
        _initialized   = true;
    }

    private async Task<Nethereum.RPC.Eth.DTOs.TransactionReceipt?> WaitForReceiptAsync(
        string txHash, int maxAttempts = 20)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(1500);
            var receipt = await _web3!.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(txHash);
            if (receipt != null) return receipt;
        }
        return null;
    }

    private async Task<BlockchainReceipt> SimulateTransactionAsync(
        string operation, string commitment, string context)
    {
        // Realistic simulation — generates a plausible-looking tx hash
        // so the dashboard can show blockchain fields without a real wallet
        await Task.Delay(800); // simulate network latency

        var seed   = $"{operation}:{commitment}:{context}:{DateTime.UtcNow.Ticks}";
        var hash   = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var txHash = "0x" + Convert.ToHexString(hash).ToLowerInvariant();
        var block  = 40_000_000 + Random.Shared.Next(100_000);

        _logger.LogInformation("[SIMULATED] {Op} tx: {Tx}", operation, txHash[..20] + "...");

        return new BlockchainReceipt
        {
            TxHash         = txHash,
            CommitmentHash = commitment,
            BlockNumber    = block,
            Network        = "Simulated (Polygon Mumbai)",
            ExplorerUrl    = $"https://mumbai.polygonscan.com/tx/{txHash}",
            Timestamp      = DateTime.UtcNow,
            Success        = true,
            IsSimulated    = true
        };
    }

    private static byte[] HexToBytes32(string hex)
    {
        hex = hex.TrimStart('0', 'x');
        var bytes = new byte[32];
        var data  = Convert.FromHexString(hex.PadLeft(64, '0'));
        Array.Copy(data, bytes, Math.Min(data.Length, 32));
        return bytes;
    }

    private string BuildExplorerUrl(string txHash)
        => $"{GetExplorerBase()}/tx/{txHash}";

    private string GetExplorerBase()
    {
        var chainId = _config["Blockchain:ChainId"] ?? MumbaiChainId;
        return chainId == "137" ? MainnetExplorer : MumbaiExplorer;
    }
}

// ─── Response Models ──────────────────────────────────────────────────────────

public class BlockchainReceipt
{
    public string   TxHash         { get; set; } = string.Empty;
    public string   CommitmentHash { get; set; } = string.Empty;
    public long     BlockNumber    { get; set; }
    public string   Network        { get; set; } = string.Empty;
    public string?  ExplorerUrl    { get; set; }
    public DateTime Timestamp      { get; set; }
    public bool     Success        { get; set; }
    public bool     IsSimulated    { get; set; }
}

public class OwnershipProof
{
    public string    CommitmentHash { get; set; } = string.Empty;
    public bool      Exists         { get; set; }
    public string?   Organization   { get; set; }
    public DateTime? RegisteredAt   { get; set; }
    public string?   Network        { get; set; }
    public string?   ExplorerUrl    { get; set; }
    public bool      IsSimulated    { get; set; }
    public string?   Error          { get; set; }
}
