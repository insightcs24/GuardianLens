GuardianLens - Live blockchain (quick path)
============================================

1) Wallet (Polygon Mumbai, chain id 80001)
   - Create a dedicated hot wallet in MetaMask (not your main savings wallet).
   - Account detail -> Export private key (hex). The API reads configuration key
     Blockchain:PrivateKey (env: Blockchain__PrivateKey). Nethereum accepts the
     hex string with or without a leading 0x; use the same key that will pay gas.
   - Fund the address with test MATIC using any working "Polygon Mumbai faucet"
     (search the web; faucets change often). If Mumbai is unavailable, deploy on
     Polygon Amoy instead and update RpcUrl, ChainId, ExplorerBase, and Network
     in appsettings.Production.json to match that chain.

2) Deploy this contract
   - Open https://remix.ethereum.org
   - Create file GuardianLens.sol and paste contents from blockchain/GuardianLens.sol
   - Compiler: 0.8.20+ (or compatible with pragma). Compile GuardianLens.
   - Deploy & Run: Environment "Injected Provider", network Mumbai.
   - Deploy GuardianLens. Copy the contract address (0x...).

3) Configure the API on the server (no secrets in git)
   Template in repo: deploy\windows\config\api-extra-env.example.txt
   Preferred: create C:\guardianlens\config\api-extra-env.txt on EC2 with lines:
     Blockchain__PrivateKey=your_hex_private_key_here
     Blockchain__ContractAddress=0xYourDeployedContract
   Optional overrides:
     Blockchain__RpcUrl=https://...
     Blockchain__ChainId=80001
     Blockchain__Network=Polygon Mumbai
     Blockchain__ExplorerBase=https://mumbai.polygonscan.com
   Re-run install-services.ps1 (or remote-sync-windows-services.ps1) so the
   GuardianLensAPI service picks up the merged environment.

   Alternative: edit appsettings.Production.json next to GuardianLens.API.exe
   (same keys under "Blockchain") and restart the service.

4) Restart and verify
   - Restart Windows service GuardianLensAPI (and GuardianLensProxy if needed).
   - Run check-health.ps1 or GET http://localhost:5000/api/blockchain/status
     Expect isConfigured true and mode Live.
