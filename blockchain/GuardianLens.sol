// SPDX-License-Identifier: MIT
pragma solidity ^0.8.20;

/// @title GuardianLens on-chain provenance (Polygon Mumbai / any EVM chain)
/// @notice Matches the ABI embedded in GuardianLens.API/Services/BlockchainService.cs
contract GuardianLens {
    struct Asset {
        bool exists;
        string organisation;
        uint256 registeredAt;
    }

    mapping(bytes32 => Asset) private assets;

    event AssetRegistered(
        bytes32 indexed commitmentHash,
        string organisation,
        uint256 timestamp
    );

    event ViolationRecorded(
        bytes32 indexed assetHash,
        string platform,
        string infringingUrl,
        uint256 confidenceBps
    );

    /// @notice Commitment hash is SHA256(UTF-8 bytes of "pHash:watermarkToken:organisation") from the API.
    function registerAsset(bytes32 commitmentHash, string calldata organisation) external {
        require(!assets[commitmentHash].exists, "GuardianLens: already registered");
        assets[commitmentHash] = Asset({
            exists: true,
            organisation: organisation,
            registeredAt: block.timestamp
        });
        emit AssetRegistered(commitmentHash, organisation, block.timestamp);
    }

    function recordViolation(
        bytes32 assetHash,
        string calldata platform,
        string calldata infringingUrl,
        uint256 confidenceBps
    ) external {
        require(assets[assetHash].exists, "GuardianLens: unknown asset");
        emit ViolationRecorded(assetHash, platform, infringingUrl, confidenceBps);
    }

    function verifyAsset(bytes32 commitmentHash)
        external
        view
        returns (bool exists, string memory organisation, uint256 registeredAt)
    {
        Asset storage a = assets[commitmentHash];
        return (a.exists, a.organisation, a.registeredAt);
    }
}
